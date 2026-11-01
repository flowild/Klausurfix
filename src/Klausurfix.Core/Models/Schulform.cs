namespace Klausurfix.Core.Models;

public record Schulform
{
    public string Id { get; init; } = Guid.NewGuid().ToString();
    public string Name { get; init; } = string.Empty;
    public GradingProfile Profile { get; init; } = GradingProfile.CreateDefaultSchulnoten();
    public bool IsArchived { get; init; } = false;
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}

