namespace Muesli.Windows.Services;

public sealed class MeetingRecordingCoordinator : IDisposable
{
    private const string SystemAudioMissingWarning = "System audio was not captured; remote speakers may be missing.";
    private const string SystemTranscriptEmptyWarning = "System transcript was empty even though system audio was captured.";
    private const string MicTranscriptEmptyWarning = "Mic transcript was empty even though mic audio was captured.";
    private const string DiarizationFailedWarning = "Speaker diarization failed; transcript used fallback speaker labels.";
    private const string DiarizationNoSegmentsWarning = "Speaker diarization returned no speaker segments; transcript used [System audio] fallback.";
    private const string HfTokenMissingWarning = "HF_TOKEN is not set; pyannote speaker diarization may fail unless the model is already cached.";
    private const string DiarizationDependenciesMissingWarning = "Speaker diarization dependencies are missing.";
    private const string DiarizationModelAccessWarning = "Speaker diarization model access failed. Check HF_TOKEN and accepted Hugging Face model access.";
    private const string TranscriptMissingWarning = "Meeting saved with no transcript; audio capture or transcription may have failed.";

    private readonly AudioCaptureService _micCapture = new();
    private readonly SystemAudioCaptureService _systemCapture = new();
    private readonly TranscriptionWorkerClient _workerClient = new();
    private readonly AppLogService? _logService;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private DateTime _startedAt;

    public bool IsRecording { get; private set; }
    public bool IsBusy { get; private set; }

    public MeetingRecordingCoordinator(AppLogService? logService = null)
    {
        _logService = logService;
    }

    public async Task StartAsync(string? microphoneName)
    {
        await _gate.WaitAsync();
        try
        {
            if (IsRecording || IsBusy)
            {
                return;
            }

            IsBusy = true;
            _startedAt = DateTime.Now;
            await _micCapture.StartAsync(microphoneName);
            try
            {
                await _systemCapture.StartAsync();
            }
            catch
            {
                // Some machines block loopback capture. Mic recording still remains useful.
            }

            IsRecording = true;
        }
        finally
        {
            IsBusy = false;
            _gate.Release();
        }
    }

    public async Task<RecordedMeetingResult> StopAsync(string title, TranscriptionOptions options)
    {
        await _gate.WaitAsync();
        try
        {
            if (!IsRecording || IsBusy)
            {
                throw new InvalidOperationException("Meeting recording is not running.");
            }

            IsBusy = true;
            IsRecording = false;
            CapturedAudio? micAudio = null;
            CapturedAudio? systemAudio = null;

            micAudio = await _micCapture.StopAsync();
            _logService?.Info($"Mic audio: {micAudio.LastCapturePath} ({micAudio.Bytes.Length} bytes)");

            try
            {
                systemAudio = await _systemCapture.StopAsync();
                _logService?.Info($"System audio: {systemAudio.LastCapturePath} ({systemAudio.Bytes.Length} bytes)");
            }
            catch (Exception exception)
            {
                _logService?.Info($"System audio unavailable: {exception.Message}");
                systemAudio = null;
            }

            List<string> healthWarnings = new();

            if (systemAudio is null)
            {
                healthWarnings.Add(SystemAudioMissingWarning);
            }
            else if (systemAudio.Bytes.Length <= 44)
            {
                healthWarnings.Add("System audio was nearly silent; remote speakers may be missing.");
            }

            if (micAudio.Bytes.Length <= 44)
            {
                healthWarnings.Add("Microphone audio was nearly silent; your voice may not have been recorded.");
            }

            var micTranscript = micAudio.Bytes.Length > 44
                ? await _workerClient.TranscribeFileAsync($"{title} mic", micAudio.LastCapturePath, options)
                : new TranscriptionResult("");
            _logService?.Info($"Mic transcript segments: {micTranscript.Segments?.Count ?? 0}");

            if (micAudio.Bytes.Length > 44 && (micTranscript.Segments?.Count ?? 0) == 0)
            {
                healthWarnings.Add(MicTranscriptEmptyWarning);
            }

            var systemTranscript = systemAudio is not null && systemAudio.Bytes.Length > 44
                ? await _workerClient.TranscribeFileAsync($"{title} system", systemAudio.LastCapturePath, options)
                : new TranscriptionResult("");
            _logService?.Info($"System transcript segments: {systemTranscript.Segments?.Count ?? 0}");

            if (systemAudio is not null && systemAudio.Bytes.Length > 44 && (systemTranscript.Segments?.Count ?? 0) == 0)
            {
                if ((micTranscript.Segments?.Count ?? 0) > 0)
                {
                    _logService?.Info("System transcript empty; treating as non-actionable because meeting still has mic transcript.");
                }
                else
                {
                    _logService?.Info("System transcript empty and no mic transcript segments were available.");
                }
            }

            // Attempt speaker diarization on system audio when available
            List<DiarizedSegment> diarizationSegments = new();
            if (systemAudio is not null && systemAudio.Bytes.Length > 44)
            {
                _logService?.Info($"Starting diarization on system audio: {systemAudio.LastCapturePath}");
                try
                {
                    var diarizationResult = await _workerClient.DiarizeFileAsync(systemAudio.LastCapturePath);
                    diarizationSegments = diarizationResult.Segments ?? new List<DiarizedSegment>();
                    _logService?.Info($"Diarization returned {diarizationSegments.Count} segments");
                    if (diarizationResult.Warnings is not null)
                    {
                        foreach (var warning in diarizationResult.Warnings)
                        {
                            _logService?.Info($"Diarization warning: {warning}");
                            if (!string.IsNullOrWhiteSpace(warning))
                                healthWarnings.Add(warning);
                        }
                    }
                    if (diarizationSegments.Count == 0)
                    {
                        _logService?.Info("Diarization returned 0 segments; using legacy [System audio] fallback.");
                        healthWarnings.Add(DiarizationNoSegmentsWarning);
                    }
                }
                catch (Exception exception)
                {
                    _logService?.Error($"Diarization failed on {systemAudio.LastCapturePath}", exception);
                    diarizationSegments = new List<DiarizedSegment>();
                    healthWarnings.Add(DiarizationFailedWarning);
                }
            }
            else
            {
                _logService?.Info("Diarization skipped: no system audio available.");
            }

            string merged;
            if (diarizationSegments.Count > 0)
            {
                _logService?.Info("Using diarized transcript merge.");
                merged = TranscriptFormatter.Merge(
                    micTranscript.Segments ?? new List<TranscriptSegment>(),
                    systemTranscript.Segments ?? new List<TranscriptSegment>(),
                    diarizationSegments,
                    _startedAt);
            }
            else
            {
                _logService?.Info("Using legacy transcript merge.");
                merged = TranscriptFormatter.Merge(
                    micTranscript.Segments ?? new List<TranscriptSegment>(),
                    systemTranscript.Segments ?? new List<TranscriptSegment>(),
                    diarizationSegments,
                    _startedAt);
            }

            var summary = MeetingSummaryService.CreateSummary(merged);
            var durationMs = (int)(DateTime.Now - _startedAt).TotalMilliseconds;
            if (string.IsNullOrWhiteSpace(merged))
            {
                healthWarnings.Add(TranscriptMissingWarning);
            }

            var distinctWarnings = CleanupHealthWarnings(
                healthWarnings,
                transcript: merged,
                diarizationSucceededWithSegments: diarizationSegments.Count > 0);

            return new RecordedMeetingResult(
                title,
                _startedAt,
                durationMs,
                merged,
                summary,
                micAudio.LastCapturePath,
                systemAudio?.LastCapturePath,
                distinctWarnings);
        }
        finally
        {
            IsBusy = false;
            _gate.Release();
        }
    }

    public void Dispose()
    {
        _micCapture.Dispose();
        _systemCapture.Dispose();
        _workerClient.Dispose();
    }

    public static List<string> CleanupHealthWarnings(
        IEnumerable<string>? warnings,
        string? transcript = null,
        bool diarizationSucceededWithSegments = false)
    {
        var cleaned = new List<string>();
        if (warnings is null)
        {
            return cleaned;
        }

        var warningList = warnings
            .Where(warning => !string.IsNullOrWhiteSpace(warning))
            .Select(warning => warning.Trim())
            .ToList();
        var effectiveDiarizationSuccess = diarizationSucceededWithSegments
            || warningList.Any(HasSuccessfulDiarizationSegmentCount)
            || TranscriptHasDiarizedSpeakerLabels(transcript);

        foreach (var warning in warningList)
        {
            var normalized = NormalizeHealthWarning(warning, effectiveDiarizationSuccess);
            if (!string.IsNullOrWhiteSpace(normalized)
                && !cleaned.Any(existing => string.Equals(existing, normalized, StringComparison.Ordinal)))
            {
                cleaned.Add(normalized);
            }
        }

        return cleaned;
    }

    private static string? NormalizeHealthWarning(string? warning, bool diarizationSucceededWithSegments)
    {
        if (string.IsNullOrWhiteSpace(warning))
        {
            return null;
        }

        var text = warning.Trim();

        if (text.Equals(SystemAudioMissingWarning, StringComparison.OrdinalIgnoreCase))
            return SystemAudioMissingWarning;
        if (text.Equals(SystemTranscriptEmptyWarning, StringComparison.OrdinalIgnoreCase)
            || text.Contains("System transcript was empty", StringComparison.OrdinalIgnoreCase))
            return null;
        if (text.Equals(MicTranscriptEmptyWarning, StringComparison.OrdinalIgnoreCase)
            || text.Contains("Microphone transcript was empty", StringComparison.OrdinalIgnoreCase)
            || text.Contains("Mic transcript was empty", StringComparison.OrdinalIgnoreCase))
            return MicTranscriptEmptyWarning;
        if (text.Equals("System audio was nearly silent; remote speakers may be missing.", StringComparison.OrdinalIgnoreCase))
            return "System audio was nearly silent; remote speakers may be missing.";
        if (text.Equals("Microphone audio was nearly silent; your voice may not have been recorded.", StringComparison.OrdinalIgnoreCase))
            return "Microphone audio was nearly silent; your voice may not have been recorded.";

        if (text.Equals(DiarizationNoSegmentsWarning, StringComparison.OrdinalIgnoreCase)
            || text.Contains("returned no speaker segments", StringComparison.OrdinalIgnoreCase))
            return DiarizationNoSegmentsWarning;
        if (text.Equals(DiarizationFailedWarning, StringComparison.OrdinalIgnoreCase)
            || text.Contains("Speaker diarization failed", StringComparison.OrdinalIgnoreCase)
            || text.Equals("Diarization failed.", StringComparison.OrdinalIgnoreCase))
            return DiarizationFailedWarning;
        if (text.Equals(TranscriptMissingWarning, StringComparison.OrdinalIgnoreCase)
            || text.Contains("no transcript", StringComparison.OrdinalIgnoreCase))
            return TranscriptMissingWarning;

        if (text.Contains("dependencies missing", StringComparison.OrdinalIgnoreCase))
            return DiarizationDependenciesMissingWarning;

        var mentionsHfToken = text.Contains("HF_TOKEN", StringComparison.OrdinalIgnoreCase);
        var mentionsUnauthenticatedRequests = text.Contains("unauthenticated requests", StringComparison.OrdinalIgnoreCase);
        var mentionsModelAccess = text.Contains("access denied", StringComparison.OrdinalIgnoreCase)
            || text.Contains("model access failed", StringComparison.OrdinalIgnoreCase)
            || text.Contains("gated repo", StringComparison.OrdinalIgnoreCase)
            || text.Contains("gated repository", StringComparison.OrdinalIgnoreCase)
            || text.Contains("403", StringComparison.OrdinalIgnoreCase)
            || text.Contains("401", StringComparison.OrdinalIgnoreCase)
            || text.Contains("permission", StringComparison.OrdinalIgnoreCase);
        if (mentionsModelAccess)
            return DiarizationModelAccessWarning;
        if (mentionsHfToken || mentionsUnauthenticatedRequests)
            return diarizationSucceededWithSegments ? null : HfTokenMissingWarning;

        if (text.StartsWith("ASR engine:", StringComparison.OrdinalIgnoreCase)
            || text.StartsWith("Diarization segments:", StringComparison.OrdinalIgnoreCase)
            || text.StartsWith("Timing diarization ", StringComparison.OrdinalIgnoreCase)
            || text.StartsWith("Worker stderr:", StringComparison.OrdinalIgnoreCase)
            || text.StartsWith("Input path:", StringComparison.OrdinalIgnoreCase)
            || text.StartsWith("Input bytes:", StringComparison.OrdinalIgnoreCase)
            || text.StartsWith("Model cache:", StringComparison.OrdinalIgnoreCase)
            || text.StartsWith("Backend:", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (text.Contains("torchvision", StringComparison.OrdinalIgnoreCase)
            || text.Contains("UserWarning", StringComparison.OrdinalIgnoreCase)
            || text.Contains("site-packages", StringComparison.OrdinalIgnoreCase)
            || text.Contains(".py:", StringComparison.OrdinalIgnoreCase)
            || text.Contains("\\", StringComparison.OrdinalIgnoreCase)
            || text.Contains("/", StringComparison.OrdinalIgnoreCase)
            || text.Contains("waveform", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return null;
    }

    private static bool HasSuccessfulDiarizationSegmentCount(string warning)
    {
        if (!warning.StartsWith("Diarization segments:", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var parts = warning.Split(':', 2);
        return parts.Length == 2
            && int.TryParse(parts[1].Trim(), out var count)
            && count > 0;
    }

    private static bool TranscriptHasDiarizedSpeakerLabels(string? transcript)
    {
        if (string.IsNullOrWhiteSpace(transcript))
        {
            return false;
        }

        return System.Text.RegularExpressions.Regex.IsMatch(
            transcript,
            @"\[\d{2}:\d{2}:\d{2}\]\s+[^:\r\n]+:\s");
    }
}

public sealed record RecordedMeetingResult(
    string Title,
    DateTime StartedAt,
    int DurationMs,
    string Transcript,
    string Summary,
    string MicAudioPath,
    string? SystemAudioPath,
    List<string>? HealthWarnings = null);
