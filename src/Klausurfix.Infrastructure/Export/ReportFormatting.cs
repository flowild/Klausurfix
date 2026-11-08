namespace Klausurfix.Infrastructure.Export;

using Klausurfix.Core.Models;
using Klausurfix.Core.Calculations;
using System.Globalization;

internal static class ReportFormatting
{
    public static string ErrorThresholds(int? words, GradingProfile profile)
    {
        if (words is not > 0) return "Wortzahl fehlt oder ist 0";
        var ranges = ExamCalculator.CalculateErrorThresholds(words, profile)
            .Where((range, index) => index != 0 || range.Deduction != 0).ToList();
        if (ranges.Count == 0) return "Kein Abzug";
        var unit = profile.DeductionUnit switch
        {
            Klausurfix.Core.Models.DeductionUnit.Notenpunkte => "NP",
            Klausurfix.Core.Models.DeductionUnit.Schulnoten => "Noten",
            Klausurfix.Core.Models.DeductionUnit.ProzentDerGesamtpunkte => "%",
            _ => "Abzug"
        };
        var culture = CultureInfo.GetCultureInfo("de-DE");
        return string.Join("; ", ranges.Select(range =>
            $"{range.MinErrors.ToString("G", culture)}: {(range.Deduction > 0 ? "-" : "")}{range.Deduction.ToString("G", culture)} {unit}"));
    }

    public static string DeductionUnit(DeductionUnit unit) => unit switch
    {
        Klausurfix.Core.Models.DeductionUnit.Schulnoten => "Schulnoten",
        Klausurfix.Core.Models.DeductionUnit.Notenpunkte => "Notenpunkte",
        Klausurfix.Core.Models.DeductionUnit.ProzentDerGesamtpunkte => "% der Gesamtpunkte",
        _ => "Abzug"
    };
}
