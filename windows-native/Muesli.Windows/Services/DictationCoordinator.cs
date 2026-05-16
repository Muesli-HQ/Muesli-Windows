namespace Muesli.Windows.Services;

public sealed class DictationCoordinator
{
    private readonly TranscriptionWorkerClient _workerClient = new();
    private readonly AudioCaptureService _audioCaptureService = new();
    private readonly SemaphoreSlim _gate = new(1, 1);

    public bool IsRecording { get; private set; }
    public bool IsBusy { get; private set; }

    public IReadOnlyList<string> ListMicrophones() => _audioCaptureService.ListCaptureDevices();
    public string PickPreferredMicrophone() => _audioCaptureService.PickPreferredDeviceName();

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
            await _audioCaptureService.StartAsync(microphoneName);
            IsRecording = true;
        }
        finally
        {
            IsBusy = false;
            _gate.Release();
        }
    }

    public async Task<TranscriptionResult> StopAsync(TranscriptionOptions options)
    {
        await _gate.WaitAsync();
        try
        {
            if (!IsRecording || IsBusy)
            {
                return new TranscriptionResult("", "Dictation was not recording.");
            }

            IsBusy = true;
            IsRecording = false;
            var capturedAudio = await _audioCaptureService.StopAsync();

            var result = await _workerClient.TranscribeAsync(capturedAudio.Bytes, options);
            var diagnostic = string.Join(
                Environment.NewLine,
                result.Diagnostic,
                $"Native WASAPI capture: {capturedAudio.FilePath}",
                $"Last capture saved: {capturedAudio.LastCapturePath}",
                $"Captured audio bytes: {capturedAudio.Bytes.Length}",
                $"Native capture held ms: {capturedAudio.HeldMs}",
                $"Native input RMS: {capturedAudio.Rms:F6}",
                $"Native input peak: {capturedAudio.Peak:F6}");

            return result with { Diagnostic = diagnostic, DurationMs = capturedAudio.HeldMs };
        }
        finally
        {
            IsBusy = false;
            _gate.Release();
        }
    }

    public async Task CancelAsync()
    {
        await _gate.WaitAsync();
        try
        {
            if (!IsRecording || IsBusy)
            {
                return;
            }

            IsBusy = true;
            IsRecording = false;
            await _audioCaptureService.CancelAsync();
        }
        finally
        {
            IsBusy = false;
            _gate.Release();
        }
    }

    public Task<PostProcessingResult> PostProcessAsync(string text, string context, string systemPrompt = "")
    {
        return _workerClient.PostProcessAsync(text, context, systemPrompt);
    }

    public Task<PostProcessingResult> DownloadModelAsync(string kind, string model)
    {
        return _workerClient.DownloadModelAsync(kind, model);
    }
}

public sealed record TranscriptionResult(string Text, string? Diagnostic = null, int DurationMs = 0, List<TranscriptSegment>? Segments = null);
