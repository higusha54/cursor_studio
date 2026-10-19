using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace CursorMaster;

internal sealed record CursorPack(
    string Name,
    string DirectoryPath,
    string SchemeValue,
    IReadOnlyDictionary<string, string> Files);

internal static class CursorPackService
{
    private static readonly string[] SchemeRoleOrder =
    [
        "Arrow",
        "Help",
        "AppStarting",
        "Wait",
        "Crosshair",
        "IBeam",
        "NWPen",
        "No",
        "SizeNS",
        "SizeWE",
        "SizeNWSE",
        "SizeNESW",
        "SizeAll",
        "UpArrow",
        "Hand",
        "Pin",
        "Person"
    ];

    private static readonly Regex InvalidFileNameCharacters =
        new(@"[<>:""/\\|?*\x00-\x1F]", RegexOptions.Compiled);

    private const string SchemesRegistryPath = @"Control Panel\Cursors\Schemes";

    public static CursorPack Build(
        string name,
        IReadOnlyDictionary<string, string> assignedCursors,
        string packsDirectory)
    {
        var packName = ValidatePackName(name);
        var selectedFiles = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var role in SchemeRoleOrder)
        {
            if (!assignedCursors.TryGetValue(role, out var configuredPath) ||
                string.IsNullOrWhiteSpace(configuredPath))
                continue;

            var sourcePath = Environment.ExpandEnvironmentVariables(
                configuredPath.Trim());
            if (!File.Exists(sourcePath))
                throw new FileNotFoundException(
                    $"The cursor assigned to {role} could not be found.",
                    sourcePath);

            ValidateCursorFile(sourcePath, role);
            selectedFiles.Add(role, sourcePath);
        }

        if (selectedFiles.Count == 0)
            throw new InvalidOperationException(
                "Choose at least one custom cursor before creating a pack.");

        Directory.CreateDirectory(packsDirectory);
        var packDirectory = Path.Combine(
            packsDirectory, $"{MakeFolderName(packName)}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(packDirectory);

        try
        {
            var files = new Dictionary<string, string>(StringComparer.Ordinal);
            var schemePaths = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var role in SchemeRoleOrder)
            {
                if (!selectedFiles.TryGetValue(role, out var sourcePath))
                {
                    schemePaths.Add(role, string.Empty);
                    continue;
                }

                var extension = Path.GetExtension(sourcePath).ToLowerInvariant();
                var fileName = role + extension;
                File.Copy(sourcePath, Path.Combine(packDirectory, fileName));
                files.Add(role, fileName);
                schemePaths.Add(role,
                    @"%LOCALAPPDATA%\PointerStudio\Packs\" +
                    Path.GetFileName(packDirectory) + "\\" + fileName);
            }

            var schemeValue = string.Join(
                ",", SchemeRoleOrder.Select(role => schemePaths[role]));
            var manifest = new
            {
                Name = packName,
                Format = 1,
                SchemeValue = schemeValue,
                Cursors = files
            };
            File.WriteAllText(
                Path.Combine(packDirectory, "pack.json"),
                JsonSerializer.Serialize(manifest, new JsonSerializerOptions
                {
                    WriteIndented = true
                }));

            return new CursorPack(
                packName, packDirectory, schemeValue, files);
        }
        catch
        {
            Directory.Delete(packDirectory, recursive: true);
            throw;
        }
    }

    public static void Register(CursorPack pack)
    {
        using var schemes = Registry.CurrentUser.CreateSubKey(
            SchemesRegistryPath, writable: true)
            ?? throw new IOException(
                "Could not open the current-user cursor scheme registry key.");

        schemes.SetValue(
            pack.Name, pack.SchemeValue, RegistryValueKind.ExpandString);
    }

    public static bool IsRegistered(string name)
    {
        using var schemes = Registry.CurrentUser.OpenSubKey(SchemesRegistryPath);
        return schemes?.GetValueNames().Contains(
            name, StringComparer.OrdinalIgnoreCase) ?? false;
    }

    private static string ValidatePackName(string name)
    {
        var value = name.Trim();
        if (value.Length is < 1 or > 64 ||
            value.Any(char.IsControl) ||
            value.Any(character => character is ',' or '"' or '%' or '\\'))
            throw new ArgumentException(
                "Pack names must contain 1–64 characters and cannot include commas, quotes, percent signs, or backslashes.",
                nameof(name));
        return value;
    }

    private static string MakeFolderName(string name)
    {
        var value = InvalidFileNameCharacters.Replace(name, "_").Trim(' ', '.');
        return value.Length == 0 ? "CursorPack" : value;
    }

    private static void ValidateCursorFile(string path, string role)
    {
        var extension = Path.GetExtension(path);
        if (!extension.Equals(".cur", StringComparison.OrdinalIgnoreCase) &&
            !extension.Equals(".ani", StringComparison.OrdinalIgnoreCase))
            throw new FormatException(
                $"The file assigned to {role} must use the .cur or .ani format.");

        using var stream = File.OpenRead(path);
        Span<byte> header = stackalloc byte[22];
        var read = stream.Read(header);
        var isCursor = extension.Equals(".cur", StringComparison.OrdinalIgnoreCase) &&
                       read >= 22 &&
                       BitConverter.ToUInt16(header[..2]) == 0 &&
                       BitConverter.ToUInt16(header.Slice(2, 2)) == 2 &&
                       BitConverter.ToUInt16(header.Slice(4, 2)) > 0 &&
                       BitConverter.ToUInt32(header.Slice(14, 4)) > 0 &&
                       BitConverter.ToUInt32(header.Slice(18, 4)) >= 22 &&
                       BitConverter.ToUInt32(header.Slice(18, 4)) +
                       BitConverter.ToUInt32(header.Slice(14, 4)) <= stream.Length;
        var isAnimatedCursor = extension.Equals(
                                  ".ani", StringComparison.OrdinalIgnoreCase) &&
                              read >= 12 &&
                              header[..4].SequenceEqual("RIFF"u8) &&
                              header.Slice(8, 4).SequenceEqual("ACON"u8) &&
                              BitConverter.ToUInt32(header.Slice(4, 4)) >= 4 &&
                              (long)BitConverter.ToUInt32(header.Slice(4, 4)) +
                              8 <= stream.Length;
        if (!isCursor && !isAnimatedCursor)
            throw new FormatException(
                $"The file assigned to {role} is not a valid {extension} cursor.");
    }
}
