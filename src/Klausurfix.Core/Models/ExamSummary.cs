namespace Klausurfix.Core.Models;

public record ExamSummary
{
    public int TotalStudents { get; init; }
    public int GradedCount { get; init; }
    public int ExcludedCount { get; init; }
    public int MissingCount { get; init; }
    public int NotParticipatedCount { get; init; }
    public int IncompleteCount { get; init; }
    public string ApprovalNotice { get; init; } = string.Empty;

    public decimal? ClassAverage { get; init; }
    public string Unit { get; init; } = "Noten";

    // Notenspiegel: Grade value/label -> count
    public Dictionary<string, int> GradeDistribution { get; init; } = new();

    public List<ExamCalculationResult> Results { get; init; } = new();
}

