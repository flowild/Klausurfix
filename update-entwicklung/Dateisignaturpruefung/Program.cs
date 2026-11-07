using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

if (args.Length != 2)
{
    Console.Error.WriteLine("Aufruf: Dateisignaturpruefung <Datei.exe> <vertrauenswürdiger-Zertifikat-SHA256>");
    return 2;
}
try
{
    if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Diese Prüfung erfordert Windows.");
    var expected = Convert.FromHexString(args[1].Replace(" ", "", StringComparison.Ordinal));
    if (expected.Length != 32) throw new ArgumentException("Der Zertifikat-Fingerabdruck muss 32 Byte lang sein.");
    var path = Path.GetFullPath(args[0]);
    // Deny concurrent writes/deletion while Windows and the certificate reader inspect the file.
    using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
    var result = Authenticode.Verify(path, file.SafeFileHandle.DangerousGetHandle());
    if (result != 0) throw new CryptographicException($"Windows hat die Signatur abgelehnt (0x{result:X8}).");
#pragma warning disable SYSLIB0057
    using var certificate = X509Certificate.CreateFromSignedFile(path);
#pragma warning restore SYSLIB0057
    var actual = SHA256.HashData(certificate.GetRawCertData());
    if (!CryptographicOperations.FixedTimeEquals(actual, expected))
        throw new CryptographicException("Das Signaturzertifikat stimmt nicht mit dem vertrauenswürdigen Fingerabdruck überein.");
    Console.WriteLine("Windows-Signatur und vorgegebener Zertifikat-Fingerabdruck geprüft. Die Datei wurde nicht ausgeführt.");
    return 0;
}
catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException or ArgumentException or PlatformNotSupportedException)
{
    Console.Error.WriteLine($"Signaturprüfung fehlgeschlagen: {ex.Message}");
    return 1;
}

internal static class Authenticode
{
    private static readonly Guid GenericVerifyV2 = new("00AAC56B-CD44-11D0-8CC2-00C04FC295EE");

    public static uint Verify(string path, IntPtr fileHandle)
    {
        var file = new WinTrustFileInfo { Size = (uint)Marshal.SizeOf<WinTrustFileInfo>(), FilePath = path, File = fileHandle };
        var filePtr = Marshal.AllocHGlobal(Marshal.SizeOf<WinTrustFileInfo>());
        Marshal.StructureToPtr(file, filePtr, false);
        var data = new WinTrustData
        {
            Size = (uint)Marshal.SizeOf<WinTrustData>(), UIChoice = 2, RevocationChecks = 0,
            UnionChoice = 1, FileInfo = filePtr, StateAction = 1,
            // No network retrieval; revocation cannot be checked reliably offline.
            ProviderFlags = 0x00001000 | 0x00000010
        };
        var action = GenericVerifyV2;
        try { return WinVerifyTrust(new IntPtr(-1), ref action, ref data); }
        finally
        {
            data.StateAction = 2;
            WinVerifyTrust(new IntPtr(-1), ref action, ref data);
            Marshal.DestroyStructure<WinTrustFileInfo>(filePtr);
            Marshal.FreeHGlobal(filePtr);
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WinTrustFileInfo
    {
        public uint Size;
        [MarshalAs(UnmanagedType.LPWStr)] public string FilePath;
        public IntPtr File;
        public IntPtr KnownSubject;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WinTrustData
    {
        public uint Size;
        public IntPtr PolicyCallbackData;
        public IntPtr SIPClientData;
        public uint UIChoice;
        public uint RevocationChecks;
        public uint UnionChoice;
        public IntPtr FileInfo;
        public uint StateAction;
        public IntPtr StateData;
        public IntPtr URLReference;
        public uint ProviderFlags;
        public uint UIContext;
        public IntPtr SignatureSettings;
    }

    [DllImport("wintrust.dll", ExactSpelling = true, PreserveSig = true)]
    private static extern uint WinVerifyTrust(IntPtr hwnd, ref Guid action, ref WinTrustData data);
}
