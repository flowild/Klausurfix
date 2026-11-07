using System.Globalization;
using System.Text.Json;
using System.Security.Cryptography;
using Fido2NetLib;
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
builder.Services.AddSingleton<ILocalKeyProtector, UserKeyProtector>();
builder.Services.AddSingleton<KeyManager>();
builder.Services.AddSingleton<LocalSessions>();
builder.Services.AddSingleton<UpdateWriteGate>();
builder.Services.AddSingleton<UpdateViewRegistry>();
builder.Services.AddSingleton(services => new UpdatePreparation(
    services.GetRequiredService<UpdateWriteGate>(),
    services.GetRequiredService<BackupService>().CreateUpdateBackup));
builder.Services.AddSingleton<HelloCeremonies>();
builder.Services.AddSingleton<DatabaseContext>();
builder.Services.AddSingleton<BackupService>();
builder.Services.AddSingleton<AutomaticBackupWorker>();
builder.Services.AddHostedService(services => services.GetRequiredService<AutomaticBackupWorker>());
builder.Services.AddHostedService<TrayShutdownWorker>();
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

    var origin = context.Request.Headers.Origin.ToString();
    if ((!string.IsNullOrEmpty(origin) &&
        (!Uri.TryCreate(origin, UriKind.Absolute, out var originUri) ||
         originUri.Scheme != context.Request.Scheme || originUri.Authority != context.Request.Host.Value)) ||
        context.Request.Headers["Sec-Fetch-Site"] == "cross-site")
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        return;
    }
    if (context.Request.Path.StartsWithSegments("/api") &&
        !HttpMethods.IsGet(context.Request.Method) &&
        context.Request.Headers["X-Requested-With"] != "XMLHttpRequest")
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        return;
    }

    // Cache-Control: no-store on all API routes to protect student data
    if (context.Request.Path.StartsWithSegments("/api"))
    {
        context.Response.Headers.CacheControl = "no-store, no-cache, must-revalidate";
        context.Response.Headers.Pragma = "no-cache";
    }

    try { await next(); }
    catch (ArchivedExamReadOnlyException)
    {
        context.Response.StatusCode = StatusCodes.Status409Conflict;
        await context.Response.WriteAsJsonAsync(new { error = ArchivedExamReadOnlyException.Description });
    }
});

// Coordination calls do not modify application data and must remain available
// while a snapshot is taken, so view changes can invalidate the round.
app.Use(async (context, next) =>
{
    var path = context.Request.Path;
    bool coordination = path == "/api/update/view/register" || path == "/api/update/view/poll" ||
        path == "/api/update/view/respond" || path == "/api/update/view/unregister" ||
        path == "/api/update/save-round/begin" || path == "/api/update/save-round/status" ||
        path == "/api/update/save-round/cancel" || path == "/api/update/coordinated-backup";
    if (!context.Request.Path.StartsWithSegments("/api") || HttpMethods.IsGet(context.Request.Method) ||
        HttpMethods.IsHead(context.Request.Method) || HttpMethods.IsOptions(context.Request.Method) || coordination)
    {
        await next();
        return;
    }
    var gate = context.RequestServices.GetRequiredService<UpdateWriteGate>();
    using var lease = await gate.EnterWriteAsync(context.RequestAborted);
    if (lease is null)
    {
        context.Response.StatusCode = StatusCodes.Status423Locked;
        await context.Response.WriteAsJsonAsync(new { error = "Klausurfix wird für ein Update vorbereitet. Bitte anschließend erneut versuchen." });
        return;
    }
    await next();
});

// Authorization Middleware: check lock state on /api/ endpoints (except /api/setup and /api/auth)
app.Use(async (context, next) =>
{
    var path = context.Request.Path;
    bool isPublic = path == "/api/health" || path == "/api/setup/status" || path == "/api/setup/generate-recovery-key" ||
        path == "/api/setup/init" || path == "/api/update/view/register" ||
        path == "/api/update/view/poll" || path == "/api/update/view/respond" || path == "/api/update/view/unregister" ||
        path == "/api/auth/unlock-pin" ||
        path == "/api/auth/unlock-recovery" || path == "/api/auth/unlock-hello" ||
        path == "/api/auth/hello/unlock/options" || path == "/api/auth/hello/unlock/complete";
    if (path.StartsWithSegments("/api") && !isPublic)
    {
        var keyManager = context.RequestServices.GetRequiredService<KeyManager>();
        var sessions = context.RequestServices.GetRequiredService<LocalSessions>();
        if (!keyManager.IsInitialized || !keyManager.IsUnlocked || !sessions.IsValid(context))
        {
            if (!keyManager.IsUnlocked) sessions.RevokeAll(context);
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
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = context =>
    {
        // Revalidate frontend files so updates also reach an existing browser cache.
        context.Context.Response.Headers.CacheControl = "no-cache";
    }
});

#region Setup & Auth APIs

// Used by the local launcher to distinguish this instance from another service.
app.MapGet("/api/health", () => Results.Text("Klausurfix/1", "text/plain"));

app.MapPost("/api/application/shutdown", (HttpContext context, KeyManager km, LocalSessions sessions, IHostApplicationLifetime lifetime) =>
{
    km.Lock();
    sessions.RevokeAll(context);
    context.Response.OnCompleted(() =>
    {
        lifetime.StopApplication();
        return Task.CompletedTask;
    });
    return Results.Ok(new { success = true });
});

app.MapGet("/api/setup/status", (HttpContext context, KeyManager km, LocalSessions sessions) =>
{
    return Results.Ok(new
    {
        isInitialized = km.IsInitialized,
        isUnlocked = km.IsUnlocked && sessions.IsValid(context),
        isHelloRegistered = km.IsHelloRegistered,
        isHelloAvailable = true,
        inactivityTimeoutMinutes = km.InactivityTimeoutMinutes
    });
});

app.MapGet("/api/setup/generate-recovery-key", () =>
{
    return Results.Ok(new { recoveryKey = KeyManager.GenerateRecoveryKey() });
});

app.MapPost("/api/setup/init", (InitRequest req, HttpContext context, KeyManager km, DatabaseContext db, LocalSessions sessions, AutomaticBackupWorker automaticBackup) =>
{
    if (km.IsInitialized)
        return Results.BadRequest(new { error = "Die Anwendung ist bereits eingerichtet." });

    if (string.IsNullOrWhiteSpace(req.Pin) || req.Pin.Length is < 6 or > 64 || req.Pin.Any(c => c is < '0' or > '9'))
        return Results.BadRequest(new { error = "PIN muss aus 6 bis 64 Ziffern bestehen." });

    if (string.IsNullOrWhiteSpace(req.RecoveryKey))
        return Results.BadRequest(new { error = "Wiederherstellungsschlüssel ist erforderlich." });

    km.Setup(req.Pin, req.RecoveryKey, req.InactivityTimeoutMinutes ?? 15);
    db.InitializeDatabase();

    // Create default Schulformen for a smooth first start
    var defaultSek1 = new Schulform
    {
        Name = "Fachschule Sozialwesen",
        Profile = GradingProfile.FachschuleSozialwesen()
    };
    db.SaveSchulform(defaultSek1);

    var defaultSek2 = new Schulform
    {
        Name = "Berufliches Gymnasium",
        Profile = GradingProfile.BeruflichesGymnasium()
    };
    db.SaveSchulform(defaultSek2);
    db.EnsureRequestedSchoolProfiles();

    automaticBackup.ResetSchedule();
    automaticBackup.SetReady(true);
    sessions.Issue(context);
    return Results.Ok(new { success = true });
});

app.MapPost("/api/auth/unlock-pin", (PinUnlockRequest req, HttpContext context, KeyManager km, DatabaseContext db, LocalSessions sessions, AutomaticBackupWorker automaticBackup) =>
{
    automaticBackup.SetReady(false);
    if (!km.IsInitialized)
        return Results.BadRequest(new { error = "Anwendung noch nicht eingerichtet." });

    var (success, msg, lockoutSec) = km.UnlockWithPin(req.Pin ?? "");
    if (!success)
    {
        return Results.Json(new { success = false, message = msg, lockoutSeconds = lockoutSec }, statusCode: 400);
    }

    sessions.RevokeAll(context);
    try { db.InitializeDatabase(); db.MigrateLegacyPlaintext(); db.EnsureRequestedSchoolProfiles(); }
    catch { km.Lock(); return Results.Problem("Die Datenmigration konnte nicht abgeschlossen werden.", statusCode: 500); }
    automaticBackup.SetReady(true);
    sessions.Issue(context);
    return Results.Ok(new { success = true, message = msg });
});

app.MapPost("/api/auth/unlock-recovery", (RecoveryUnlockRequest req, HttpContext context, KeyManager km, DatabaseContext db, LocalSessions sessions, AutomaticBackupWorker automaticBackup) =>
{
    automaticBackup.SetReady(false);
    if (!km.IsInitialized)
        return Results.BadRequest(new { error = "Anwendung noch nicht eingerichtet." });

    var (success, msg) = km.UnlockWithRecoveryKey(req.RecoveryKey ?? "");
    if (!success)
        return Results.BadRequest(new { success = false, message = msg });

    sessions.RevokeAll(context);
    try { db.InitializeDatabase(); db.MigrateLegacyPlaintext(); db.EnsureRequestedSchoolProfiles(); }
    catch { km.Lock(); return Results.Problem("Die Datenmigration konnte nicht abgeschlossen werden.", statusCode: 500); }
    automaticBackup.SetReady(true);
    sessions.Issue(context);
    return Results.Ok(new { success = true, message = msg });
});

// The old DPAPI-only shortcut did not authenticate Windows Hello. Fail closed.
app.MapPost("/api/auth/unlock-hello", () => Results.Problem(
    "Windows Hello ist derzeit nicht verfügbar. Bitte verwenden Sie die App-PIN.", statusCode: 501));

// WebAuthn registration and authentication use a local platform authenticator.
{
    const string helloCookie = "klausurfix_hello_attempt";
    static string SessionBinding(HttpContext context) =>
        context.Request.Cookies[LocalSessions.CookieName] ?? throw new InvalidOperationException("Sitzung abgelaufen.");
    static bool CorrectHelloOrigin(HttpContext context) =>
        context.Request.Scheme == "http" && context.Request.Host.Value == "localhost:5000";

    app.MapPost("/api/auth/hello/register/options", (PinUnlockRequest request, HttpContext context, KeyManager km, HelloCeremonies hello) =>
    {
        if (!CorrectHelloOrigin(context)) return Results.BadRequest(new { error = "Bitte Klausurfix über http://localhost:5000 öffnen." });
        var (success, message, _) = km.UnlockWithPin(request.Pin ?? "");
        if (!success) return Results.BadRequest(new { error = message });
        try
        {
            var (token, options) = hello.BeginRegistration(SessionBinding(context));
            return Results.Ok(new { token, options = JsonDocument.Parse(options.ToJson()).RootElement.Clone() });
        }
        catch { return Results.BadRequest(new { error = "Die Einrichtung konnte nicht gestartet werden." }); }
    });

    app.MapPost("/api/auth/hello/register/credential", async (HelloRegistrationRequest request, HttpContext context, HelloCeremonies hello) =>
    {
        if (!CorrectHelloOrigin(context)) return Results.BadRequest(new { error = "Ungültige Adresse." });
        try
        {
            var (token, options, salt) = await hello.CompleteRegistrationAsync(request.Token, SessionBinding(context), request.Credential);
            return Results.Ok(new { token, options = JsonDocument.Parse(options.ToJson()).RootElement.Clone(), salt = Convert.ToBase64String(salt) });
        }
        catch { return Results.BadRequest(new { error = "Die Geräteanmeldung konnte nicht bestätigt werden. Wählen Sie im Passkey-Dialog Windows Hello beziehungsweise dieses Gerät und versuchen Sie es erneut." }); }
    });

    app.MapPost("/api/auth/hello/register/complete", async (HelloAssertionRequest request, HttpContext context, HelloCeremonies hello) =>
    {
        if (!CorrectHelloOrigin(context)) return Results.BadRequest(new { error = "Ungültige Adresse." });
        byte[]? prf = null;
        try
        {
            prf = Convert.FromBase64String(request.Prf);
            await hello.ConfirmRegistrationAsync(request.Token, SessionBinding(context), request.Credential, prf);
            return Results.Ok(new { success = true });
        }
        catch { return Results.BadRequest(new { error = "Die Einrichtung ist fehlgeschlagen. Die App-PIN bleibt verwendbar." }); }
        finally { if (prf is not null) CryptographicOperations.ZeroMemory(prf); }
    });

    app.MapPost("/api/auth/hello/unlock/options", (HttpContext context, HelloCeremonies hello) =>
    {
        if (!CorrectHelloOrigin(context)) return Results.BadRequest(new { error = "Bitte Klausurfix über http://localhost:5000 öffnen." });
        try
        {
            var binding = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            var (token, options, salt) = hello.BeginUnlock(binding);
            context.Response.Cookies.Append(helloCookie, binding, new CookieOptions
            {
                HttpOnly = true, SameSite = SameSiteMode.Strict, IsEssential = true,
                Path = "/api/auth/hello/unlock", MaxAge = TimeSpan.FromMinutes(2)
            });
            return Results.Ok(new { token, options = JsonDocument.Parse(options.ToJson()).RootElement.Clone(), salt = Convert.ToBase64String(salt) });
        }
        catch { return Results.BadRequest(new { error = "Geräteanmeldung ist nicht eingerichtet." }); }
    });

    app.MapPost("/api/auth/hello/unlock/complete", async (HelloAssertionRequest request, HttpContext context, HelloCeremonies hello, KeyManager km, DatabaseContext db, LocalSessions sessions, AutomaticBackupWorker automaticBackup) =>
    {
        automaticBackup.SetReady(false);
        if (!CorrectHelloOrigin(context)) return Results.BadRequest(new { error = "Ungültige Adresse." });
        byte[]? prf = null;
        try
        {
            var binding = context.Request.Cookies[helloCookie] ?? "";
            context.Response.Cookies.Delete(helloCookie, new CookieOptions { Path = "/api/auth/hello/unlock" });
            prf = Convert.FromBase64String(request.Prf);
            await hello.CompleteUnlockAsync(request.Token, binding, request.Credential, prf);
            sessions.RevokeAll(context);
            try { db.InitializeDatabase(); db.MigrateLegacyPlaintext(); db.EnsureRequestedSchoolProfiles(); }
            catch { km.Lock(); return Results.Problem("Die Datenmigration konnte nicht abgeschlossen werden.", statusCode: 500); }
            automaticBackup.SetReady(true);
            sessions.Issue(context);
            return Results.Ok(new { success = true });
        }
        catch { return Results.BadRequest(new { error = "Geräteanmeldung fehlgeschlagen. Bitte die App-PIN verwenden." }); }
        finally { if (prf is not null) CryptographicOperations.ZeroMemory(prf); }
    });
}

app.MapPost("/api/auth/lock", (HttpContext context, KeyManager km, LocalSessions sessions, AutomaticBackupWorker automaticBackup) =>
{
    automaticBackup.SetReady(false);
    km.Lock();
    sessions.RevokeAll(context);
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

app.MapPost("/api/auth/hello/register", () => Results.Problem(
    "Windows Hello ist derzeit nicht verfügbar.", statusCode: 501));

app.MapPost("/api/auth/hello/remove", (PinUnlockRequest req, KeyManager km) =>
{
    var (success, message, _) = km.UnlockWithPin(req.Pin ?? "");
    if (!success) return Results.BadRequest(new { error = message });
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

app.MapGet("/api/deckblatt/operatoren", () => Results.Ok(new { source = OperatorCatalog.Source, operators = OperatorCatalog.All }));
app.MapGet("/api/deckblatt/logo", () => Results.File(CoverSheetExports.TemplateLogo(), "image/png"));

app.MapGet("/api/klausuren/{id}/deckblatt", (string id, DatabaseContext db) =>
{
    var exam = db.GetExamById(id);
    return exam == null ? Results.NotFound() : Results.Ok(CoverSheetDocument.Create(exam));
});

app.MapPut("/api/klausuren/{id}/deckblatt", (string id, SaveCoverSheetRequest req, DatabaseContext db) =>
{
    var exam = db.GetExamById(id);
    if (exam == null) return Results.NotFound();
    if (exam.IsArchived) return Results.Conflict(new { error = ArchivedExamReadOnlyException.Description });
    if (req.OperatorIds == null || req.OperatorIds.Count > 6 || req.OperatorIds.Distinct().Count() != req.OperatorIds.Count)
        return Results.BadRequest(new { error = "Bitte bis zu sechs unterschiedliche Operatoren für die Fußzeile auswählen." });
    var operators = new List<ExamOperator>();
    foreach (var operatorId in req.OperatorIds)
    {
        // Preserve the definitions already frozen for this exam.
        var op = exam.CoverSheet?.Operators.FirstOrDefault(o => o.Id == operatorId)
            ?? OperatorCatalog.All.FirstOrDefault(o => o.Id == operatorId);
        if (op == null) return Results.BadRequest(new { error = "Unbekannter Operator." });
        operators.Add(op);
    }
    var settings = new CoverSheet
    {
        Subject = req.Subject, Instructions = req.Instructions,
        Tasks = req.SaveEarnedPoints || req.Tasks == null ? req.Tasks! : req.Tasks.Select(t => t == null ? null! : t with
        {
            EarnedPoints = exam.CoverSheet?.Tasks.FirstOrDefault(old => old.TaskNumber == t.TaskNumber)?.EarnedPoints
        }).ToList(),
        UseTemplateLogo = req.UseTemplateLogo, ShowGradeKey = req.ShowGradeKey,
        ShowDeductionRules = req.ShowDeductionRules, ShowWordCount = req.ShowWordCount,
        DeductionNotice = req.SaveDeductionNotice ? req.DeductionNotice : exam.CoverSheet?.DeductionNotice,
        Operators = operators, OperatorSource = exam.CoverSheet?.OperatorSource ?? OperatorCatalog.Source
    };
    try { settings.Validate(exam); }
    catch (InvalidOperationException e) { return Results.BadRequest(new { error = e.Message }); }
    var updated = exam with { CoverSheet = settings, UpdatedAt = DateTimeOffset.UtcNow };
    db.SaveExam(updated);
    return Results.Ok(CoverSheetDocument.Create(updated));
});

app.MapGet("/api/klausuren/{id}/deckblatt/pdf", (string id, DatabaseContext db, PdfExportService service) =>
{
    var exam = db.GetExamById(id);
    return exam == null ? Results.NotFound() : Results.File(service.GenerateCoverSheet(exam), "application/pdf", $"Deckblatt_{exam.Date:yyyy-MM-dd}.pdf");
});

app.MapGet("/api/klausuren/{id}/deckblatt/docx", (string id, DatabaseContext db, DocxExportService service) =>
{
    var exam = db.GetExamById(id);
    return exam == null ? Results.NotFound() : Results.File(service.GenerateCoverSheet(exam),
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document", CoverSheetFileName.Docx(exam));
});

app.MapGet("/api/klausuren", (DatabaseContext db, bool? archived) =>
{
    var list = archived == true ? db.GetExams(includeArchived: true).Where(e => e.IsArchived).ToList() : db.GetExams();
    return Results.Ok(list);
});

app.MapPut("/api/klausuren/{id}/archivierung", (string id, ArchiveExamRequest req, DatabaseContext db) =>
{
    if (!req.Archived.HasValue) return Results.BadRequest(new { error = "Archivstatus fehlt." });
    return db.SetExamArchived(id, req.Archived.Value) ? Results.Ok(new { archived = req.Archived.Value }) : Results.NotFound();
});

app.MapGet("/api/klausuren/{id}", (DatabaseContext db, string id) =>
{
    var exam = db.GetExamById(id);
    return exam != null ? Results.Ok(exam) : Results.NotFound();
});

app.MapPost("/api/klausuren", (CreateExamRequest req, DatabaseContext db) =>
{
    if (req.Tasks == null || req.Tasks.Count == 0 || req.Tasks.Any(t => t.MaxPoints <= 0 || t.MaxPoints != decimal.Truncate(t.MaxPoints)))
        return Results.BadRequest(new { error = "Mindestens eine Aufgabe mit positiver ganzzahliger Höchstpunktzahl ist erforderlich." });
    if (req.Tasks.Sum(t => t.MaxPoints) != 100m)
        return Results.BadRequest(new { error = "Die Höchstpunkte aller Aufgaben müssen zusammen genau 100 Punkte ergeben." });
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
    if (exam.IsArchived) return Results.Conflict(new { error = ArchivedExamReadOnlyException.Description });

    if (entries.Count != exam.StudentEntries.Count || entries.Select(e => e.StudentId).Distinct().Count() != entries.Count ||
        entries.Any(e => !exam.StudentEntries.Any(original => original.StudentId == e.StudentId)) ||
        entries.Any(e => e.WordCount < 0 || e.ErrorCount < 0 || !Enum.IsDefined(e.Status) || e.TaskPoints == null ||
            e.TaskPoints.Any(p => !exam.Tasks.Any(t => t.TaskNumber == p.Key &&
                (!p.Value.HasValue || (p.Value.Value >= 0 && p.Value.Value <= t.MaxPoints))))))
        return Results.BadRequest(new { error = "Ungültige Schülerliste oder ungültige Punkte-, Wort- oder Fehlerzahl." });

    if (entries.Any(e => e.TaskPoints.Any(p => p.Value.HasValue && p.Value.Value != decimal.Truncate(p.Value.Value) &&
        (!exam.StudentEntries.Single(original => original.StudentId == e.StudentId).TaskPoints.TryGetValue(p.Key, out var previous) || previous != p.Value))))
        return Results.BadRequest(new { error = "Erreichte Aufgabenpunkte müssen ganze Zahlen sein." });

    var updatedEntries = exam.StudentEntries.Select(original =>
    {
        var edited = entries.Single(e => e.StudentId == original.StudentId);
        return original with { TaskPoints = edited.TaskPoints, WordCount = edited.WordCount,
            ErrorCount = edited.ErrorCount, Status = edited.Status, Note = edited.Note };
    }).ToList();
    var updatedExam = exam with { StudentEntries = updatedEntries, UpdatedAt = DateTimeOffset.UtcNow };
    db.SaveExam(updatedExam);
    return Results.Ok(new { success = true, savedAt = updatedExam.UpdatedAt });
});

app.MapPost("/api/klausuren/{id}/preview-entry", (string id, ExamStudentEntry entry, DatabaseContext db) =>
{
    var exam = db.GetExamById(id);
    return exam == null ? Results.NotFound() : Results.Ok(ExamCalculator.CalculateStudentEntry(entry, exam) with
    {
        ErrorThresholds = ExamCalculator.CalculateErrorThresholds(entry.WordCount, exam.GradingProfileSnapshot)
    });
});

app.MapGet("/api/grading/defaults/{system:int}", (int system) =>
    system == (int)GradingSystem.Schulnoten ? Results.Ok(GradingProfile.CreateDefaultSchulnoten().GradeKey) :
    system == (int)GradingSystem.Notenpunkte ? Results.Ok(GradingProfile.CreateDefaultNotenpunkte().GradeKey) : Results.BadRequest());

app.MapPost("/api/klausuren/{id}/recalculate", (string id, RecalculateRequest? req, DatabaseContext db) =>
{
    var exam = db.GetExamById(id);
    if (exam == null) return Results.NotFound();
    if (exam.IsArchived) return Results.Conflict(new { error = ArchivedExamReadOnlyException.Description });

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

// View tokens are random process-local capabilities. These endpoints never disclose
// student data; round creation and result inspection still require a login.
app.MapPost("/api/update/view/register", (UpdateViewRegistry views) =>
{
    try { return Results.Ok(new { viewId = views.Register() }); }
    catch (InvalidOperationException) { return Results.Conflict(new { error = "Zu viele Ansichten registriert." }); }
});
app.MapPost("/api/update/view/poll", (UpdateViewRequest request, UpdateViewRegistry views) =>
    !views.Contains(request.ViewId ?? "") ? Results.NotFound() : Results.Ok(new { roundId = views.PendingFor(request.ViewId!) }));
app.MapPost("/api/update/view/respond", (UpdateViewResponse request, UpdateViewRegistry views) =>
    views.Respond(request.ViewId ?? "", request.RoundId ?? "", request.Saved == true) ? Results.Ok() : Results.Conflict());
app.MapPost("/api/update/view/unregister", (UpdateViewRequest request, UpdateViewRegistry views) =>
{
    views.Unregister(request.ViewId ?? "");
    return Results.Ok();
});
app.MapPost("/api/update/save-round/begin", (UpdateViewRegistry views) =>
{
    try { return Results.Ok(new { roundId = views.Begin() }); }
    catch (InvalidOperationException ex) { return Results.Conflict(new { error = ex.Message }); }
});
app.MapPost("/api/update/save-round/status", (UpdateRoundRequest request, UpdateViewRegistry views) =>
{
    try { return Results.Ok(views.Status(request.RoundId ?? "")); }
    catch (InvalidOperationException) { return Results.NotFound(); }
});
app.MapPost("/api/update/save-round/cancel", (UpdateRoundRequest request, UpdateViewRegistry views) =>
    views.Cancel(request.RoundId ?? "") ? Results.Ok() : Results.NotFound());

// This snapshot is not an installation permit: the registry cannot prove that
// every browser view is registered, especially across a service restart.
app.MapPost("/api/update/coordinated-backup", async (UpdateRoundRequest request, UpdateViewRegistry views,
    UpdatePreparation preparation, HttpContext context) =>
{
    void ValidateRound()
    {
        var status = views.Status(request.RoundId ?? "");
        if (!status.Complete) throw new InvalidOperationException("Die Speicherabstimmung ist unvollständig oder nicht mehr gültig.");
    }
    try
    {
        using var prepared = await preparation.PrepareAsync(context.RequestAborted, ValidateRound);
        return Results.Ok(prepared.Backup);
    }
    catch (InvalidOperationException ex) { return Results.Conflict(new { error = ex.Message }); }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException)
    {
        return Results.Problem("Die verschlüsselte Update-Sicherung konnte nicht erstellt werden.");
    }
});

// This prepares a recoverable snapshot only. It does not assert that browser inputs
// have been flushed, authorize an installer, or stop the application.
app.MapPost("/api/update/backup", (BackupService backupService) =>
{
    try { return Results.Ok(backupService.CreateUpdateBackup()); }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException)
    {
        return Results.Problem("Die Sicherung vor dem Update konnte nicht erstellt werden.");
    }
});

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

app.MapGet("/api/backups/automatic/status", (BackupService backupService, AutomaticBackupWorker worker, KeyManager km) =>
{
    var latest = backupService.GetManagedBackups().FirstOrDefault(b => b.FileName.StartsWith("klausurfix_automatisch_", StringComparison.Ordinal));
    var settings = km.GetAutomaticBackupSettings();
    return Results.Ok(new { settings.Enabled, settings.IntervalHours, lastBackup = latest?.CreatedAt, error = worker.Error });
});

app.MapPut("/api/backups/automatic/settings", (AutomaticBackupSettingsRequest request, BackupService backupService, KeyManager km) =>
{
    if (request.Enabled is null || request.IntervalHours is not (6 or 12 or 24 or 168))
        return Results.BadRequest(new { error = "Bitte ein gültiges Sicherungsintervall auswählen." });
    backupService.ExecuteExclusive(() => km.SetAutomaticBackupSettings(request.Enabled.Value, request.IntervalHours.Value));
    return Results.Ok(new { enabled = request.Enabled.Value, intervalHours = request.IntervalHours.Value });
});

app.MapPost("/api/backups/create", (BackupService backupService) =>
{
    try
    {
        var info = backupService.CreateManagedBackup();
        return Results.Ok(info);
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

app.MapPost("/api/backups/download", (BackupService backupService) =>
{
    try
    {
        var bytes = backupService.CreateBackupBytes();
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

app.MapPost("/api/backups/restore-file", (BackupFileRestoreRequest req, BackupService backupService) =>
{
    try
    {
        if (string.IsNullOrEmpty(req.DataBase64) || req.DataBase64.Length > 25_000_000)
            return Results.BadRequest(new { error = "Die Sicherungsdatei ist leer oder zu groß." });
        backupService.RestoreBackup(Convert.FromBase64String(req.DataBase64), req.RecoveryKey);
        return Results.Ok(new { success = true });
    }
    catch (Exception ex) { return Results.BadRequest(new { error = ex.Message }); }
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

app.MapPost("/api/settings/danger/full-reset", (DangerActionRequest req, KeyManager km, CleanupService cleanup, AutomaticBackupWorker automaticBackup) =>
{
    var (success, msg, _) = km.UnlockWithPin(req.Pin);
    if (!success)
        return Results.BadRequest(new { error = "Falsche PIN. Vollständiges Zurücksetzen abgebrochen." });

    automaticBackup.SetReady(false);
    cleanup.FullReset();
    automaticBackup.ResetSchedule();
    return Results.Ok(new { success = true, message = "Anwendung wurde vollständig zurückgesetzt." });
});

#endregion

app.Run();

#region Request DTOs

public record InitRequest(string Pin, string RecoveryKey, int? InactivityTimeoutMinutes);
public record PinUnlockRequest(string Pin);
public record RecoveryUnlockRequest(string RecoveryKey);
public record HelloRegistrationRequest(string Token, AuthenticatorAttestationRawResponse Credential);
public record HelloAssertionRequest(string Token, AuthenticatorAssertionRawResponse Credential, string Prf);
public record ChangePinRequest(string OldPin, string NewPin);
public record TimeoutRequest(int TimeoutMinutes);
public record AutomaticBackupSettingsRequest(bool? Enabled, int? IntervalHours);
public record UpdateViewRequest(string? ViewId);
public record UpdateViewResponse(string? ViewId, string? RoundId, bool? Saved);
public record UpdateRoundRequest(string? RoundId);
public record BackupRestoreRequest(string RecoveryKey);
public record BackupFileRestoreRequest(string RecoveryKey, string DataBase64);
public record DangerActionRequest(string Pin);
public record DeductionPreviewRequest(int? Words, int? Errors, List<DeductionTier> Tiers, decimal? MaxDeduction);
public record GradePreviewRequest(decimal Percentage, List<GradeBoundary> GradeKey, GradingSystem System, decimal Deduction);
public record CreateExamTaskDto(string Name, decimal MaxPoints);
public record CreateExamRequest(string ClassId, string Title, DateOnly Date, List<CreateExamTaskDto> Tasks);
public record RecalculateRequest(GradingProfile? NewProfile);
public record ArchiveExamRequest(bool? Archived);
public record SaveCoverSheetRequest(string Subject, string Instructions, bool UseTemplateLogo, bool ShowGradeKey,
    bool ShowDeductionRules, bool ShowWordCount, List<CoverSheetTask> Tasks, List<string> OperatorIds,
    string? DeductionNotice = null, bool SaveDeductionNotice = false, bool SaveEarnedPoints = false);

#endregion

// Make Program public for WebApplicationFactory in integration tests
public partial class Program { }
