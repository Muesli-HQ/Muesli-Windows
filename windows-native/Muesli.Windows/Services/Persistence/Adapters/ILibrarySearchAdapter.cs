namespace Muesli.Windows.Services.Persistence;

/// <summary>
/// FeatureRuntime-facing search contract. Unused in Wave 1. Production search is still the
/// in-memory filter in <c>FeatureRuntime.Search.PassesSearch</c>, which does not query
/// <see cref="ISearchRepository"/> and does not match manual notes.
/// </summary>
public interface ILibrarySearchAdapter
{
    /// <summary>
    /// Current production dictation match: transcript text and model profile, case-insensitive
    /// substring. Folder ancestry and titles are not consulted.
    /// </summary>
    bool DictationMatches(string text, string modelProfile, string query);

    /// <summary>
    /// Current production meeting match: title, generated summary, transcript, and metadata.
    /// Manual notes, aliases, follow-ups, and folder names are not consulted.
    /// </summary>
    bool MeetingMatches(string title, string summary, string transcript, string metadata, string query);
}

/// <summary>
/// Frozen copy of the production in-memory search field list. L29 must not treat this as the
/// destination contract: <see cref="ISearchRepository"/> already indexes notes, aliases,
/// follow-ups, and folder text that the UI still ignores.
/// </summary>
public static class ProductionInMemorySearchMatch
{
    public static bool DictationMatches(string text, string modelProfile, string query)
    {
        var needle = query.Trim();
        if (needle.Length == 0)
        {
            return true;
        }

        return Contains(text, needle) || Contains(modelProfile, needle);
    }

    public static bool MeetingMatches(string title, string summary, string transcript, string metadata, string query)
    {
        var needle = query.Trim();
        if (needle.Length == 0)
        {
            return true;
        }

        return Contains(title, needle) ||
               Contains(summary, needle) ||
               Contains(transcript, needle) ||
               Contains(metadata, needle);
    }

    private static bool Contains(string value, string query) =>
        value.Contains(query, StringComparison.OrdinalIgnoreCase);
}
