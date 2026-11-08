namespace Klausurfix.Core.Models;

using System.Globalization;

public static class TaskPointExpression
{
    public static bool TrySum(string? expression, out decimal sum)
    {
        sum = 0;
        if (string.IsNullOrWhiteSpace(expression) || expression.Length > 256) return false;
        var parts = expression.Split('+');
        if (parts.Length < 2) return false;
        foreach (var part in parts)
        {
            var text = part.Trim();
            if (text.Length == 0 || text.Any(c => c is < '0' or > '9') ||
                !decimal.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var number) ||
                number > 1000000 || sum > 1000000 - number) return false;
            sum += number;
        }
        return true;
    }

    public static string Format(ExamStudentEntry? entry, int taskNumber)
    {
        if (entry == null || !entry.TaskPoints.TryGetValue(taskNumber, out var points) || !points.HasValue) return "-";
        var result = points.Value.ToString("G", CultureInfo.GetCultureInfo("de-DE"));
        return entry.TaskPointExpressions != null && entry.TaskPointExpressions.TryGetValue(taskNumber, out var expression)
            ? $"{string.Join(" + ", expression.Split('+').Select(part => part.Trim()))} = {result}"
            : result;
    }
}
