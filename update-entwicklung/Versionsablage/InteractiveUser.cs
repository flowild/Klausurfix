using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;

[SupportedOSPlatform("windows")]
public static class InteractiveUser
{
    public static void EnsureSame()
    {
        var shell = GetShellWindow();
        if (shell == IntPtr.Zero || GetWindowThreadProcessId(shell, out var pid) == 0)
            throw new InvalidOperationException("Das angemeldete Windows-Konto konnte nicht geprüft werden. Bitte das Update vom Windows-Desktop starten.");
        var process = OpenProcess(0x1000, false, pid);
        if (process == IntPtr.Zero) throw new InvalidOperationException("Das angemeldete Windows-Konto konnte nicht geprüft werden.");
        try
        {
            if (!OpenProcessToken(process, 0x0008, out var token)) throw new InvalidOperationException("Das angemeldete Windows-Konto konnte nicht geprüft werden.");
            using (token)
            using (var shellIdentity = new WindowsIdentity(token.DangerousGetHandle()))
            using (var identity = WindowsIdentity.GetCurrent())
                if (shellIdentity.User is null || identity.User is null || !shellIdentity.User.Equals(identity.User))
                    throw new InvalidOperationException("Die Administratorbestätigung verwendet ein anderes Windows-Konto. Das Update wurde nicht installiert.");
        }
        finally { CloseHandle(process); }
    }
    [DllImport("user32.dll")] private static extern IntPtr GetShellWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("kernel32.dll")] private static extern IntPtr OpenProcess(uint access, bool inherit, uint processId);
    [DllImport("advapi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool OpenProcessToken(IntPtr process, uint access, out SafeAccessTokenHandle token);
    [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CloseHandle(IntPtr handle);
}
