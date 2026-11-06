namespace Klausurfix.Tests;

using Klausurfix.Core.Calculations;
using Klausurfix.Core.Models;
using Xunit;

public class ExamCalculatorTests
{
    [Fact]
    public void ErrorThresholds_UseWholeErrorsAndExamProfileEvenBeforeGrading()
    {
        var profile = GradingProfile.BeruflichesGymnasium();
        Assert.Equal(new[] {
            new ErrorThresholdRange(0, 17, 0), new ErrorThresholdRange(18, 35, 1),
            new ErrorThresholdRange(36, null, 2)
        }, ExamCalculator.CalculateErrorThresholds(600, profile));
        Assert.Equal(new[] {
            new ErrorThresholdRange(0, 0, 0), new ErrorThresholdRange(1, 1, 1),
            new ErrorThresholdRange(2, null, 2)
        }, ExamCalculator.CalculateErrorThresholds(20, profile));
        Assert.Empty(ExamCalculator.CalculateErrorThresholds(0, profile));
        Assert.Empty(ExamCalculator.CalculateErrorThresholds(null, profile));
    }

    [Fact]
    public void ErrorThresholds_SkipUnreachableStagesAndRespectCap()
    {
        var profile = GradingProfile.BeruflichesGymnasium() with { MaxDeduction = 1m };
        Assert.Equal(new[] { new ErrorThresholdRange(0, 0, 0), new ErrorThresholdRange(1, null, 1) },
            ExamCalculator.CalculateErrorThresholds(10, profile));
        var percent = GradingProfile.FachschuleSozialwesen();
        Assert.Equal(new[] { new ErrorThresholdRange(0, 1, 0), new ErrorThresholdRange(2, 3, 1),
            new ErrorThresholdRange(4, 5, 3), new ErrorThresholdRange(6, null, 6) },
            ExamCalculator.CalculateErrorThresholds(100, percent));
    }

    [Fact]
    public void MaximumDeduction_IsTrueOnlyForCompleteResultsAtTheConfiguredMaximum()
    {
        var exam = new Exam { Tasks = new() { new TaskDefinition(1, "A", 100m) },
            GradingProfileSnapshot = GradingProfile.BeruflichesGymnasium() };
        var entry = new ExamStudentEntry { TaskPoints = new() { [1] = 80m }, WordCount = 10000, ErrorCount = 599 };
        Assert.False(ExamCalculator.CalculateStudentEntry(entry, exam).IsMaximumDeduction);
        Assert.True(ExamCalculator.CalculateStudentEntry(entry with { ErrorCount = 600 }, exam).IsMaximumDeduction);
        Assert.False(ExamCalculator.CalculateStudentEntry(entry with { ErrorCount = 600, WordCount = 0 }, exam).IsMaximumDeduction);
        Assert.False(ExamCalculator.CalculateStudentEntry(entry with { ErrorCount = 600, TaskPoints = new() }, exam).IsMaximumDeduction);
        var capped = exam with { GradingProfileSnapshot = exam.GradingProfileSnapshot with { MaxDeduction = 1m } };
        Assert.True(ExamCalculator.CalculateStudentEntry(entry with { ErrorCount = 300 }, capped).IsMaximumDeduction);
        var noRules = exam with { GradingProfileSnapshot = exam.GradingProfileSnapshot with { DeductionTiers = new() } };
        Assert.False(ExamCalculator.CalculateStudentEntry(entry with { ErrorCount = 600 }, noRules).IsMaximumDeduction);
        var percent = exam with { GradingProfileSnapshot = GradingProfile.FachschuleSozialwesen() };
        Assert.True(ExamCalculator.CalculateStudentEntry(entry with { ErrorCount = 600 }, percent).IsMaximumDeduction);
    }

    [Theory]
    [InlineData(299, 0)]
    [InlineData(300, 1)]
    [InlineData(599, 1)]
    [InlineData(600, 2)]
    public void BeruflichesGymnasium_DeductsGradePointsAtBoundaries(int errors, int expectedDeduction)
    {
        var exam = new Exam { Tasks = new() { new TaskDefinition(1, "A", 100m) },
            GradingProfileSnapshot = GradingProfile.BeruflichesGymnasium() };
        var entry = new ExamStudentEntry { TaskPoints = new() { [1] = 100m }, WordCount = 10000, ErrorCount = errors };
        var result = ExamCalculator.CalculateStudentEntry(entry, exam);
        Assert.Equal(expectedDeduction, result.Deduction);
        Assert.Equal(15 - expectedDeduction, result.FinalGrade);
    }

    [Theory]
    [InlineData(199, 0)]
    [InlineData(200, 1)]
    [InlineData(399, 1)]
    [InlineData(400, 3)]
    [InlineData(599, 3)]
    [InlineData(600, 6)]
    public void Fachschule_DeductsPercentageOfMaximumPointsAtBoundaries(int errors, int expectedDeduction)
    {
        var exam = new Exam { Tasks = new() { new TaskDefinition(1, "Aufgabe", 100m) },
            GradingProfileSnapshot = GradingProfile.FachschuleSozialwesen() };
        var entry = new ExamStudentEntry { TaskPoints = new() { [1] = 80m }, WordCount = 10000, ErrorCount = errors };
        var result = ExamCalculator.CalculateStudentEntry(entry, exam);
        Assert.Equal(expectedDeduction, result.DeductionPoints);
        Assert.Equal(80m - expectedDeduction, result.AdjustedPoints);
    }

    [Fact]
    public void BlankAndPartiallyEnteredRowsAreNeverIncluded()
    {
        var exam = new Exam { Tasks = new() { new TaskDefinition(1, "A", 10m) },
            StudentEntries = new() { new ExamStudentEntry(), new ExamStudentEntry { TaskPoints = new() { [1] = 0m } } } };
        var summary = ExamCalculator.CalculateExamSummary(exam);
        Assert.Equal(0, summary.GradedCount);
        Assert.Equal(2, summary.IncompleteCount);
        Assert.Null(summary.ClassAverage);
    }
    [Fact]
    public void CalculateErrorQuotient_ShouldReturnCorrectValue()
    {
        // Example from AGENTS.md: 12 errors and 600 words = 2.0
        var fq = ExamCalculator.CalculateErrorQuotient(600, 12);
        Assert.NotNull(fq);
        Assert.Equal(2.0m, fq.Value);
    }

    [Theory]
    [InlineData(null, 5)]
    [InlineData(0, 5)]
    [InlineData(-10, 5)]
    [InlineData(600, null)]
    [InlineData(600, -1)]
    public void CalculateErrorQuotient_InvalidInputs_ShouldReturnNull(int? words, int? errors)
    {
        var fq = ExamCalculator.CalculateErrorQuotient(words, errors);
        Assert.Null(fq);
    }

    [Fact]
    public void CalculateDeduction_ShouldRespectInclusiveThresholdAndExclusiveNext()
    {
        var tiers = new List<DeductionTier>
        {
            new DeductionTier(2.0m, 1.0m),
            new DeductionTier(4.0m, 2.0m)
        };

        // Below first threshold -> 0 deduction
        Assert.Equal(0.0m, ExamCalculator.CalculateDeduction(1.99m, tiers));
        Assert.Equal(0.0m, ExamCalculator.CalculateDeduction(0.0m, tiers));
        Assert.Equal(0.0m, ExamCalculator.CalculateDeduction(null, tiers));

        // Exactly at first threshold -> 1.0
        Assert.Equal(1.0m, ExamCalculator.CalculateDeduction(2.0m, tiers));

        // Between first and second -> 1.0
        Assert.Equal(1.0m, ExamCalculator.CalculateDeduction(3.99m, tiers));

        // Exactly at second threshold -> 2.0
        Assert.Equal(2.0m, ExamCalculator.CalculateDeduction(4.0m, tiers));

        // Above second threshold -> 2.0
        Assert.Equal(2.0m, ExamCalculator.CalculateDeduction(8.5m, tiers));
    }

    [Fact]
    public void CalculateDeduction_MaxDeductionCap_ShouldClamp()
    {
        var tiers = new List<DeductionTier>
        {
            new DeductionTier(2.0m, 1.0m),
            new DeductionTier(4.0m, 3.0m)
        };

        // Capped at 2.0
        Assert.Equal(2.0m, ExamCalculator.CalculateDeduction(5.0m, tiers, maxDeduction: 2.0m));
    }

    [Fact]
    public void CalculateFinalGrade_Schulnoten_ShouldWorsenNumericallyUpTo6()
    {
        // Initial grade 2, deduction 1 -> 3
        var (grade1, _) = ExamCalculator.CalculateFinalGrade(2, 1.0m, GradingSystem.Schulnoten);
        Assert.Equal(3, grade1);

        // Initial grade 5, deduction 2 -> 6 (clamped)
        var (grade2, _) = ExamCalculator.CalculateFinalGrade(5, 2.0m, GradingSystem.Schulnoten);
        Assert.Equal(6, grade2);
    }

    [Fact]
    public void CalculateFinalGrade_Notenpunkte_ShouldDecreaseValueDownTo0()
    {
        // Initial 12 points, deduction 2 -> 10
        var (pts1, _) = ExamCalculator.CalculateFinalGrade(12, 2.0m, GradingSystem.Notenpunkte);
        Assert.Equal(10, pts1);

        // Initial 1 point, deduction 2 -> 0 (clamped)
        var (pts2, _) = ExamCalculator.CalculateFinalGrade(1, 2.0m, GradingSystem.Notenpunkte);
        Assert.Equal(0, pts2);
    }

    [Fact]
    public void CalculateExamSummary_ShouldExcludeMissingAndIncompleteFromAverage()
    {
        var profile = GradingProfile.CreateDefaultSchulnoten();
        var exam = new Exam
        {
            Title = "Testklausur",
            Tasks = new List<TaskDefinition>
            {
                new TaskDefinition(1, "Aufgabe 1", 20m),
                new TaskDefinition(2, "Aufgabe 2", 30m)
            }, // Total 50 points
            GradingProfileSnapshot = profile,
            StudentEntries = new List<ExamStudentEntry>
            {
                // Student 1: 45 pts (90% -> Grade 1, 0 deduction -> Final 1)
                new ExamStudentEntry
                {
                    StudentId = "s1",
                    StudentName = "Schüler 1",
                    Status = StudentExamStatus.Graded,
                    TaskPoints = new() { { 1, 20m }, { 2, 25m } },
                    WordCount = 500,
                    ErrorCount = 5 // FQ = 1.0 -> 0 deduction
                },
                // Student 2: 25 pts (50% -> Grade 4, 0 deduction -> Final 4)
                new ExamStudentEntry
                {
                    StudentId = "s2",
                    StudentName = "Schüler 2",
                    Status = StudentExamStatus.Graded,
                    TaskPoints = new() { { 1, 10m }, { 2, 15m } },
                    WordCount = 400,
                    ErrorCount = 4
                },
                // Student 3: Missing (Krank) - MUST BE EXCLUDED FROM AVERAGE
                new ExamStudentEntry
                {
                    StudentId = "s3",
                    StudentName = "Schüler 3 (Krank)",
                    Status = StudentExamStatus.Missing,
                    TaskPoints = new(),
                    WordCount = null,
                    ErrorCount = null
                },
                // Student 4: Incomplete - MUST BE EXCLUDED FROM AVERAGE
                new ExamStudentEntry
                {
                    StudentId = "s4",
                    StudentName = "Schüler 4 (Unvollständig)",
                    Status = StudentExamStatus.Incomplete,
                    TaskPoints = new() { { 1, 10m } },
                    WordCount = null,
                    ErrorCount = null
                }
            }
        };

        var summary = ExamCalculator.CalculateExamSummary(exam);

        Assert.Equal(4, summary.TotalStudents);
        Assert.Equal(2, summary.GradedCount);
        Assert.Equal(2, summary.ExcludedCount);
        Assert.Equal(1, summary.MissingCount);
        Assert.Equal(1, summary.IncompleteCount);

        // Average must be (1 + 4) / 2 = 2.5
        Assert.NotNull(summary.ClassAverage);
        Assert.Equal(2.5m, summary.ClassAverage.Value);
    }

    [Fact]
    public void CalculateExamSummary_StudentWithZeroPoints_IsIncludedInAverageWhenGraded()
    {
        var profile = GradingProfile.CreateDefaultSchulnoten();
        var exam = new Exam
        {
            Title = "Klausur mit 0 Punkten",
            Tasks = new List<TaskDefinition>
            {
                new TaskDefinition(1, "Aufgabe 1", 50m)
            },
            GradingProfileSnapshot = profile,
            StudentEntries = new List<ExamStudentEntry>
            {
                new ExamStudentEntry
                {
                    StudentId = "s1",
                    StudentName = "Sehr gut",
                    Status = StudentExamStatus.Graded,
                    TaskPoints = new() { { 1, 50m } }, WordCount = 100, ErrorCount = 0 // 100% -> Grade 1
                },
                new ExamStudentEntry
                {
                    StudentId = "s2",
                    StudentName = "Null Punkte abgegeben",
                    Status = StudentExamStatus.Graded,
                    TaskPoints = new() { { 1, 0m } }, WordCount = 100, ErrorCount = 0 // 0% -> Grade 6
                }
            }
        };

        var summary = ExamCalculator.CalculateExamSummary(exam);

        Assert.Equal(2, summary.GradedCount);
        // Average: (1 + 6) / 2 = 3.5
        Assert.Equal(3.5m, summary.ClassAverage);
        Assert.Equal(1, summary.GradeDistribution["1"]);
        Assert.Equal(1, summary.GradeDistribution["6"]);
    }
}

