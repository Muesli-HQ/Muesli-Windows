using System.Text.RegularExpressions;

namespace Muesli.Windows.Services;

public static class DictionaryCorrectionService
{
    public static string Apply(string text, IEnumerable<DictionaryEntryRecord> entries)
    {
        var corrected = text;
        foreach (var entry in entries)
        {
            if (string.IsNullOrWhiteSpace(entry.Phrase) || string.IsNullOrWhiteSpace(entry.Replacement))
            {
                continue;
            }

            var phrase = entry.Phrase.Trim();
            var replacement = entry.Replacement.Trim();
            corrected = Regex.Replace(
                corrected,
                $@"\b{Regex.Escape(phrase)}\b",
                replacement,
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

            if (phrase.Contains(' ', StringComparison.Ordinal))
            {
                continue;
            }

            corrected = Regex.Replace(
                corrected,
                @"\b[\p{L}\p{N}'-]+\b",
                match =>
                {
                    var token = match.Value;
                    if (token.Equals(phrase, StringComparison.OrdinalIgnoreCase))
                    {
                        return token;
                    }

                    return Similarity(token, phrase) >= ClampThreshold(entry.MatchingThreshold)
                        ? PreserveSimpleCasing(replacement, token)
                        : token;
                },
                RegexOptions.CultureInvariant);
        }

        return corrected;
    }

    private static double ClampThreshold(double threshold)
    {
        if (double.IsNaN(threshold) || threshold <= 0)
        {
            return 0.85;
        }

        return Math.Clamp(threshold, 0.70, 0.98);
    }

    private static double Similarity(string left, string right)
    {
        var a = left.Trim().ToLowerInvariant();
        var b = right.Trim().ToLowerInvariant();
        if (a.Length == 0 || b.Length == 0)
        {
            return 0;
        }

        var distance = LevenshteinDistance(a, b);
        return 1.0 - distance / (double)Math.Max(a.Length, b.Length);
    }

    private static int LevenshteinDistance(string left, string right)
    {
        var previous = new int[right.Length + 1];
        var current = new int[right.Length + 1];

        for (var j = 0; j <= right.Length; j++)
        {
            previous[j] = j;
        }

        for (var i = 1; i <= left.Length; i++)
        {
            current[0] = i;
            for (var j = 1; j <= right.Length; j++)
            {
                var cost = left[i - 1] == right[j - 1] ? 0 : 1;
                current[j] = Math.Min(
                    Math.Min(current[j - 1] + 1, previous[j] + 1),
                    previous[j - 1] + cost);
            }

            (previous, current) = (current, previous);
        }

        return previous[right.Length];
    }

    private static string PreserveSimpleCasing(string replacement, string source)
    {
        if (source.Length == 0 || replacement.Length == 0)
        {
            return replacement;
        }

        return char.IsUpper(source[0])
            ? char.ToUpperInvariant(replacement[0]) + replacement[1..]
            : replacement;
    }
}
