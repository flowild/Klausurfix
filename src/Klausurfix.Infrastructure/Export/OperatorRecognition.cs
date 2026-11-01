namespace Klausurfix.Infrastructure.Export;

using System.Text.RegularExpressions;

public record OperatorTextPart(string Text, bool Underlined);
public record OperatorRecognitionResult(List<OperatorTextPart> Parts, List<string> OperatorIds);

/// <summary>Recognises common instruction forms without changing the saved task text.</summary>
public static class OperatorRecognition
{
    private static readonly (string Id, string Pattern)[] Separable =
    {
        ("wiedergeben", @"\b(?<op>geben)\s+Sie\b[^.!?;\r\n]{0,300}?\b(?<op>wieder)\b"),
        ("zusammenfassen", @"\b(?<op>fassen)\s+Sie\b[^.!?;\r\n]{0,300}?\b(?<op>zusammen)\b"),
        ("darstellen", @"\b(?<op>stellen)\s+Sie\b[^.!?;\r\n]{0,300}?\b(?<op>dar)\b"),
        ("einordnen", @"\b(?<op>ordnen)\s+Sie\b[^.!?;\r\n]{0,300}?\b(?<op>ein|zu)\b"),
        ("auswerten", @"\b(?<op>werten)\s+Sie\b[^.!?;\r\n]{0,300}?\b(?<op>aus)\b"),
        ("herausarbeiten", @"\b(?<op>arbeiten)\s+Sie\b[^.!?;\r\n]{0,300}?\b(?<op>heraus)\b"),
        ("vergleichen", @"\b(?<op>stellen)\s+Sie\b[^.!?;\r\n]{0,300}?\b(?<op>gegenüber)\b"),
        ("in-beziehung-setzen", @"\b(?<op>setzen)\s+Sie\b[^.!?;\r\n]{0,300}?\b(?<op>in\s+Beziehung)\b"),
        ("bewerten", @"\b(?<op>nehmen)\s+Sie\b[^.!?;\r\n]{0,300}?\b(?<op>Stellung)\b"),
        ("diskutieren", @"\b(?<op>setzen)\s+Sie\s+sich\b[^.!?;\r\n]{0,300}?\b(?<op>auseinander)\b")
    };

    public static OperatorRecognitionResult Recognize(string text)
    {
        var spans = new List<(int Start, int Length, string Id)>();
        var options = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;
        foreach (var (id, pattern) in Separable)
            foreach (Match match in Regex.Matches(text, pattern, options, TimeSpan.FromMilliseconds(100)))
                foreach (Capture capture in match.Groups["op"].Captures)
                    spans.Add((capture.Index, capture.Length, id));

        foreach (var op in OperatorCatalog.All)
        foreach (var name in op.Name.Split('/', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var pattern = @"(?<![\p{L}\p{M}])" + Regex.Escape(name).Replace(@"\ ", @"\s+") + @"(?![\p{L}\p{M}])";
            foreach (Match match in Regex.Matches(text, pattern, options, TimeSpan.FromMilliseconds(100)))
                spans.Add((match.Index, match.Length, op.Id));
        }

        var ordered = spans.OrderBy(s => s.Start).ThenByDescending(s => s.Length).ToList();
        var parts = new List<OperatorTextPart>();
        var ids = new List<string>();
        var position = 0;
        foreach (var span in ordered)
        {
            if (span.Start < position) continue;
            if (span.Start > position) parts.Add(new(text[position..span.Start], false));
            parts.Add(new(text.Substring(span.Start, span.Length), true));
            if (!ids.Contains(span.Id)) ids.Add(span.Id);
            position = span.Start + span.Length;
        }
        if (position < text.Length) parts.Add(new(text[position..], false));
        return new(parts, ids);
    }
}
