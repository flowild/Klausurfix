namespace Klausurfix.Infrastructure.Security;

using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;

public static class WindowsDpapiProtector
{
    private static readonly byte[] DpapiEntropy = new byte[] { 0x4B, 0x6C, 0x61, 0x75, 0x73, 0x75, 0x72, 0x66, 0x69, 0x78, 0x5F, 0x53, 0x65, 0x63 }; // "Klausurfix_Sec"

    public static bool IsSupported => OperatingSystem.IsWindows();

    [SupportedOSPlatform("windows")]
    public static byte[] Protect(byte[] data)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("DPAPI is only supported on Windows.");

        return ProtectedData.Protect(data, DpapiEntropy, DataProtectionScope.CurrentUser);
    }

    [SupportedOSPlatform("windows")]
    public static byte[] Unprotect(byte[] protectedData)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("DPAPI is only supported on Windows.");

        return ProtectedData.Unprotect(protectedData, DpapiEntropy, DataProtectionScope.CurrentUser);
    }
}
