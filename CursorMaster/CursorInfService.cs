using System.Text;
using System.Text.RegularExpressions;

namespace CursorMaster;

internal sealed record CursorInfRole(
    string Name,
    string RegistryName,
    string Reference,
    string? SourcePath);

internal sealed record CursorInfPreview(
    string InfPath,
    string Name,
    IReadOnlyList<CursorInfRole> Roles,
    string? Message)
{
    public bool IsCursorScheme => Roles.Count > 0;

    public bool CanInstall => IsCursorScheme &&
                              Roles.Where(role => role.Reference.Length > 0)
                                  .All(role => role.SourcePath is not null);

    public IReadOnlyDictionary<string, string> AvailableCursors =>
        Roles.Where(role => role.SourcePath is not null)
            .ToDictionary(role => role.RegistryName, role => role.SourcePath!,
                StringComparer.Ordinal);
}

internal static class CursorInfService
{
    private const int MaxInfBytes = 2 * 1024 * 1024;

    private static readonly (string Name, string RegistryName)[] Roles =
    [
        ("Normal select", "Arrow"),
        ("Help select", "Help"),
        ("Working in background", "AppStarting"),
        ("Busy", "Wait"),
        ("Precision select", "Crosshair"),
        ("Text select", "IBeam"),
        ("Handwriting", "NWPen"),
        ("Unavailable", "No"),
        ("Vertical resize", "SizeNS"),
        ("Horizontal resize", "SizeWE"),
        ("Diagonal resize 1", "SizeNWSE"),
        ("Diagonal resize 2", "SizeNESW"),
        ("Move", "SizeAll"),
        ("Alternate select", "UpArrow"),
        ("Link select", "Hand"),
        ("Location select", "Pin"),
        ("Person select", "Person")
    ];

    private static readonly Regex Token = new(
        @"%([^%]+)%",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static CursorInfPreview Parse(string infPath)
    {
        var fullPath = Path.GetFullPath(infPath);
        var info = new FileInfo(fullPath);
        if (!info.Exists)
            throw new FileNotFoundException("The INF file was not found.", fullPath);
        if (info.Length > MaxInfBytes)
            throw new FormatException("INF files larger than 2 MB cannot be previewed.");

        var text = ReadText(fullPath);
        var sections = ReadSections(text);
        var strings = ReadStrings(sections);
        var scheme = FindFirstCursorScheme(sections, strings);
        if (scheme is null)
        {
            return new CursorInfPreview(
                fullPath,
                Path.GetFileNameWithoutExtension(fullPath),
                [],
                "This INF does not contain a supported HKCU cursor-scheme entry. "
                + "Its text is shown below; no INF commands or driver directives are executed.");
        }

        var references = scheme.Value.Value.Split(',');
        if (references.Length is not (15 or 16 or 17))
        {
            return new CursorInfPreview(
                fullPath, scheme.Value.Name, [],
                $"The cursor scheme has {references.Length} entries; Windows cursor schemes must have 15, 16, or 17.");
        }

        var infDirectory = Path.GetDirectoryName(fullPath)!;
        var roles = new List<CursorInfRole>(Roles.Length);
        for (var index = 0; index < Roles.Length; index++)
        {
            var reference = index < references.Length
                ? ExpandString(references[index].Trim(), strings)
                : string.Empty;
            var source = reference.Length == 0
                ? null
                : FindCursorFile(infDirectory, reference);
            roles.Add(new CursorInfRole(
                Roles[index].Name,
                Roles[index].RegistryName,
                reference,
                source));
        }

        var missing = roles
            .Where(role => role.Reference.Length > 0 && role.SourcePath is null)
            .Select(role => role.RegistryName)
            .ToArray();
        return new CursorInfPreview(
            fullPath,
            scheme.Value.Name,
            roles,
            missing.Length == 0
                ? null
                : $"Cursor files were not found beside the INF for: {string.Join(", ", missing)}. "
                  + "Extract the entire cursor pack before importing it.");
    }

    private static string ReadText(string path)
    {
        using var stream = File.OpenRead(path);
        using var reader = new StreamReader(
            stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }

    private static Dictionary<string, List<string>> ReadSections(string text)
    {
        var sections = new Dictionary<string, List<string>>(
            StringComparer.OrdinalIgnoreCase);
        string? current = null;
        var pending = new StringBuilder();
        foreach (var originalLine in text.Split('\n'))
        {
            var line = StripComment(originalLine.TrimEnd('\r')).Trim();
            if (line.Length == 0)
                continue;
            if (pending.Length > 0)
                pending.Append(line);
            else
                pending.Append(line);
            if (pending[^1] == '\\')
            {
                pending.Length--;
                pending.Append(' ');
                continue;
            }

            line = pending.ToString().Trim();
            pending.Clear();
            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                current = line[1..^1].Trim();
                if (current.Length > 0 && !sections.ContainsKey(current))
                    sections.Add(current, []);
                continue;
            }
            if (current is not null)
                sections[current].Add(line);
        }
        if (pending.Length > 0 && current is not null)
            sections[current].Add(pending.ToString());
        return sections;
    }

    private static Dictionary<string, string> ReadStrings(
        Dictionary<string, List<string>> sections)
    {
        var result = new Dictionary<string, string>(
            StringComparer.OrdinalIgnoreCase);
        var stringSections = sections
            .Where(section => section.Key.Equals("Strings",
                                     StringComparison.OrdinalIgnoreCase) ||
                              section.Key.StartsWith("Strings.",
                                  StringComparison.OrdinalIgnoreCase))
            .OrderBy(section => section.Key.Contains('.'))
            .ToArray();
        foreach (var section in stringSections)
        {
            foreach (var line in section.Value)
            {
                var equals = line.IndexOf('=');
                if (equals <= 0)
                    continue;
                var key = line[..equals].Trim().Trim('"');
                var value = line[(equals + 1)..].Trim();
                result[key] = Unquote(value);
            }
        }
        return result;
    }

    private static (string Name, string Value)? FindFirstCursorScheme(
        Dictionary<string, List<string>> sections,
        Dictionary<string, string> strings)
    {
        var addRegSections = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var section in sections.Where(section =>
                     section.Key.StartsWith("DefaultInstall",
                         StringComparison.OrdinalIgnoreCase)))
        {
            foreach (var line in section.Value)
            {
                var equals = line.IndexOf('=');
                if (equals <= 0 ||
                    !line[..equals].Trim().Equals("AddReg",
                        StringComparison.OrdinalIgnoreCase))
                    continue;
                foreach (var referencedSection in SplitInfFields(line[(equals + 1)..]))
                    if (referencedSection.Length > 0)
                        addRegSections.Add(referencedSection);
            }
        }

        foreach (var section in sections)
        {
            if (addRegSections.Count > 0 && !addRegSections.Contains(section.Key))
                continue;
            foreach (var line in section.Value)
            {
                var fields = SplitInfFields(line);
                if (fields.Count < 5 ||
                    !(fields[0].Equals("HKCU", StringComparison.OrdinalIgnoreCase) ||
                      fields[0].Equals("HKEY_CURRENT_USER",
                          StringComparison.OrdinalIgnoreCase)) ||
                    !fields[1].Trim('\\').Equals(
                        @"Control Panel\Cursors\Schemes",
                        StringComparison.OrdinalIgnoreCase))
                    continue;

                var name = ExpandString(Unquote(fields[2]).Trim(), strings);
                var flags = fields[3].Trim();
                if (!IsStringRegistryValue(flags))
                    continue;
                var value = ExpandString(Unquote(string.Join(",", fields.Skip(4))), strings);
                if (name.Length > 0 && value.Length > 0)
                    return (name, value);
            }
        }
        return null;
    }

    private static bool IsStringRegistryValue(string flags)
    {
        if (flags.Length == 0)
            return true;
        if (!flags.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ||
            !uint.TryParse(flags.AsSpan(2),
                System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture,
                out var value))
            return false;

        const uint RegistryTypeMask = 0x00010001 | 0x00020000;
        var type = value & RegistryTypeMask;
        return type is 0 or 0x00020000;
    }

    private static List<string> SplitInfFields(string line)
    {
        var fields = new List<string>();
        var value = new StringBuilder();
        var quoted = false;
        for (var i = 0; i < line.Length; i++)
        {
            var character = line[i];
            if (character == '"')
            {
                if (quoted && i + 1 < line.Length && line[i + 1] == '"')
                {
                    value.Append('"');
                    i++;
                }
                else
                {
                    quoted = !quoted;
                }
                continue;
            }
            if (character == ',' && !quoted)
            {
                fields.Add(value.ToString().Trim());
                value.Clear();
            }
            else
            {
                value.Append(character);
            }
        }
        fields.Add(value.ToString().Trim());
        return fields;
    }

    private static string ExpandString(
        string value,
        Dictionary<string, string> strings)
    {
        var current = value.Trim().Trim('"');
        for (var pass = 0; pass < 8; pass++)
        {
            var expanded = Token.Replace(current, match =>
            {
                var token = match.Groups[1].Value;
                if (strings.TryGetValue(token, out var replacement))
                    return replacement;
                if (token.All(char.IsDigit))
                    return match.Value;
                return Environment.GetEnvironmentVariable(token) ?? match.Value;
            });
            if (expanded == current)
                break;
            current = expanded;
        }
        return current.Trim().Trim('"');
    }

    private static string? FindCursorFile(string infDirectory, string reference)
    {
        var root = Path.GetFullPath(infDirectory)
            .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var normalized = reference.Replace('/', Path.DirectorySeparatorChar);
        if (Path.IsPathRooted(normalized))
        {
            var exact = Path.GetFullPath(normalized);
            if (IsInsideRoot(exact, root) && File.Exists(exact))
                return exact;
        }
        else
        {
            var exact = Path.GetFullPath(Path.Combine(root, normalized));
            if (IsInsideRoot(exact, root) && File.Exists(exact))
                return exact;
        }

        var fileName = Path.GetFileName(normalized);
        if (string.IsNullOrWhiteSpace(fileName) ||
            fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            return null;

        string? match = null;
        var inspected = 0;
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            var directory = pending.Pop();
            IEnumerable<string> entries;
            try
            {
                entries = Directory.EnumerateFileSystemEntries(directory).ToArray();
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }
            catch (IOException)
            {
                continue;
            }

            foreach (var entry in entries)
            {
                if (++inspected > 10000)
                    throw new FormatException(
                        "The extracted pack contains too many files to search safely.");
                FileAttributes attributes;
                try
                {
                    attributes = File.GetAttributes(entry);
                }
                catch (IOException)
                {
                    continue;
                }
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                    continue;
                if ((attributes & FileAttributes.Directory) != 0)
                {
                    pending.Push(entry);
                    continue;
                }
                if (!Path.GetFileName(entry).Equals(
                        fileName, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (match is not null)
                    return null;
                match = Path.GetFullPath(entry);
            }
        }
        return match;
    }

    private static bool IsInsideRoot(string path, string root) =>
        path.StartsWith(root, StringComparison.OrdinalIgnoreCase);

    private static string StripComment(string line)
    {
        var quoted = false;
        for (var i = 0; i < line.Length; i++)
        {
            if (line[i] == '"')
                quoted = !quoted;
            else if (line[i] == ';' && !quoted)
                return line[..i];
        }
        return line;
    }

    private static string Unquote(string value)
    {
        var result = value.Trim();
        if (result.Length >= 2 && result[0] == '"' && result[^1] == '"')
            return result[1..^1].Replace("\"\"", "\"");
        return result;
    }
}
