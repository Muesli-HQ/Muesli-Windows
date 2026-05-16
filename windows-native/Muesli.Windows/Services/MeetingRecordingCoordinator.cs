namespace Muesli.Windows.Services;

public sealed class MeetingRecordingCoordinator : IDisposable
{
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
                healthWarnings.Add("System audio was not captured; remote speakers may be missing.");
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
                healthWarnings.Add("Microphone transcript was empty even though microphone audio was captured.");
            }

            var systemTranscript = systemAudio is not null && systemAudio.Bytes.Length > 44
                ? await _workerClient.TranscribeFileAsync($"{title} system", systemAudio.LastCapturePath, options)
                : new TranscriptionResult("");
            _logService?.Info($"System transcript segments: {systemTranscript.Segments?.Count ?? 0}");

            if (systemAudio is not null && systemAudio.Bytes.Length > 44 && (systemTranscript.Segments?.Count ?? 0) == 0)
            {
                healthWarnings.Add("System transcript was empty even though system audio was captured.");
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
                        healthWarnings.Add("Speaker diarization returned no speaker segments; transcript used [System audio] fallback.");
                    }
                }
                catch (Exception exception)
                {
                    _logService?.Error($"Diarization failed on {systemAudio.LastCapturePath}", exception);
                    diarizationSegments = new List<DiarizedSegment>();
                    healthWarnings.Add("Speaker diarization failed; transcript used [System audio] fallback.");
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

            var distinctWarnings = healthWarnings.Distinct().ToList();

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
