namespace Klausurfix.Infrastructure.Export;

using System.Text.RegularExpressions;
using Klausurfix.Core.Models;

public static class CoverSheetFileName
{
    public static string Docx(Exam exam)
    {
        var title = Regex.Replace(exam.Title.Trim(), @"^Klausur(?=$|[\s:\-–])\s*[:\-–]?\s*", "", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        title = Regex.Replace(title, @"\s+Klausur\s*$", "", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        var name = string.Join(" ", new[] { Clean(exam.ClassName), Clean(title), "Klausur", exam.Date.ToString("dd.MM.yyyy", System.Globalization.CultureInfo.InvariantCulture) }
            .Where(part => part.Length > 0));
        return name + ".docx";
    }

    private static string Clean(string value)
    {
        var safe = new string(value.Select(c => char.IsControl(c) || "<>:\"/\\|?*".Contains(c) ? ' ' : c).ToArray());
        safe = Regex.Replace(safe, @"\s+", " ").Trim(' ', '.');
        // Keep the complete date and extension even for unusually long class names or titles.
        return safe.Length <= 100 ? safe : safe[..100].TrimEnd(' ', '.');
    }
}
