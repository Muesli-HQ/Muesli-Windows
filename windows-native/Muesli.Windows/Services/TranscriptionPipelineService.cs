using System.Diagnostics;
using System.Text.RegularExpressions;

namespace Muesli.Windows.Services;

public sealed class TranscriptionPipelineService
{
    private static readonly Regex TimestampSpeakerLine = new(
        @"^(?<prefix>\[\d{2}:\d{2}:\d{2}\]\s+[^:\r\n]+:\s*)(?<body>.*)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex BracketSpeakerLine = new(
        @"^(?<prefix>\[[^\]\r\n]+\]\s*)(?<body>.*)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private readonly NativeTextCleanupService _cleanupService;
    private readonly AppLogService? _logService;

    public TranscriptionPipelineService(NativeTextCleanupService cleanupService, AppLogService? logService = null)
    {
        _cleanupService = cleanupService;
        _logService = logService;
    }

    public async Task<string> PrepareDictationTextAsync(
        string rawText,
        bool enableCleanup,
        bool removeFillerWords,
        IEnumerable<DictionaryEntryRecord> dictionaryEntries)
    {
        var cleaned = await CleanupAsync(rawText, enableCleanup, "dictation");
        if (removeFillerWords)
        {
            cleaned = FillerWordFilter.Apply(cleaned);
        }
        return DictionaryCorrectionService.Apply(cleaned, dictionaryEntries);
    }

    public async Task<string> PrepareMeetingTranscriptAsync(
        string mergedTranscript,
        bool enableCleanup,
        IEnumerable<DictionaryEntryRecord> dictionaryEntries)
    {
        var cleaned = await CleanupAsync(mergedTranscript, enableCleanup, "meeting");
        return ApplyDictionaryPreservingSpeakerPrefixes(cleaned, dictionaryEntries);
    }

    public async Task<string> PrepareImportedTranscriptAsync(
        string rawTranscript,
        bool enableCleanup,
        IEnumerable<DictionaryEntryRecord> dictionaryEntries,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var cleaned = await CleanupAsync(rawTranscript, enableCleanup, "imported media");
        cancellationToken.ThrowIfCancellationRequested();
        return ApplyDictionaryPreservingSpeakerPrefixes(cleaned, dictionaryEntries);
    }

    public void LogTranscriptionResult(string context, TranscriptionResult result, string engineId, string modelId)
    {
        var diagnostic = string.IsNullOrWhiteSpace(result.Diagnostic)
            ? "no diagnostic"
            : result.Diagnostic.Replace(Environment.NewLine, " | ");
        _logService?.Info(
            $"Transcription completed. context={context}; engine={engineId}; model={modelId}; durationMs={result.DurationMs}; segments={result.Segments?.Count ?? 0}; {diagnostic}");
    }

    public static bool HasUsableTimestampedSegments(TranscriptionResult result)
    {
        return result.Segments is { Count: > 0 } &&
               result.Segments.Any(segment => segment.EndMs > segment.StartMs);
    }

    public static string ApplyDictionaryPreservingSpeakerPrefixes(string text, IEnumerable<DictionaryEntryRecord> dictionaryEntries)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return text;
        }

        var entries = dictionaryEntries.ToList();
        if (entries.Count == 0)
        {
            return text;
        }

        var lines = text.Replace("\r\n", "\n").Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            lines[i] = ApplyDictionaryToLineBody(lines[i], entries);
        }

        return string.Join(Environment.NewLine, lines);
    }

    private async Task<string> CleanupAsync(string transcript, bool enableCleanup, string context)
    {
        if (!enableCleanup || string.IsNullOrWhiteSpace(transcript))
        {
            _logService?.Info($"Native cleanup skipped. context={context}; enabled={enableCleanup}; status={NativeTextCleanupService.Status(enableCleanup)}");
            return transcript;
        }

        var stopwatch = Stopwatch.StartNew();
        try
        {
            var cleaned = await _cleanupService.CleanupAsync(transcript, enableCleanup);
            stopwatch.Stop();
            _logService?.Info(
                $"Native cleanup completed. context={context}; status={NativeTextCleanupService.Status(enableCleanup)}; durationMs={stopwatch.ElapsedMilliseconds}; changed={!string.Equals(cleaned, transcript, StringComparison.Ordinal)}");
            return cleaned;
        }
        catch (Exception exception)
        {
            stopwatch.Stop();
            _logService?.Error($"Native cleanup failed. context={context}; durationMs={stopwatch.ElapsedMilliseconds}; using raw ASR text.", exception);
            return transcript;
        }
    }

    private static string ApplyDictionaryToLineBody(string line, IReadOnlyList<DictionaryEntryRecord> entries)
    {
        var timestampMatch = TimestampSpeakerLine.Match(line);
        if (timestampMatch.Success)
        {
            return timestampMatch.Groups["prefix"].Value +
                   DictionaryCorrectionService.Apply(timestampMatch.Groups["body"].Value, entries);
        }

        var bracketMatch = BracketSpeakerLine.Match(line);
        if (bracketMatch.Success)
        {
            return bracketMatch.Groups["prefix"].Value +
                   DictionaryCorrectionService.Apply(bracketMatch.Groups["body"].Value, entries);
        }

        return DictionaryCorrectionService.Apply(line, entries);
    }

}
