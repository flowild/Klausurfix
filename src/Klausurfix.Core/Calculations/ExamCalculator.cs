namespace Klausurfix.Core.Calculations;

using Klausurfix.Core.Models;

public static class ExamCalculator
{
    /// <summary>Ganzzahlige Fehlerbereiche für die gespeicherten FQ-Abzugsstufen.</summary>
    public static List<ErrorThresholdRange> CalculateErrorThresholds(int? wordCount, GradingProfile profile)
    {
        if (wordCount is not > 0) return new();

        var starts = new List<(decimal MinErrors, decimal Deduction)> { (0m, 0m) };
        foreach (var tier in profile.DeductionTiers.OrderBy(t => t.Threshold))
        {
            // Ab der ersten ganzen Fehlerzahl, die die FQ-Grenze erreicht, gilt die Stufe.
            // Fehlerzahlen werden als int gespeichert; größere Grenzen sind nicht erreichbar.
            if (tier.Threshold > (decimal)int.MaxValue * 100m / wordCount.Value) break;
            var minimum = decimal.Ceiling(tier.Threshold * wordCount.Value / 100m);
            var deduction = profile.MaxDeduction.HasValue ? Math.Min(tier.Deduction, profile.MaxDeduction.Value) : tier.Deduction;
            if (minimum == starts[^1].MinErrors) starts[^1] = (minimum, deduction);
            else if (deduction != starts[^1].Deduction) starts.Add((minimum, deduction));
        }

        return starts.Select((start, i) => new ErrorThresholdRange(start.MinErrors,
            i + 1 < starts.Count ? starts[i + 1].MinErrors - 1m : null, start.Deduction)).ToList();
    }
    /// <summary>
    /// Calculates the Error Quotient (Fehlerquotient): (Rechtschreibfehler * 100) / Wortanzahl.
    /// Returns null if words is null, zero, or negative (as words=0 cannot produce a valid quotient).
    /// </summary>
    public static decimal? CalculateErrorQuotient(int? wordCount, int? errorCount)
    {
        if (!wordCount.HasValue || wordCount.Value <= 0)
            return null;

        if (!errorCount.HasValue || errorCount.Value < 0)
            return null;

        return ((decimal)errorCount.Value * 100m) / (decimal)wordCount.Value;
    }

    /// <summary>
    /// Determines the applicable deduction based on the Error Quotient and deduction tiers.
    /// Below the first threshold, the deduction is 0.
    /// Tiers apply inclusive of their threshold up to next tier exclusive.
    /// </summary>
    public static decimal CalculateDeduction(decimal? errorQuotient, IEnumerable<DeductionTier> tiers, decimal? maxDeduction = null)
    {
        if (!errorQuotient.HasValue || tiers == null)
            return 0.0m;

        var sortedTiers = tiers.OrderBy(t => t.Threshold).ToList();
        if (sortedTiers.Count == 0)
            return 0.0m;

        decimal deduction = 0.0m;
        foreach (var tier in sortedTiers)
        {
            if (errorQuotient.Value >= tier.Threshold)
            {
                deduction = tier.Deduction;
            }
            else
            {
                break;
            }
        }

        if (maxDeduction.HasValue && deduction > maxDeduction.Value)
        {
            deduction = maxDeduction.Value;
        }

        return deduction;
    }

    /// <summary>
    /// Finds the initial grade from the grade key using exact decimal percentage.
    /// Boundaries are evaluated descending without pre-rounding.
    /// </summary>
    public static (int GradeValue, string Label) CalculateInitialGrade(decimal percentage, IEnumerable<GradeBoundary> gradeKey, GradingSystem system)
    {
        var sorted = gradeKey.OrderByDescending(b => b.MinPercentage).ToList();
        if (sorted.Count == 0)
        {
            return system == GradingSystem.Schulnoten ? (6, "6") : (0, "0");
        }

        foreach (var boundary in sorted)
        {
            if (percentage >= boundary.MinPercentage)
            {
                return (boundary.GradeValue, boundary.Label);
            }
        }

        var last = sorted.Last();
        return (last.GradeValue, last.Label);
    }

    /// <summary>
    /// Calculates final grade from initial grade and deduction.
    /// Schulnoten: initial grade + deduction (worsens numerically up to max 6).
    /// Notenpunkte: initial grade - deduction (decreases down to min 0).
    /// </summary>
    public static (int GradeValue, string Label) CalculateFinalGrade(int initialGrade, decimal deduction, GradingSystem system)
    {
        int roundedDeduction = (int)Math.Round(deduction, MidpointRounding.AwayFromZero);

        if (system == GradingSystem.Schulnoten)
        {
            int finalVal = Math.Clamp(initialGrade + roundedDeduction, 1, 6);
            return (finalVal, finalVal.ToString());
        }
        else
        {
            int finalVal = Math.Clamp(initialGrade - roundedDeduction, 0, 15);
            return (finalVal, $"{finalVal} Notenpunkte");
        }
    }

    /// <summary>
    /// Calculates results for a single student entry in an exam.
    /// </summary>
    public static ExamCalculationResult CalculateStudentEntry(
        ExamStudentEntry entry,
        Exam exam,
        GradingProfile? profileOverride = null)
    {
        var profile = profileOverride ?? exam.GradingProfileSnapshot;
        decimal totalMaxPoints = exam.TotalMaxPoints;

        // A missing input is not a zero score. Keep excluded rows out of the
        // distribution even if an older exam stored their status as Graded.
        bool complete = exam.Tasks.Count > 0 && exam.Tasks.All(t =>
            entry.TaskPoints.TryGetValue(t.TaskNumber, out var p) && p.HasValue && p >= 0 && p <= t.MaxPoints)
            && entry.WordCount is > 0 && entry.ErrorCount is >= 0;
        if (entry.Status != StudentExamStatus.Graded || !complete)
        {
            return new ExamCalculationResult
            {
                StudentId = entry.StudentId,
                StudentName = entry.StudentName,
                Status = entry.Status == StudentExamStatus.Graded ? StudentExamStatus.Incomplete : entry.Status,
                MaxPoints = totalMaxPoints,
                WordCount = entry.WordCount,
                ErrorCount = entry.ErrorCount
            };
        }

        // If total max points is 0, we can't calculate percentage
        if (totalMaxPoints <= 0)
        {
            return new ExamCalculationResult
            {
                StudentId = entry.StudentId,
                StudentName = entry.StudentName,
                Status = entry.Status,
                MaxPoints = 0
            };
        }

        // Sum points for entered tasks
        decimal rawPoints = 0;
        foreach (var task in exam.Tasks)
        {
            if (entry.TaskPoints.TryGetValue(task.TaskNumber, out var pts) && pts.HasValue)
            {
                // Clamp task points to task max points
                decimal clamped = Math.Clamp(pts.Value, 0m, task.MaxPoints);
                rawPoints += clamped;
            }
        }

        // Percentage calculated with full decimal precision
        decimal percentage = (rawPoints / totalMaxPoints) * 100m;

        // Initial grade from grade key
        var (initGrade, initLabel) = CalculateInitialGrade(percentage, profile.GradeKey, profile.System);

        // Error quotient
        decimal? fq = CalculateErrorQuotient(entry.WordCount, entry.ErrorCount);

        // Deduction
        decimal deduction = CalculateDeduction(fq, profile.DeductionTiers, profile.MaxDeduction);
        var maximumDeduction = profile.DeductionTiers.Select(t => t.Deduction).DefaultIfEmpty(0m).Max();
        if (profile.MaxDeduction.HasValue) maximumDeduction = Math.Min(maximumDeduction, profile.MaxDeduction.Value);

        // Final grade
        decimal? deductionPoints = null;
        decimal? adjustedPoints = null;
        int finalGrade;
        string finalLabel;
        if (profile.DeductionUnit == DeductionUnit.ProzentDerGesamtpunkte)
        {
            deductionPoints = totalMaxPoints * deduction / 100m;
            adjustedPoints = Math.Max(0m, rawPoints - deductionPoints.Value);
            (finalGrade, finalLabel) = CalculateInitialGrade(adjustedPoints.Value / totalMaxPoints * 100m, profile.GradeKey, profile.System);
        }
        else
            (finalGrade, finalLabel) = CalculateFinalGrade(initGrade, deduction, profile.System);

        return new ExamCalculationResult
        {
            StudentId = entry.StudentId,
            StudentName = entry.StudentName,
            Status = entry.Status,
            RawPoints = rawPoints,
            MaxPoints = totalMaxPoints,
            Percentage = percentage,
            InitialGrade = initGrade,
            InitialGradeLabel = initLabel,
            WordCount = entry.WordCount,
            ErrorCount = entry.ErrorCount,
            ErrorQuotient = fq,
            Deduction = deduction,
            IsMaximumDeduction = maximumDeduction > 0 && deduction == maximumDeduction,
            DeductionPoints = deductionPoints,
            AdjustedPoints = adjustedPoints,
            FinalGrade = finalGrade,
            FinalGradeLabel = finalLabel
        };
    }

    /// <summary>
    /// Evaluates the complete exam, calculating all student entries, summary statistics,
    /// class average, and grade distribution (Notenspiegel).
    /// </summary>
    public static ExamSummary CalculateExamSummary(Exam exam, GradingProfile? profileOverride = null)
    {
        var profile = profileOverride ?? exam.GradingProfileSnapshot;
        var results = new List<ExamCalculationResult>();

        foreach (var entry in exam.StudentEntries)
        {
            results.Add(CalculateStudentEntry(entry, exam, profile));
        }

        var gradedResults = results.Where(r => r.Status == StudentExamStatus.Graded && r.FinalGrade.HasValue).ToList();
        var excludedResults = results.Where(r => r.Status != StudentExamStatus.Graded).ToList();

        decimal? average = null;
        if (gradedResults.Count > 0)
        {
            decimal sum = gradedResults.Sum(r => (decimal)r.FinalGrade!.Value);
            average = sum / gradedResults.Count;
        }

        // Prepare Grade Distribution (Notenspiegel)
        var distribution = new Dictionary<string, int>();
        if (profile.System == GradingSystem.Schulnoten)
        {
            for (int grade = 1; grade <= 6; grade++)
            {
                distribution[grade.ToString()] = 0;
            }

            foreach (var r in gradedResults)
            {
                if (r.FinalGrade.HasValue)
                {
                    string key = r.FinalGrade.Value.ToString();
                    if (distribution.ContainsKey(key))
                        distribution[key]++;
                    else
                        distribution[key] = 1;
                }
            }
        }
        else
        {
            for (int pts = 15; pts >= 0; pts--)
            {
                distribution[pts.ToString()] = 0;
            }

            foreach (var r in gradedResults)
            {
                if (r.FinalGrade.HasValue)
                {
                    string key = r.FinalGrade.Value.ToString();
                    if (distribution.ContainsKey(key))
                        distribution[key]++;
                    else
                        distribution[key] = 1;
                }
            }
        }

        return new ExamSummary
        {
            TotalStudents = results.Count,
            GradedCount = gradedResults.Count,
            ExcludedCount = excludedResults.Count,
            MissingCount = results.Count(r => r.Status == StudentExamStatus.Missing),
            NotParticipatedCount = results.Count(r => r.Status == StudentExamStatus.NotParticipated),
            IncompleteCount = results.Count(r => r.Status == StudentExamStatus.Incomplete),
            ClassAverage = average,
            Unit = profile.System == GradingSystem.Schulnoten ? "Noten" : "Notenpunkte",
            GradeDistribution = distribution,
            Results = results
        };
    }
}

