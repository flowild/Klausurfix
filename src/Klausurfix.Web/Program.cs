using System.Globalization;
using System.Text.Json;
using Klausurfix.Core.Calculations;
using Klausurfix.Core.Models;
using Klausurfix.Infrastructure.Backup;
using Klausurfix.Infrastructure.Export;
using Klausurfix.Infrastructure.Security;
using Klausurfix.Infrastructure.Storage;
using Microsoft.AspNetCore.Http.Json;

var builder = WebApplication.CreateBuilder(args);

// Ensure local loopback binding only
builder.WebHost.ConfigureKestrel(options =>
{
    // Bind strictly to loopback 127.0.0.1
    options.Listen(System.Net.IPAddress.Loopback, 5000);
});

// Configure JSON serialization
builder.Services.Configure<JsonOptions>(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
});

// Register Core & Infrastructure services
builder.Services.AddSingleton<KeyManager>();
builder.Services.AddSingleton<DatabaseContext>();
builder.Services.AddSingleton<BackupService>();
builder.Services.AddSingleton<CleanupService>();
builder.Services.AddSingleton<PdfExportService>();
builder.Services.AddSingleton<DocxExportService>();

var app = builder.Build();

// Ensure local directories exist
AppPaths.EnsureDirectoriesExist();

// Security Middleware: Loopback host validation & DNS rebinding protection
app.Use(async (context, next) =>
{
    var host = context.Request.Host.Host;
    if (host != "localhost" && host != "127.0.0.1" && host != "[::1]")
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        await context.Response.WriteAsync("Forbidden: Klausurfix is configured for local loopback only.");
        return;
    }

    // Cache-Control: no-store on all API routes to protect student data
    if (context.Request.Path.StartsWithSegments("/api"))
    {
        context.Response.Headers.CacheControl = "no-store, no-cache, must-revalidate";
        context.Response.Headers.Pragma = "no-cache";
    }

    await next();
});

// Authorization Middleware: check lock state on /api/ endpoints (except /api/setup and /api/auth)
app.Use(async (context, next) =>
{
    var path = context.Request.Path;
    if (path.StartsWithSegments("/api") &&
        !path.StartsWithSegments("/api/setup") &&
        !path.StartsWithSegments("/api/auth"))
    {
        var keyManager = context.RequestServices.GetRequiredService<KeyManager>();
        if (!keyManager.IsInitialized || !keyManager.IsUnlocked)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { error = "Anwendung ist gesperrt oder noch nicht eingerichtet." });
            return;
        }

        keyManager.RecordActivity();
    }

    await next();
});

// Serve frontend from wwwroot
app.UseDefaultFiles();
app.UseStaticFiles();

#region Setup & Auth APIs

app.MapGet("/api/setup/status", (KeyManager km) =>
{
    return Results.Ok(new
    {
        isInitialized = km.IsInitialized,
        isUnlocked = km.IsUnlocked,
        isHelloRegistered = km.IsHelloRegistered,
        inactivityTimeoutMinutes = km.InactivityTimeoutMinutes
    });
});

app.MapGet("/api/setup/generate-recovery-key", () =>
{
    return Results.Ok(new { recoveryKey = KeyManager.GenerateRecoveryKey() });
});

app.MapPost("/api/setup/init", (InitRequest req, KeyManager km, DatabaseContext db) =>
{
    if (km.IsInitialized)
        return Results.BadRequest(new { error = "Die Anwendung ist bereits eingerichtet." });

    if (string.IsNullOrWhiteSpace(req.Pin) || req.Pin.Length < 6)
        return Results.BadRequest(new { error = "PIN muss mindestens 6 Stellen lang sein." });

    if (string.IsNullOrWhiteSpace(req.RecoveryKey))
        return Results.BadRequest(new { error = "Wiederherstellungsschlüssel ist erforderlich." });

    km.Setup(req.Pin, req.RecoveryKey, req.InactivityTimeoutMinutes ?? 15);
    db.InitializeDatabase();

    // Create default Schulformen for a smooth first start
    var defaultSek1 = new Schulform
    {
        Name = "Sekundarstufe I (Noten 1–6)",
        Profile = GradingProfile.CreateDefaultSchulnoten()
    };
    db.SaveSchulform(defaultSek1);

    var defaultSek2 = new Schulform
    {
        Name = "Gymnasiale Oberstufe (Punkte 0–15)",
        Profile = GradingProfile.CreateDefaultNotenpunkte()
    };
    db.SaveSchulform(defaultSek2);

    return Results.Ok(new { success = true });
});

app.MapPost("/api/auth/unlock-pin", (PinUnlockRequest req, KeyManager km) =>
{
    if (!km.IsInitialized)
        return Results.BadRequest(new { error = "Anwendung noch nicht eingerichtet." });

    var (success, msg, lockoutSec) = km.UnlockWithPin(req.Pin ?? "");
    if (!success)
    {
        return Results.Json(new { success = false, message = msg, lockoutSeconds = lockoutSec }, statusCode: 400);
    }

    return Results.Ok(new { success = true, message = msg });
});

app.MapPost("/api/auth/unlock-recovery", (RecoveryUnlockRequest req, KeyManager km) =>
{
    if (!km.IsInitialized)
        return Results.BadRequest(new { error = "Anwendung noch nicht eingerichtet." });

    var (success, msg) = km.UnlockWithRecoveryKey(req.RecoveryKey ?? "");
    if (!success)
        return Results.BadRequest(new { success = false, message = msg });

    return Results.Ok(new { success = true, message = msg });
});

app.MapPost("/api/auth/unlock-hello", (KeyManager km) =>
{
    if (!km.IsInitialized)
        return Results.BadRequest(new { error = "Anwendung noch nicht eingerichtet." });

    var (success, msg) = km.UnlockWithDpapiSession();
    if (!success)
        return Results.BadRequest(new { success = false, message = msg });

    return Results.Ok(new { success = true, message = msg });
});

app.MapPost("/api/auth/lock", (KeyManager km) =>
{
    km.Lock();
    return Results.Ok(new { success = true });
});

app.MapPost("/api/auth/change-pin", (ChangePinRequest req, KeyManager km) =>
{
    try
    {
        km.ChangePin(req.OldPin, req.NewPin);
        return Results.Ok(new { success = true, message = "PIN erfolgreich geändert." });
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

app.MapPost("/api/auth/hello/register", (KeyManager km) =>
{
    try
    {
        // Register Hello / DPAPI token for current Windows user
        km.RegisterHello("windows-hello-local", "local-key");
        return Results.Ok(new { success = true, message = "Windows Hello erfolgreich registriert." });
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

app.MapPost("/api/auth/hello/remove", (KeyManager km) =>
{
    km.RemoveHello();
    return Results.Ok(new { success = true, message = "Windows Hello entfernt." });
});

#endregion

#region Schulformen APIs

app.MapGet("/api/schulformen", (DatabaseContext db, bool? includeArchived) =>
{
    var list = db.GetSchulformen(includeArchived ?? false);
    return Results.Ok(list);
});

app.MapGet("/api/schulformen/{id}", (DatabaseContext db, string id) =>
{
    var sf = db.GetSchulformById(id);
    return sf != null ? Results.Ok(sf) : Results.NotFound();
});

app.MapPost("/api/schulformen", (Schulform sf, DatabaseContext db) =>
{
    try
    {
        sf.Profile.Validate();
        db.SaveSchulform(sf);
        return Results.Ok(sf);
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

app.MapDelete("/api/schulformen/{id}", (DatabaseContext db, string id) =>
{
    db.DeleteSchulform(id);
    return Results.Ok(new { success = true });
});

app.MapPost("/api/schulformen/preview-deduction", (DeductionPreviewRequest req) =>
{
    var fq = ExamCalculator.CalculateErrorQuotient(req.Words, req.Errors);
    var deduction = ExamCalculator.CalculateDeduction(fq, req.Tiers, req.MaxDeduction);
    return Results.Ok(new
    {
        errorQuotient = fq,
        deduction = deduction
    });
});

app.MapPost("/api/schulformen/preview-grade", (GradePreviewRequest req) =>
{
    var (initGrade, initLabel) = ExamCalculator.CalculateInitialGrade(req.Percentage, req.GradeKey, req.System);
    var (finalGrade, finalLabel) = ExamCalculator.CalculateFinalGrade(initGrade, req.Deduction, req.System);
    return Results.Ok(new
    {
        initialGrade = initGrade,
        initialGradeLabel = initLabel,
        finalGrade = finalGrade,
        finalGradeLabel = finalLabel
    });
});

#endregion

#region Klassen & Schüler APIs

app.MapGet("/api/klassen", (DatabaseContext db, bool? includeArchived) =>
{
    var list = db.GetClasses(includeArchived ?? false);
    return Results.Ok(list);
});

app.MapGet("/api/klassen/{id}", (DatabaseContext db, string id) =>
{
    var cls = db.GetClassById(id);
    return cls != null ? Results.Ok(cls) : Results.NotFound();
});

app.MapPost("/api/klassen", (ClassGroup cls, DatabaseContext db) =>
{
    db.SaveClass(cls);
    return Results.Ok(cls);
});

app.MapDelete("/api/klassen/{id}", (DatabaseContext db, string id) =>
{
    db.DeleteClass(id);
    return Results.Ok(new { success = true });
});

app.MapGet("/api/klassen/{id}/students", (DatabaseContext db, string id, bool? includeArchived) =>
{
    var list = db.GetStudentsByClass(id, includeArchived ?? false);
    return Results.Ok(list);
});

app.MapPost("/api/klassen/{id}/students", (string id, Student student, DatabaseContext db) =>
{
    var st = student with { ClassId = id };
    db.SaveStudent(st);
    return Results.Ok(st);
});

app.MapDelete("/api/klassen/{classId}/students/{studentId}", (DatabaseContext db, string classId, string studentId) =>
{
    db.DeleteStudent(studentId);
    return Results.Ok(new { success = true });
});

#endregion

#region Klausuren APIs

app.MapGet("/api/klausuren", (DatabaseContext db) =>
{
    var list = db.GetExams();
    return Results.Ok(list);
});

app.MapGet("/api/klausuren/{id}", (DatabaseContext db, string id) =>
{
    var exam = db.GetExamById(id);
    return exam != null ? Results.Ok(exam) : Results.NotFound();
});

app.MapPost("/api/klausuren", (CreateExamRequest req, DatabaseContext db) =>
{
    var cls = db.GetClassById(req.ClassId);
    if (cls == null) return Results.BadRequest(new { error = "Klasse nicht gefunden." });

    var sf = db.GetSchulformById(cls.SchulformId);
    if (sf == null) return Results.BadRequest(new { error = "Schulform der Klasse nicht gefunden." });

    var students = db.GetStudentsByClass(cls.Id);

    // Create tasks
    var tasks = new List<TaskDefinition>();
    for (int i = 0; i < req.Tasks.Count; i++)
    {
        var t = req.Tasks[i];
        tasks.Add(new TaskDefinition(i + 1, t.Name, t.MaxPoints));
    }

    // Freeze snapshot of students and grading profile!
    var entries = students.Select(s => new ExamStudentEntry
    {
        StudentId = s.Id,
        StudentName = s.FullName,
        Status = StudentExamStatus.Graded,
        TaskPoints = new(),
        WordCount = null,
        ErrorCount = null
    }).ToList();

    var exam = new Exam
    {
        ClassId = cls.Id,
        ClassName = cls.Name,
        SchulformId = sf.Id,
        SchulformName = sf.Name,
        Title = req.Title,
        Date = req.Date,
        Tasks = tasks,
        GradingProfileSnapshot = sf.Profile, // Frozen snapshot of rules
        StudentEntries = entries
    };

    db.SaveExam(exam);
    return Results.Ok(exam);
});

app.MapDelete("/api/klausuren/{id}", (DatabaseContext db, string id) =>
{
    db.DeleteExam(id);
    return Results.Ok(new { success = true });
});

app.MapPost("/api/klausuren/{id}/entries", (string id, List<ExamStudentEntry> entries, DatabaseContext db) =>
{
    var exam = db.GetExamById(id);
    if (exam == null) return Results.NotFound();

    var updatedExam = exam with { StudentEntries = entries, UpdatedAt = DateTimeOffset.UtcNow };
    db.SaveExam(updatedExam);
    return Results.Ok(new { success = true, savedAt = updatedExam.UpdatedAt });
});

app.MapPost("/api/klausuren/{id}/recalculate", (string id, RecalculateRequest? req, DatabaseContext db) =>
{
    var exam = db.GetExamById(id);
    if (exam == null) return Results.NotFound();

    GradingProfile profile;
    if (req?.NewProfile != null)
    {
        req.NewProfile.Validate();
        profile = req.NewProfile;
    }
    else
    {
        // Re-read Schulform profile from master data
        var sf = db.GetSchulformById(exam.SchulformId);
        profile = sf?.Profile ?? exam.GradingProfileSnapshot;
    }

    var updatedExam = exam with { GradingProfileSnapshot = profile, UpdatedAt = DateTimeOffset.UtcNow };
    db.SaveExam(updatedExam);

    var summary = ExamCalculator.CalculateExamSummary(updatedExam);
    return Results.Ok(new { exam = updatedExam, summary });
});

app.MapGet("/api/klausuren/{id}/summary", (string id, DatabaseContext db) =>
{
    var exam = db.GetExamById(id);
    if (exam == null) return Results.NotFound();

    var summary = ExamCalculator.CalculateExamSummary(exam);
    return Results.Ok(summary);
});

app.MapGet("/api/klausuren/{id}/export/pdf", (string id, DatabaseContext db, PdfExportService pdfService) =>
{
    var exam = db.GetExamById(id);
    if (exam == null) return Results.NotFound();

    var pdfBytes = pdfService.GenerateExamReport(exam);
    var fileName = $"Klausur_{exam.ClassName}_{exam.Title.Replace(" ", "_")}.pdf";
    return Results.File(pdfBytes, "application/pdf", fileName);
});

app.MapGet("/api/klausuren/{id}/export/docx", (string id, DatabaseContext db, DocxExportService docxService) =>
{
    var exam = db.GetExamById(id);
    if (exam == null) return Results.NotFound();

    var docxBytes = docxService.GenerateExamDocument(exam);
    var fileName = $"Klausur_{exam.ClassName}_{exam.Title.Replace(" ", "_")}.docx";
    return Results.File(docxBytes, "application/vnd.openxmlformats-officedocument.wordprocessingml.document", fileName);
});

#endregion

#region Settings & Backup APIs

app.MapPost("/api/settings/timeout", (TimeoutRequest req, KeyManager km) =>
{
    km.InactivityTimeoutMinutes = req.TimeoutMinutes;
    return Results.Ok(new { success = true, timeoutMinutes = km.InactivityTimeoutMinutes });
});

app.MapGet("/api/backups", (BackupService backupService) =>
{
    var list = backupService.GetManagedBackups();
    return Results.Ok(list);
});

app.MapPost("/api/backups/create", (BackupCreateRequest req, BackupService backupService) =>
{
    try
    {
        var info = backupService.CreateManagedBackup(req.RecoveryKey);
        return Results.Ok(info);
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

app.MapPost("/api/backups/download", (BackupCreateRequest req, BackupService backupService) =>
{
    try
    {
        var bytes = backupService.CreateBackupBytes(req.RecoveryKey);
        var fileName = $"klausurfix_backup_{DateTime.UtcNow:yyyyMMdd_HHmmss}.kfbak";
        return Results.File(bytes, "application/octet-stream", fileName);
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

app.MapPost("/api/backups/{fileName}/restore", (string fileName, BackupRestoreRequest req, BackupService backupService) =>
{
    try
    {
        backupService.RestoreManagedBackup(fileName, req.RecoveryKey);
        return Results.Ok(new { success = true, message = "Sicherung erfolgreich wiederhergestellt." });
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

app.MapDelete("/api/backups/{fileName}", (string fileName, BackupService backupService) =>
{
    backupService.DeleteManagedBackup(fileName);
    return Results.Ok(new { success = true });
});

app.MapPost("/api/settings/danger/clear-active-data", (DangerActionRequest req, KeyManager km, CleanupService cleanup) =>
{
    // Re-verify PIN for destructive actions
    var (success, msg, _) = km.UnlockWithPin(req.Pin);
    if (!success)
        return Results.BadRequest(new { error = "Falsche PIN. Destruktive Aktion abgebrochen." });

    cleanup.ClearActiveApplicationData();
    return Results.Ok(new { success = true, message = "Aktive Anwendungsdaten wurden gelöscht. Sicherungen blieben erhalten." });
});

app.MapPost("/api/settings/danger/delete-all-backups", (DangerActionRequest req, KeyManager km, CleanupService cleanup) =>
{
    var (success, msg, _) = km.UnlockWithPin(req.Pin);
    if (!success)
        return Results.BadRequest(new { error = "Falsche PIN. Destruktive Aktion abgebrochen." });

    int count = cleanup.DeleteAllManagedBackups();
    return Results.Ok(new { success = true, deletedCount = count, message = $"{count} Sicherungen wurden gelöscht." });
});

app.MapPost("/api/settings/danger/full-reset", (DangerActionRequest req, KeyManager km, CleanupService cleanup) =>
{
    var (success, msg, _) = km.UnlockWithPin(req.Pin);
    if (!success)
        return Results.BadRequest(new { error = "Falsche PIN. Vollständiges Zurücksetzen abgebrochen." });

    cleanup.FullReset();
    return Results.Ok(new { success = true, message = "Anwendung wurde vollständig zurückgesetzt." });
});

#endregion

app.Run();

#region Request DTOs

public record InitRequest(string Pin, string RecoveryKey, int? InactivityTimeoutMinutes);
public record PinUnlockRequest(string Pin);
public record RecoveryUnlockRequest(string RecoveryKey);
public record ChangePinRequest(string OldPin, string NewPin);
public record TimeoutRequest(int TimeoutMinutes);
public record BackupCreateRequest(string RecoveryKey);
public record BackupRestoreRequest(string RecoveryKey);
public record DangerActionRequest(string Pin);
public record DeductionPreviewRequest(int? Words, int? Errors, List<DeductionTier> Tiers, decimal? MaxDeduction);
public record GradePreviewRequest(decimal Percentage, List<GradeBoundary> GradeKey, GradingSystem System, decimal Deduction);
public record CreateExamTaskDto(string Name, decimal MaxPoints);
public record CreateExamRequest(string ClassId, string Title, DateOnly Date, List<CreateExamTaskDto> Tasks);
public record RecalculateRequest(GradingProfile? NewProfile);

#endregion

// Make Program public for WebApplicationFactory in integration tests
public partial class Program { }
