namespace Klausurfix.Tests;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Klausurfix.Core.Models;
using Klausurfix.Infrastructure.Security;
using Klausurfix.Infrastructure.Storage;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Xunit;

public class WebApiIntegrationTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    [Fact]
    public async Task UpdateViews_CoordinateRegisteredViewsAcrossSessionsAndRequireLoginToStart()
    {
        var first = (await (await _client.PostAsync("/api/update/view/register", null)).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("viewId").GetString();
        var second = (await (await _client.PostAsync("/api/update/view/register", null)).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("viewId").GetString();
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.PostAsync("/api/update/save-round/begin", null)).StatusCode);
        var recovery = KeyManager.GenerateRecoveryKey();
        Assert.Equal(HttpStatusCode.OK, (await _client.PostAsJsonAsync("/api/setup/init", new { pin = "123456", recoveryKey = recovery })).StatusCode);
        var begin = await _client.PostAsync("/api/update/save-round/begin", null);
        var round = (await begin.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("roundId").GetString();
        Assert.Equal(HttpStatusCode.OK, (await _client.PostAsJsonAsync("/api/update/view/respond", new { viewId = first, roundId = round, saved = true })).StatusCode);
        var pending = await (await _client.PostAsJsonAsync("/api/update/save-round/status", new { roundId = round })).Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(pending.GetProperty("complete").GetBoolean());
        Assert.Equal(HttpStatusCode.OK, (await _client.PostAsJsonAsync("/api/update/view/respond", new { viewId = second, roundId = round, saved = true })).StatusCode);
        var done = await (await _client.PostAsJsonAsync("/api/update/save-round/status", new { roundId = round })).Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(done.GetProperty("complete").GetBoolean());
        Assert.Equal(2, done.GetProperty("expected").GetInt32());
    }

    [Fact]
    public async Task UpdatePreparation_CreatesRecoverableBackupWhileApiWritesAreFrozen()
    {
        var recovery = KeyManager.GenerateRecoveryKey();
        Assert.Equal(HttpStatusCode.OK, (await _client.PostAsJsonAsync("/api/setup/init", new { pin = "123456", recoveryKey = recovery })).StatusCode);
        var preparation = _factory.Services.GetRequiredService<UpdatePreparation>();
        string name;
        using (var prepared = await preparation.PrepareAsync())
        {
            name = prepared.Backup.FileName;
            Assert.StartsWith("klausurfix_update_", name);
            Assert.Equal((HttpStatusCode)423, (await _client.PostAsync("/api/backups/create", null)).StatusCode);
        }
        Assert.Equal(HttpStatusCode.OK, (await _client.PostAsJsonAsync($"/api/backups/{name}/restore", new { recoveryKey = recovery })).StatusCode);
    }

    [Fact]
    public async Task UpdateWriteGate_RejectsMutationsWhileFrozenAndAllowsReads()
    {
        var recovery = KeyManager.GenerateRecoveryKey();
        Assert.Equal(HttpStatusCode.OK, (await _client.PostAsJsonAsync("/api/setup/init", new { pin = "123456", recoveryKey = recovery })).StatusCode);
        var gate = _factory.Services.GetRequiredService<UpdateWriteGate>();
        using (var freeze = await gate.FreezeAsync())
        {
            Assert.NotNull(freeze);
            Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync("/api/backups")).StatusCode);
            Assert.Equal((HttpStatusCode)423, (await _client.PostAsync("/api/update/backup", null)).StatusCode);
        }
        Assert.Equal(HttpStatusCode.OK, (await _client.PostAsync("/api/update/backup", null)).StatusCode);
    }

    [Fact]
    public async Task UpdateBackup_RequiresSessionAndCanBeRestored()
    {
        const string url = "/api/update/backup";
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.PostAsync(url, null)).StatusCode);
        var recovery = KeyManager.GenerateRecoveryKey();
        Assert.Equal(HttpStatusCode.OK, (await _client.PostAsJsonAsync("/api/setup/init", new { pin = "123456", recoveryKey = recovery })).StatusCode);
        var response = await _client.PostAsync(url, null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var backup = await response.Content.ReadFromJsonAsync<Klausurfix.Infrastructure.Backup.ManagedBackupInfo>();
        Assert.NotNull(backup);
        Assert.StartsWith("klausurfix_update_", backup.FileName);
        Assert.Contains((await _client.GetFromJsonAsync<List<Klausurfix.Infrastructure.Backup.ManagedBackupInfo>>("/api/backups"))!, b => b.FileName == backup.FileName);
        Assert.Equal(HttpStatusCode.OK, (await _client.PostAsJsonAsync($"/api/backups/{backup.FileName}/restore", new { recoveryKey = recovery })).StatusCode);
        await _client.PostAsync("/api/auth/lock", null);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.PostAsync(url, null)).StatusCode);
    }

    [Fact]
    public async Task ManualBackups_CreateAndDownloadWithoutRecoveryKeyButRestoreRequiresIt()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.PostAsync("/api/backups/create", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.PostAsync("/api/backups/download", null)).StatusCode);
        var recovery = KeyManager.GenerateRecoveryKey();
        Assert.Equal(HttpStatusCode.OK, (await _client.PostAsJsonAsync("/api/setup/init", new { pin = "123456", recoveryKey = recovery })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _client.PostAsync("/api/backups/create", null)).StatusCode);
        var response = await _client.PostAsync("/api/backups/download", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var bytes = await response.Content.ReadAsByteArrayAsync();
        var envelope = JsonSerializer.Deserialize<Klausurfix.Infrastructure.Backup.BackupEnvelope>(bytes)!;
        Assert.Equal(2, envelope.FormatVersion);
        var backups = await _client.GetFromJsonAsync<List<Klausurfix.Infrastructure.Backup.ManagedBackupInfo>>("/api/backups");
        var saved = Assert.Single(backups!);
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.PostAsJsonAsync($"/api/backups/{saved.FileName}/restore", new { recoveryKey = KeyManager.GenerateRecoveryKey() })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _client.PostAsJsonAsync($"/api/backups/{saved.FileName}/restore", new { recoveryKey = recovery })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _client.PostAsJsonAsync("/api/backups/restore-file", new { recoveryKey = recovery, dataBase64 = Convert.ToBase64String(bytes) })).StatusCode);
        await _client.PostAsync("/api/auth/lock", null);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.PostAsync("/api/backups/create", null)).StatusCode);
    }

    [Fact]
    public async Task AutomaticBackupSettings_RequireSessionAndValidateAndPersist()
    {
        const string url = "/api/backups/automatic/settings";
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.PutAsJsonAsync(url, new { enabled = false, intervalHours = 6 })).StatusCode);
        var recovery = KeyManager.GenerateRecoveryKey();
        Assert.Equal(HttpStatusCode.OK, (await _client.PostAsJsonAsync("/api/setup/init", new { pin = "123456", recoveryKey = recovery })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.PutAsJsonAsync(url, new { enabled = false, intervalHours = 1 })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.PutAsJsonAsync(url, new { intervalHours = 6 })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _client.PutAsJsonAsync(url, new { enabled = false, intervalHours = 168 })).StatusCode);
        var status = await _client.GetFromJsonAsync<JsonElement>("/api/backups/automatic/status");
        Assert.False(status.GetProperty("enabled").GetBoolean());
        Assert.Equal(168, status.GetProperty("intervalHours").GetInt32());
        await _client.PostAsync("/api/auth/lock", null);
        Assert.Equal(HttpStatusCode.OK, (await _client.PostAsJsonAsync("/api/auth/unlock-pin", new { pin = "123456" })).StatusCode);
        status = await _client.GetFromJsonAsync<JsonElement>("/api/backups/automatic/status");
        Assert.False(status.GetProperty("enabled").GetBoolean());
        Assert.Equal(168, status.GetProperty("intervalHours").GetInt32());
    }

    [Fact]
    public async Task TaskPoints_RequireIntegersButPreserveExistingDecimals()
    {
        var recovery = KeyManager.GenerateRecoveryKey();
        Assert.Equal(HttpStatusCode.OK, (await _client.PostAsJsonAsync("/api/setup/init", new
        { pin = "123456", recoveryKey = recovery, inactivityTimeoutMinutes = 15 })).StatusCode);
        var db = _factory.Services.GetRequiredService<DatabaseContext>();
        var school = db.GetSchulformen().First();
        var cls = new ClassGroup { Name = "Testklasse", SchulformId = school.Id, SchulformName = school.Name };
        db.SaveClass(cls);
        var invalidExam = await _client.PostAsJsonAsync("/api/klausuren", new
        { classId = cls.Id, title = "Test", date = "2026-11-01", tasks = new[] { new { name = "A", maxPoints = 10.5m } } });
        Assert.Equal(HttpStatusCode.BadRequest, invalidExam.StatusCode);
        foreach (var total in new[] { 99, 100, 101 })
        {
            var response = await _client.PostAsJsonAsync("/api/klausuren", new
            { classId = cls.Id, title = "Punktesumme", date = "2026-11-01", tasks = new[] { new { name = "A", maxPoints = 40 }, new { name = "B", maxPoints = total - 40 } } });
            Assert.Equal(total == 100 ? HttpStatusCode.OK : HttpStatusCode.BadRequest, response.StatusCode);
            if (total == 100) Assert.Equal(100m, (await response.Content.ReadFromJsonAsync<Exam>())!.TotalMaxPoints);
        }

        var exam = new Exam { ClassId = cls.Id, ClassName = cls.Name, SchulformId = school.Id, SchulformName = school.Name,
            Title = "Test", Tasks = new() { new(1, "A", 10m) }, GradingProfileSnapshot = school.Profile,
            StudentEntries = new() { new() { StudentId = "synthetisch", StudentName = "Test", TaskPoints = new() { [1] = 7.25m } } } };
        db.SaveExam(exam);
        var entry = exam.StudentEntries[0];
        var url = $"/api/klausuren/{exam.Id}/entries";
        Assert.Equal(HttpStatusCode.OK, (await _client.PostAsJsonAsync(url, new[] { entry with { WordCount = 200 } })).StatusCode);
        Assert.Equal(7.25m, db.GetExamById(exam.Id)!.StudentEntries[0].TaskPoints[1]);
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.PostAsJsonAsync(url,
            new[] { entry with { TaskPoints = new() { [1] = 7.5m } } })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _client.PostAsJsonAsync(url,
            new[] { entry with { TaskPoints = new() { [1] = 8m } } })).StatusCode);
        Assert.Equal(8m, db.GetExamById(exam.Id)!.StudentEntries[0].TaskPoints[1]);
    }

    private readonly WebApplicationFactory<Program> _factory;
    private readonly string _testDir;
    private readonly HttpClient _client;

    public WebApiIntegrationTests(WebApplicationFactory<Program> factory)
    {
        _testDir = Path.Combine(Path.GetTempPath(), "KlausurfixWebTests_" + Guid.NewGuid().ToString("N"));
        AppPaths.SetCustomBaseDirectory(_testDir);

        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("KLAUSURFIX_DATA_DIR", _testDir);
            builder.ConfigureTestServices(services =>
                services.Replace(ServiceDescriptor.Singleton<ILocalKeyProtector, TestKeyProtector>()));
        });

        _client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("http://localhost:5000")
        });
        _client.DefaultRequestHeaders.Add("X-Requested-With", "XMLHttpRequest");
    }

    public void Dispose()
    {
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
    public async Task ArchivedExams_CanBeOpenedRestoredAndIncludedInEncryptedBackups()
    {
        var recovery = KeyManager.GenerateRecoveryKey();
        Assert.Equal(HttpStatusCode.OK, (await _client.PostAsJsonAsync("/api/setup/init", new
        {
            pin = "123456", recoveryKey = recovery, inactivityTimeoutMinutes = 15
        })).StatusCode);
        var db = _factory.Services.GetRequiredService<DatabaseContext>();
        var exam = new Exam
        {
            Title = "Archiv-Testklausur", ClassName = "Testklasse", Date = new DateOnly(2026, 11, 2),
            Tasks = new() { new(1, "Aufgabe 1", 25m) }, GradingProfileSnapshot = GradingProfile.BeruflichesGymnasium(),
            StudentEntries = new() { new() { StudentId = "synthetic", StudentName = "Testperson", TaskPoints = new() { [1] = 20m }, WordCount = 200, ErrorCount = 2 } }
        };
        exam = exam with { CoverSheet = Klausurfix.Infrastructure.Export.CoverSheetDocument.Defaults(exam) with
        {
            Tasks = new() { new(1, "Analysieren Sie das Material.") }
        } };
        db.SaveExam(exam);
        var url = $"/api/klausuren/{exam.Id}/archivierung";
        Assert.Single((await _client.GetFromJsonAsync<List<Exam>>("/api/klausuren"))!);
        Assert.Empty((await _client.GetFromJsonAsync<List<Exam>>("/api/klausuren?archived=true"))!);
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.PutAsJsonAsync(url, new { })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.PutAsJsonAsync("/api/klausuren/fehlt/archivierung", new { archived = true })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _client.PutAsJsonAsync(url, new { archived = true })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _client.PutAsJsonAsync(url, new { archived = true })).StatusCode);
        Assert.Empty((await _client.GetFromJsonAsync<List<Exam>>("/api/klausuren"))!);
        var archived = Assert.Single((await _client.GetFromJsonAsync<List<Exam>>("/api/klausuren?archived=true"))!);
        Assert.True(archived.IsArchived);
        Assert.Equal(exam.Id, archived.Id);
        var opened = (await _client.GetFromJsonAsync<Exam>($"/api/klausuren/{exam.Id}"))!;
        Assert.True(opened.IsArchived);
        Assert.Equal(20m, opened.StudentEntries[0].TaskPoints[1]);
        Assert.Equal(exam.CoverSheet.Tasks[0], opened.CoverSheet!.Tasks[0]);
        Assert.Equal(3m, opened.GradingProfileSnapshot.DeductionTiers[0].Threshold);
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync($"/api/klausuren/{exam.Id}/summary")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync($"/api/klausuren/{exam.Id}/export/pdf")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync($"/api/klausuren/{exam.Id}/export/docx")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync($"/api/klausuren/{exam.Id}/deckblatt/pdf")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync($"/api/klausuren/{exam.Id}/deckblatt/docx")).StatusCode);
        var preview = await _client.GetFromJsonAsync<JsonElement>($"/api/klausuren/{exam.Id}/deckblatt");
        Assert.True(preview.GetProperty("isArchived").GetBoolean());
        Assert.Equal(HttpStatusCode.OK, (await _client.PostAsJsonAsync($"/api/klausuren/{exam.Id}/preview-entry", opened.StudentEntries[0])).StatusCode);

        var editedEntry = opened.StudentEntries[0] with { TaskPoints = new() { [1] = 21m } };
        var blockedEntries = await _client.PostAsJsonAsync($"/api/klausuren/{exam.Id}/entries", new[] { editedEntry });
        Assert.Equal(HttpStatusCode.Conflict, blockedEntries.StatusCode);
        Assert.Contains("schreibgeschützt", await blockedEntries.Content.ReadAsStringAsync());
        var coverRequest = new SaveCoverSheetRequest("Fach", "Neuer Hinweis", true, true, true, true,
            new() { new(1, "Neue Aufgabenstellung", 12m) }, new() { "erklaeren" }, "Anderer Hinweis", true, true);
        Assert.Equal(HttpStatusCode.Conflict, (await _client.PutAsJsonAsync($"/api/klausuren/{exam.Id}/deckblatt", coverRequest)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await _client.PostAsJsonAsync($"/api/klausuren/{exam.Id}/recalculate", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await _client.DeleteAsync($"/api/klausuren/{exam.Id}")).StatusCode);
        // A stale snapshot captured before archiving must also fail inside the transaction.
        Assert.Throws<ArchivedExamReadOnlyException>(() => db.SaveExam(exam));
        Assert.Throws<ArchivedExamReadOnlyException>(() => db.DeleteExam(exam.Id));
        Assert.True(db.GetExamById(exam.Id)!.IsArchived);
        Assert.Equal(20m, db.GetExamById(exam.Id)!.StudentEntries[0].TaskPoints[1]);
        Assert.Equal(exam.CoverSheet.Tasks[0], db.GetExamById(exam.Id)!.CoverSheet!.Tasks[0]);

        var noCover = new Exam { Title = "Ohne Deckblatt", Tasks = new() { new(1, "Aufgabe", 10m) } };
        db.SaveExam(noCover);
        Assert.True(db.SetExamArchived(noCover.Id, true));
        var noCoverPreview = await _client.GetFromJsonAsync<JsonElement>($"/api/klausuren/{noCover.Id}/deckblatt");
        Assert.True(noCoverPreview.GetProperty("isArchived").GetBoolean());
        Assert.False(noCoverPreview.GetProperty("isSaved").GetBoolean());
        Assert.Null(db.GetExamById(noCover.Id)!.CoverSheet);
        Assert.True(db.SetExamArchived(noCover.Id, false));
        db.DeleteExam(noCover.Id);
        var backup = await _client.PostAsJsonAsync("/api/backups/download", new { recoveryKey = recovery });
        Assert.Equal(HttpStatusCode.OK, backup.StatusCode);
        var bytes = await backup.Content.ReadAsByteArrayAsync();
        Assert.Equal(HttpStatusCode.OK, (await _client.PutAsJsonAsync(url, new { archived = false })).StatusCode);
        Assert.False(db.GetExamById(exam.Id)!.IsArchived);
        Assert.Equal(HttpStatusCode.OK, (await _client.PostAsJsonAsync($"/api/klausuren/{exam.Id}/entries", new[] { editedEntry })).StatusCode);
        Assert.Equal(21m, db.GetExamById(exam.Id)!.StudentEntries[0].TaskPoints[1]);
        Assert.Equal(HttpStatusCode.OK, (await _client.PutAsJsonAsync($"/api/klausuren/{exam.Id}/deckblatt", coverRequest)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _client.PostAsJsonAsync("/api/backups/restore-file", new
        {
            recoveryKey = recovery, dataBase64 = Convert.ToBase64String(bytes)
        })).StatusCode);
        var restored = db.GetExamById(exam.Id)!;
        Assert.True(restored.IsArchived);
        Assert.Equal(20m, restored.StudentEntries[0].TaskPoints[1]);
        Assert.Equal(exam.CoverSheet.Tasks[0], restored.CoverSheet!.Tasks[0]);
        Assert.Equal(HttpStatusCode.OK, (await _client.PutAsJsonAsync(url, new { archived = false })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _client.PostAsJsonAsync($"/api/klausuren/{exam.Id}/recalculate", new { })).StatusCode);
        Assert.Single((await _client.GetFromJsonAsync<List<Exam>>("/api/klausuren"))!);
        Assert.Empty((await _client.GetFromJsonAsync<List<Exam>>("/api/klausuren?archived=true"))!);
        Assert.Equal(HttpStatusCode.OK, (await _client.DeleteAsync($"/api/klausuren/{exam.Id}")).StatusCode);
        Assert.Null(db.GetExamById(exam.Id));
        Assert.Equal(HttpStatusCode.OK, (await _client.PostAsync("/api/auth/lock", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.GetAsync("/api/klausuren?archived=true")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.PutAsJsonAsync(url, new { archived = true })).StatusCode);
    }

    [Fact]
    public async Task CoverSheets_AreSavedAsSnapshotsAndExportsRequireAnUnlockedSession()
    {
        Assert.Equal(HttpStatusCode.OK, (await _client.PostAsJsonAsync("/api/setup/init", new
        {
            pin = "123456", recoveryKey = KeyManager.GenerateRecoveryKey(), inactivityTimeoutMinutes = 15
        })).StatusCode);
        var db = _factory.Services.GetRequiredService<DatabaseContext>();
        var exam = new Exam
        {
            Title = "Synthetische Deckblattprüfung", ClassName = "Testklasse", GradingProfileSnapshot = GradingProfile.BeruflichesGymnasium(),
            Tasks = new() { new(1, "Materialanalyse", 25.5m) },
            StudentEntries = new() { new() { StudentId = "test", StudentName = "Testperson", TaskPoints = new() { [1] = 20m } } }
        };
        db.SaveExam(exam);
        var endpoint = $"/api/klausuren/{exam.Id}/deckblatt";
        var template = await _client.GetFromJsonAsync<JsonElement>(endpoint);
        Assert.False(template.GetProperty("isSaved").GetBoolean());
        Assert.Null(db.GetExamById(exam.Id)!.CoverSheet);
        var request = new SaveCoverSheetRequest("Politik und Wirtschaft", "Hilfsmittel: Wörterbuch.", true, true, true, true,
            new() { new(1, "Erklären Sie die Zusammenhänge.") }, new() { "erklaeren", "diskutieren" });
        Assert.Equal(HttpStatusCode.OK, (await _client.PutAsJsonAsync(endpoint, request)).StatusCode);
        var saved = db.GetExamById(exam.Id)!;
        Assert.Equal(request.Tasks[0].Text, saved.CoverSheet!.Tasks[0].Text);
        Assert.Equal(20m, saved.StudentEntries[0].TaskPoints[1]);
        Assert.Equal(3m, saved.GradingProfileSnapshot.DeductionTiers[0].Threshold);
        Assert.Contains("Begründungszusammenhang", saved.CoverSheet.Operators[0].Definition);

        // A previously frozen definition remains intact when other document fields change.
        db.SaveExam(saved with { CoverSheet = saved.CoverSheet with
        {
            Operators = new() { saved.CoverSheet.Operators[0] with { Definition = "Historisch gespeicherte Definition." }, saved.CoverSheet.Operators[1] }
        } });
        Assert.Equal(HttpStatusCode.OK, (await _client.PutAsJsonAsync(endpoint, request with { Subject = "Neues Fach" })).StatusCode);
        Assert.Equal("Historisch gespeicherte Definition.", db.GetExamById(exam.Id)!.CoverSheet!.Operators[0].Definition);

        var pointsSave = new
        {
            subject = "Neues Fach", instructions = "", useTemplateLogo = true, showGradeKey = true,
            showDeductionRules = true, showWordCount = true, saveEarnedPoints = true, saveDeductionNotice = true,
            deductionNotice = "Mein Hinweis.", tasks = new[] { new { taskNumber = 1, text = "Erklären Sie die Zusammenhänge.", earnedPoints = "12.75" } },
            operatorIds = new[] { "erklaeren" }
        };
        Assert.Equal(HttpStatusCode.OK, (await _client.PutAsJsonAsync(endpoint, pointsSave)).StatusCode);
        var pointsSaved = db.GetExamById(exam.Id)!;
        Assert.Equal(12.75m, pointsSaved.CoverSheet!.Tasks[0].EarnedPoints);
        Assert.Equal("Mein Hinweis.", pointsSaved.CoverSheet.DeductionNotice);
        Assert.Equal(20m, pointsSaved.StudentEntries[0].TaskPoints[1]);
        // An older client that omits the new fields must not erase them.
        Assert.Equal(HttpStatusCode.OK, (await _client.PutAsJsonAsync(endpoint, request with { Subject = "Neues Fach" })).StatusCode);
        Assert.Equal(12.75m, db.GetExamById(exam.Id)!.CoverSheet!.Tasks[0].EarnedPoints);
        Assert.Equal("Mein Hinweis.", db.GetExamById(exam.Id)!.CoverSheet!.DeductionNotice);
        var invalidPoints = pointsSave with { tasks = new[] { new { taskNumber = 1, text = "", earnedPoints = "25.500001" } } };
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.PutAsJsonAsync(endpoint, invalidPoints)).StatusCode);
        Assert.Equal(12.75m, db.GetExamById(exam.Id)!.CoverSheet!.Tasks[0].EarnedPoints);
        var zero = pointsSave with { tasks = new[] { new { taskNumber = 1, text = "", earnedPoints = "0" } } };
        Assert.Equal(HttpStatusCode.OK, (await _client.PutAsJsonAsync(endpoint, zero)).StatusCode);
        Assert.Equal(0m, db.GetExamById(exam.Id)!.CoverSheet!.Tasks[0].EarnedPoints);
        var negative = pointsSave with { tasks = new[] { new { taskNumber = 1, text = "", earnedPoints = "-0.1" } } };
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.PutAsJsonAsync(endpoint, negative)).StatusCode);
        var clear = request with { Subject = "Neues Fach", Tasks = new() { new(1, "", null) }, SaveEarnedPoints = true };
        Assert.Equal(HttpStatusCode.OK, (await _client.PutAsJsonAsync(endpoint, clear)).StatusCode);
        Assert.Null(db.GetExamById(exam.Id)!.CoverSheet!.Tasks[0].EarnedPoints);

        Assert.Equal(HttpStatusCode.BadRequest, (await _client.PutAsJsonAsync(endpoint, request with { OperatorIds = new() { "unbekannt" } })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.PutAsJsonAsync(endpoint, request with { OperatorIds = new() { "erklaeren", "erklaeren" } })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.PutAsJsonAsync(endpoint, request with { Tasks = new() { new(9, "Falsche Aufgabe") } })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.PutAsJsonAsync(endpoint, request with { Subject = new string('x', 201) })).StatusCode);
        Assert.Equal("Neues Fach", db.GetExamById(exam.Id)!.CoverSheet!.Subject);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync("/api/klausuren/unbekannt/deckblatt")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.PutAsJsonAsync("/api/klausuren/unbekannt/deckblatt", request)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync("/api/klausuren/unbekannt/deckblatt/pdf")).StatusCode);

        foreach (var format in new[] { "pdf", "docx" })
        {
            var response = await _client.GetAsync(endpoint + "/" + format);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains("no-store", response.Headers.CacheControl!.ToString());
            Assert.NotNull(response.Content.Headers.ContentDisposition?.FileNameStar);
        }
        Assert.Equal(HttpStatusCode.OK, (await _client.PostAsync("/api/auth/lock", null)).StatusCode);
        foreach (var route in new[] { endpoint, endpoint + "/pdf", endpoint + "/docx", "/api/deckblatt/operatoren", "/api/deckblatt/logo" })
            Assert.Equal(HttpStatusCode.Unauthorized, (await _client.GetAsync(route)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.PutAsJsonAsync(endpoint, request)).StatusCode);
    }

    [Fact]
    public async Task CoverSheetDocx_DownloadHeaderUsesTemplateFileName()
    {
        Assert.Equal(HttpStatusCode.OK, (await _client.PostAsJsonAsync("/api/setup/init", new
        {
            pin = "123456", recoveryKey = KeyManager.GenerateRecoveryKey(), inactivityTimeoutMinutes = 15
        })).StatusCode);
        var db = _factory.Services.GetRequiredService<DatabaseContext>();
        var exam = new Exam { Title = "PoWi", ClassName = "11 BG", Date = new DateOnly(2025, 5, 8), Tasks = new() { new(1, "Aufgabe 1", 25m) } };
        db.SaveExam(exam);
        var response = await _client.GetAsync($"/api/klausuren/{exam.Id}/deckblatt/docx");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("11 BG PoWi Klausur 08.05.2025.docx", response.Content.Headers.ContentDisposition!.FileNameStar);
    }

    [Fact]
    public async Task CoverSheetOperatorRecognition_FollowsSavedTaskTextAndAllowsManualSelection()
    {
        Assert.Equal(HttpStatusCode.OK, (await _client.PostAsJsonAsync("/api/setup/init", new
        {
            pin = "123456", recoveryKey = KeyManager.GenerateRecoveryKey(), inactivityTimeoutMinutes = 15
        })).StatusCode);
        var db = _factory.Services.GetRequiredService<DatabaseContext>();
        var exam = new Exam { Title = "Test", Tasks = new() { new(1, "Aufgabe 1", 25m) } };
        db.SaveExam(exam);
        var endpoint = $"/api/klausuren/{exam.Id}/deckblatt";
        var request = new SaveCoverSheetRequest("", "", false, true, true, true,
            new() { new(1, "Geben Sie die Aussagen wieder und diskutieren Sie die These.") }, new() { "erklaeren" });
        var response = await _client.PutAsJsonAsync(endpoint, request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var model = await response.Content.ReadFromJsonAsync<JsonElement>();
        var task = model.GetProperty("tasks")[0];
        Assert.Equal(new[] { "wiedergeben", "diskutieren" }, task.GetProperty("recognizedOperatorIds").EnumerateArray().Select(x => x.GetString()));
        Assert.Equal(new[] { "Geben", "wieder", "diskutieren" }, task.GetProperty("textParts").EnumerateArray()
            .Where(x => x.GetProperty("underlined").GetBoolean()).Select(x => x.GetProperty("text").GetString()));
        Assert.Equal(new[] { "erklaeren" }, db.GetExamById(exam.Id)!.CoverSheet!.Operators.Select(x => x.Id));
        Assert.Equal(HttpStatusCode.OK, (await _client.PutAsJsonAsync(endpoint, request with
        {
            OperatorIds = new() { "wiedergeben", "diskutieren" }, Tasks = new() { new(1, "Analysieren Sie die Aussage.") }
        })).StatusCode);
        var updated = await _client.GetFromJsonAsync<JsonElement>(endpoint);
        Assert.Equal("analysieren", updated.GetProperty("tasks")[0].GetProperty("recognizedOperatorIds")[0].GetString());
        Assert.Equal(new[] { "wiedergeben", "diskutieren" }, db.GetExamById(exam.Id)!.CoverSheet!.Operators.Select(x => x.Id));
    }

    [Fact]
    public async Task CoverSheetNotice_CanBeHiddenEditedShownAndRestored()
    {
        Assert.Equal(HttpStatusCode.OK, (await _client.PostAsJsonAsync("/api/setup/init", new
        {
            pin = "123456", recoveryKey = KeyManager.GenerateRecoveryKey(), inactivityTimeoutMinutes = 15
        })).StatusCode);
        var db = _factory.Services.GetRequiredService<DatabaseContext>();
        var exam = new Exam
        {
            Title = "Politik und Wirtschaft", ClassName = "11 BG-E", Date = new DateOnly(2026, 11, 2),
            Tasks = new() { new(1, "Aufgabe 1", 25m) }, GradingProfileSnapshot = GradingProfile.BeruflichesGymnasium()
        };
        db.SaveExam(exam);
        var endpoint = $"/api/klausuren/{exam.Id}/deckblatt";
        var request = new SaveCoverSheetRequest("", "", true, true, false, true,
            new() { new(1, "Aufgabe") }, new() { "erklaeren" }, "Individueller Hinweis.", true);
        Assert.Equal(HttpStatusCode.OK, (await _client.PutAsJsonAsync(endpoint, request)).StatusCode);
        var hidden = await _client.GetFromJsonAsync<JsonElement>(endpoint);
        Assert.False(hidden.GetProperty("settings").GetProperty("showDeductionRules").GetBoolean());
        Assert.Equal("Individueller Hinweis.", hidden.GetProperty("deductionNotice").GetString());
        async Task<string> ExportText()
        {
            using var stream = new MemoryStream(await _client.GetByteArrayAsync(endpoint + "/docx"));
            using var document = DocumentFormat.OpenXml.Packaging.WordprocessingDocument.Open(stream, false);
            return document.MainDocumentPart!.Document!.Body!.InnerText;
        }
        Assert.DoesNotContain("Individueller Hinweis.", await ExportText());
        Assert.Equal(HttpStatusCode.OK, (await _client.PutAsJsonAsync(endpoint, request with { ShowDeductionRules = true })).StatusCode);
        var shown = await _client.GetFromJsonAsync<JsonElement>(endpoint);
        Assert.True(shown.GetProperty("settings").GetProperty("showDeductionRules").GetBoolean());
        Assert.Contains("Individueller Hinweis.", await ExportText());
        Assert.Equal(HttpStatusCode.OK, (await _client.PutAsJsonAsync(endpoint, request with { ShowDeductionRules = true, DeductionNotice = null })).StatusCode);
        var reset = await _client.GetFromJsonAsync<JsonElement>(endpoint);
        Assert.Equal(reset.GetProperty("defaultDeductionNotice").GetString(), reset.GetProperty("deductionNotice").GetString());
        Assert.Contains(reset.GetProperty("deductionNotice").GetString()!, await ExportText());
    }

    [Fact]
    public async Task ExternalHost_ShouldReturn403Forbidden()
    {
        using var externalClient = _factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/setup/status");
        request.Headers.Host = "malicious-site.com";

        var response = await externalClient.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task LockedApp_AccessingDataApis_Returns401Unauthorized()
    {
        // Try accessing classes when locked
        var response = await _client.GetAsync("/api/klassen");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.PostAsync("/api/application/shutdown", null)).StatusCode);
        var health = await _client.GetAsync("/api/health");
        Assert.Equal("Klausurfix/1", await health.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task SessionsAreIsolatedAndCrossOriginActionsAreRejected()
    {
        var recovery = KeyManager.GenerateRecoveryKey();
        Assert.Equal(HttpStatusCode.OK, (await _client.PostAsJsonAsync("/api/setup/init", new
            { pin = "123456", recoveryKey = recovery, inactivityTimeoutMinutes = 15 })).StatusCode);
        using var other = _factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("http://localhost:5000") });
        Assert.Equal(HttpStatusCode.Unauthorized, (await other.GetAsync("/api/klassen")).StatusCode);
        using var foreign = new HttpRequestMessage(HttpMethod.Post, "/api/auth/lock");
        foreign.Headers.Add("Origin", "https://attacker.example");
        Assert.Equal(HttpStatusCode.Forbidden, (await _client.SendAsync(foreign)).StatusCode);
        using var foreignShutdown = new HttpRequestMessage(HttpMethod.Post, "/api/application/shutdown");
        foreignShutdown.Headers.Add("Origin", "https://attacker.example");
        Assert.Equal(HttpStatusCode.Forbidden, (await _client.SendAsync(foreignShutdown)).StatusCode);
        other.DefaultRequestHeaders.Add("X-Requested-With", "XMLHttpRequest");
        Assert.Equal(HttpStatusCode.Unauthorized, (await other.PostAsync("/api/application/shutdown", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotImplemented, (await _client.PostAsync("/api/auth/unlock-hello", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync("/api/klassen")).StatusCode);
    }

    [Fact]
    public async Task HelloApiCanRegisterLockAndUnlockWithSignedCredential()
    {
        Assert.Equal(HttpStatusCode.OK, (await _client.PostAsJsonAsync("/api/setup/init", new
        {
            pin = "123456", recoveryKey = KeyManager.GenerateRecoveryKey(), inactivityTimeoutMinutes = 15
        })).StatusCode);
        using var key = System.Security.Cryptography.ECDsa.Create(System.Security.Cryptography.ECCurve.NamedCurves.nistP256);
        var credentialId = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);
        var prf = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
        var beginResponse = await _client.PostAsJsonAsync("/api/auth/hello/register/options", new { pin = "123456" });
        Assert.Equal(HttpStatusCode.OK, beginResponse.StatusCode);
        var begin = await beginResponse.Content.ReadFromJsonAsync<JsonElement>();
        var createOptions = Fido2NetLib.CredentialCreateOptions.FromJson(begin.GetProperty("options").GetRawText());
        var attestation = KeyManagerAndBackupTests.CreateAttestation(key, credentialId, createOptions.Challenge);
        var registerResponse = await _client.PostAsJsonAsync("/api/auth/hello/register/credential", new
        {
            token = begin.GetProperty("token").GetString(), credential = attestation
        });
        Assert.Equal(HttpStatusCode.OK, registerResponse.StatusCode);
        var register = await registerResponse.Content.ReadFromJsonAsync<JsonElement>();
        var confirmOptions = Fido2NetLib.AssertionOptions.FromJson(register.GetProperty("options").GetRawText());
        var confirm = KeyManagerAndBackupTests.CreateAssertion(key, credentialId, confirmOptions.Challenge, 1);
        var confirmResponse = await _client.PostAsJsonAsync("/api/auth/hello/register/complete", new
        {
            token = register.GetProperty("token").GetString(), credential = confirm, prf
        });
        Assert.Equal(HttpStatusCode.OK, confirmResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _client.PostAsync("/api/auth/lock", null)).StatusCode);
        var unlockResponse = await _client.PostAsync("/api/auth/hello/unlock/options", null);
        Assert.Equal(HttpStatusCode.OK, unlockResponse.StatusCode);
        var unlock = await unlockResponse.Content.ReadFromJsonAsync<JsonElement>();
        var unlockOptions = Fido2NetLib.AssertionOptions.FromJson(unlock.GetProperty("options").GetRawText());
        var assertion = KeyManagerAndBackupTests.CreateAssertion(key, credentialId, unlockOptions.Challenge, 2);
        var completeResponse = await _client.PostAsJsonAsync("/api/auth/hello/unlock/complete", new
        {
            token = unlock.GetProperty("token").GetString(), credential = assertion, prf
        });
        Assert.Equal(HttpStatusCode.OK, completeResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync("/api/klassen")).StatusCode);
    }

    [Fact]
    public async Task HelloRegistrationRequiresSessionAndPinAndRejectsOtherOrigins()
    {
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await _client.PostAsJsonAsync("/api/auth/hello/register/options", new { pin = "123456" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _client.PostAsJsonAsync("/api/setup/init", new
        {
            pin = "123456", recoveryKey = KeyManager.GenerateRecoveryKey(), inactivityTimeoutMinutes = 15
        })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await _client.PostAsJsonAsync("/api/auth/hello/register/options", new { pin = "654321" })).StatusCode);
        using var foreign = new HttpRequestMessage(HttpMethod.Post, "/api/auth/hello/register/options")
        {
            Content = JsonContent.Create(new { pin = "123456" })
        };
        foreign.Headers.Add("Origin", "https://attacker.example");
        Assert.Equal(HttpStatusCode.Forbidden, (await _client.SendAsync(foreign)).StatusCode);
        var options = await _client.PostAsJsonAsync("/api/auth/hello/register/options", new { pin = "123456" });
        Assert.Equal(HttpStatusCode.OK, options.StatusCode);
        var data = await options.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(string.IsNullOrEmpty(data.GetProperty("token").GetString()));
        Assert.Equal("required", data.GetProperty("options").GetProperty("authenticatorSelection").GetProperty("userVerification").GetString());
        Assert.False(_factory.Services.GetRequiredService<KeyManager>().IsHelloRegistered);
    }

    [Fact]
    public async Task FullEndToEndFlow_SetupUnlockClassExamExport()
    {
        // 1. Check Setup Status
        var statusRes = await _client.GetAsync("/api/setup/status");
        Assert.Equal(HttpStatusCode.OK, statusRes.StatusCode);
        var initialStatus = await statusRes.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(initialStatus.GetProperty("isInitialized").GetBoolean());

        // 2. Generate Recovery Key
        var recKeyRes = await _client.GetFromJsonAsync<JsonElement>("/api/setup/generate-recovery-key");
        var recoveryKey = recKeyRes.GetProperty("recoveryKey").GetString();
        Assert.False(string.IsNullOrEmpty(recoveryKey));

        // 3. Init Setup
        var initRes = await _client.PostAsJsonAsync("/api/setup/init", new
        {
            pin = "123456",
            recoveryKey = recoveryKey,
            inactivityTimeoutMinutes = 15
        });
        Assert.Equal(HttpStatusCode.OK, initRes.StatusCode);
        var setupStatus = await _client.GetFromJsonAsync<JsonElement>("/api/setup/status");
        Assert.True(setupStatus.GetProperty("isInitialized").GetBoolean());
        Assert.True(setupStatus.GetProperty("isUnlocked").GetBoolean());
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.PostAsJsonAsync("/api/setup/init", new
        {
            pin = "654321", recoveryKey, inactivityTimeoutMinutes = 15
        })).StatusCode);

        // 4. Access Schulformen (already unlocked upon setup)
        var sfList = await _client.GetFromJsonAsync<List<Schulform>>("/api/schulformen");
        Assert.NotNull(sfList);
        Assert.True(sfList.Count >= 2);
        var sek1 = sfList.First(s => s.Profile.System == GradingSystem.Schulnoten);

        // 5. Create Class
        var clsRes = await _client.PostAsJsonAsync("/api/klassen", new
        {
            name = "8a",
            schulformId = sek1.Id,
            schulformName = sek1.Name
        });
        Assert.Equal(HttpStatusCode.OK, clsRes.StatusCode);
        var cls = await clsRes.Content.ReadFromJsonAsync<ClassGroup>();
        Assert.NotNull(cls);

        // 6. Add Student
        var stRes = await _client.PostAsJsonAsync($"/api/klassen/{cls.Id}/students", new
        {
            firstName = "Julian"
        });
        Assert.Equal(HttpStatusCode.OK, stRes.StatusCode);
        var student = await stRes.Content.ReadFromJsonAsync<Student>();
        Assert.NotNull(student);
        Assert.Equal("Julian", student.FullName);
        var students = await _client.GetFromJsonAsync<List<Student>>($"/api/klassen/{cls.Id}/students");
        Assert.Equal("Julian", Assert.Single(students!).FullName);

        // 7. Create Exam
        var examRes = await _client.PostAsJsonAsync("/api/klausuren", new
        {
            classId = cls.Id,
            title = "Mathematik 1. SA",
            date = "2026-11-01",
            tasks = new[]
            {
                new { name = "Aufgabe 1", maxPoints = 40.0m },
                new { name = "Aufgabe 2", maxPoints = 60.0m }
            }
        });
        Assert.Equal(HttpStatusCode.OK, examRes.StatusCode);
        var exam = await examRes.Content.ReadFromJsonAsync<Exam>();
        Assert.NotNull(exam);
        Assert.Single(exam.StudentEntries);
        Assert.Equal("Julian", exam.StudentEntries[0].StudentName);

        // 8. Save Student Scores
        var entry = exam.StudentEntries[0];
        var updatedEntry = entry with
        {
            TaskPoints = new() { { 1, 36.0m }, { 2, 54.0m } }, // 90 / 100 -> 90%
            WordCount = 500,
            ErrorCount = 5, // FQ = 1.0 -> 0 deduction
            Status = StudentExamStatus.Graded
        };

        var invalid = updatedEntry with { TaskPoints = new() { [1] = -1m } };
        Assert.Equal(HttpStatusCode.BadRequest,
            (await _client.PostAsJsonAsync($"/api/klausuren/{exam.Id}/entries", new[] { invalid })).StatusCode);

        var saveRes = await _client.PostAsJsonAsync($"/api/klausuren/{exam.Id}/entries", new[] { updatedEntry });
        Assert.Equal(HttpStatusCode.OK, saveRes.StatusCode);

        // 9. Query Summary
        var summary = await _client.GetFromJsonAsync<ExamSummary>($"/api/klausuren/{exam.Id}/summary");
        Assert.NotNull(summary);
        Assert.Equal(1, summary.GradedCount);
        Assert.Equal(1, summary.ClassAverage); // 90% -> Grade 1
        Assert.Equal("Noten", summary.Unit);

        // 10. PDF & DOCX Export endpoints
        var pdfRes = await _client.GetAsync($"/api/klausuren/{exam.Id}/export/pdf");
        Assert.Equal(HttpStatusCode.OK, pdfRes.StatusCode);
        Assert.Equal("application/pdf", pdfRes.Content.Headers.ContentType?.MediaType);

        var docxRes = await _client.GetAsync($"/api/klausuren/{exam.Id}/export/docx");
        Assert.Equal(HttpStatusCode.OK, docxRes.StatusCode);

        // Downloaded files can be imported and replace later changes.
        var backupResponse = await _client.PostAsJsonAsync("/api/backups/download", new { recoveryKey });
        Assert.Equal(HttpStatusCode.OK, backupResponse.StatusCode);
        var backup = await backupResponse.Content.ReadAsByteArrayAsync();
        await _client.PostAsJsonAsync("/api/klassen", new { name = "Nach Sicherung", schulformId = sek1.Id, schulformName = sek1.Name });
        var restoreResponse = await _client.PostAsJsonAsync("/api/backups/restore-file", new
        {
            recoveryKey, dataBase64 = Convert.ToBase64String(backup)
        });
        Assert.Equal(HttpStatusCode.OK, restoreResponse.StatusCode);
        var restoredClasses = await _client.GetFromJsonAsync<List<ClassGroup>>("/api/klassen");
        Assert.Single(restoredClasses!);

        // 11. Lock and Verify 401
        var lockRes = await _client.PostAsync("/api/auth/lock", null);
        Assert.Equal(HttpStatusCode.OK, lockRes.StatusCode);

        var lockedCheck = await _client.GetAsync("/api/klassen");
        Assert.Equal(HttpStatusCode.Unauthorized, lockedCheck.StatusCode);

        // 12. A wrong PIN must leave the application locked.
        var wrongPin = await _client.PostAsJsonAsync("/api/auth/unlock-pin", new { pin = "654321" });
        Assert.Equal(HttpStatusCode.BadRequest, wrongPin.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.GetAsync("/api/klassen")).StatusCode);

        // 13. Unlock with the PIN chosen during setup.
        var unlockRes = await _client.PostAsJsonAsync("/api/auth/unlock-pin", new { pin = "123456" });
        Assert.Equal(HttpStatusCode.OK, unlockRes.StatusCode);

        var unlockedCheck = await _client.GetAsync("/api/klassen");
        Assert.Equal(HttpStatusCode.OK, unlockedCheck.StatusCode);

        var shutdown = await _client.PostAsync("/api/application/shutdown", null);
        Assert.Equal(HttpStatusCode.OK, shutdown.StatusCode);
        await shutdown.Content.ReadAsStringAsync();
        Assert.False(_factory.Services.GetRequiredService<KeyManager>().IsUnlocked);
        var stopping = _factory.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping;
        Assert.True(stopping.IsCancellationRequested || stopping.WaitHandle.WaitOne(TimeSpan.FromSeconds(2)));
    }
}

