namespace Klausurfix.Tests;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Klausurfix.Core.Models;
using Klausurfix.Infrastructure.Security;
using Klausurfix.Infrastructure.Storage;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
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
    }

    [Fact]
    public async Task FullEndToEndFlow_SetupUnlockClassExamExport()
    {
        // 1. Check Setup Status
        var statusRes = await _client.GetAsync("/api/setup/status");
        Assert.Equal(HttpStatusCode.OK, statusRes.StatusCode);

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
            firstName = "Julian",
            lastName = "Weber"
        });
        Assert.Equal(HttpStatusCode.OK, stRes.StatusCode);
        var student = await stRes.Content.ReadFromJsonAsync<Student>();
        Assert.NotNull(student);

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

        // 8. Save Student Scores
        var entry = exam.StudentEntries[0];
        var updatedEntry = entry with
        {
            TaskPoints = new() { { 1, 18.0m }, { 2, 27.0m } }, // 45 / 50 -> 90%
            WordCount = 500,
            ErrorCount = 5, // FQ = 1.0 -> 0 deduction
            Status = StudentExamStatus.Graded
        };

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

        // 11. Lock and Verify 401
        var lockRes = await _client.PostAsync("/api/auth/lock", null);
        Assert.Equal(HttpStatusCode.OK, lockRes.StatusCode);

        var lockedCheck = await _client.GetAsync("/api/klassen");
        Assert.Equal(HttpStatusCode.Unauthorized, lockedCheck.StatusCode);

        // 12. Unlock with PIN
        var unlockRes = await _client.PostAsJsonAsync("/api/auth/unlock-pin", new { pin = "123456" });
        Assert.Equal(HttpStatusCode.OK, unlockRes.StatusCode);

        var unlockedCheck = await _client.GetAsync("/api/klassen");
        Assert.Equal(HttpStatusCode.OK, unlockedCheck.StatusCode);
    }
}

