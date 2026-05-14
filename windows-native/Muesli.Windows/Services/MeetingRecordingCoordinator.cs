namespace Muesli.Windows.Services;

public sealed class MeetingRecordingCoordinator : IDisposable
{
    private readonly AudioCaptureService _micCapture = new();
    private readonly SystemAudioCaptureService _systemCapture = new();
    private readonly TranscriptionWorkerClient _workerClient = new();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private DateTime _startedAt;

    public bool IsRecording { get; private set; }
    public bool IsBusy { get; private set; }

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
            try
            {
                systemAudio = await _systemCapture.StopAsync();
            }
            catch
            {
                systemAudio = null;
            }

            var micTranscript = micAudio.Bytes.Length > 44
                ? await _workerClient.TranscribeFileAsync($"{title} mic", micAudio.LastCapturePath, options)
                : new TranscriptionResult("");
            var systemTranscript = systemAudio is not null && systemAudio.Bytes.Length > 44
                ? await _workerClient.TranscribeFileAsync($"{title} system", systemAudio.LastCapturePath, options)
                : new TranscriptionResult("");

            var merged = MergeTranscripts(micTranscript.Text, systemTranscript.Text);
            var summary = MeetingSummaryService.CreateSummary(merged);
            var durationMs = (int)(DateTime.Now - _startedAt).TotalMilliseconds;

            return new RecordedMeetingResult(
                title,
                _startedAt,
                durationMs,
                merged,
                summary,
                micAudio.LastCapturePath,
                systemAudio?.LastCapturePath);
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

    private static string MergeTranscripts(string micTranscript, string systemTranscript)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(micTranscript))
        {
            parts.Add($"[You] {micTranscript.Trim()}");
        }

        if (!string.IsNullOrWhiteSpace(systemTranscript))
        {
            parts.Add($"[System audio] {systemTranscript.Trim()}");
        }

        return string.Join(Environment.NewLine + Environment.NewLine, parts);
    }
}

public sealed record RecordedMeetingResult(
    string Title,
    DateTime StartedAt,
    int DurationMs,
    string Transcript,
    string Summary,
    string MicAudioPath,
    string? SystemAudioPath);
