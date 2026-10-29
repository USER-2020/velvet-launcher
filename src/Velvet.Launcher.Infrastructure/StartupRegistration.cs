using Microsoft.Win32;
using System.Diagnostics;
using System.Runtime.Versioning;
using System.Xml.Linq;

namespace Velvet.Launcher.Infrastructure;

/// <summary>Registers the packaged Launcher to start with the current user session.</summary>
public static class StartupRegistration
{
    private const string AppName = "VELVET Launcher";
    private const string MacLabel = "co.thevelvetstudio.launcher";

    public static void Ensure()
    {
        if (Environment.GetEnvironmentVariable("VELVET_DISABLE_AUTOSTART") == "1") return;

        var executable = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executable) || IsDotnetHost(executable)) return;

        try
        {
            if (OperatingSystem.IsMacOS()) RegisterMac(executable);
            else if (OperatingSystem.IsWindows()) RegisterWindows(executable);
        }
        catch
        {
            // Startup registration must never prevent the Launcher from opening.
        }
    }

    private static bool IsDotnetHost(string executable) =>
        string.Equals(Path.GetFileNameWithoutExtension(executable), "dotnet", StringComparison.OrdinalIgnoreCase);

    private static void RegisterMac(string executable)
    {
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "LaunchAgents");
        Directory.CreateDirectory(directory);
        var plistPath = Path.Combine(directory, $"{MacLabel}.plist");
        var plist = new XDocument(
            new XElement("plist", new XAttribute("version", "1.0"),
                new XElement("dict",
                    new XElement("key", "Label"), new XElement("string", MacLabel),
                    new XElement("key", "ProgramArguments"),
                    new XElement("array", new XElement("string", executable)),
                    new XElement("key", "RunAtLoad"), new XElement("true"),
                    new XElement("key", "KeepAlive"), new XElement("true"))));
        File.WriteAllText(plistPath, plist.ToString(SaveOptions.DisableFormatting));
    }

    [SupportedOSPlatform("windows")]
    private static void RegisterWindows(string executable)
    {
        using var key = Registry.CurrentUser.OpenSubKey(
            "Software\\Microsoft\\Windows\\CurrentVersion\\Run", writable: true)
            ?? Registry.CurrentUser.CreateSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Run");
        key?.SetValue(AppName, $"\"{executable}\"");
    }
}
