namespace Klausurfix.Tests;

using Klausurfix.Core.Models;
using Klausurfix.Infrastructure.Backup;
using Klausurfix.Infrastructure.Security;
using Klausurfix.Infrastructure.Storage;
using Xunit;

public class KeyManagerAndBackupTests : IDisposable
{
    private readonly string _testDir;
    private readonly KeyManager _keyManager;
    private readonly DatabaseContext _dbContext;
    private readonly BackupService _backupService;

    public KeyManagerAndBackupTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "KlausurfixTests_" + Guid.NewGuid().ToString("N"));
        AppPaths.SetCustomBaseDirectory(_testDir);

        _keyManager = new KeyManager();
        _dbContext = new DatabaseContext(_keyManager);
        _backupService = new BackupService(_dbContext, _keyManager);
    }

    public void Dispose()
    {
        _keyManager.Lock();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            if (Directory.Exists(_testDir))
            {
                Directory.Delete(_testDir, recursive: true);
            }
        }
        catch { }
        AppPaths.SetCustomBaseDirectory(null);
    }

    [Fact]
    public void SetupAndUnlock_WithPin_Succeeds()
    {
        Assert.False(_keyManager.IsInitialized);
        Assert.False(_keyManager.IsUnlocked);

        var recoveryKey = KeyManager.GenerateRecoveryKey();
        _keyManager.Setup("123456", recoveryKey, inactivityTimeoutMinutes: 15);

        Assert.True(_keyManager.IsInitialized);
        Assert.True(_keyManager.IsUnlocked);

        // Lock
        _keyManager.Lock();
        Assert.False(_keyManager.IsUnlocked);

        // Unlock with correct PIN
        var result = _keyManager.UnlockWithPin("123456");
        Assert.True(result.Success);
        Assert.True(_keyManager.IsUnlocked);
    }

    [Fact]
    public void Unlock_WithWrongPin_TracksFailedAttemptsAndLocksOut()
    {
        var recoveryKey = KeyManager.GenerateRecoveryKey();
        _keyManager.Setup("123456", recoveryKey);
        _keyManager.Lock();

        // 1st wrong attempt
        var r1 = _keyManager.UnlockWithPin("000000");
        Assert.False(r1.Success);
        Assert.Null(r1.LockoutSeconds);

        // 2nd wrong attempt
        var r2 = _keyManager.UnlockWithPin("000000");
        Assert.False(r2.Success);
        Assert.Null(r2.LockoutSeconds);

        // 3rd wrong attempt -> triggers 3s lockout
        var r3 = _keyManager.UnlockWithPin("000000");
        Assert.False(r3.Success);
        Assert.Equal(3, r3.LockoutSeconds);

        // Immediate next attempt blocked by timer
        var rBlocked = _keyManager.UnlockWithPin("123456");
        Assert.False(rBlocked.Success);
        Assert.NotNull(rBlocked.LockoutSeconds);
    }

    [Fact]
    public void Unlock_WithRecoveryKey_SucceedsEvenWhenLocked()
    {
        var recoveryKey = KeyManager.GenerateRecoveryKey();
        _keyManager.Setup("123456", recoveryKey);
        _keyManager.Lock();

        var recResult = _keyManager.UnlockWithRecoveryKey(recoveryKey);
        Assert.True(recResult.Success);
        Assert.True(_keyManager.IsUnlocked);
    }

    [Fact]
    public void BackupAndRestore_RoundTrip_PreservesAllData()
    {
        var recoveryKey = KeyManager.GenerateRecoveryKey();
        _keyManager.Setup("123456", recoveryKey);
        _dbContext.InitializeDatabase();

        // Save Schulform
        var sf = new Schulform
        {
            Name = "Gymnasium",
            Profile = GradingProfile.CreateDefaultSchulnoten()
        };
        _dbContext.SaveSchulform(sf);

        // Save Class
        var cls = new ClassGroup
        {
            Name = "9a",
            SchulformId = sf.Id,
            SchulformName = sf.Name
        };
        _dbContext.SaveClass(cls);

        // Save Student
        var st = new Student
        {
            ClassId = cls.Id,
            FirstName = "Anna",
            LastName = "Schmidt",
            OrderIndex = 1
        };
        _dbContext.SaveStudent(st);

        // Save Exam
        var exam = new Exam
        {
            ClassId = cls.Id,
            ClassName = cls.Name,
            SchulformId = sf.Id,
            SchulformName = sf.Name,
            Title = "1. Schulaufgabe",
            Date = new DateOnly(2026, 11, 1),
            Tasks = new()
            {
                new TaskDefinition(1, "Textanalyse", 30m),
                new TaskDefinition(2, "Erörterung", 20m)
            },
            GradingProfileSnapshot = sf.Profile,
            StudentEntries = new()
            {
                new ExamStudentEntry
                {
                    StudentId = st.Id,
                    StudentName = st.FullName,
                    Status = StudentExamStatus.Graded,
                    TaskPoints = new() { { 1, 28m }, { 2, 18m } },
                    WordCount = 450,
                    ErrorCount = 2
                }
            }
        };
        _dbContext.SaveExam(exam);

        // Create backup
        var backupBytes = _backupService.CreateBackupBytes(recoveryKey);
        Assert.NotNull(backupBytes);
        Assert.True(backupBytes.Length > 0);

        // Modify/Delete active data
        _dbContext.DeleteExam(exam.Id);
        _dbContext.DeleteStudent(st.Id);
        Assert.Empty(_dbContext.GetExams());

        // Restore backup
        _backupService.RestoreBackup(backupBytes, recoveryKey);

        // Verify restoration
        var restoredExams = _dbContext.GetExams();
        Assert.Single(restoredExams);
        Assert.Equal("1. Schulaufgabe", restoredExams[0].Title);

        var restoredExam = _dbContext.GetExamById(restoredExams[0].Id);
        Assert.NotNull(restoredExam);
        Assert.Single(restoredExam.StudentEntries);
        Assert.Equal(st.FullName, restoredExam.StudentEntries[0].StudentName);
        Assert.Equal(28m, restoredExam.StudentEntries[0].TaskPoints[1]);
    }
}

