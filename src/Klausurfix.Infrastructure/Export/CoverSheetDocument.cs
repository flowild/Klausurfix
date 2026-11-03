namespace Klausurfix.Infrastructure.Export;

using System.Globalization;
using Klausurfix.Core.Models;

public record CoverSheetDocumentTask(int TaskNumber, string Title, string Text, string Points, string ShortPoints,
    string MaxPointsText, string? EarnedPointsText, List<OperatorTextPart> TextParts, List<string> RecognizedOperatorIds);
public record CoverSheetGrade(string Percentage, string Grade);

/// <summary>One presentation model shared by preview, PDF and DOCX.</summary>
public record CoverSheetDocument
{
    public bool IsSaved { get; init; }
    public bool IsArchived { get; init; }
    public required CoverSheet Settings { get; init; }
    public required string Title { get; init; }
    public required string Meta { get; init; }
    public required string HeaderLine { get; init; }
    public required string HeaderTitle { get; init; }
    public required string HeaderClass { get; init; }
    public required string HeaderDate { get; init; }
    public required string TotalPoints { get; init; }
    public required string GradeUnit { get; init; }
    public required List<CoverSheetGrade> GradeKey { get; init; }
    public required string DefaultDeductionNotice { get; init; }
    public required string DeductionNotice { get; init; }
    public required List<CoverSheetDocumentTask> Tasks { get; init; }
    public const string WordCountHint = "Zählen Sie bitte am Ende der Klausur Ihre geschriebenen Wörter!";
    public const string WordCountField = "Anzahl Wörter: ____________________";
    public const string NameField = "Name:___________________";

    private static string Number(decimal n) => n.ToString("G", CultureInfo.GetCultureInfo("de-DE"));

    public static CoverSheet Defaults(Exam exam) => new()
    {
        Tasks = exam.Tasks.Select(t => new CoverSheetTask(t.TaskNumber, "")).ToList(),
        Operators = OperatorCatalog.All.Where(o => o.Id is "wiedergeben" or "erklaeren" or "diskutieren").ToList(),
        OperatorSource = OperatorCatalog.Source
    };

    public static CoverSheetDocument Create(Exam exam)
    {
        var settings = exam.CoverSheet ?? Defaults(exam);
        settings.Validate(exam);
        var profile = exam.GradingProfileSnapshot;
        var defaultNotice = FormatDeductionNotice(profile);
        var headerTitle = FormatHeaderTitle(exam.Title, settings.Subject);
        var headerDate = exam.Date.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);
        var boundaries = profile.GradeKey.OrderByDescending(b => b.MinPercentage).ToList();
        return new()
        {
            IsSaved = exam.CoverSheet != null,
            IsArchived = exam.IsArchived,
            Settings = settings,
            Title = exam.Title,
            Meta = $"{(settings.Subject.Length == 0 ? "" : settings.Subject + " · ")}Klasse: {exam.ClassName} · {exam.SchulformName} · {exam.Date:dd.MM.yyyy}",
            HeaderTitle = headerTitle,
            HeaderClass = exam.ClassName,
            HeaderDate = headerDate,
            HeaderLine = string.Join("   ", new[] { headerTitle, exam.ClassName, headerDate }.Where(s => !string.IsNullOrWhiteSpace(s))),
            TotalPoints = $"Gesamt: ______ / {Number(exam.TotalMaxPoints)} Punkte",
            GradeUnit = profile.System == GradingSystem.Notenpunkte ? "Notenpunkte" : "Schulnote",
            GradeKey = boundaries.Select((b, i) => new CoverSheetGrade(
                FormatWholePercentageRange(b.MinPercentage, i == 0 ? null : boundaries[i - 1].MinPercentage),
                b.GradeValue.ToString(CultureInfo.InvariantCulture))).ToList(),
            DefaultDeductionNotice = defaultNotice,
            DeductionNotice = settings.DeductionNotice ?? defaultNotice,
            Tasks = exam.Tasks.OrderBy(t => t.TaskNumber).Select(t =>
            {
                var sheetTask = settings.Tasks.Single(a => a.TaskNumber == t.TaskNumber);
                var earned = sheetTask.EarnedPoints.HasValue ? Number(sheetTask.EarnedPoints.Value) : null;
                var recognition = OperatorRecognition.Recognize(sheetTask.Text);
                return new CoverSheetDocumentTask(t.TaskNumber,
                    t.Name == $"Aufgabe {t.TaskNumber}" ? t.Name : $"Aufgabe {t.TaskNumber}: {t.Name}",
                    sheetTask.Text, $"______ / {Number(t.MaxPoints)} Punkte", $"{earned ?? "__"}/{Number(t.MaxPoints)}",
                    Number(t.MaxPoints), earned, recognition.Parts, recognition.OperatorIds);
            }).ToList()
        };
    }

    private static string FormatWholePercentageRange(decimal minimum, decimal? nextMinimum)
    {
        var lower = decimal.Ceiling(minimum);
        var upper = nextMinimum.HasValue ? decimal.Ceiling(nextMinimum.Value) - 1 : 100m;
        return lower > upper ? "–" : lower == upper ? Number(lower) : $"{Number(upper)}–{Number(lower)}";
    }

    private static string FormatHeaderTitle(string title, string subject)
    {
        var trimmed = title.Trim();
        const string prefix = "Klausur";
        var hasPrefix = trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
            (trimmed.Length == prefix.Length || char.IsWhiteSpace(trimmed[prefix.Length]) || ":–-".Contains(trimmed[prefix.Length]));
        var titleWithoutPrefix = hasPrefix ? trimmed[prefix.Length..].TrimStart().TrimStart(':', '–', '-').TrimStart() : trimmed;
        var result = hasPrefix ? trimmed : string.IsNullOrWhiteSpace(trimmed) ? prefix : $"{prefix} {trimmed}";
        var additional = subject.Trim();
        if (additional.Length > 0 && !additional.Equals(titleWithoutPrefix, StringComparison.OrdinalIgnoreCase) &&
            !additional.Equals(trimmed, StringComparison.OrdinalIgnoreCase))
            result += $" {additional}";
        return result;
    }

    private static string FormatDeductionNotice(GradingProfile profile)
    {
        var maximum = profile.DeductionTiers.Select(t => t.Deduction).DefaultIfEmpty(0m).Max();
        if (profile.MaxDeduction.HasValue) maximum = Math.Min(maximum, profile.MaxDeduction.Value);
        if (maximum <= 0) return "Für Rechtschreibfehler ist kein Abzug vorgesehen.";
        var amount = Number(maximum);
        return profile.DeductionUnit switch
        {
            DeductionUnit.Notenpunkte => $"Mängel in Rechtschreibung, Grammatik und Ausdruck können zu einem Punktabzug von bis zu {amount} {(maximum == 1m ? "Notenpunkt" : "Notenpunkten")} führen.",
            DeductionUnit.Schulnoten => $"Mängel in Rechtschreibung, Grammatik und Ausdruck können zu einem Abzug von bis zu {amount} {(maximum == 1m ? "Schulnote" : "Schulnoten")} führen.",
            DeductionUnit.ProzentDerGesamtpunkte => $"Mängel in Rechtschreibung, Grammatik und Ausdruck können zu einem Punktabzug von bis zu {amount} % der maximalen Gesamtpunktzahl führen.",
            _ => throw new InvalidOperationException("Unbekannte Abzugseinheit.")
        };
    }
}
