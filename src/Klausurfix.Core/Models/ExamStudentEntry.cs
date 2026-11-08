namespace Klausurfix.Core.Models;

public record ExamStudentEntry
{
    public string StudentId { get; init; } = string.Empty;
    public string StudentName { get; init; } = string.Empty;
    public StudentExamStatus Status { get; init; } = StudentExamStatus.Graded;
    public Dictionary<int, decimal?> TaskPoints { get; init; } = new();
    public Dictionary<int, string> TaskPointExpressions { get; init; } = new();
    public int? WordCount { get; init; } = null;
    public int? ErrorCount { get; init; } = null;
    public string? Note { get; init; } = null;
}

