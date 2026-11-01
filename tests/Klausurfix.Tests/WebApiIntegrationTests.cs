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
                new { name = "Aufgabe 1", maxPoints = 20.0m },
                new { name = "Aufgabe 2", maxPoints = 30.0m }
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
            TaskPoints = new() { { 1, 18.0m }, { 2, 27.0m } }, // 45 / 50 -> 90%
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
        Assert.False(_factory.Services.GetRequiredService<KeyManager>().IsUnlocked);
        Assert.True(_factory.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping.IsCancellationRequested);
    }
}

