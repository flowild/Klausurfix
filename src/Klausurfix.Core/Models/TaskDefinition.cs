namespace Klausurfix.Core.Models;

public record TaskDefinition
{
    public int TaskNumber { get; init; }
    public string Name { get; init; } = string.Empty;
    public decimal MaxPoints { get; init; }

    public TaskDefinition() { }

    public TaskDefinition(int taskNumber, string name, decimal maxPoints)
    {
        if (taskNumber <= 0)
            throw new ArgumentOutOfRangeException(nameof(taskNumber), "Aufgabennummer muss positiv sein.");
        if (maxPoints <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxPoints), "Höchstpunktzahl muss größer als 0 sein.");

        TaskNumber = taskNumber;
        Name = string.IsNullOrWhiteSpace(name) ? $"Aufgabe {taskNumber}" : name;
        MaxPoints = maxPoints;
    }
}

