namespace Klausurfix.Core.Models;

public record GradeBoundary
{
    public decimal MinPercentage { get; init; } // e.g. 90.0m
    public int GradeValue { get; init; }        // e.g. 1 (for Schulnote) or 15 (for Notenpunkte)
    public string Label { get; init; } = string.Empty; // e.g. "1", "15 Notenpunkte", etc.

    public GradeBoundary() { }

    public GradeBoundary(decimal minPercentage, int gradeValue, string? label = null)
    {
        if (minPercentage < 0 || minPercentage > 100)
            throw new ArgumentOutOfRangeException(nameof(minPercentage), "Prozentwert muss zwischen 0 und 100 liegen.");

        MinPercentage = minPercentage;
        GradeValue = gradeValue;
        Label = label ?? gradeValue.ToString();
    }

    public static void ValidateBoundaries(IEnumerable<GradeBoundary> boundaries, GradingSystem system)
    {
        if (boundaries == null)
            throw new ArgumentNullException(nameof(boundaries));

        var list = boundaries.OrderByDescending(b => b.MinPercentage).ToList();
        if (list.Count == 0)
            throw new InvalidOperationException("Der Notenschlüssel darf nicht leer sein.");

        // Check lowest boundary reaches 0%
        if (list.Last().MinPercentage > 0)
        {
            throw new InvalidOperationException("Der Notenschlüssel muss eine Stufe ab 0% abdecken.");
        }

        // Validate values according to system
        foreach (var b in list)
        {
            if (system == GradingSystem.Schulnoten && (b.GradeValue < 1 || b.GradeValue > 6))
                throw new InvalidOperationException($"Schulnote {b.GradeValue} liegt außerhalb von 1 bis 6.");
            if (system == GradingSystem.Notenpunkte && (b.GradeValue < 0 || b.GradeValue > 15))
                throw new InvalidOperationException($"Notenpunkt {b.GradeValue} liegt außerhalb von 0 bis 15.");
        }
    }
}

