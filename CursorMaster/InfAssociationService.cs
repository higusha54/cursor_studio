using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace CursorMaster;

internal sealed record InfAssociationStatus(
    bool HasProtectedDefault,
    string? CurrentDefault,
    bool IsPointerStudioDefault);

internal static class InfAssociationService
{
    public const string ProgramId = "PointerStudio.CursorInf";

    private const string ClassesPath = @"Software\Classes";
    private const string CapabilitiesPath = @"Software\PointerStudio\Capabilities";
    private const string RegisteredApplicationsPath = @"Software\RegisteredApplications";
    private const string UserChoicePath =
        @"Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\.inf\UserChoice";

    private const uint ShcneAssocChanged = 0x08000000;
    private const uint ShcnfIdList = 0x0000;

    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(
        uint eventId, uint flags, IntPtr item1, IntPtr item2);

    public static InfAssociationStatus RegisterHandler()
    {
        var applicationPath = Environment.ProcessPath
            ?? throw new InvalidOperationException(
                "Windows could not determine the application executable path.");
        var command = $"\"{applicationPath}\" \"%1\"";

        using (var classes = Registry.CurrentUser.CreateSubKey(ClassesPath))
        {
            if (classes is null)
                throw new IOException("Could not register the .inf file handler.");

            using (var program = classes.CreateSubKey(ProgramId))
            {
                if (program is null)
                    throw new IOException("Could not register the .inf file handler.");
                program.SetValue("", "Pointer Studio cursor INF preview");
                using var icon = program.CreateSubKey("DefaultIcon");
                icon?.SetValue("", $"\"{applicationPath}\",0");
                using var open = program.CreateSubKey(@"shell\open\command");
                open?.SetValue("", command);
            }

            var extensionKey = classes.CreateSubKey(".inf");
            if (extensionKey is null)
                throw new IOException("Could not register the .inf file handler.");
            using (extensionKey)
            {
                using var openWith = extensionKey.CreateSubKey("OpenWithProgids");
                openWith?.SetValue(ProgramId, "", RegistryValueKind.String);
            }

            using (var application = classes.CreateSubKey(
                       $@"Applications\{Path.GetFileName(applicationPath)}"))
            {
                using var open = application?.CreateSubKey(@"shell\open\command");
                open?.SetValue("", command);
                using var supportedTypes = application?.CreateSubKey("SupportedTypes");
                supportedTypes?.SetValue(".inf", "");
            }
        }

        using (var capabilities = Registry.CurrentUser.CreateSubKey(CapabilitiesPath))
        {
            if (capabilities is null)
                throw new IOException("Could not publish the .inf handler in Default Apps.");
            capabilities.SetValue("ApplicationName", "Pointer Studio");
            capabilities.SetValue(
                "ApplicationDescription",
                "Preview and import Windows cursor packs from INF files.");
            using var associations = capabilities.CreateSubKey("FileAssociations");
            associations?.SetValue(".inf", ProgramId);
        }

        using (var applications =
               Registry.CurrentUser.CreateSubKey(RegisteredApplicationsPath))
        {
            if (applications is null)
                throw new IOException("Could not publish the .inf handler in Default Apps.");
            applications.SetValue("Pointer Studio", CapabilitiesPath);
        }

        NotifyShell();
        return ReadStatus();
    }

    public static InfAssociationStatus SetAsDefault()
    {
        var status = RegisterHandler();
        if (status.IsPointerStudioDefault || status.HasProtectedDefault)
            return status;

        using var extension = Registry.CurrentUser.CreateSubKey(
            $@"{ClassesPath}\.inf");
        if (extension is null)
            throw new IOException("Could not set the current-user .inf default.");
        extension.SetValue("", ProgramId, RegistryValueKind.String);
        NotifyShell();
        return ReadStatus();
    }

    public static void OpenDefaultAppsSettings()
    {
        try
        {
            Process.Start(new ProcessStartInfo(
                "ms-settings:defaultapps?registeredAppUser=Pointer%20Studio")
            {
                UseShellExecute = true
            });
        }
        catch (Win32Exception)
        {
            Process.Start(new ProcessStartInfo("ms-settings:defaultapps")
            {
                UseShellExecute = true
            });
        }
    }

    private static InfAssociationStatus ReadStatus()
    {
        using var choice = Registry.CurrentUser.OpenSubKey(UserChoicePath);
        var current = choice?.GetValue("ProgId")?.ToString();
        return new InfAssociationStatus(
            choice is not null,
            current,
            string.Equals(current, ProgramId, StringComparison.OrdinalIgnoreCase));
    }

    private static void NotifyShell() =>
        SHChangeNotify(ShcneAssocChanged, ShcnfIdList, IntPtr.Zero, IntPtr.Zero);
}
