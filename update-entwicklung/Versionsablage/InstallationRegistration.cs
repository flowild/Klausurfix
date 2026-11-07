using Microsoft.Win32;
using System.Runtime.Versioning;

[SupportedOSPlatform("windows")]
public static class InstallationRegistration
{
    private const string RegistryPath = @"Software\Klausurfix";

    public static string? Find()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RegistryPath);
        var path = key?.GetValue("Installationspfad") as string;
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path)) return null;
        path = Path.GetFullPath(path);
        return File.Exists(Path.Combine(path, "Klausurfix.exe")) && File.Exists(Path.Combine(path, "update-starter-v1.txt")) ? path : null;
    }

    public static void Register(string path)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RegistryPath, writable: true)
            ?? throw new IOException("Der Installationspfad konnte nicht registriert werden.");
        key.SetValue("Installationspfad", Path.GetFullPath(path), RegistryValueKind.String);
    }
}
