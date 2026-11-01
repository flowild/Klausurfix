namespace Klausurfix.Core.Models;

public record ClassGroup
{
    public string Id { get; init; } = Guid.NewGuid().ToString();
    public string Name { get; init; } = string.Empty;
    public string SchulformId { get; init; } = string.Empty;
    public string SchulformName { get; init; } = string.Empty;
    public bool IsArchived { get; init; } = false;
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}

