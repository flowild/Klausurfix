using Klausurfix.Infrastructure.Backup;

/// <summary>Prepares a manually stopped installation and then switches and starts it.</summary>
public static class UpdateWorkflow
{
    public static StagedVersion Install(Stream package, string installationDirectory,
        string architecture, byte[] backup, string recoveryKey)
        => Run(package, installationDirectory, architecture, backup, recoveryKey,
            StoppedServiceGuard.EnsureStopped, BackupService.VerifyBackup, StoppedVersionInstaller.Install);

    public static StagedVersion Install(Stream package, string installationDirectory,
        string architecture, byte[] backup, string recoveryKey, bool allowSameVersion)
        => Run(package, installationDirectory, architecture, backup, recoveryKey,
            StoppedServiceGuard.EnsureStopped, BackupService.VerifyBackup,
            (stream, root, arch) => StoppedVersionInstaller.Install(stream, root, arch, allowSameVersion));

    public static StagedVersion InstallAndStart(Stream package, string installationDirectory,
        string architecture, byte[] backup, string recoveryKey)
        => Run(package, installationDirectory, architecture, backup, recoveryKey,
            StoppedServiceGuard.EnsureStopped, BackupService.VerifyBackup, StoppedVersionInstaller.InstallAndStart);

    internal static StagedVersion Run(Stream package, string installationDirectory,
        string architecture, byte[] backup, string recoveryKey, Action ensureStopped, Action<byte[], string> verifyBackup,
        Func<Stream, string, string, StagedVersion> installAndStart)
    {
        ensureStopped();
        verifyBackup(backup, recoveryKey);
        return installAndStart(package, installationDirectory, architecture);
    }
}
