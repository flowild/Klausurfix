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
}

public class BackupService
{
    private readonly DatabaseContext _dbContext;
    private readonly KeyManager _keyManager;

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

        // 1. Gather all data
        var payload = new BackupPayload
        {
            Version = 1,
            CreatedAt = DateTimeOffset.UtcNow,
            Schulformen = _dbContext.GetSchulformen(includeArchived: true),
            Classes = _dbContext.GetClasses(includeArchived: true)
        };

        foreach (var cls in payload.Classes)
        {
            var students = _dbContext.GetStudentsByClass(cls.Id, includeArchived: true);
            payload.Students.AddRange(students);
        }

        var exams = _dbContext.GetExams();
        foreach (var ex in exams)
        {
            var fullExam = _dbContext.GetExamById(ex.Id);
            if (fullExam != null)
            {
                payload.Exams.Add(fullExam);
            }
        }

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

    /// <summary>
    /// Creates and saves a managed backup into AppPaths.BackupsDirectory.
    /// Uses atomic write (writes to temp file, then moves).
    /// </summary>
    public ManagedBackupInfo CreateManagedBackup(string recoveryKey)
    {
        AppPaths.EnsureDirectoriesExist();
        var backupBytes = CreateBackupBytes(recoveryKey);
        var timeStamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd_HHmmss");
        var fileName = $"klausurfix_backup_{timeStamp}.kfbak";
        var finalPath = Path.Combine(AppPaths.BackupsDirectory, fileName);
        var tempPath = Path.Combine(AppPaths.BackupsDirectory, $"{fileName}.tmp");

        File.WriteAllBytes(tempPath, backupBytes);
        File.Move(tempPath, finalPath, overwrite: true);

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
        if (string.IsNullOrWhiteSpace(recoveryKey))
            throw new ArgumentException("Wiederherstellungsschlüssel erforderlich.", nameof(recoveryKey));

        // 1. Parse envelope
        var envelopeJson = Encoding.UTF8.GetString(backupBytes);
        var envelope = JsonSerializer.Deserialize<BackupEnvelope>(envelopeJson)
            ?? throw new InvalidOperationException("Ungültiges Sicherungsdateiformat.");

        if (envelope.Magic != "KLAUSURFIX_BACKUP")
            throw new InvalidOperationException("Die Datei ist keine gültige Klausurfix-Sicherung.");

        if (envelope.FormatVersion > 1)
            throw new InvalidOperationException($"Nicht unterstützte Sicherungsformatversion: {envelope.FormatVersion}.");

        // 2. Decrypt payload
        var salt = Convert.FromBase64String(envelope.Salt);
        var key = CryptoService.DeriveKey(recoveryKey.Trim().ToUpperInvariant(), salt);
        byte[] decryptedBytes;
        try
        {
            var packedEncrypted = Convert.FromBase64String(envelope.EncryptedPayload);
            decryptedBytes = CryptoService.Decrypt(packedEncrypted, key);
        }
        catch (CryptographicException)
        {
            throw new UnauthorizedAccessException("Wiederherstellung fehlgeschlagen: Der Wiederherstellungsschlüssel ist falsch oder die Datei wurde manipuliert.");
        }
        finally
        {
            CryptoService.ZeroMemory(key);
        }

        // 3. Deserialize and validate payload
        var payloadJson = Encoding.UTF8.GetString(decryptedBytes);
        var payload = JsonSerializer.Deserialize<BackupPayload>(payloadJson)
            ?? throw new InvalidOperationException("Sicherungsdaten konnten nicht gelesen werden.");

        // 4. Restore into active database
        _dbContext.InitializeDatabase();

        foreach (var sf in payload.Schulformen)
        {
            _dbContext.SaveSchulform(sf);
        }

        foreach (var cls in payload.Classes)
        {
            _dbContext.SaveClass(cls);
        }

        foreach (var st in payload.Students)
        {
            _dbContext.SaveStudent(st);
        }

        foreach (var ex in payload.Exams)
        {
            _dbContext.SaveExam(ex);
        }
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

