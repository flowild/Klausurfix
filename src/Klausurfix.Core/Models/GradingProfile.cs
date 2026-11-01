namespace Klausurfix.Core.Models;

public record GradingProfile
{
    public GradingSystem System { get; init; } = GradingSystem.Schulnoten;
    public DeductionUnit DeductionUnit { get; init; } = DeductionUnit.Schulnoten;
    public decimal? MaxDeduction { get; init; } = null; // optional cap on deduction
    public List<DeductionTier> DeductionTiers { get; init; } = new();
    public List<GradeBoundary> GradeKey { get; init; } = new();

    public void Validate()
    {
        // Consistency check: system and deduction unit
        if (System == GradingSystem.Schulnoten && DeductionUnit != DeductionUnit.Schulnoten && DeductionUnit != DeductionUnit.ProzentDerGesamtpunkte)
            throw new InvalidOperationException("Für das System Schulnoten muss die Abzugseinheit Schulnoten sein.");
        if (System == GradingSystem.Notenpunkte && DeductionUnit != DeductionUnit.Notenpunkte && DeductionUnit != DeductionUnit.ProzentDerGesamtpunkte)
            throw new InvalidOperationException("Für das System Notenpunkte muss die Abzugseinheit Notenpunkte sein.");

        if (MaxDeduction.HasValue && MaxDeduction.Value < 0)
            throw new InvalidOperationException("Maximaler Abzug darf nicht negativ sein.");

        DeductionTier.ValidateTiers(DeductionTiers);
        if (DeductionUnit == DeductionUnit.ProzentDerGesamtpunkte && DeductionTiers.Any(t => t.Deduction > 100))
            throw new InvalidOperationException("Der prozentuale Abzug darf 100 % nicht überschreiten.");
        GradeBoundary.ValidateBoundaries(GradeKey, System);
    }

    public static GradingProfile BeruflichesGymnasium() => CreateDefaultNotenpunkte() with
    {
        DeductionTiers = new() { new(3m, 1m), new(6m, 2m) }
    };

    public static GradingProfile FachschuleSozialwesen() => CreateDefaultSchulnoten() with
    {
        DeductionUnit = DeductionUnit.ProzentDerGesamtpunkte,
        DeductionTiers = new() { new(2m, 1m), new(4m, 3m), new(6m, 6m) }
    };

    public static GradingProfile CreateDefaultSchulnoten()
    {
        return new GradingProfile
        {
            System = GradingSystem.Schulnoten,
            DeductionUnit = DeductionUnit.Schulnoten,
            MaxDeduction = null,
            DeductionTiers = new List<DeductionTier>
            {
                new DeductionTier(2.0m, 1.0m), // ab FQ 2.0 -> 1 Note Abzug
                new DeductionTier(4.0m, 2.0m)  // ab FQ 4.0 -> 2 Noten Abzug
            },
            GradeKey = new List<GradeBoundary>
            {
                new GradeBoundary(87.5m, 1, "Sehr gut (1)"),
                new GradeBoundary(75.0m, 2, "Gut (2)"),
                new GradeBoundary(62.5m, 3, "Befriedigend (3)"),
                new GradeBoundary(50.0m, 4, "Ausreichend (4)"),
                new GradeBoundary(25.0m, 5, "Mangelhaft (5)"),
                new GradeBoundary(0.0m,  6, "Ungenügend (6)")
            }
        };
    }

    public static GradingProfile CreateDefaultNotenpunkte()
    {
        return new GradingProfile
        {
            System = GradingSystem.Notenpunkte,
            DeductionUnit = DeductionUnit.Notenpunkte,
            MaxDeduction = null,
            DeductionTiers = new List<DeductionTier>
            {
                new DeductionTier(2.0m, 1.0m), // ab FQ 2.0 -> 1 Notenpunkt Abzug
                new DeductionTier(3.5m, 2.0m)  // ab FQ 3.5 -> 2 Notenpunkte Abzug
            },
            GradeKey = new List<GradeBoundary>
            {
                new GradeBoundary(95.0m, 15, "15 Punkte (1+)"),
                new GradeBoundary(90.0m, 14, "14 Punkte (1)"),
                new GradeBoundary(85.0m, 13, "13 Punkte (1-)"),
                new GradeBoundary(80.0m, 12, "12 Punkte (2+)"),
                new GradeBoundary(75.0m, 11, "11 Punkte (2)"),
                new GradeBoundary(70.0m, 10, "10 Punkte (2-)"),
                new GradeBoundary(65.0m, 9,  "9 Punkte (3+)"),
                new GradeBoundary(60.0m, 8,  "8 Punkte (3)"),
                new GradeBoundary(55.0m, 7,  "7 Punkte (3-)"),
                new GradeBoundary(50.0m, 6,  "6 Punkte (4+)"),
                new GradeBoundary(45.0m, 5,  "5 Punkte (4)"),
                new GradeBoundary(40.0m, 4,  "4 Punkte (4-)"),
                new GradeBoundary(33.0m, 3,  "3 Punkte (5+)"),
                new GradeBoundary(27.0m, 2,  "2 Punkte (5)"),
                new GradeBoundary(20.0m, 1,  "1 Punkt (5-)"),
                new GradeBoundary(0.0m,  0,  "0 Punkte (6)")
            }
        };
    }
}

