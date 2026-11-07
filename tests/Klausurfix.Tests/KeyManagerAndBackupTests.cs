namespace Klausurfix.Tests;

using Klausurfix.Core.Models;
using Klausurfix.Infrastructure.Backup;
using Klausurfix.Infrastructure.Security;
using Klausurfix.Infrastructure.Storage;
using Xunit;
using System.Text.Json;
using Microsoft.Data.Sqlite;

public class KeyManagerAndBackupTests : IDisposable
{
    [Fact]
    public void UpdateWorkflowVerifiesRealBackupAndActivatesVersion()
    {
        var recovery = KeyManager.GenerateRecoveryKey();
        _keyManager.Setup("123456", recovery);
        _dbContext.InitializeDatabase();
        var backup = _backupService.CreateBackupBytes();
        var architecture = System.Runtime.InteropServices.RuntimeInformation.OSArchitecture == System.Runtime.InteropServices.Architecture.Arm64 ? "win-arm64" : "win-x64";
        var installation = Path.Combine(_testDir, "installation");
        Directory.CreateDirectory(installation);
        File.WriteAllText(Path.Combine(installation, "Klausurfix.exe"), "synthetisch");
        File.WriteAllText(Path.Combine(installation, "update-starter-v1.txt"), "synthetisch");
        File.WriteAllText(Path.Combine(installation, "basis-version.txt"), "0.1.5|" + architecture);
        var packagePath = VersionStagerTests.CreatePackage(_testDir, architecture);
        using var package = File.OpenRead(packagePath);
        var started = false;
        var result = UpdateWorkflow.Run(package, installation, architecture,
            backup, recovery, () => { }, BackupService.VerifyBackup,
            (stream, root, arch) => StoppedVersionInstaller.InstallCore(stream, root, arch, () => { }, _ => started = true));
        Assert.True(started);
        Assert.Equal("0.2.0", result.Version);
        Assert.Equal(Path.Combine(result.Directory, "app", "Klausurfix.Web.exe"), ActiveVersion.ResolveBackend(installation));
    }

    [Fact]
    public void AutomaticBackup_RestoresAfterNewSetupAndRejectsWrongKeyAndTampering()
    {
        var originalRecovery = KeyManager.GenerateRecoveryKey();
        _keyManager.Setup("123456", originalRecovery);
        _dbContext.InitializeDatabase();
        var school = new Schulform { Name = "Synthetisch Äöü", Profile = GradingProfile.FachschuleSozialwesen() };
        _dbContext.SaveSchulform(school);
        var file = _backupService.CreateAutomaticBackup();
        var bytes = File.ReadAllBytes(Path.Combine(AppPaths.BackupsDirectory, file.FileName));
        Assert.DoesNotContain("Synthetisch", System.Text.Encoding.UTF8.GetString(bytes));
        BackupService.VerifyBackup(bytes, originalRecovery);
        Assert.Throws<UnauthorizedAccessException>(() => BackupService.VerifyBackup(bytes, KeyManager.GenerateRecoveryKey()));
        Assert.Contains(_dbContext.GetSchulformen(), s => s.Name == school.Name);
        var damaged = JsonSerializer.Deserialize<BackupEnvelope>(bytes)!;
        var ciphertext = Convert.FromBase64String(damaged.EncryptedPayload);
        ciphertext[^1] ^= 1;
        damaged.EncryptedPayload = Convert.ToBase64String(ciphertext);
        Assert.Throws<UnauthorizedAccessException>(() => BackupService.VerifyBackup(JsonSerializer.SerializeToUtf8Bytes(damaged), originalRecovery));
        Assert.Throws<UnauthorizedAccessException>(() => _backupService.RestoreBackup(JsonSerializer.SerializeToUtf8Bytes(damaged), originalRecovery));
        Assert.Throws<UnauthorizedAccessException>(() => _backupService.RestoreBackup(bytes, KeyManager.GenerateRecoveryKey()));
        Assert.Contains(_dbContext.GetSchulformen(), s => s.Name == school.Name);
        _keyManager.Lock();
        Assert.Throws<InvalidOperationException>(() => _backupService.CreateAutomaticBackup());
        _keyManager.Setup("654321", KeyManager.GenerateRecoveryKey());
        _dbContext.InitializeDatabase();
        _backupService.RestoreBackup(bytes, originalRecovery);
        Assert.Contains(_dbContext.GetSchulformen(), s => s.Name == school.Name);
    }

    [Fact]
    public async Task AutomaticBackupSchedule_SkipsLockedAndDoesNotRepeatWithinOneDay()
    {
        var gate = new UpdateWriteGate();
        var worker = new AutomaticBackupWorker(_backupService, _keyManager, gate);
        Assert.False(await worker.RunIfDueAsync());
        _keyManager.Setup("123456", KeyManager.GenerateRecoveryKey());
        _dbContext.InitializeDatabase();
        worker.SetReady(true);
        _keyManager.SetAutomaticBackupSettings(false, 6);
        Assert.False(await worker.RunIfDueAsync());
        Assert.Empty(_backupService.GetManagedBackups());
        var reloaded = new KeyManager(new TestKeyProtector());
        Assert.Equal((false, 6), reloaded.GetAutomaticBackupSettings());
        _keyManager.SetAutomaticBackupSettings(true, 24);
        using (var freeze = await gate.FreezeAsync())
        {
            Assert.False(await worker.RunIfDueAsync());
            Assert.Empty(_backupService.GetManagedBackups());
        }
        Assert.True(await worker.RunIfDueAsync());
        Assert.False(await worker.RunIfDueAsync());
        Assert.Single(_backupService.GetManagedBackups());
        _backupService.DeleteAllManagedBackups();
        Assert.False(await worker.RunIfDueAsync());
        _keyManager.Lock();
        Assert.False(await worker.RunIfDueAsync());
    }

    [Fact]
    public void LargeDataset_WithUnicode_RestoresClassesStudentsAndExams()
    {
        var recovery = KeyManager.GenerateRecoveryKey();
        _keyManager.Setup("123456", recovery);
        _dbContext.InitializeDatabase();
        var school = new Schulform { Name = "Schulform ÄÖÜ ß & < >", Profile = GradingProfile.FachschuleSozialwesen() };
        _dbContext.SaveSchulform(school);
        const int classCount = 20, studentCount = 30, examsPerClass = 4;
        for (int c = 0; c < classCount; c++)
        {
            var cls = new ClassGroup { Name = $"Klässe {c} Ö", SchulformId = school.Id, SchulformName = school.Name };
            _dbContext.SaveClass(cls);
            var entries = new List<ExamStudentEntry>();
            for (int s = 0; s < studentCount; s++)
            {
                var student = new Student { ClassId = cls.Id, FirstName = "Änne", LastName = $"Öztürk ß {s}", OrderIndex = s };
                _dbContext.SaveStudent(student);
                entries.Add(new ExamStudentEntry { StudentId = student.Id, StudentName = student.FullName,
                    Status = StudentExamStatus.Graded, TaskPoints = new() { [1] = 7.5m }, WordCount = 200, ErrorCount = 2 });
            }
            for (int e = 0; e < examsPerClass; e++)
                _dbContext.SaveExam(new Exam { ClassId = cls.Id, ClassName = cls.Name, SchulformId = school.Id,
                    SchulformName = school.Name, Title = $"Prüfung {c}-{e} Ä & Ö", Tasks = new() { new(1, "Übung", 10m) },
                    GradingProfileSnapshot = school.Profile, StudentEntries = entries });
        }
        var backup = _backupService.CreateBackupBytes(recovery);
        Assert.DoesNotContain("Öztürk", System.Text.Encoding.UTF8.GetString(backup));
        _backupService.RestoreBackup(backup, recovery);
        var classes = _dbContext.GetClasses();
        Assert.Equal(classCount, classes.Count);
        Assert.Equal(classCount * studentCount, classes.Sum(c => _dbContext.GetStudentsByClass(c.Id).Count));
        var exams = _dbContext.GetExams();
        Assert.Equal(classCount * examsPerClass, exams.Count);
        var restored = _dbContext.GetExamById(exams[0].Id)!;
        Assert.Equal(studentCount, restored.StudentEntries.Count);
        Assert.Contains("Öztürk ß", restored.StudentEntries[0].StudentName);
        Assert.Contains("Prüfung", restored.Title);
    }

    [Fact]
    public async Task HelloRegistrationAndUnlockRequireValidSignedResponses()
    {
        _keyManager.Setup("123456", KeyManager.GenerateRecoveryKey());
        var original = _keyManager.GetActiveDek();
        var ceremonies = new HelloCeremonies(_keyManager);
        using var key = System.Security.Cryptography.ECDsa.Create(System.Security.Cryptography.ECCurve.NamedCurves.nistP256);
        var id = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);
        var prf = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);
        var (registrationToken, registrationOptions) = ceremonies.BeginRegistration("session");
        var attestation = CreateAttestation(key, id, registrationOptions.Challenge);
        var (confirmationToken, confirmationOptions, _) = await ceremonies.CompleteRegistrationAsync(registrationToken, "session", attestation);
        var confirmation = CreateAssertion(key, id, confirmationOptions.Challenge, 1);
        await ceremonies.ConfirmRegistrationAsync(confirmationToken, "session", confirmation, prf);
        Assert.True(_keyManager.IsHelloRegistered);
        _keyManager.Lock();

        var (invalidToken, invalidOptions, _) = ceremonies.BeginUnlock("browser");
        var wrongOrigin = CreateAssertion(key, id, invalidOptions.Challenge, 2, "http://attacker.example");
        await Assert.ThrowsAnyAsync<Exception>(() => ceremonies.CompleteUnlockAsync(invalidToken, "browser", wrongOrigin, prf));
        Assert.False(_keyManager.IsUnlocked);

        var (token, options, _) = ceremonies.BeginUnlock("browser");
        var response = CreateAssertion(key, id, options.Challenge, 2);
        await ceremonies.CompleteUnlockAsync(token, "browser", response, prf);
        Assert.Equal(original, _keyManager.GetActiveDek());
        _keyManager.Lock();
        await Assert.ThrowsAnyAsync<Exception>(() => ceremonies.CompleteUnlockAsync(token, "browser", response, prf));
        Assert.False(_keyManager.IsUnlocked);
    }

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] ClientData(string type, byte[] challenge, string origin = "http://localhost:5000") =>
        System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new { type, challenge = Base64Url(challenge), origin, crossOrigin = false });

    internal static Fido2NetLib.AuthenticatorAttestationRawResponse CreateAttestation(System.Security.Cryptography.ECDsa key, byte[] id, byte[] challenge)
    {
        var pub = key.ExportParameters(false).Q;
        byte[] cose = new byte[] { 0xa5, 0x01, 0x02, 0x03, 0x26, 0x20, 0x01, 0x21, 0x58, 0x20 }
            .Concat(pub.X!).Concat(new byte[] { 0x22, 0x58, 0x20 }).Concat(pub.Y!).ToArray();
        var authData = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes("localhost"))
            .Concat(new byte[] { 0x45, 0, 0, 0, 0 }).Concat(new byte[16])
            .Concat(new byte[] { 0, (byte)id.Length }).Concat(id).Concat(cose).ToArray();
        var attestation = new byte[] { 0xa3, 0x63, 0x66, 0x6d, 0x74, 0x64, 0x6e, 0x6f, 0x6e, 0x65,
            0x67, 0x61, 0x74, 0x74, 0x53, 0x74, 0x6d, 0x74, 0xa0,
            0x68, 0x61, 0x75, 0x74, 0x68, 0x44, 0x61, 0x74, 0x61, 0x58, (byte)authData.Length }
            .Concat(authData).ToArray();
        return new Fido2NetLib.AuthenticatorAttestationRawResponse
        {
            Id = Base64Url(id), RawId = id, Type = Fido2NetLib.Objects.PublicKeyCredentialType.PublicKey,
            ClientExtensionResults = new(), Response = new()
            {
                ClientDataJson = ClientData("webauthn.create", challenge), AttestationObject = attestation,
                Transports = Array.Empty<Fido2NetLib.Objects.AuthenticatorTransport>()
            }
        };
    }

    internal static Fido2NetLib.AuthenticatorAssertionRawResponse CreateAssertion(System.Security.Cryptography.ECDsa key, byte[] id, byte[] challenge, byte counter, string origin = "http://localhost:5000")
    {
        var data = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes("localhost"))
            .Concat(new byte[] { 0x05, 0, 0, 0, counter }).ToArray();
        var client = ClientData("webauthn.get", challenge, origin);
        var signed = data.Concat(System.Security.Cryptography.SHA256.HashData(client)).ToArray();
        var signature = key.SignData(signed, System.Security.Cryptography.HashAlgorithmName.SHA256,
            System.Security.Cryptography.DSASignatureFormat.Rfc3279DerSequence);
        return new Fido2NetLib.AuthenticatorAssertionRawResponse
        {
            Id = Base64Url(id), RawId = id, Type = Fido2NetLib.Objects.PublicKeyCredentialType.PublicKey,
            ClientExtensionResults = new(), Response = new() { AuthenticatorData = data, ClientDataJson = client, Signature = signature }
        };
    }

    [Fact]
    public async Task HelloRegistrationChallengeIsBoundToSessionAndSingleUse()
    {
        _keyManager.Setup("123456", KeyManager.GenerateRecoveryKey());
        var ceremonies = new HelloCeremonies(_keyManager);
        var (token, options) = ceremonies.BeginRegistration("session-a");
        Assert.Equal(Fido2NetLib.Objects.UserVerificationRequirement.Required, options.AuthenticatorSelection.UserVerification);
        Assert.Equal(Fido2NetLib.Objects.AuthenticatorAttachment.Platform, options.AuthenticatorSelection.AuthenticatorAttachment);
        await Assert.ThrowsAsync<InvalidOperationException>(() => ceremonies.CompleteRegistrationAsync(token, "session-b", null!));
        await Assert.ThrowsAsync<InvalidOperationException>(() => ceremonies.CompleteRegistrationAsync(token, "session-a", null!));
    }

    [Fact]
    public void HelloKeyEnvelopeSurvivesPinUnlockAndRequiresTheCorrectPrf()
    {
        _keyManager.Setup("123456", KeyManager.GenerateRecoveryKey());
        var original = _keyManager.GetActiveDek();
        var credential = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);
        var prf = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);
        var salt = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);
        _keyManager.SaveVerifiedHelloCredential(credential, new byte[] { 1 }, salt, prf, 1, new byte[] { 2 });
        _keyManager.Lock();
        Assert.True(_keyManager.UnlockWithPin("123456").Success);
        Assert.True(_keyManager.IsHelloRegistered);
        _keyManager.Lock();
        Assert.ThrowsAny<System.Security.Cryptography.CryptographicException>(() =>
            _keyManager.UnlockWithVerifiedHello(credential, new byte[32], 2));
        Assert.False(_keyManager.IsUnlocked);
        _keyManager.UnlockWithVerifiedHello(credential, prf, 2);
        Assert.Equal(original, _keyManager.GetActiveDek());
        _keyManager.Lock();
        Assert.ThrowsAny<System.Security.Cryptography.CryptographicException>(() =>
            _keyManager.UnlockWithVerifiedHello(credential, prf, 2));
        Assert.False(_keyManager.IsUnlocked);
        Assert.True(_keyManager.UnlockWithPin("123456").Success);
        _keyManager.RemoveHello();
        Assert.False(_keyManager.IsHelloRegistered);
    }

    [Fact]
    public void NewSchoolDefaultsDoNotOverwriteExistingProfilesOrReappearAfterDeletion()
    {
        _keyManager.Setup("123456", KeyManager.GenerateRecoveryKey());
        _dbContext.InitializeDatabase();
        var existing = new Schulform { Name = "Fachschule Sozialwesen", Profile = GradingProfile.CreateDefaultSchulnoten() };
        _dbContext.SaveSchulform(existing);
        _dbContext.EnsureRequestedSchoolProfiles();
        Assert.Equal(DeductionUnit.Schulnoten, _dbContext.GetSchulformById(existing.Id)!.Profile.DeductionUnit);
        Assert.Equal(2, _dbContext.GetSchulformen(true).Count);
        _dbContext.DeleteSchulform(existing.Id);
        _dbContext.EnsureRequestedSchoolProfiles();
        Assert.Single(_dbContext.GetSchulformen(true));
    }

    [Fact]
    public void CopiedPinEnvelopeCannotBeOpenedWithPinAlone()
    {
        _keyManager.Setup("123456", KeyManager.GenerateRecoveryKey());
        var envelope = JsonSerializer.Deserialize<KeyEnvelope>(File.ReadAllText(AppPaths.KeyEnvelopeFile))!;
        Assert.True(envelope.PinBoundToWindows);
        var pinKey = CryptoService.DeriveKey("123456", Convert.FromBase64String(envelope.MasterSalt));
        Assert.ThrowsAny<System.Security.Cryptography.CryptographicException>(() =>
            CryptoService.Decrypt(Convert.FromBase64String(envelope.EncryptedDekWithPin), pinKey));
        _keyManager.Lock();
        Assert.True(_keyManager.UnlockWithPin("123456").Success);
    }

    [Fact]
    public void RestoreReplacesNewerDataAndRollsBackWhenAnInsertFails()
    {
        var recovery = KeyManager.GenerateRecoveryKey();
        _keyManager.Setup("123456", recovery);
        _dbContext.InitializeDatabase();
        var school = new Schulform { Name = "Alt", Profile = GradingProfile.CreateDefaultSchulnoten() };
        _dbContext.SaveSchulform(school);
        var original = _backupService.CreateBackupBytes(recovery);
        _dbContext.SaveClass(new ClassGroup { Name = "Nach Sicherung", SchulformId = school.Id });
        _backupService.RestoreBackup(original, recovery);
        Assert.Empty(_dbContext.GetClasses(true));

        var broken = new BackupPayload
        {
            Schulformen = new() { school with { Name = "Teiländerung" } },
            Students = new() { new Student { ClassId = "missing-class", FirstName = "Test", LastName = "Kind" } }
        };
        var salt = CryptoService.GenerateRandomBytes(CryptoService.SaltSize);
        var key = CryptoService.DeriveKey(recovery, salt);
        var envelope = new BackupEnvelope { Salt = Convert.ToBase64String(salt),
            EncryptedPayload = Convert.ToBase64String(CryptoService.Encrypt(JsonSerializer.SerializeToUtf8Bytes(broken), key)) };
        Assert.Throws<SqliteException>(() => _backupService.RestoreBackup(JsonSerializer.SerializeToUtf8Bytes(envelope), recovery));
        Assert.Equal("Alt", _dbContext.GetSchulformById(school.Id)!.Name);
    }

    [Fact]
    public void ArchiveMigrationAndLegacyEncryptionPreserveArchivedAndActiveExams()
    {
        _keyManager.Setup("123456", KeyManager.GenerateRecoveryKey());
        _dbContext.InitializeDatabase();
        var active = new Exam { Title = "Aktive Testklausur", Tasks = new() { new(1, "Aufgabe 1", 10m) } };
        var archived = new Exam { Title = "Archivierte Testklausur", Tasks = new() { new(1, "Aufgabe 1", 10m) },
            StudentEntries = new() { new() { StudentId = "synthetic", StudentName = "Testperson", TaskPoints = new() { [1] = 9m } } } };
        _dbContext.SaveExam(active);
        _dbContext.SaveExam(archived);
        using (var conn = new SqliteConnection($"Data Source={AppPaths.DatabaseFile}"))
        {
            conn.Open();
            using var command = conn.CreateCommand();
            command.CommandText = "DROP TABLE archived_exams";
            command.ExecuteNonQuery();
        }
        _dbContext.InitializeDatabase();
        Assert.Equal(2, _dbContext.GetExams().Count);
        Assert.True(_dbContext.SetExamArchived(archived.Id, true));
        Assert.False(_dbContext.SetExamArchived("fehlt", true));
        Assert.Single(_dbContext.GetExams());
        Assert.Equal(2, _dbContext.GetExams(includeArchived: true).Count);
        LegacyDatabaseFixture.Downgrade(_keyManager.GetActiveDek());
        using (var conn = new SqliteConnection($"Data Source={AppPaths.DatabaseFile}"))
        {
            conn.Open();
            using var command = conn.CreateCommand();
            command.CommandText = "UPDATE exams SET title=$title WHERE id=$id";
            command.Parameters.AddWithValue("$title", "Alte archivierte Klausur");
            command.Parameters.AddWithValue("$id", archived.Id);
            command.ExecuteNonQuery();
        }
        _dbContext.MigrateLegacyPlaintext();
        var migrated = _dbContext.GetExamById(archived.Id)!;
        Assert.True(migrated.IsArchived);
        Assert.Equal("Alte archivierte Klausur", migrated.Title);
        Assert.Equal(9m, migrated.StudentEntries[0].TaskPoints[1]);
        Assert.Equal(active.Id, Assert.Single(_dbContext.GetExams()).Id);
        Assert.True(_dbContext.SetExamArchived(archived.Id, false));
        Assert.Equal(2, _dbContext.GetExams().Count);
        Assert.True(_dbContext.SetExamArchived(archived.Id, true));
        Assert.Throws<ArchivedExamReadOnlyException>(() => _dbContext.DeleteExam(archived.Id));
        Assert.True(_dbContext.SetExamArchived(archived.Id, false));
        _dbContext.DeleteExam(archived.Id);
        using var checkConn = new SqliteConnection($"Data Source={AppPaths.DatabaseFile}");
        checkConn.Open();
        using var check = checkConn.CreateCommand();
        check.CommandText = "SELECT COUNT(*) FROM archived_exams";
        Assert.Equal(0L, (long)check.ExecuteScalar()!);
        _dbContext.ReplaceFromBackup(new BackupPayload { Version = 2, Exams = new() { active } });
        Assert.False(Assert.Single(_dbContext.GetExams()).IsArchived);
        _dbContext.ReplaceFromBackup(new BackupPayload { Version = 3, Exams = new() { active with { IsArchived = true } } });
        Assert.True(_dbContext.GetExamById(active.Id)!.IsArchived);
    }

    [Fact]
    public void CoverSheetMigrationLegacyRestoreTamperDetectionAndDeletionPreserveExamData()
    {
        _keyManager.Setup("123456", KeyManager.GenerateRecoveryKey());
        _dbContext.InitializeDatabase();
        var exam = new Exam { Title = "Vorhandene Testklausur", Tasks = new() { new(1, "Aufgabe 1", 10m) },
            GradingProfileSnapshot = GradingProfile.BeruflichesGymnasium() };
        _dbContext.SaveExam(exam);
        using var conn = new SqliteConnection($"Data Source={AppPaths.DatabaseFile}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        // Reproduce a database created before the cover sheet table existed.
        cmd.CommandText = "DROP TABLE exam_cover_sheets";
        cmd.ExecuteNonQuery();
        _dbContext.InitializeDatabase();
        var migrated = _dbContext.GetExamById(exam.Id)!;
        Assert.Null(migrated.CoverSheet);
        Assert.Equal(exam.Title, migrated.Title);
        Assert.Equal(3m, migrated.GradingProfileSnapshot.DeductionTiers[0].Threshold);

        var settings = Klausurfix.Infrastructure.Export.CoverSheetDocument.Defaults(exam);
        var invalidBackup = new BackupPayload { Version = 2, Exams = new() { exam with { CoverSheet = settings with { Version = 99 } } } };
        Assert.Throws<InvalidOperationException>(() => _dbContext.ReplaceFromBackup(invalidBackup));
        Assert.Equal(exam.Title, _dbContext.GetExamById(exam.Id)!.Title);

        _dbContext.SaveExam(exam with { CoverSheet = settings });
        _dbContext.ReplaceFromBackup(new BackupPayload { Version = 1, Exams = new() { exam } });
        Assert.Null(_dbContext.GetExamById(exam.Id)!.CoverSheet);
        _dbContext.SaveExam(exam with { CoverSheet = settings });
        cmd.CommandText = "SELECT document_enc FROM exam_cover_sheets WHERE exam_id=$id";
        cmd.Parameters.AddWithValue("$id", exam.Id);
        var encrypted = Convert.FromBase64String(((string)cmd.ExecuteScalar()!)[4..]);
        encrypted[^1] ^= 1;
        cmd.CommandText = "UPDATE exam_cover_sheets SET document_enc=$document WHERE exam_id=$id";
        cmd.Parameters.AddWithValue("$document", "kf2:" + Convert.ToBase64String(encrypted));
        cmd.ExecuteNonQuery();
        Assert.ThrowsAny<System.Security.Cryptography.CryptographicException>(() => _dbContext.GetExamById(exam.Id));
        _dbContext.SaveExam(exam with { CoverSheet = settings });
        _dbContext.DeleteExam(exam.Id);
        cmd.CommandText = "SELECT COUNT(*) FROM exam_cover_sheets";
        Assert.Equal(0L, (long)cmd.ExecuteScalar()!);
    }

    [Fact]
    public void SensitiveExamMetadataAndErrorCountsAreEncryptedInDatabase()
    {
        _keyManager.Setup("123456", KeyManager.GenerateRecoveryKey());
        _dbContext.InitializeDatabase();
        var school = new Schulform { Name = "Private Schulform", Profile = GradingProfile.CreateDefaultSchulnoten() };
        _dbContext.SaveSchulform(school);
        var cls = new ClassGroup { Name = "Private Klasse", SchulformId = school.Id, SchulformName = school.Name };
        _dbContext.SaveClass(cls);
        var exam = new Exam { ClassId = cls.Id, ClassName = cls.Name, SchulformId = school.Id, SchulformName = school.Name,
            Title = "Private Klausur", Tasks = new() { new TaskDefinition(1, "Geheime Aufgabe", 10) },
            StudentEntries = new() { new ExamStudentEntry { StudentId = "s", StudentName = "Test Name", WordCount = 600, ErrorCount = 12 } } };
        _dbContext.SaveExam(exam);
        using var conn = new SqliteConnection($"Data Source={AppPaths.DatabaseFile}");
        conn.Open(); using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT class_name,title,tasks_json FROM exams";
        using (var reader = cmd.ExecuteReader())
        {
            Assert.True(reader.Read());
            Assert.DoesNotContain("Private", reader.GetString(0));
            Assert.DoesNotContain("Private", reader.GetString(1));
            Assert.DoesNotContain("Geheime", reader.GetString(2));
        }
        cmd.CommandText = "SELECT status,word_count,error_count FROM exam_entries";
        using var entry = cmd.ExecuteReader(); Assert.True(entry.Read());
        Assert.Equal(0, entry.GetInt32(0)); Assert.True(entry.IsDBNull(1)); Assert.True(entry.IsDBNull(2));
        var restored = _dbContext.GetExamById(exam.Id)!;
        Assert.Equal("Private Klausur", restored.Title);
        Assert.Equal(600, restored.StudentEntries[0].WordCount);
        Assert.Equal(12, restored.StudentEntries[0].ErrorCount);
    }

    [Fact]
    public void LegacyPlaintextIsMigratedWithoutChangingExamResults()
    {
        _keyManager.Setup("123456", KeyManager.GenerateRecoveryKey());
        _dbContext.InitializeDatabase();
        var school = new Schulform { Name = "Alte Schulform", Profile = GradingProfile.CreateDefaultSchulnoten() };
        _dbContext.SaveSchulform(school);
        var cls = new ClassGroup { Name = "Alte Klasse", SchulformId = school.Id, SchulformName = school.Name };
        _dbContext.SaveClass(cls);
        var exam = new Exam { ClassId = cls.Id, ClassName = cls.Name, SchulformId = school.Id, SchulformName = school.Name,
            Title = "Alte Klausur", Tasks = new() { new TaskDefinition(1, "Alte Aufgabe", 10) },
            StudentEntries = new() { new ExamStudentEntry { StudentId = "s", StudentName = "Test Name", WordCount = 600,
                ErrorCount = 12, TaskPoints = new() { [1] = 9 } } } };
        _dbContext.SaveExam(exam);
        LegacyDatabaseFixture.Downgrade(_keyManager.GetActiveDek());
        using (var conn = new SqliteConnection($"Data Source={AppPaths.DatabaseFile}"))
        {
            conn.Open(); using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE schulformen SET name='Alte Schulform'; UPDATE classes SET name='Alte Klasse',schulform_name='Alte Schulform'; UPDATE exams SET title='Alte Klausur',class_name='Alte Klasse',schulform_name='Alte Schulform',date='2026-01-01',tasks_json=$tasks,profile_snapshot_json=$profile; UPDATE exam_entries SET status=1,word_count=600,error_count=12,task_points_enc=$points";
            cmd.Parameters.AddWithValue("$tasks", JsonSerializer.Serialize(exam.Tasks));
            cmd.Parameters.AddWithValue("$profile", JsonSerializer.Serialize(exam.GradingProfileSnapshot));
            cmd.Parameters.AddWithValue("$points", CryptoService.EncryptString(JsonSerializer.Serialize(exam.StudentEntries[0].TaskPoints), _keyManager.GetActiveDek()));
            cmd.ExecuteNonQuery();
        }
        _dbContext.MigrateLegacyPlaintext();
        var result = _dbContext.GetExamById(exam.Id)!;
        Assert.Equal("Alte Klausur", result.Title);
        Assert.Equal(600, result.StudentEntries[0].WordCount);
        Assert.Equal(12, result.StudentEntries[0].ErrorCount);
        Assert.Equal(9, result.StudentEntries[0].TaskPoints[1]);
        SqliteConnection.ClearAllPools();
        var bytes = File.ReadAllBytes(AppPaths.DatabaseFile);
        Assert.DoesNotContain("Alte Klausur", System.Text.Encoding.UTF8.GetString(bytes));
        Assert.DoesNotContain("Alte Klasse", System.Text.Encoding.UTF8.GetString(bytes));
    }
    private readonly string _testDir;
    private readonly KeyManager _keyManager;
    private readonly DatabaseContext _dbContext;
    private readonly BackupService _backupService;

    public KeyManagerAndBackupTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "KlausurfixTests_" + Guid.NewGuid().ToString("N"));
        AppPaths.SetCustomBaseDirectory(_testDir);

        _keyManager = new KeyManager(new TestKeyProtector());
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
        exam = exam with { CoverSheet = Klausurfix.Infrastructure.Export.CoverSheetDocument.Defaults(exam) with
        {
            Instructions = "SYNTHETISCHER VERTRAULICHER DECKBLATTHINWEIS",
            DeductionNotice = "Individueller Fehlerhinweis.",
            Tasks = new() { new(1, "Analysieren Sie den Testtext.", 12.75m), new(2, "Begründen Sie Ihr Urteil.", 0m) }
        } };
        _dbContext.SaveExam(exam);
        using (var connection = new SqliteConnection($"Data Source={AppPaths.DatabaseFile}"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT document_enc FROM exam_cover_sheets WHERE exam_id=$id";
            command.Parameters.AddWithValue("$id", exam.Id);
            Assert.DoesNotContain("DECKBLATTHINWEIS", (string)command.ExecuteScalar()!);
        }
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
        Assert.Equal(exam.CoverSheet.Instructions, restoredExam.CoverSheet!.Instructions);
        Assert.Equal(exam.CoverSheet.Tasks, restoredExam.CoverSheet.Tasks);
        Assert.Equal("Individueller Fehlerhinweis.", restoredExam.CoverSheet.DeductionNotice);
        Assert.Equal(exam.CoverSheet.Operators, restoredExam.CoverSheet.Operators);
        Assert.Equal(exam.CoverSheet.OperatorSource, restoredExam.CoverSheet.OperatorSource);
    }
}

