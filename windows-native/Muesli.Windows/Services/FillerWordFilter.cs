using System.Text.RegularExpressions;

namespace Muesli.Windows.Services;

/// <summary>
/// Removes filler words and verbal disfluencies from transcribed text.
/// Applied as post-processing after ASR, before custom word matching.
/// Ported from macOS MuesliNativeApp/FillerWordFilter.swift.
/// </summary>
public static partial class FillerWordFilter
{
    private static readonly HashSet<string> Fillers = new(StringComparer.OrdinalIgnoreCase)
    {
        "uh", "um", "uh,", "um,", "uhh", "umm",
        "er", "err", "ah", "ahh",
        "hmm", "hm", "mm", "mmm",
        "like,",
    };

    private static readonly (string Pattern, string Replacement)[] FillerPhrases =
    [
        ("you know,", ""),
        ("i mean,", ""),
        ("sort of", ""),
        ("kind of", ""),
    ];

    [GeneratedRegex(@"\s{2,}", RegexOptions.Compiled)]
    private static partial Regex MultiSpacePattern();

    /// <summary>
    /// Remove filler words from transcribed text.
    /// </summary>
    public static string Apply(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        var result = text;

        // Phase 1: Remove multi-word filler phrases (case-insensitive)
        foreach (var (pattern, replacement) in FillerPhrases)
        {
            while (result.Contains(pattern, StringComparison.OrdinalIgnoreCase))
            {
                var index = result.IndexOf(pattern, StringComparison.OrdinalIgnoreCase);
                result = string.Concat(result.AsSpan(0, index), replacement, result.AsSpan(index + pattern.Length));
            }
        }

        // Phase 2: Remove single filler words
        var words = result.Split(' ');
        var filtered = words.Where(word => !Fillers.Contains(word)).ToArray();
        result = string.Join(' ', filtered);

        // Clean up: collapse multiple spaces, trim
        result = MultiSpacePattern().Replace(result, " ").Trim();

        // Fix capitalization after removal (re-capitalize sentence starts)
        if (result.Length > 0 && char.IsLower(result[0]))
        {
            result = string.Concat(char.ToUpperInvariant(result[0]).ToString(), result.AsSpan(1));
        }

        return result;
    }
}
