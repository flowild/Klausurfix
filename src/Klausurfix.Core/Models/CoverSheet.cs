namespace Klausurfix.Core.Models;

using System.Text.Json.Serialization;

public record CoverSheetTask(int TaskNumber, string Text,
    [property: JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)] decimal? EarnedPoints = null);

public record ExamOperator(string Id, string Name, string Definition, string RequirementArea);

/// <summary>Per-exam document settings, including frozen operator definitions.</summary>
public record CoverSheet
{
    public int Version { get; init; } = 1;
    public string Subject { get; init; } = "";
    public string Instructions { get; init; } = "";
    public bool UseTemplateLogo { get; init; } = true;
    public bool ShowGradeKey { get; init; } = true;
    public bool ShowDeductionRules { get; init; } = true;
    public string? DeductionNotice { get; init; }
    public bool ShowWordCount { get; init; } = true;
    public List<CoverSheetTask> Tasks { get; init; } = new();
    public List<ExamOperator> Operators { get; init; } = new();
    public string OperatorSource { get; init; } = "";

    public void Validate(Exam exam)
    {
        if (Version != 1) throw new InvalidOperationException("Unbekannte Deckblattversion.");
        if (Subject == null || Subject.Length > 200 || Instructions == null || Instructions.Length > 2000 || DeductionNotice?.Length > 1000)
            throw new InvalidOperationException("Fach: höchstens 200 Zeichen; Hinweise: höchstens 2.000 Zeichen; Abzugshinweis: höchstens 1.000 Zeichen.");
        if (Tasks == null || Tasks.Count != exam.Tasks.Count || Tasks.Any(t => t == null ||
                !exam.Tasks.Any(a => a.TaskNumber == t.TaskNumber) || t.Text == null || t.Text.Length > 4000) ||
            Tasks.Select(t => t.TaskNumber).Distinct().Count() != Tasks.Count)
            throw new InvalidOperationException("Jede Klausuraufgabe benötigt genau eine Aufgabenstellung mit höchstens 4.000 Zeichen.");
        if (Tasks.Any(t => t.EarnedPoints.HasValue && !exam.Tasks.Any(a => a.TaskNumber == t.TaskNumber &&
                t.EarnedPoints.Value >= 0 && t.EarnedPoints.Value <= a.MaxPoints)))
            throw new InvalidOperationException("Die erreichten Punkte müssen zwischen null und der jeweiligen Höchstpunktzahl liegen.");
        if (Operators == null || Operators.Count > 6 || Operators.Any(o => o == null ||
                string.IsNullOrWhiteSpace(o.Id) || o.Id.Length > 100 || string.IsNullOrWhiteSpace(o.Name) || o.Name.Length > 100 ||
                string.IsNullOrWhiteSpace(o.Definition) || o.Definition.Length > 800 ||
                string.IsNullOrWhiteSpace(o.RequirementArea) || o.RequirementArea.Length > 20) ||
            Operators.Select(o => o.Id).Distinct().Count() != Operators.Count || OperatorSource == null || OperatorSource.Length > 500)
            throw new InvalidOperationException("Bitte bis zu sechs unterschiedliche Operatoren für die Fußzeile auswählen.");
    }
}
