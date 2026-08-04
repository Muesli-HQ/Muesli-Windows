using System.Diagnostics;

namespace Muesli.Windows.Services;

public sealed class DictationCoordinator : IDisposable
{
    private readonly NativeTranscriptionClient _transcriptionClient;
    private readonly AudioCaptureService _audioCaptureService = new();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private Task<TranscriptionResult>? _activeTranscriptionTask;

    public bool IsRecording { get; private set; }
    public bool IsBusy { get; private set; }
    public bool IsTranscribing { get; private set; }
    public string EngineId => _transcriptionClient.EngineId;
    public string ModelId => _transcriptionClient.ModelId;
    public string ModelDisplayName => _transcriptionClient.ModelDisplayName;
    public bool IsModelReady => _transcriptionClient.IsSelectedModelReady;
    public CaptureCleanupResult StartupCaptureCleanup => _audioCaptureService.StartupCleanupResult;

    public DictationCoordinator(NativeTranscriptionClient? transcriptionClient = null)
    {
        _transcriptionClient = transcriptionClient ?? new NativeTranscriptionClient();
        _audioCaptureService.DeviceListChanged += OnDeviceListChanged;
        _audioCaptureService.RouteChanged += OnRouteChanged;
        _audioCaptureService.LevelChanged += OnLevelChanged;
    }

    public event EventHandler? DeviceListChanged;
    public event EventHandler<AudioRouteChangedEventArgs>? RouteChanged;
    public event EventHandler<AudioLevelEventArgs>? LevelChanged;

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

    public async Task<TranscriptionResult> StopAsync()
    {
        var stopResult = await StopAsync(
            $"aux-{Guid.NewGuid().ToString("N")[..8]}");
        return stopResult.Transcription;
    }

    /// <summary>Non-retaining setup path: transcribes a temporary capture without updating the benchmark alias.</summary>
    public async Task<TranscriptionResult> StopForOnboardingTestAsync(CancellationToken cancellationToken)
    {
        var stopResult = await StopAsync($"setup-{Guid.NewGuid().ToString("N")[..8]}", cancellationToken, keepLatestDictationAlias: false);
        return stopResult.Transcription;
    }

    public async Task<DictationStopResult> StopAsync(string traceId)
    {
        return await StopAsync(traceId, CancellationToken.None);
    }

    public async Task<DictationStopResult> StopAsync(string traceId, CancellationToken cancellationToken)
    {
        return await StopAsync(traceId, cancellationToken, keepLatestDictationAlias: true);
    }

    private async Task<DictationStopResult> StopAsync(string traceId, CancellationToken cancellationToken, bool keepLatestDictationAlias)
    {
        await _gate.WaitAsync();
        CapturedAudio? capturedAudio = null;
        Task<TranscriptionResult>? transcriptionTask = null;
        var disposeCapturedAudio = true;
        try
        {
            if (!IsRecording || IsBusy)
            {
                return new DictationStopResult(
                    new TranscriptionResult("", "Dictation was not recording."),
                    new DictationLatencyMetrics(traceId, 0, 0, 0, 0, "not captured", 0, 0));
            }

            IsBusy = true;
            IsRecording = false;
            var coordinatorStarted = Stopwatch.StartNew();
            var stopStarted = Stopwatch.StartNew();
            capturedAudio = await _audioCaptureService.StopAsync(keepLatestDictationAlias);
            stopStarted.Stop();
            try
            {
                if (DictationAudioQualityPolicy.IsNoSpeech(capturedAudio))
                {
                    coordinatorStarted.Stop();
                    return new DictationStopResult(
                        new TranscriptionResult(
                            "",
                            $"No speech preflight: heldMs={capturedAudio.HeldMs}; bytes={capturedAudio.ByteLength}; rms={capturedAudio.Rms:F6}; peak={capturedAudio.Peak:F6}",
                            DurationMs: capturedAudio.HeldMs),
                        new DictationLatencyMetrics(
                            traceId,
                            stopStarted.ElapsedMilliseconds,
                            capturedAudio.StopDisposeMs,
                            capturedAudio.FlushWaitMs,
                            capturedAudio.PreparationMs,
                            capturedAudio.Preparation,
                            0,
                            coordinatorStarted.ElapsedMilliseconds));
                }

                var transcriptionStarted = Stopwatch.StartNew();
                IsTranscribing = true;
                transcriptionTask = _transcriptionClient.TranscribeFileAsync("Dictation", capturedAudio.TranscriptionPath);
                _activeTranscriptionTask = transcriptionTask;
                TranscriptionResult result;
                try
                {
                    result = await transcriptionTask.WaitAsync(cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    if (!transcriptionTask.IsCompleted)
                    {
                        disposeCapturedAudio = false;
                        var audioToDispose = capturedAudio;
                        _ = transcriptionTask.ContinueWith(
                            completed =>
                            {
                                _ = completed.Exception;
                                audioToDispose.Dispose();
                            },
                            CancellationToken.None,
                            TaskContinuationOptions.ExecuteSynchronously,
                            TaskScheduler.Default);
                    }
                    throw;
                }
                transcriptionStarted.Stop();
                coordinatorStarted.Stop();
                var diagnostic = string.Join(
                    Environment.NewLine,
                    result.Diagnostic,
                    $"Latency trace: {traceId}",
                    $"Capture total stop ms: {stopStarted.ElapsedMilliseconds}",
                    $"Capture stop/dispose ms: {capturedAudio.StopDisposeMs}",
                    $"Capture flush wait ms: {capturedAudio.FlushWaitMs}",
                    $"Capture audio preparation ms: {capturedAudio.PreparationMs}",
                    $"Capture audio preparation: {capturedAudio.Preparation}",
                    $"Coordinator transcription wall ms: {transcriptionStarted.ElapsedMilliseconds}",
                    $"Coordinator total stop/transcribe ms: {coordinatorStarted.ElapsedMilliseconds}",
                    $"Native capture device: {capturedAudio.DeviceName}",
                    $"Native capture device id: {capturedAudio.DeviceId}",
                    $"Capture route segments: {capturedAudio.RouteSegmentCount}",
                    $"Captured audio bytes: {capturedAudio.ByteLength}",
                    $"Native capture held ms: {capturedAudio.HeldMs}",
                    $"Native input RMS: {capturedAudio.Rms:F6}",
                    $"Native input peak: {capturedAudio.Peak:F6}");

                return new DictationStopResult(
                    result with { Diagnostic = diagnostic, DurationMs = capturedAudio.HeldMs },
                    new DictationLatencyMetrics(
                        traceId,
                        stopStarted.ElapsedMilliseconds,
                        capturedAudio.StopDisposeMs,
                        capturedAudio.FlushWaitMs,
                        capturedAudio.PreparationMs,
                        capturedAudio.Preparation,
                        transcriptionStarted.ElapsedMilliseconds,
                        coordinatorStarted.ElapsedMilliseconds));
            }
            finally
            {
                // Normal dictation may retain the bounded benchmark alias; setup tests always dispose temporary audio.
                IsTranscribing = false;
                if (transcriptionTask?.IsCompleted == true && ReferenceEquals(_activeTranscriptionTask, transcriptionTask))
                {
                    _activeTranscriptionTask = null;
                }
                if (disposeCapturedAudio)
                {
                    capturedAudio.Dispose();
                }
            }
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

    public Task SwitchModelAsync(string modelId, CancellationToken cancellationToken = default) =>
        _transcriptionClient.SwitchModelAsync(modelId, cancellationToken);

    public Task<ModelOperationResult> InitializeModelAsync() => _transcriptionClient.InitializeAsync();

    public Task ReleaseModelAsync(string modelId, CancellationToken cancellationToken = default) =>
        _transcriptionClient.ReleaseModelAsync(modelId, cancellationToken);

    public void Dispose()
    {
        _audioCaptureService.DeviceListChanged -= OnDeviceListChanged;
        _audioCaptureService.RouteChanged -= OnRouteChanged;
        _audioCaptureService.LevelChanged -= OnLevelChanged;
        _audioCaptureService.Dispose();
        try
        {
            _activeTranscriptionTask?.GetAwaiter().GetResult();
        }
        catch
        {
            // Shutdown still disposes the native recognizer after an in-flight failure.
        }
        _transcriptionClient.Dispose();
        _gate.Dispose();
    }

    private void OnDeviceListChanged(object? sender, EventArgs e) => DeviceListChanged?.Invoke(this, e);
    private void OnRouteChanged(object? sender, AudioRouteChangedEventArgs e) => RouteChanged?.Invoke(this, e);
    private void OnLevelChanged(object? sender, AudioLevelEventArgs e) => LevelChanged?.Invoke(this, e);

}

public static class DictationAudioQualityPolicy
{
    public static bool IsNoSpeech(CapturedAudio audio) =>
        audio.HeldMs < 120 ||
        audio.ByteLength <= 44 ||
        (audio.Rms < 0.00008 && audio.Peak < 0.0005);
}

public sealed record DictationStopResult(
    TranscriptionResult Transcription,
    DictationLatencyMetrics Latency);

public sealed record DictationLatencyMetrics(
    string TraceId,
    long CaptureTotalMs,
    long CaptureStopDisposeMs,
    long CaptureFlushWaitMs,
    long CapturePreparationMs,
    string CapturePreparation,
    long TranscriptionWallMs,
    long CoordinatorTotalMs);
