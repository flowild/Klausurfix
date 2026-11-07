namespace Klausurfix.Infrastructure.Backup;

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Klausurfix.Core.Models;
using Klausurfix.Infrastructure.Security;
using Klausurfix.Infrastructure.Storage;

public record ManagedBackupInfo
{
    public string FileName { get; init; } = string.Empty;
    public long FileSizeBytes { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
}

public class BackupPayload
{
    public int Version { get; set; } = 1;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public List<Schulform> Schulformen { get; set; } = new();
    public List<ClassGroup> Classes { get; set; } = new();
    public List<Student> Students { get; set; } = new();
    public List<Exam> Exams { get; set; } = new();
}

public class BackupEnvelope
{
    public string Magic { get; set; } = "KLAUSURFIX_BACKUP";
    public int FormatVersion { get; set; } = 1;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public string Salt { get; set; } = string.Empty;
    public string EncryptedPayload { get; set; } = string.Empty;
    public string? RecoverySalt { get; set; }
    public string? RecoveryWrappedDek { get; set; }
}

public class BackupService
{
    private readonly DatabaseContext _dbContext;
    private readonly KeyManager _keyManager;
    private readonly object _automaticGate = new();

    public void ExecuteExclusive(Action action) { lock (_automaticGate) action(); }

    public BackupService(DatabaseContext dbContext, KeyManager keyManager)
    {
        _dbContext = dbContext;
        _keyManager = keyManager;
    }

    public List<ManagedBackupInfo> GetManagedBackups()
    {
        AppPaths.EnsureDirectoriesExist();
        var dir = new DirectoryInfo(AppPaths.BackupsDirectory);
        var files = dir.GetFiles("*.kfbak").OrderByDescending(f => f.CreationTimeUtc).ToList();

        return files.Select(f => new ManagedBackupInfo
        {
            FileName = f.Name,
            FileSizeBytes = f.Length,
            CreatedAt = f.CreationTimeUtc
        }).ToList();
    }

    /// <summary>
    /// Creates a complete encrypted backup payload using the recovery key or custom secret.
    /// </summary>
    public byte[] CreateBackupBytes(string recoveryKey)
    {
        if (string.IsNullOrWhiteSpace(recoveryKey))
            throw new ArgumentException("Wiederherstellungsschlüssel erforderlich.", nameof(recoveryKey));
        if (!_keyManager.VerifyRecoveryKey(recoveryKey))
            throw new UnauthorizedAccessException("Der Wiederherstellungsschlüssel ist nicht korrekt.");

        // 1. Gather all data
        var payload = _dbContext.WithSnapshot(BuildPayload);
        var payloadJson = JsonSerializer.Serialize(payload);
        var payloadBytes = Encoding.UTF8.GetBytes(payloadJson);

        // 2. Derive key from recoveryKey + fresh salt
        var salt = CryptoService.GenerateRandomBytes(CryptoService.SaltSize);
        var key = CryptoService.DeriveKey(recoveryKey.Trim().ToUpperInvariant(), salt);

        // 3. Encrypt payload
        var encrypted = CryptoService.Encrypt(payloadBytes, key);
        CryptoService.ZeroMemory(key);

        var envelope = new BackupEnvelope
        {
            Magic = "KLAUSURFIX_BACKUP",
            FormatVersion = 1,
            CreatedAt = DateTimeOffset.UtcNow,
            Salt = Convert.ToBase64String(salt),
            EncryptedPayload = Convert.ToBase64String(encrypted)
        };

        var envelopeJson = JsonSerializer.Serialize(envelope, new JsonSerializerOptions { WriteIndented = true });
        return Encoding.UTF8.GetBytes(envelopeJson);
    }

    private static BackupPayload BuildPayload(DatabaseContext db)
    {
        var payload = new BackupPayload
        {
            Version = 4, // Includes archive state and editable cover-sheet notices.
            CreatedAt = DateTimeOffset.UtcNow,
            Schulformen = db.GetSchulformen(includeArchived: true),
            Classes = db.GetClasses(includeArchived: true)
        };

        foreach (var cls in payload.Classes)
        {
            var students = db.GetStudentsByClass(cls.Id, includeArchived: true);
            payload.Students.AddRange(students);
        }

        var exams = db.GetExams(includeArchived: true);
        foreach (var ex in exams)
        {
            var fullExam = db.GetExamById(ex.Id);
            if (fullExam != null)
            {
                payload.Exams.Add(fullExam);
            }
        }

        return payload;
    }

    private static readonly byte[] AutomaticBackupInfo = Encoding.UTF8.GetBytes("Klausurfix automatic backup v2");

    public ManagedBackupInfo CreateAutomaticBackup()
    {
        lock (_automaticGate) return WriteManagedBackup(CreateBackupBytesCore(), "automatisch");
    }

    public byte[] CreateBackupBytes()
    {
        lock (_automaticGate) return CreateBackupBytesCore();
    }

    private byte[] CreateBackupBytesCore()
    {
        var (dek, recoverySalt, wrappedDek) = _keyManager.GetBackupKeyMaterial();
        byte[]? key = null;
        byte[]? payloadBytes = null;
        try
        {
            payloadBytes = JsonSerializer.SerializeToUtf8Bytes(_dbContext.WithSnapshot(BuildPayload));
            var salt = CryptoService.GenerateRandomBytes(CryptoService.SaltSize);
            key = HKDF.DeriveKey(HashAlgorithmName.SHA256, dek, CryptoService.KeySize, salt, AutomaticBackupInfo);
            var encrypted = CryptoService.Encrypt(payloadBytes, key);
            var envelope = new BackupEnvelope { FormatVersion = 2, Salt = Convert.ToBase64String(salt),
                RecoverySalt = recoverySalt, RecoveryWrappedDek = wrappedDek, EncryptedPayload = Convert.ToBase64String(encrypted) };
            // Verify authentication before committing a file.
            var verified = CryptoService.Decrypt(encrypted, key);
            CryptoService.ZeroMemory(verified);
            return JsonSerializer.SerializeToUtf8Bytes(envelope);
        }
        finally { CryptoService.ZeroMemory(dek); if (key != null) CryptoService.ZeroMemory(key); if (payloadBytes != null) CryptoService.ZeroMemory(payloadBytes); }
    }

    /// <summary>
    /// Creates and saves a managed backup into AppPaths.BackupsDirectory.
    /// Uses atomic write (writes to temp file, then moves).
    /// </summary>
    public ManagedBackupInfo CreateManagedBackup()
    {
        lock (_automaticGate) return WriteManagedBackup(CreateBackupBytesCore(), "backup");
    }

    // Kept separate from shutdown: a future updater must first coordinate all browser tabs.
    public ManagedBackupInfo CreateUpdateBackup()
    {
        lock (_automaticGate) return WriteManagedBackup(CreateBackupBytesCore(), "update");
    }

    private static ManagedBackupInfo WriteManagedBackup(byte[] backupBytes, string kind)
    {
        AppPaths.EnsureDirectoriesExist();
        var timeStamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd_HHmmss");
        var fileName = $"klausurfix_{kind}_{timeStamp}_{Guid.NewGuid():N}.kfbak";
        var finalPath = Path.Combine(AppPaths.BackupsDirectory, fileName);
        var tempPath = Path.Combine(AppPaths.BackupsDirectory, $"{fileName}.tmp");

        try
        {
            File.WriteAllBytes(tempPath, backupBytes);
            if (!CryptographicOperations.FixedTimeEquals(File.ReadAllBytes(tempPath), backupBytes))
                throw new IOException("Die Sicherungsdatei konnte nicht geprüft werden.");
            File.Move(tempPath, finalPath, overwrite: false);
        }
        finally { if (File.Exists(tempPath)) File.Delete(tempPath); }

        var fi = new FileInfo(finalPath);
        return new ManagedBackupInfo
        {
            FileName = fi.Name,
            FileSizeBytes = fi.Length,
            CreatedAt = fi.CreationTimeUtc
        };
    }

    /// <summary>
    /// Validates and restores a backup from bytes.
    /// Before modifying the database, format, version, and decryption integrity are verified.
    /// Existing data is replaced transactionally.
    /// </summary>
    public void RestoreBackup(byte[] backupBytes, string recoveryKey)
    {
        _dbContext.ReplaceFromBackup(ReadBackupPayload(backupBytes, recoveryKey));
    }

    /// <summary>Checks decryption and supported payload structure without changing the database.</summary>
    public static void VerifyBackup(byte[] backupBytes, string recoveryKey)
        => _ = ReadBackupPayload(backupBytes, recoveryKey);

    private static BackupPayload ReadBackupPayload(byte[] backupBytes, string recoveryKey)
    {
        if (string.IsNullOrWhiteSpace(recoveryKey))
            throw new ArgumentException("Wiederherstellungsschlüssel erforderlich.", nameof(recoveryKey));

        // 1. Parse envelope
        var envelopeJson = Encoding.UTF8.GetString(backupBytes);
        var envelope = JsonSerializer.Deserialize<BackupEnvelope>(envelopeJson)
            ?? throw new InvalidOperationException("Ungültiges Sicherungsdateiformat.");

        if (envelope.Magic != "KLAUSURFIX_BACKUP")
            throw new InvalidOperationException("Die Datei ist keine gültige Klausurfix-Sicherung.");

        if (envelope.FormatVersion is < 1 or > 2)
            throw new InvalidOperationException($"Nicht unterstützte Sicherungsformatversion: {envelope.FormatVersion}.");

        // 2. Decrypt payload
        var salt = Convert.FromBase64String(envelope.Salt);
        byte[]? key = null;
        byte[] decryptedBytes;
        try
        {
            if (envelope.FormatVersion == 1) key = CryptoService.DeriveKey(recoveryKey.Trim().ToUpperInvariant(), salt);
            else
            {
                var recoveryKek = CryptoService.DeriveKey(recoveryKey.Trim().ToUpperInvariant(), Convert.FromBase64String(envelope.RecoverySalt!));
                byte[]? backupDek = null;
                try
                {
                    backupDek = CryptoService.Decrypt(Convert.FromBase64String(envelope.RecoveryWrappedDek!), recoveryKek);
                    key = HKDF.DeriveKey(HashAlgorithmName.SHA256, backupDek, CryptoService.KeySize, salt, AutomaticBackupInfo);
                }
                finally { CryptoService.ZeroMemory(recoveryKek); if (backupDek != null) CryptoService.ZeroMemory(backupDek); }
            }
            var packedEncrypted = Convert.FromBase64String(envelope.EncryptedPayload);
            decryptedBytes = CryptoService.Decrypt(packedEncrypted, key);
        }
        catch (CryptographicException)
        {
            throw new UnauthorizedAccessException("Wiederherstellung fehlgeschlagen: Der Wiederherstellungsschlüssel ist falsch oder die Datei wurde manipuliert.");
        }
        finally
        {
            if (key != null) CryptoService.ZeroMemory(key);
        }

        // 3. Deserialize and validate payload
        try
        {
            var payload = JsonSerializer.Deserialize<BackupPayload>(decryptedBytes)
                ?? throw new InvalidOperationException("Sicherungsdaten konnten nicht gelesen werden.");
            if (payload.Version is not (1 or 2 or 3 or 4) || payload.Schulformen == null || payload.Classes == null ||
                payload.Students == null || payload.Exams == null)
                throw new InvalidOperationException("Ungültige Sicherungsdaten.");
            foreach (var school in payload.Schulformen) school.Profile.Validate();
            return payload;
        }
        finally { CryptoService.ZeroMemory(decryptedBytes); }
    }

    public void RestoreManagedBackup(string fileName, string recoveryKey)
    {
        // Path traversal guard
        var safeFileName = Path.GetFileName(fileName);
        var filePath = Path.Combine(AppPaths.BackupsDirectory, safeFileName);
        if (!File.Exists(filePath))
            throw new FileNotFoundException($"Sicherungsdatei '{safeFileName}' nicht gefunden.");

        var bytes = File.ReadAllBytes(filePath);
        RestoreBackup(bytes, recoveryKey);
    }

    public void DeleteManagedBackup(string fileName)
    {
        var safeFileName = Path.GetFileName(fileName);
        var filePath = Path.Combine(AppPaths.BackupsDirectory, safeFileName);
        if (File.Exists(filePath))
        {
            File.Delete(filePath);
        }
    }

    public void DeleteAllManagedBackups()
    {
        AppPaths.EnsureDirectoriesExist();
        var dir = new DirectoryInfo(AppPaths.BackupsDirectory);
        foreach (var file in dir.GetFiles("*.kfbak"))
        {
            file.Delete();
        }
    }
}

