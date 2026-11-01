namespace Klausurfix.Infrastructure.Export;

using Klausurfix.Core.Models;

internal static class ReportFormatting
{
    public static string DeductionUnit(DeductionUnit unit) => unit switch
    {
        Klausurfix.Core.Models.DeductionUnit.Schulnoten => "Schulnoten",
        Klausurfix.Core.Models.DeductionUnit.Notenpunkte => "Notenpunkte",
        Klausurfix.Core.Models.DeductionUnit.ProzentDerGesamtpunkte => "% der Gesamtpunkte",
        _ => "Abzug"
    };
}
