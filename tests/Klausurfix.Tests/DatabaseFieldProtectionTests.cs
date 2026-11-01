namespace Klausurfix.Tests;

using System.Security.Cryptography;
using System.Text.Json;
using Klausurfix.Core.Models;
using Klausurfix.Infrastructure.Backup;
using Klausurfix.Infrastructure.Export;
using Klausurfix.Infrastructure.Security;
using Klausurfix.Infrastructure.Storage;
using Microsoft.Data.Sqlite;

public sealed class DatabaseFieldProtectionTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "KlausurfixFields_" + Guid.NewGuid().ToString("N"));
    private readonly KeyManager _keys = new(new TestKeyProtector());
    private readonly DatabaseContext _db;
    private readonly string _recovery = KeyManager.GenerateRecoveryKey();

    public DatabaseFieldProtectionTests()
    {
        AppPaths.SetCustomBaseDirectory(_directory);
        _keys.Setup("123456", _recovery); // Short PINs deliberately remain supported.
        _db = new DatabaseContext(_keys);
        _db.InitializeDatabase();
        foreach (var id in new[] { "a", "b" })
        {
            _db.SaveSchulform(new Schulform { Id = id, Name = "Testschulform " + id, Profile = GradingProfile.BeruflichesGymnasium() });
            _db.SaveClass(new ClassGroup { Id = id, Name = "Testklasse " + id, SchulformId = id, SchulformName = "Testschulform " + id });
            _db.SaveStudent(new Student { Id = id, ClassId = "a", FirstName = "Testvorname " + id, LastName = "Testnachname " + id });
            var exam = new Exam
            {
                Id = id, Title = "Testklausur " + id, ClassId = "a", ClassName = "Testklasse a",
                SchulformId = "a", SchulformName = "Testschulform a", Tasks = [new(1, "Testaufgabe", 10m)],
                GradingProfileSnapshot = GradingProfile.BeruflichesGymnasium(),
                StudentEntries = [new() { StudentId = "a", StudentName = "Testperson A", TaskPoints = new() { [1] = 7.25m }, WordCount = 600, ErrorCount = 12, Note = "Testnotiz A" },
                    new() { StudentId = "b", StudentName = "Testperson B", TaskPoints = new() { [1] = null }, Status = StudentExamStatus.NotParticipated, Note = "Testnotiz B" }]
            };
            _db.SaveExam(exam with { CoverSheet = CoverSheetDocument.Defaults(exam) with { Instructions = "Testhinweis " + id,
                Tasks = [new(1, "Erklären Sie den Testtext.", 2.5m)] } });
        }
        _db.SetExamArchived("b", true);
    }

    public static IEnumerable<object[]> Fields() => LegacyDatabaseFixture.Tables.SelectMany(t => t.Fields.Select(f => new object[] { t.Table, f }));

    private object? Sql(string sql, params (string Name, object Value)[] parameters)
    {
        using var connection = new SqliteConnection($"Data Source={AppPaths.DatabaseFile}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value);
        return command.ExecuteScalar();
    }

    private void Read(string table, string id)
    {
        switch (table)
        {
            case "schulformen": _db.GetSchulformById(id); break;
            case "classes": _db.GetClassById(id); break;
            case "students": _db.GetStudentsByClass("a", true); break;
            default: _db.GetExamById(id); break;
        }
    }

    [Theory]
    [MemberData(nameof(Fields))]
    public void CopyingCiphertextToAnotherRecordIsRejected(string table, string column)
    {
        var key = table.StartsWith("exam_", StringComparison.Ordinal) ? "exam_id" : "id";
        var extra = table == "exam_entries" ? " AND student_id='a'" : "";
        Sql($"UPDATE {table} SET {column}=(SELECT {column} FROM {table} WHERE {key}='a'{extra}) WHERE {key}='b'{extra}");
        Assert.ThrowsAny<CryptographicException>(() => Read(table, "b"));
        if (table == "schulformen") Assert.ThrowsAny<CryptographicException>(() => _db.GetSchulformen(true));
        if (table == "classes") Assert.ThrowsAny<CryptographicException>(() => _db.GetClasses(true));
        if (table == "exams") Assert.ThrowsAny<CryptographicException>(() => _db.GetExams(true));
    }

    [Theory]
    [InlineData("student_name_enc")]
    [InlineData("task_points_enc")]
    [InlineData("note_enc")]
    public void EntriesAreAlsoBoundToStudentId(string column)
    {
        Sql($"UPDATE exam_entries SET {column}=(SELECT {column} FROM exam_entries WHERE exam_id='a' AND student_id='a') WHERE exam_id='a' AND student_id='b'");
        Assert.ThrowsAny<CryptographicException>(() => _db.GetExamById("a"));
    }

    [Theory]
    [InlineData("students", "first_name_enc", "students", "last_name_enc")]
    [InlineData("schulformen", "name", "classes", "name")]
    [InlineData("exams", "title", "exams", "class_name")]
    [InlineData("exams", "profile_snapshot_json", "schulformen", "profile_json_enc")]
    public void CopyingBetweenColumnsOrTablesIsRejected(string sourceTable, string sourceColumn, string table, string column)
    {
        Sql($"UPDATE {table} SET {column}=(SELECT {sourceColumn} FROM {sourceTable} WHERE id='a') WHERE id='a'");
        Assert.ThrowsAny<CryptographicException>(() => Read(table, "a"));
    }

    [Theory]
    [InlineData("plaintext")]
    [InlineData("empty")]
    [InlineData("unbound")]
    [InlineData("stripped")]
    [InlineData("version")]
    public void FormatDowngradesAreRejectedAfterMigration(string attack)
    {
        var original = (string)Sql("SELECT first_name_enc FROM students WHERE id='a'")!;
        var key = _keys.GetActiveDek();
        string replacement;
        try
        {
            replacement = attack switch
            {
                "plaintext" => "Eingeschleuster Testname", "empty" => "",
                "unbound" => CryptoService.EncryptString("Eingeschleuster Testname", key),
                "stripped" => original[4..], _ => "kf1:" + original[4..]
            };
        }
        finally { CryptoService.ZeroMemory(key); }
        Sql("UPDATE students SET first_name_enc=$value WHERE id='a'", ("$value", replacement));
        _db.InitializeDatabase();
        Assert.ThrowsAny<CryptographicException>(() => _db.GetStudentsByClass("a"));
        // Removing the migration markers must not turn mixed formats into an upgrade.
        Sql("DELETE FROM app_migrations WHERE name='database_fields_aad_v2'; PRAGMA user_version=2");
        Assert.ThrowsAny<CryptographicException>(() => _db.InitializeDatabase());
    }

    private string LogicalData() => JsonSerializer.Serialize(new
    {
        Schools = _db.GetSchulformen(true), Classes = _db.GetClasses(true), Students = _db.GetStudentsByClass("a", true),
        Exams = new[] { _db.GetExamById("a"), _db.GetExamById("b") }
    });

    [Fact]
    public void LegacyMigrationPreservesAllDataAndIsIdempotent()
    {
        var before = LogicalData();
        LegacyDatabaseFixture.Downgrade(_keys.GetActiveDek());
        _db.InitializeDatabase();
        Assert.Equal(before, LogicalData());
        var ciphertext = (string)Sql("SELECT title FROM exams WHERE id='a'")!;
        Assert.StartsWith("kf2:", ciphertext);
        _db.InitializeDatabase();
        Assert.Equal(ciphertext, Sql("SELECT title FROM exams WHERE id='a'"));
        Assert.Equal(4L, Sql("PRAGMA user_version"));
        Assert.Throws<ArchivedExamReadOnlyException>(() => _db.SaveExam(_db.GetExamById("b")!));
    }

    [Fact]
    public void MigrationFailureLeavesLegacyRowsAndVersionUnchanged()
    {
        LegacyDatabaseFixture.Downgrade(_keys.GetActiveDek());
        var original = (string)Sql("SELECT name FROM schulformen WHERE id='a'")!;
        var damaged = Convert.FromBase64String((string)Sql("SELECT document_enc FROM exam_cover_sheets WHERE exam_id='b'")!);
        damaged[^1] ^= 1;
        Sql("UPDATE exam_cover_sheets SET document_enc=$value WHERE exam_id='b'", ("$value", Convert.ToBase64String(damaged)));
        Assert.ThrowsAny<CryptographicException>(() => _db.InitializeDatabase());
        Assert.Equal(original, Sql("SELECT name FROM schulformen WHERE id='a'"));
        Assert.Equal(2L, Sql("PRAGMA user_version"));
        Assert.Equal(0L, Sql("SELECT COUNT(*) FROM app_migrations WHERE name='database_fields_aad_v2'"));
    }

    [Fact]
    public void InterruptedCleanupResumesWithoutChangingSnapshots()
    {
        var before = LogicalData();
        Sql("PRAGMA user_version=3");
        _db.InitializeDatabase();
        Assert.Equal(before, LogicalData());
        Assert.Equal(4L, Sql("PRAGMA user_version"));
    }

    [Fact]
    public void FailedMigrationWriteRollsBackAndCanBeRetried()
    {
        var before = LogicalData();
        LegacyDatabaseFixture.Downgrade(_keys.GetActiveDek());
        var encrypted = Sql("SELECT name FROM schulformen WHERE id='a'");
        Sql("CREATE TRIGGER reject_migration BEFORE UPDATE ON exam_cover_sheets BEGIN SELECT RAISE(ABORT,'synthetic failure'); END");
        Assert.Throws<SqliteException>(() => _db.InitializeDatabase());
        Assert.Equal(encrypted, Sql("SELECT name FROM schulformen WHERE id='a'"));
        Assert.Equal(2L, Sql("PRAGMA user_version"));
        Assert.Equal(0L, Sql("SELECT COUNT(*) FROM app_migrations WHERE name='database_fields_aad_v2'"));
        Sql("DROP TRIGGER reject_migration");
        _db.InitializeDatabase();
        Assert.Equal(before, LogicalData());
    }

    [Fact]
    public void BackupRestoresBoundFieldsAndRejectsSubstitutionAfterwards()
    {
        var before = LogicalData();
        var service = new BackupService(_db, _keys);
        var backup = service.CreateBackupBytes(_recovery);
        _db.ReplaceFromBackup(new BackupPayload { Version = 4 });
        service.RestoreBackup(backup, _recovery);
        Assert.Equal(before, LogicalData());
        Sql("UPDATE students SET first_name_enc=(SELECT first_name_enc FROM students WHERE id='a') WHERE id='b'");
        Assert.ThrowsAny<CryptographicException>(() => _db.GetStudentsByClass("a"));
    }

    [Fact]
    public void EmptyNamesHaveAuthenticatedCiphertext()
    {
        _db.SaveStudent(new Student { Id = "empty", ClassId = "a", FirstName = "", LastName = "" });
        Assert.Equal("", _db.GetStudentsByClass("a").Single(s => s.Id == "empty").FirstName);
        var encrypted = (string)Sql("SELECT first_name_enc FROM students WHERE id='empty'")!;
        Assert.StartsWith("kf2:", encrypted);
        Sql("UPDATE students SET last_name_enc=$value WHERE id='empty'", ("$value", encrypted));
        Assert.ThrowsAny<CryptographicException>(() => _db.GetStudentsByClass("a"));
    }

    public void Dispose()
    {
        _keys.Lock();
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
        AppPaths.SetCustomBaseDirectory(null);
    }
}
