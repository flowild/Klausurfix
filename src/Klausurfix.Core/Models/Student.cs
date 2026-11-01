namespace Klausurfix.Core.Models;

public record Student
{
    public string Id { get; init; } = Guid.NewGuid().ToString();
    public string ClassId { get; init; } = string.Empty;
    public string FirstName { get; init; } = string.Empty;
    public string LastName { get; init; } = string.Empty;
    public int OrderIndex { get; init; } = 0;
    public bool IsArchived { get; init; } = false;

    public string FullName => string.IsNullOrWhiteSpace(FirstName) ? LastName : $"{LastName}, {FirstName}";
}

