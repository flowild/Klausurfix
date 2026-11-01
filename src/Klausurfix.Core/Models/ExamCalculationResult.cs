namespace Klausurfix.Core.Models;

public record ExamCalculationResult
{
    public string StudentId { get; init; } = string.Empty;
    public string StudentName { get; init; } = string.Empty;
    public StudentExamStatus Status { get; init; } = StudentExamStatus.Graded;
    public bool IsGraded => Status == StudentExamStatus.Graded;

    public decimal? RawPoints { get; init; }
    public decimal MaxPoints { get; init; }
    public decimal? Percentage { get; init; }

    public int? InitialGrade { get; init; }
    public string InitialGradeLabel { get; init; } = string.Empty;

    public int? WordCount { get; init; }
    public int? ErrorCount { get; init; }
    public decimal? ErrorQuotient { get; init; }

    public decimal Deduction { get; init; } = 0.0m;

    public int? FinalGrade { get; init; }
    public string FinalGradeLabel { get; init; } = string.Empty;

    public string StatusDescription => Status switch
    {
        StudentExamStatus.Graded => "Bewertet",
        StudentExamStatus.Missing => "Krank / Fehlend",
        StudentExamStatus.NotParticipated => "Nicht mitgeschrieben",
        StudentExamStatus.Incomplete => "Unvollständig",
        _ => "Unbekannt"
    };
}

