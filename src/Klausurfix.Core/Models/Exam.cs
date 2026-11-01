namespace Klausurfix.Core.Models;

public record Exam
{
    public string Id { get; init; } = Guid.NewGuid().ToString();
    public string ClassId { get; init; } = string.Empty;
    public string ClassName { get; init; } = string.Empty;
    public string SchulformId { get; init; } = string.Empty;
    public string SchulformName { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public DateOnly Date { get; init; } = DateOnly.FromDateTime(DateTime.Today);

    public List<TaskDefinition> Tasks { get; init; } = new();
    public decimal TotalMaxPoints => Tasks.Sum(t => t.MaxPoints);

    public CoverSheet? CoverSheet { get; init; }
    public bool IsArchived { get; init; }

    // Frozen snapshot of grading rules at exam creation time
    public GradingProfile GradingProfileSnapshot { get; init; } = GradingProfile.CreateDefaultSchulnoten();

    // Student score entries
    public List<ExamStudentEntry> StudentEntries { get; init; } = new();

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; init; } = DateTimeOffset.UtcNow;
}

