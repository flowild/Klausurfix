namespace Klausurfix.Core.Models;

public record DeductionTier
{
    public decimal Threshold { get; init; }
    public decimal Deduction { get; init; }

    public DeductionTier() { }

    public DeductionTier(decimal threshold, decimal deduction)
    {
        if (threshold < 0)
            throw new ArgumentOutOfRangeException(nameof(threshold), "Schwellenwert für Fehlerquotient darf nicht negativ sein.");
        if (deduction < 0)
            throw new ArgumentOutOfRangeException(nameof(deduction), "Abzug darf nicht negativ sein.");

        Threshold = threshold;
        Deduction = deduction;
    }

    public static void ValidateTiers(IEnumerable<DeductionTier> tiers)
    {
        if (tiers == null) return;
        var list = tiers.ToList();
        for (int i = 0; i < list.Count; i++)
        {
            if (list[i].Threshold < 0)
                throw new InvalidOperationException($"Stufe {i + 1}: Fehlerquotient-Grenze darf nicht negativ sein ({list[i].Threshold}).");
            if (list[i].Deduction < 0)
                throw new InvalidOperationException($"Stufe {i + 1}: Abzug darf nicht negativ sein ({list[i].Deduction}).");

            if (i > 0)
            {
                if (list[i].Threshold <= list[i - 1].Threshold)
                {
                    throw new InvalidOperationException(
                        $"Stufen müssen streng aufsteigend sortiert sein: Grenze {list[i].Threshold} ist nicht größer als vorherige Grenze {list[i - 1].Threshold}.");
                }
            }
        }
    }
}

