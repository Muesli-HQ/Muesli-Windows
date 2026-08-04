using NAudio.Wave;

namespace Muesli.Windows.Services;

public sealed class MeetingRecordingCoordinator : IDisposable
{
    private const string SystemAudioMissingWarning = "System audio was not captured; remote speakers may be missing.";
    private const string SystemTranscriptEmptyWarning = "System transcript was empty even though system audio was captured.";
    private const string MicTranscriptEmptyWarning = "Mic transcript was empty even though mic audio was captured.";
    private const string DiarizationFailedWarning = "Speaker diarization failed; transcript used fallback speaker labels.";
    private const string DiarizationNoSegmentsWarning = "Speaker diarization returned no speaker segments; transcript used [System audio] fallback.";
    private const string TranscriptMissingWarning = "Meeting saved with no transcript; audio capture or transcription may have failed.";

    private readonly NativeTranscriptionClient _transcriptionClient;
    private readonly NativeDiarizationClient _diarizationClient = new();
    private readonly MeetingSessionJournalStore _journalStore;
    private readonly AppLogService? _logService;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly SemaphoreSlim _repairGate = new(1, 1);
    private readonly object _warningGate = new();
    private readonly object _liveCheckpointGate = new();
    private readonly MeetingSessionStateMachine _stateMachine = new();
    private readonly List<string> _healthWarnings = [];
    private IReadOnlyList<LiveTranscriptSegment>? _pendingLiveCheckpoint;
    private int _liveCheckpointScheduled;
    private long? _micPartStartedAtMs;
    private long? _systemPartStartedAtMs;
    private AudioCaptureService? _micCapture;
    private SystemAudioCaptureService? _systemCapture;
    private MeetingSessionJournal? _journal;
    private MeetingAudioHealthMonitor? _healthMonitor;
    private MeetingLiveTranscriptionSession? _liveSession;
    private MeetingLiveTranscriptionResult? _liveResult;
    private LiveTranscriptionConfiguration? _liveConfiguration;
    private CancellationTokenSource? _lifecycleCancellation;
    private Task? _healthLoop;
    private DateTime _startedAt;
    private string _meetingId = "";
    private string? _microphoneName;
    private int? _targetProcessId;
    private bool _micAvailable;
    private bool _systemAvailable;
    private int _disposed;

    public MeetingRecordingCoordinator(
        NativeTranscriptionClient transcriptionClient,
        AppLogService? logService = null,
        MeetingSessionJournalStore? journalStore = null)
    {
        _transcriptionClient = transcriptionClient;
        _logService = logService;
        _journalStore = journalStore ?? new MeetingSessionJournalStore(
            report: warning => _logService?.Info($"Meeting journal recovery notice. category={DiagnosticCategory(warning)}"));
    }

    public event EventHandler<MeetingSessionStateChangedEventArgs>? StateChanged;
    public event EventHandler<MeetingAudioHealthChangedEventArgs>? HealthChanged;
    public event EventHandler<AudioLevelEventArgs>? LevelChanged;
    public event EventHandler<LiveTranscriptSnapshot>? LiveTranscriptChanged;
    public event EventHandler<Exception>? LiveTranscriptionFailed;

    public MeetingSessionState State => _stateMachine.State;
    public bool IsRecording => State is MeetingSessionState.Recording or MeetingSessionState.DegradedRecording;
    public bool IsBusy => State is MeetingSessionState.Preparing or MeetingSessionState.Stopping or MeetingSessionState.Finalizing;

    public IReadOnlyList<RecoverableMeetingSession> DiscoverRecoverableSessions() =>
        _journalStore.DiscoverRecoverable();

    public async Task<MeetingStartResult> StartAsync(
        string? microphoneName,
        string? title = null,
        bool retainRecording = true,
        int? targetProcessId = null,
        CancellationToken cancellationToken = default,
        LiveTranscriptionConfiguration? liveConfiguration = null)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            if (IsRecording || IsBusy)
            {
                throw new InvalidOperationException("A meeting session is already active.");
            }
            ResetTerminalStateIfNeeded();
            Transition(MeetingSessionTrigger.Prepare, "meeting capture requested");

            _startedAt = DateTime.Now;
            _meetingId = $"meet_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}_{Guid.NewGuid().ToString("N")[..8]}";
            _microphoneName = string.IsNullOrWhiteSpace(microphoneName)
                ? AudioCaptureService.SystemDefaultMicrophone
                : microphoneName;
            _targetProcessId = targetProcessId is > 0 ? targetProcessId : null;
            _liveConfiguration = liveConfiguration;
            _liveResult = null;
            lock (_warningGate)
            {
                _healthWarnings.Clear();
            }

            _journal = _journalStore.Create(
                _meetingId,
                string.IsNullOrWhiteSpace(title) ? $"Meeting {_startedAt:yyyy-MM-dd HH-mm}" : title.Trim(),
                new DateTimeOffset(_startedAt),
                _microphoneName,
                _transcriptionClient.ModelId,
                retainRecording,
                _targetProcessId,
                liveConfiguration?.ModelId,
                liveConfiguration?.OwnershipMode);
            _lifecycleCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _healthMonitor = new MeetingAudioHealthMonitor(new DateTimeOffset(_startedAt));

            await StartLiveSessionUnderGateAsync(_lifecycleCancellation.Token).ConfigureAwait(false);

            var start = await StartCapturePairUnderGateAsync(_lifecycleCancellation.Token).ConfigureAwait(false);
            if (!_micAvailable && !_systemAvailable)
            {
                Transition(MeetingSessionTrigger.Fail, "both capture channels failed to start");
                SaveJournalState(MeetingSessionState.Failed, "capture-start-failed");
                _journalStore.DeleteSession(_meetingId);
                throw new InvalidOperationException("Neither microphone nor system audio could start.");
            }

            Transition(
                _micAvailable && _systemAvailable
                    ? MeetingSessionTrigger.Prepared
                    : MeetingSessionTrigger.PreparedDegraded,
                _micAvailable && _systemAvailable ? "both channels started" : "one channel started");
            SaveJournalState(State);
            StartHealthLoop();
            LogSessionDiagnostic("started");
            return new MeetingStartResult(
                _meetingId,
                State,
                start.SystemCaptureMode,
                start.Warning,
                _micAvailable,
                _systemAvailable);
        }
        catch
        {
            if (State == MeetingSessionState.Preparing && _stateMachine.CanApply(MeetingSessionTrigger.Fail))
            {
                Transition(MeetingSessionTrigger.Fail, "meeting preparation failed");
            }
            await CancelCaptureServicesUnderGateAsync().ConfigureAwait(false);
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<RecordedMeetingResult> StopAsync(
        string title,
        bool retainRecording,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            if (!IsRecording)
            {
                throw new InvalidOperationException("Meeting recording is not running.");
            }

            if (_journal is not null && _journal.RetainRecording != retainRecording)
            {
                _journal = _journal with { RetainRecording = retainRecording };
            }
            Transition(MeetingSessionTrigger.Stop, "user or qualified auto-stop requested");
            StopHealthLoop();
            SaveJournalState(MeetingSessionState.Stopping);
            await CheckpointAllChannelsUnderGateAsync().ConfigureAwait(false);
            Transition(MeetingSessionTrigger.TracksFinalized, "capture tracks checkpointed");
            SaveJournalState(MeetingSessionState.Finalizing);
            return await FinalizeJournalUnderGateAsync(title, recovered: false, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            PreserveRecoverableUnderGate("finalization-cancelled");
            throw;
        }
        catch (Exception exception) when (State is MeetingSessionState.Stopping or MeetingSessionState.Finalizing)
        {
            var hasAudio = HasJournalAudio();
            if (hasAudio)
            {
                PreserveRecoverableUnderGate(exception.GetType().Name);
                throw new MeetingSessionRecoverableException(
                    "Meeting finalization failed, but its local audio is retained for retry.",
                    _meetingId,
                    exception);
            }

            Transition(MeetingSessionTrigger.Fail, "finalization failed without recoverable audio");
            SaveJournalState(MeetingSessionState.Failed, exception.GetType().Name);
            throw;
        }
        finally
        {
            await DisposeCaptureServicesUnderGateAsync().ConfigureAwait(false);
            _gate.Release();
        }
    }

    public async Task<RecordedMeetingResult> FinalizeRecoverableAsync(
        RecoverableMeetingSession recovery,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(recovery);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            if (IsRecording || IsBusy)
            {
                throw new InvalidOperationException("Another meeting session is active.");
            }
            ResetTerminalStateIfNeeded();
            _journal = recovery.Journal with { RecoveredFromInterruption = true };
            _meetingId = _journal.SessionId;
            _startedAt = _journal.StartedAtUtc.LocalDateTime;
            _microphoneName = _journal.MicrophoneName;
            _targetProcessId = _journal.TargetProcessId;
            _liveResult = _journal.LiveModelId is not null &&
                          _journal.LiveTranscriptOwnership.Equals("unified-live-final", StringComparison.OrdinalIgnoreCase)
                ? new MeetingLiveTranscriptionResult(
                    _journal.LiveTranscriptSegments,
                    _journal.LiveTranscriptGaps.Concat(new[]
                    {
                        new LiveTranscriptGap(LiveTranscriptChannel.Microphone, 0, long.MaxValue, "recoverable-interruption"),
                        new LiveTranscriptGap(LiveTranscriptChannel.System, 0, long.MaxValue, "recoverable-interruption")
                    }).ToList(),
                    _journal.LiveDroppedPacketCount,
                    _journal.LiveModelId,
                    LiveTranscriptOwnershipMode.UnifiedLiveAndFinal)
                : null;
            lock (_warningGate)
            {
                _healthWarnings.Clear();
                _healthWarnings.AddRange(_journal.Warnings);
                _healthWarnings.Add("Recording was recovered after an interrupted Muesli session.");
            }

            Transition(MeetingSessionTrigger.RestoreInterrupted, "recoverable journal loaded");
            Transition(MeetingSessionTrigger.FinalizeInterrupted, "user requested recovery finalization");
            Transition(MeetingSessionTrigger.TracksFinalized, "recovered tracks already checkpointed");
            SaveJournalState(MeetingSessionState.Finalizing);
            return await FinalizeJournalUnderGateAsync(_journal.Title, recovered: true, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            PreserveRecoverableUnderGate("recovery-finalization-cancelled");
            throw;
        }
        catch (Exception exception) when (State is MeetingSessionState.Stopping or MeetingSessionState.Finalizing)
        {
            PreserveRecoverableUnderGate(exception.GetType().Name);
            throw new MeetingSessionRecoverableException(
                "Recovered meeting audio is still retained, but finalization failed.",
                _meetingId,
                exception);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SuspendAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!IsRecording)
            {
                return;
            }
            StopHealthLoop();
            await CheckpointForInterruptionUnderGateAsync(
                "Windows power suspend",
                "Recording paused because Windows suspended. Capture will resume when the system returns.",
                "power-suspend").ConfigureAwait(false);
            LogSessionDiagnostic("suspended");
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task ResumeAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (State != MeetingSessionState.RecoverableInterruption || _journal is null)
            {
                return;
            }
            Transition(MeetingSessionTrigger.Resume, "Windows power resume");
            _lifecycleCancellation?.Dispose();
            _lifecycleCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _healthMonitor = new MeetingAudioHealthMonitor(DateTimeOffset.UtcNow);
            await StartLiveSessionUnderGateAsync(_lifecycleCancellation.Token).ConfigureAwait(false);
            await StartCapturePairUnderGateAsync(_lifecycleCancellation.Token).ConfigureAwait(false);
            if (!_micAvailable && !_systemAvailable)
            {
                Transition(MeetingSessionTrigger.Interrupt, "capture could not resume");
                SaveJournalState(MeetingSessionState.RecoverableInterruption, "resume-failed");
                return;
            }
            Transition(
                _micAvailable && _systemAvailable
                    ? MeetingSessionTrigger.Prepared
                    : MeetingSessionTrigger.PreparedDegraded,
                "capture resumed after Windows power transition");
            AddHealthWarning("Recording resumed after a Windows power interruption.");
            SaveJournalState(State);
            StartHealthLoop();
            LogSessionDiagnostic("resumed");
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task PreserveForShutdownAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!IsRecording)
            {
                return;
            }
            StopHealthLoop();
            await CheckpointForInterruptionUnderGateAsync(
                "application shutdown",
                "Recording was interrupted by application shutdown and retained for recovery.",
                "application-shutdown").ConfigureAwait(false);
            LogSessionDiagnostic("preserved-for-shutdown");
        }
        catch (Exception exception)
        {
            _logService?.Error("Meeting recording could not be fully checkpointed during shutdown.", exception);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task CancelAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!StateAllowsCancellation())
            {
                return;
            }
            StopHealthLoop();
            _lifecycleCancellation?.Cancel();
            await CancelLiveSessionUnderGateAsync().ConfigureAwait(false);
            await CancelCaptureServicesUnderGateAsync().ConfigureAwait(false);
            Transition(MeetingSessionTrigger.Cancel, "user discarded meeting recording");
            SaveJournalState(MeetingSessionState.Cancelled);
            if (!string.IsNullOrWhiteSpace(_meetingId))
            {
                try
                {
                    _journalStore.DeleteSession(_meetingId);
                }
                catch (Exception exception)
                {
                    _logService?.Info($"Cancelled meeting audio deletion failed. category={exception.GetType().Name}");
                }
            }
            _journal = null;
            LogSessionDiagnostic("cancelled");
        }
        finally
        {
            _gate.Release();
        }
    }

    public void AcknowledgePersisted(string meetingId)
    {
        _gate.Wait();
        try
        {
            ThrowIfDisposed();
            if (!meetingId.Equals(_meetingId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Meeting persistence acknowledgement does not match the active session.");
            }
            if (State == MeetingSessionState.Finalizing)
            {
                Transition(MeetingSessionTrigger.Complete, "meeting record durably persisted");
                SaveJournalState(MeetingSessionState.Completed);
                LogSessionDiagnostic("completed");
            }
            try
            {
                _journalStore.DeleteSession(meetingId);
            }
            catch (Exception exception)
            {
                _logService?.Info($"Completed meeting journal cleanup failed. category={exception.GetType().Name}");
            }
            _journal = null;
        }
        finally
        {
            _gate.Release();
        }
    }

    public void DiscardRecoverable(string meetingId) => _journalStore.DeleteSession(meetingId);

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }
        StopHealthLoop();
        _lifecycleCancellation?.Cancel();
        try
        {
            _healthLoop?.GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
        }

        var repairGateHeld = false;
        var lifecycleGateHeld = false;
        try
        {
            _repairGate.Wait();
            repairGateHeld = true;
            _gate.Wait();
            lifecycleGateHeld = true;
            DisposeCaptureServicesUnderGateAsync().GetAwaiter().GetResult();
            CancelLiveSessionUnderGateAsync().GetAwaiter().GetResult();
        }
        catch
        {
        }
        finally
        {
            if (lifecycleGateHeld)
            {
                _gate.Release();
            }
            if (repairGateHeld)
            {
                _repairGate.Release();
            }
        }
        _lifecycleCancellation?.Dispose();
        _diarizationClient.Dispose();
        _gate.Dispose();
        _repairGate.Dispose();
    }

    private async Task<CapturePairStartResult> StartCapturePairUnderGateAsync(CancellationToken cancellationToken)
    {
        if (_journal is null)
        {
            throw new InvalidOperationException("Meeting journal is unavailable.");
        }
        var sessionDirectory = _journalStore.GetSessionDirectory(_journal.SessionId);
        _micCapture = new AudioCaptureService(
            sessionDirectory,
            MeetingSessionJournalStore.MicrophoneCapturePrefix,
            cleanupInterruptedDictation: false);
        _systemCapture = new SystemAudioCaptureService(
            sessionDirectory,
            MeetingSessionJournalStore.SystemCapturePrefix);
        SubscribeCaptureEvents();

        Exception? micFailure = null;
        Exception? systemFailure = null;
        SystemAudioCaptureStartResult? systemStart = null;
        var micTask = Task.Run(async () =>
        {
            try
            {
                await _micCapture.StartAsync(_microphoneName).ConfigureAwait(false);
                _micPartStartedAtMs = ElapsedMeetingMs();
                _micAvailable = true;
            }
            catch (Exception exception)
            {
                micFailure = exception;
                _micAvailable = false;
            }
        }, cancellationToken);
        var systemTask = Task.Run(async () =>
        {
            try
            {
                systemStart = await _systemCapture.StartAsync(_targetProcessId, cancellationToken).ConfigureAwait(false);
                _systemPartStartedAtMs = ElapsedMeetingMs();
                _systemAvailable = true;
            }
            catch (Exception exception)
            {
                systemFailure = exception;
                _systemAvailable = false;
            }
        }, cancellationToken);
        await Task.WhenAll(micTask, systemTask).ConfigureAwait(false);

        if (micFailure is not null)
        {
            AddHealthWarning("Microphone audio could not start; your side may be missing.");
        }
        if (systemFailure is not null)
        {
            AddHealthWarning(SystemAudioMissingWarning);
        }
        if (!string.IsNullOrWhiteSpace(systemStart?.Warning))
        {
            AddHealthWarning(systemStart.Warning);
        }
        _journal = _journal with
        {
            SystemCaptureMode = systemStart?.Mode.ToString() ?? "unavailable"
        };
        return new CapturePairStartResult(
            systemStart?.Mode,
            systemStart?.Warning,
            micFailure,
            systemFailure);
    }

    private async Task StartLiveSessionUnderGateAsync(CancellationToken cancellationToken)
    {
        if (_liveConfiguration is null || _liveSession is not null) return;
        try
        {
            var model = StreamingModelCatalog.GetRequired(_liveConfiguration.ModelId);
            _liveSession = await Task.Run(
                () => new MeetingLiveTranscriptionSession(model, _liveConfiguration.OwnershipMode),
                cancellationToken).ConfigureAwait(false);
            _liveSession.SnapshotChanged += OnLiveTranscriptSnapshot;
            _liveSession.Failed += OnLiveTranscriptionFailed;
        }
        catch (Exception exception)
        {
            if (_liveConfiguration.OwnershipMode == LiveTranscriptOwnershipMode.UnifiedLiveAndFinal)
            {
                _liveResult = new MeetingLiveTranscriptionResult(
                    [],
                    [
                        new LiveTranscriptGap(LiveTranscriptChannel.Microphone, 0, long.MaxValue, "streaming-engine-unavailable"),
                        new LiveTranscriptGap(LiveTranscriptChannel.System, 0, long.MaxValue, "streaming-engine-unavailable")
                    ],
                    0,
                    _liveConfiguration.ModelId,
                    LiveTranscriptOwnershipMode.UnifiedLiveAndFinal);
            }
            AddHealthWarning("Live transcription could not start; retained audio will still be finalized locally.");
            _logService?.Info($"Live transcription unavailable. model={_liveConfiguration.ModelId}; category={exception.GetType().Name}");
            LiveTranscriptionFailed?.Invoke(this, exception);
        }
    }

    private async Task CheckpointAllChannelsUnderGateAsync()
    {
        await CheckpointChannelUnderGateAsync(MeetingAudioChannel.Microphone).ConfigureAwait(false);
        await CheckpointChannelUnderGateAsync(MeetingAudioChannel.System).ConfigureAwait(false);
        await FinishLiveSessionUnderGateAsync().ConfigureAwait(false);
        if (!HasJournalAudio())
        {
            throw new InvalidOperationException("Meeting capture produced no recoverable audio track.");
        }
    }

    private async Task CheckpointForInterruptionUnderGateAsync(
        string reason,
        string warning,
        string failureCategory)
    {
        try
        {
            await CheckpointAllChannelsUnderGateAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _logService?.Info(
                $"Meeting interruption checkpoint was partial. category={exception.GetType().Name}; audioRetained={HasJournalAudio()}");
        }

        if (HasJournalAudio())
        {
            Transition(MeetingSessionTrigger.Interrupt, reason);
            AddHealthWarning(warning);
            SaveJournalState(MeetingSessionState.RecoverableInterruption, failureCategory);
        }
        else
        {
            Transition(MeetingSessionTrigger.Fail, $"{reason} without recoverable audio");
            SaveJournalState(MeetingSessionState.Failed, $"{failureCategory}-no-audio");
        }
        await DisposeCaptureServicesUnderGateAsync().ConfigureAwait(false);
    }

    private async Task CheckpointChannelUnderGateAsync(MeetingAudioChannel channel)
    {
        if (_journal is null)
        {
            return;
        }
        CapturedAudio? audio = null;
        try
        {
            audio = channel == MeetingAudioChannel.Microphone
                ? _micCapture is null
                    ? null
                    : await _micCapture.StopAsync(keepLatestDictationAlias: false).ConfigureAwait(false)
                : _systemCapture is null
                    ? null
                    : await _systemCapture.StopAsync().ConfigureAwait(false);
            if (audio is not null && audio.ByteLength > 44)
            {
                // Anchor the part where its capture actually started, so a late, repaired, or
                // resumed channel is not folded back to the start of the meeting on merge.
                var anchor = channel == MeetingAudioChannel.Microphone ? _micPartStartedAtMs : _systemPartStartedAtMs;
                _journal = _journalStore.AppendPart(_journal, channel, audio.TranscriptionPath, anchor);
            }
        }
        catch (Exception exception)
        {
            AddHealthWarning(channel == MeetingAudioChannel.Microphone
                ? "Microphone audio could not be finalized; your side may be incomplete."
                : SystemAudioMissingWarning);
            _logService?.Info($"Meeting channel checkpoint failed. channel={channel}; category={exception.GetType().Name}");
        }
        finally
        {
            audio?.Dispose();
            if (channel == MeetingAudioChannel.Microphone)
            {
                _micAvailable = false;
                _micPartStartedAtMs = null;
            }
            else
            {
                _systemAvailable = false;
                _systemPartStartedAtMs = null;
            }
        }
    }

    private async Task<RecordedMeetingResult> FinalizeJournalUnderGateAsync(
        string title,
        bool recovered,
        CancellationToken cancellationToken)
    {
        if (_journal is null)
        {
            throw new InvalidOperationException("Meeting journal is unavailable.");
        }
        cancellationToken.ThrowIfCancellationRequested();
        var tracks = _journalStore.BuildFinalTracks(_journal);
        if (tracks.MicrophonePath is null && tracks.SystemPath is null)
        {
            throw new InvalidOperationException("No valid meeting track remained after recovery.");
        }

        var healthWarnings = CurrentHealthWarnings();
        if (tracks.MicrophonePath is null)
        {
            healthWarnings.Add("Microphone audio was not captured; your voice may be missing.");
        }
        if (tracks.SystemPath is null)
        {
            healthWarnings.Add(SystemAudioMissingWarning);
        }

        Task<DiarizationResult>? diarizationTask = null;
        var diarizationObserved = false;
        try
        {
            if (tracks.SystemPath is not null && NativeDiarizationClient.ShouldOverlapWithAsr)
            {
                diarizationTask = _diarizationClient.DiarizeFileAsync(tracks.SystemPath);
            }

            cancellationToken.ThrowIfCancellationRequested();
            TranscriptionResult micTranscript;
            TranscriptionResult systemTranscript;
            if (_liveResult?.OwnershipMode == LiveTranscriptOwnershipMode.UnifiedLiveAndFinal)
            {
                var owned = await MeetingGapRecoveryService.BuildUnifiedOwnerResultsAsync(
                    _liveResult,
                    tracks,
                    _transcriptionClient,
                    cancellationToken).ConfigureAwait(false);
                micTranscript = owned.Microphone;
                systemTranscript = owned.System;
                healthWarnings.Add(_liveResult.Gaps.Count == 0
                    ? "Final raw transcript is owned by the live model; no gap recovery was required."
                    : $"Final raw transcript is owned by the live model; the configured final model recovered {_liveResult.Gaps.Count} measured gap(s).");
            }
            else
            {
                micTranscript = tracks.MicrophonePath is not null
                    ? await _transcriptionClient.TranscribeFileAsync("Meeting microphone", tracks.MicrophonePath).ConfigureAwait(false)
                    : new TranscriptionResult("");
                cancellationToken.ThrowIfCancellationRequested();
                systemTranscript = tracks.SystemPath is not null
                    ? await _transcriptionClient.TranscribeFileAsync("Meeting system audio", tracks.SystemPath).ConfigureAwait(false)
                    : new TranscriptionResult("");
            }
            LogTranscriptionDiagnostic("meeting-mic", micTranscript);
            if (tracks.MicrophonePath is not null && (micTranscript.Segments?.Count ?? 0) == 0)
            {
                healthWarnings.Add(MicTranscriptEmptyWarning);
            }

            LogTranscriptionDiagnostic("meeting-system", systemTranscript);
            if (tracks.SystemPath is not null &&
                (systemTranscript.Segments?.Count ?? 0) == 0 &&
                (micTranscript.Segments?.Count ?? 0) == 0)
            {
                healthWarnings.Add(SystemTranscriptEmptyWarning);
            }

            if (diarizationTask is null && tracks.SystemPath is not null)
            {
                diarizationTask = _diarizationClient.DiarizeFileAsync(tracks.SystemPath);
            }
            List<DiarizedSegment> diarizationSegments = [];
            if (diarizationTask is not null)
            {
                try
                {
                    diarizationObserved = true;
                    var diarization = await diarizationTask.ConfigureAwait(false);
                    diarizationSegments = diarization.Segments ?? [];
                    if (diarizationSegments.Count == 0)
                    {
                        healthWarnings.Add(DiarizationNoSegmentsWarning);
                    }
                    foreach (var warning in diarization.Warnings ?? [])
                    {
                        healthWarnings.Add(warning);
                    }
                    _logService?.Info(
                        $"Meeting diarization completed. segments={diarizationSegments.Count}; provider={diarization.Provider}; processingMs={diarization.ProcessingMs}; modelReused={diarization.ModelReused}");
                }
                catch (Exception exception)
                {
                    healthWarnings.Add(DiarizationFailedWarning);
                    _logService?.Info($"Meeting diarization failed. category={exception.GetType().Name}");
                }
            }

            // Final ASR and diarization both report positions inside a concatenated track. Map each
            // channel back onto meeting time before merging, otherwise a channel that started late or
            // restarted mid-meeting is interleaved at the wrong point in the conversation.
            var micTimeline = _journalStore.BuildTrackTimeline(_journal, MeetingAudioChannel.Microphone);
            var systemTimeline = _journalStore.BuildTrackTimeline(_journal, MeetingAudioChannel.System);
            var merged = TranscriptFormatter.Merge(
                MeetingTranscriptTimeline.Normalize(micTranscript.Segments, micTimeline),
                MeetingTranscriptTimeline.Normalize(systemTranscript.Segments, systemTimeline),
                MeetingTranscriptTimeline.Normalize(diarizationSegments, systemTimeline),
                _startedAt);
            var durationMs = MeasureRecordedDurationMs(tracks);
            var cleanedWarnings = CleanupHealthWarnings(
                healthWarnings,
                merged,
                diarizationSegments.Count > 0);
            MeetingSessionState terminalState;
            if (string.IsNullOrWhiteSpace(merged))
            {
                cleanedWarnings.Add(TranscriptMissingWarning);
                Transition(MeetingSessionTrigger.Fail, "no transcript was produced");
                terminalState = MeetingSessionState.Failed;
            }
            else
            {
                terminalState = MeetingSessionState.Completed;
            }
            _journal = _journal with
            {
                Title = title,
                State = terminalState == MeetingSessionState.Completed
                    ? MeetingSessionState.Finalizing
                    : terminalState,
                Warnings = cleanedWarnings,
                RecoveredFromInterruption = recovered || _journal.RecoveredFromInterruption,
                UpdatedAtUtc = DateTimeOffset.UtcNow
            };
            _journalStore.Save(_journal);
            LogSessionDiagnostic(terminalState == MeetingSessionState.Completed
                ? "finalized-awaiting-persistence"
                : "failed-no-transcript");
            return new RecordedMeetingResult(
                _meetingId,
                title,
                _startedAt,
                durationMs,
                merged,
                _journal.RetainRecording ? tracks.MicrophonePath : null,
                _journal.RetainRecording ? tracks.SystemPath : null,
                cleanedWarnings,
                terminalState,
                _journal.SystemCaptureMode,
                recovered || _journal.RecoveredFromInterruption,
                _journal.LiveModelId,
                _journal.LiveTranscriptOwnership,
                _journal.FinalTranscriptOwnerModelId,
                _journal.GapRecoveryModelId);
        }
        finally
        {
            await AwaitDiarizationBeforeCleanupAsync(
                diarizationTask,
                diarizationObserved,
                exception => _logService?.Info($"Deferred diarization cleanup failed. category={exception.GetType().Name}"))
                .ConfigureAwait(false);
        }
    }

    private void SubscribeCaptureEvents()
    {
        if (_micCapture is not null)
        {
            _micCapture.RouteChanged += OnMicrophoneRouteChanged;
            _micCapture.MetricsAvailable += OnAudioMetrics;
            _micCapture.CaptureFaulted += OnCaptureFaulted;
            _micCapture.LevelChanged += OnMicrophoneLevelChanged;
            _micCapture.PcmSamplesAvailable += OnLivePcmSamples;
        }
        if (_systemCapture is not null)
        {
            _systemCapture.RouteChanged += OnSystemRouteChanged;
            _systemCapture.MetricsAvailable += OnAudioMetrics;
            _systemCapture.CaptureFaulted += OnCaptureFaulted;
            _systemCapture.PcmSamplesAvailable += OnLivePcmSamples;
        }
    }

    private void UnsubscribeCaptureEvents()
    {
        if (_micCapture is not null)
        {
            _micCapture.RouteChanged -= OnMicrophoneRouteChanged;
            _micCapture.MetricsAvailable -= OnAudioMetrics;
            _micCapture.CaptureFaulted -= OnCaptureFaulted;
            _micCapture.LevelChanged -= OnMicrophoneLevelChanged;
            _micCapture.PcmSamplesAvailable -= OnLivePcmSamples;
        }
        if (_systemCapture is not null)
        {
            _systemCapture.RouteChanged -= OnSystemRouteChanged;
            _systemCapture.MetricsAvailable -= OnAudioMetrics;
            _systemCapture.CaptureFaulted -= OnCaptureFaulted;
            _systemCapture.PcmSamplesAvailable -= OnLivePcmSamples;
        }
    }

    /// <summary>Milliseconds of real meeting time elapsed, used to anchor each captured part.</summary>
    private long ElapsedMeetingMs() => Math.Max(0, (long)(DateTime.Now - _startedAt).TotalMilliseconds);

    private void OnMicrophoneLevelChanged(object? sender, AudioLevelEventArgs e) =>
        LevelChanged?.Invoke(this, e);

    private void OnLivePcmSamples(object? sender, LivePcmSamplesEventArgs e) => _liveSession?.TryEnqueue(e);

    /// <summary>
    /// Raised on the streaming worker thread. Journal writes are handed to a coalesced
    /// background flush so neither disk I/O nor an unsynchronized <see cref="_journal"/>
    /// mutation happens on the thread that owns live inference.
    /// </summary>
    private void OnLiveTranscriptSnapshot(object? sender, LiveTranscriptSnapshot snapshot)
    {
        lock (_liveCheckpointGate) _pendingLiveCheckpoint = snapshot.Committed;
        if (Interlocked.Exchange(ref _liveCheckpointScheduled, 1) == 0)
        {
            _ = Task.Run(FlushLiveCheckpointAsync);
        }
        LiveTranscriptChanged?.Invoke(this, snapshot);
    }

    private async Task FlushLiveCheckpointAsync()
    {
        try
        {
            if (Volatile.Read(ref _disposed) != 0) return;
            await _gate.WaitAsync().ConfigureAwait(false);
        }
        catch (ObjectDisposedException)
        {
            return;
        }
        try
        {
            Interlocked.Exchange(ref _liveCheckpointScheduled, 0);
            IReadOnlyList<LiveTranscriptSegment>? pending;
            lock (_liveCheckpointGate)
            {
                pending = _pendingLiveCheckpoint;
                _pendingLiveCheckpoint = null;
            }
            if (pending is null || _journal is null || pending.Count <= _journal.LiveTranscriptSegments.Count)
            {
                return;
            }
            _journal = _journal with { LiveTranscriptSegments = pending.ToList() };
            _journalStore.Save(_journal);
        }
        catch (Exception exception)
        {
            _logService?.Info($"Live transcript checkpoint failed. category={exception.GetType().Name}; transcriptLogged=false");
        }
        finally
        {
            _gate.Release();
        }
    }

    private void OnLiveTranscriptionFailed(object? sender, Exception exception)
    {
        AddHealthWarning("Live transcription stopped; retained audio remains available for final transcription and measured gap recovery.");
        _logService?.Info($"Live transcription stopped. category={exception.GetType().Name}; transcriptLogged=false");
        LiveTranscriptionFailed?.Invoke(this, exception);
    }

    private async Task FinishLiveSessionUnderGateAsync()
    {
        var session = Interlocked.Exchange(ref _liveSession, null);
        if (session is null) return;
        session.SnapshotChanged -= OnLiveTranscriptSnapshot;
        session.Failed -= OnLiveTranscriptionFailed;
        try
        {
            var current = await session.FinishAsync(CancellationToken.None).ConfigureAwait(false);
            _liveResult = MergeLiveResults(_liveResult, current);
            if (_journal is not null)
            {
                _journal = _journal with
                {
                    LiveDroppedPacketCount = _liveResult.DroppedPackets,
                    LiveTranscriptGaps = _liveResult.Gaps.ToList(),
                    LiveTranscriptSegments = _liveResult.Committed.ToList()
                };
                _journalStore.Save(_journal);
            }
            _logService?.Info($"Live transcription finalized. model={_liveResult.ModelId}; ownership={_liveResult.OwnershipMode}; segments={_liveResult.Committed.Count}; gaps={_liveResult.Gaps.Count}; dropped={_liveResult.DroppedPackets}; transcriptLogged=false");
        }
        finally
        {
            await session.DisposeAsync().ConfigureAwait(false);
        }
    }

    private static MeetingLiveTranscriptionResult MergeLiveResults(MeetingLiveTranscriptionResult? prior, MeetingLiveTranscriptionResult current)
    {
        if (prior is null) return current;
        var finiteGapEnds = prior.Gaps.Where(gap => gap.EndSample != long.MaxValue).Select(gap => gap.EndSample);
        var offset = prior.Committed.Select(segment => segment.EndSample).Concat(finiteGapEnds).DefaultIfEmpty(0).Max();
        var shiftedSegments = current.Committed.Select(segment => segment with
        {
            Id = $"resume_{prior.Committed.Count}_{segment.Id}",
            StartSample = segment.StartSample + offset,
            EndSample = segment.EndSample + offset
        });
        var shiftedGaps = current.Gaps.Select(gap => gap with
        {
            StartSample = gap.StartSample + offset,
            EndSample = gap.EndSample == long.MaxValue ? long.MaxValue : gap.EndSample + offset
        });
        return current with
        {
            Committed = prior.Committed.Concat(shiftedSegments).ToList(),
            Gaps = MeetingLiveTranscriptionSession.CoalesceGaps(prior.Gaps.Concat(shiftedGaps)),
            DroppedPackets = prior.DroppedPackets + current.DroppedPackets
        };
    }

    private void OnAudioMetrics(object? sender, MeetingAudioMetrics metrics) =>
        _healthMonitor?.Note(metrics);

    private void OnMicrophoneRouteChanged(object? sender, AudioRouteChangedEventArgs args)
    {
        if (args.Kind == AudioRouteChangeKind.Failed)
        {
            _ = RepairChannelAsync(MeetingAudioChannel.Microphone, args.Exception);
            return;
        }
        AddHealthWarning(args.Message);
        if (args.Kind == AudioRouteChangeKind.Recovered)
        {
            _micAvailable = true;
            TryRestoreHealthyState("microphone route recovered");
        }
    }

    private void OnSystemRouteChanged(object? sender, AudioRouteChangedEventArgs args)
    {
        if (args.Kind == AudioRouteChangeKind.Failed)
        {
            _ = RepairChannelAsync(MeetingAudioChannel.System, args.Exception);
            return;
        }
        AddHealthWarning(args.Message);
        if (args.Kind == AudioRouteChangeKind.Recovered)
        {
            _systemAvailable = true;
            TryRestoreHealthyState("system route recovered");
        }
    }

    private void OnCaptureFaulted(object? sender, CaptureFaultedEventArgs args) =>
        _ = RepairChannelAsync(args.Channel, args.Exception);

    private async Task RepairChannelAsync(MeetingAudioChannel channel, Exception? failure)
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            return;
        }
        await _repairGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (Volatile.Read(ref _disposed) != 0)
            {
                return;
            }
            for (var completedAttempts = 0; ; completedAttempts++)
            {
                var peerAvailable = channel == MeetingAudioChannel.Microphone ? _systemAvailable : _micAvailable;
                var decision = MeetingCaptureRepairPolicy.Decide(completedAttempts, peerAvailable);
                if (decision.Action == MeetingCaptureRepairAction.ContinueDegraded)
                {
                    AddHealthWarning($"{ChannelLabel(channel)} could not restart after {completedAttempts} attempts; the other channel is still recording.");
                    SaveJournalState(MeetingSessionState.DegradedRecording, $"{channel}-repair-exhausted");
                    return;
                }
                if (decision.Action == MeetingCaptureRepairAction.PreserveForRecovery)
                {
                    await _gate.WaitAsync().ConfigureAwait(false);
                    try
                    {
                        if (IsRecording)
                        {
                            try
                            {
                                await CheckpointAllChannelsUnderGateAsync().ConfigureAwait(false);
                            }
                            catch when (HasJournalAudio())
                            {
                                // A partial checkpoint is still recoverable.
                            }
                            if (HasJournalAudio())
                            {
                                Transition(MeetingSessionTrigger.Interrupt, "both capture channels unavailable");
                                SaveJournalState(MeetingSessionState.RecoverableInterruption, "capture-repair-exhausted");
                            }
                            else
                            {
                                Transition(MeetingSessionTrigger.Fail, "capture repair exhausted without audio");
                                SaveJournalState(MeetingSessionState.Failed, "capture-repair-no-audio");
                            }
                        }
                    }
                    finally
                    {
                        _gate.Release();
                    }
                    return;
                }

                if (decision.Delay > TimeSpan.Zero)
                {
                    var token = _lifecycleCancellation?.Token ?? CancellationToken.None;
                    try
                    {
                        await Task.Delay(decision.Delay, token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }
                }

                await _gate.WaitAsync().ConfigureAwait(false);
                try
                {
                    if (!IsRecording || _journal is null)
                    {
                        return;
                    }
                    MarkChannelUnavailable(channel);
                    if (State == MeetingSessionState.Recording)
                    {
                        Transition(MeetingSessionTrigger.Degrade, $"{channel} capture fault");
                    }
                    AddHealthWarning($"{ChannelLabel(channel)} capture was interrupted; Muesli is attempting a loss-bounded restart.");
                    await CheckpointChannelUnderGateAsync(channel).ConfigureAwait(false);
                    try
                    {
                        if (channel == MeetingAudioChannel.Microphone && _micCapture is not null)
                        {
                            await _micCapture.StartAsync(_microphoneName).ConfigureAwait(false);
                            // The restarted part resumes at real meeting time, not at the end of the
                            // audio captured so far; the interruption is real elapsed time.
                            _micPartStartedAtMs = ElapsedMeetingMs();
                            _micAvailable = true;
                        }
                        else if (channel == MeetingAudioChannel.System && _systemCapture is not null)
                        {
                            var result = await _systemCapture.StartAsync(_targetProcessId).ConfigureAwait(false);
                            _systemPartStartedAtMs = ElapsedMeetingMs();
                            _systemAvailable = true;
                            if (!string.IsNullOrWhiteSpace(result.Warning))
                            {
                                AddHealthWarning(result.Warning);
                            }
                        }
                        UpdateRepairAttempts(channel, decision.Attempt);
                        TryRestoreHealthyState($"{channel} capture restarted");
                        SaveJournalState(State);
                        LogSessionDiagnostic("capture-repaired");
                        return;
                    }
                    catch (Exception exception)
                    {
                        failure = exception;
                        UpdateRepairAttempts(channel, decision.Attempt);
                        _logService?.Info(
                            $"Meeting capture repair failed. channel={channel}; attempt={decision.Attempt}; category={exception.GetType().Name}");
                    }
                }
                finally
                {
                    _gate.Release();
                }
            }
        }
        finally
        {
            _repairGate.Release();
        }
    }

    private void StartHealthLoop()
    {
        var token = _lifecycleCancellation?.Token ?? CancellationToken.None;
        _healthLoop = Task.Run(async () =>
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
            try
            {
                while (await timer.WaitForNextTickAsync(token).ConfigureAwait(false))
                {
                    var snapshot = _healthMonitor?.Evaluate(DateTimeOffset.UtcNow);
                    if (snapshot is null)
                    {
                        continue;
                    }
                    foreach (var warning in snapshot.Warnings)
                    {
                        AddHealthWarning(warning);
                    }
                    HealthChanged?.Invoke(this, new MeetingAudioHealthChangedEventArgs(snapshot));
                    if (snapshot.IsDegraded && State == MeetingSessionState.Recording)
                    {
                        TryTransition(MeetingSessionTrigger.Degrade, "audio health warning");
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
        }, token);
    }

    private void StopHealthLoop()
    {
        _lifecycleCancellation?.Cancel();
    }

    private async Task CancelCaptureServicesUnderGateAsync()
    {
        try
        {
            if (_micCapture is not null)
            {
                await _micCapture.CancelAsync().ConfigureAwait(false);
            }
        }
        catch
        {
        }
        try
        {
            if (_systemCapture is not null)
            {
                await _systemCapture.CancelAsync().ConfigureAwait(false);
            }
        }
        catch
        {
        }
        await DisposeCaptureServicesUnderGateAsync().ConfigureAwait(false);
    }

    private async Task CancelLiveSessionUnderGateAsync()
    {
        var session = Interlocked.Exchange(ref _liveSession, null);
        if (session is null) return;
        session.SnapshotChanged -= OnLiveTranscriptSnapshot;
        session.Failed -= OnLiveTranscriptionFailed;
        session.Cancel();
        await session.DisposeAsync().ConfigureAwait(false);
    }

    private Task DisposeCaptureServicesUnderGateAsync()
    {
        UnsubscribeCaptureEvents();
        _micCapture?.Dispose();
        _micCapture = null;
        _systemCapture?.Dispose();
        _systemCapture = null;
        return Task.CompletedTask;
    }

    private void AddHealthWarning(string? warning)
    {
        if (string.IsNullOrWhiteSpace(warning))
        {
            return;
        }
        lock (_warningGate)
        {
            var normalized = warning.Replace('\r', ' ').Replace('\n', ' ').Trim();
            if (!_healthWarnings.Contains(normalized, StringComparer.Ordinal))
            {
                _healthWarnings.Add(normalized);
            }
        }
    }

    private List<string> CurrentHealthWarnings()
    {
        lock (_warningGate)
        {
            return _healthWarnings.ToList();
        }
    }

    private void SaveJournalState(MeetingSessionState state, string? failureCategory = null)
    {
        if (_journal is null)
        {
            return;
        }
        _journal = _journal with
        {
            State = state,
            Warnings = CurrentHealthWarnings(),
            LastFailureCategory = failureCategory,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        };
        _journalStore.Save(_journal);
    }

    private void PreserveRecoverableUnderGate(string failureCategory)
    {
        if (_stateMachine.CanApply(MeetingSessionTrigger.Interrupt))
        {
            Transition(MeetingSessionTrigger.Interrupt, "recoverable finalization interruption");
        }
        AddHealthWarning("Meeting finalization was interrupted; local audio is retained for retry.");
        SaveJournalState(MeetingSessionState.RecoverableInterruption, failureCategory);
        LogSessionDiagnostic("recoverable-interruption");
    }

    private void UpdateRepairAttempts(MeetingAudioChannel channel, int attempts)
    {
        if (_journal is null)
        {
            return;
        }
        _journal = channel == MeetingAudioChannel.Microphone
            ? _journal with { MicrophoneRepairAttempts = attempts }
            : _journal with { SystemRepairAttempts = attempts };
    }

    private void MarkChannelUnavailable(MeetingAudioChannel channel)
    {
        if (channel == MeetingAudioChannel.Microphone)
        {
            _micAvailable = false;
        }
        else
        {
            _systemAvailable = false;
        }
    }

    private void TryRestoreHealthyState(string reason)
    {
        if (_micAvailable && _systemAvailable && State == MeetingSessionState.DegradedRecording)
        {
            TryTransition(MeetingSessionTrigger.Recover, reason);
        }
    }

    private bool HasJournalAudio() =>
        _journal is not null &&
        (_journal.MicrophoneParts.Count > 0 || _journal.SystemParts.Count > 0);

    private bool StateAllowsCancellation() => State is
        MeetingSessionState.Preparing or
        MeetingSessionState.Recording or
        MeetingSessionState.DegradedRecording or
        MeetingSessionState.Stopping or
        MeetingSessionState.Finalizing or
        MeetingSessionState.RecoverableInterruption;

    private void ResetTerminalStateIfNeeded()
    {
        if (State is MeetingSessionState.Completed or MeetingSessionState.Failed or MeetingSessionState.Cancelled)
        {
            Transition(MeetingSessionTrigger.Reset, "new meeting lifecycle");
        }
    }

    private void Transition(MeetingSessionTrigger trigger, string reason)
    {
        var transition = _stateMachine.Apply(trigger, reason);
        _logService?.Info(
            $"Meeting session transition. from={transition.From}; to={transition.To}; trigger={transition.Trigger}; reasonCategory={DiagnosticCategory(reason)}");
        StateChanged?.Invoke(this, new MeetingSessionStateChangedEventArgs(transition));
    }

    private bool TryTransition(MeetingSessionTrigger trigger, string reason)
    {
        if (!_stateMachine.TryApply(trigger, out var transition, reason) || transition is null)
        {
            return false;
        }
        _logService?.Info(
            $"Meeting session transition. from={transition.From}; to={transition.To}; trigger={transition.Trigger}; reasonCategory={DiagnosticCategory(reason)}");
        StateChanged?.Invoke(this, new MeetingSessionStateChangedEventArgs(transition));
        return true;
    }

    private void LogTranscriptionDiagnostic(string context, TranscriptionResult result)
    {
        var diagnostic = string.IsNullOrWhiteSpace(result.Diagnostic)
            ? "none"
            : DiagnosticCategory(result.Diagnostic);
        _logService?.Info(
            $"Meeting transcription completed. context={context}; engine={_transcriptionClient.EngineId}; model={_transcriptionClient.ModelId}; durationMs={result.DurationMs}; segments={result.Segments?.Count ?? 0}; diagnosticCategory={diagnostic}");
    }

    private void LogSessionDiagnostic(string outcome)
    {
        _logService?.Info(
            $"Meeting session diagnostic. outcome={outcome}; state={State}; model={_transcriptionClient.ModelId}; captureMode={_journal?.SystemCaptureMode ?? "none"}; micParts={_journal?.MicrophoneParts.Count ?? 0}; systemParts={_journal?.SystemParts.Count ?? 0}; micRepairAttempts={_journal?.MicrophoneRepairAttempts ?? 0}; systemRepairAttempts={_journal?.SystemRepairAttempts ?? 0}; warnings={CurrentHealthWarnings().Count}; targeted={_targetProcessId is > 0}; recovered={_journal?.RecoveredFromInterruption ?? false}");
    }

    private static string DiagnosticCategory(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "none";
        }
        var first = value.Split(new[] { '\r', '\n', ':', ';' }, StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault()?.Trim() ?? "unknown";
        var safe = new string(first.Where(character => char.IsLetterOrDigit(character) || character is '-' or '_' or ' ').ToArray());
        return string.IsNullOrWhiteSpace(safe) ? "unknown" : safe[..Math.Min(safe.Length, 64)].Replace(' ', '-');
    }

    private static int MeasureRecordedDurationMs(MeetingAudioPaths paths)
    {
        var durations = new List<double>();
        foreach (var path in new[] { paths.MicrophonePath, paths.SystemPath }.OfType<string>())
        {
            try
            {
                using var reader = new AudioFileReader(path);
                durations.Add(reader.TotalTime.TotalMilliseconds);
            }
            catch
            {
            }
        }
        return durations.Count == 0 ? 0 : (int)Math.Round(durations.Max());
    }

    private static string ChannelLabel(MeetingAudioChannel channel) =>
        channel == MeetingAudioChannel.Microphone ? "Microphone" : "System audio";

    private void ThrowIfDisposed() =>
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

    public static List<string> CleanupHealthWarnings(
        IEnumerable<string>? warnings,
        string? transcript = null,
        bool diarizationSucceededWithSegments = false)
    {
        if (warnings is null)
        {
            return [];
        }
        var warningList = warnings
            .Where(warning => !string.IsNullOrWhiteSpace(warning))
            .Select(warning => warning.Trim())
            .ToList();
        var diarizationSucceeded = diarizationSucceededWithSegments ||
                                   warningList.Any(HasSuccessfulDiarizationSegmentCount) ||
                                   TranscriptHasDiarizedSpeakerLabels(transcript);
        var cleaned = new List<string>();
        foreach (var warning in warningList)
        {
            var normalized = NormalizeHealthWarning(warning, diarizationSucceeded);
            if (!string.IsNullOrWhiteSpace(normalized) &&
                !cleaned.Contains(normalized, StringComparer.Ordinal))
            {
                cleaned.Add(normalized);
            }
        }
        return cleaned;
    }

    internal static async Task AwaitDiarizationBeforeCleanupAsync(
        Task? diarizationTask,
        bool alreadyObserved,
        Action<Exception>? reportFailure = null)
    {
        if (diarizationTask is null || alreadyObserved)
        {
            return;
        }
        try
        {
            await diarizationTask.ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            reportFailure?.Invoke(exception);
        }
    }

    private static string? NormalizeHealthWarning(string warning, bool diarizationSucceeded)
    {
        var text = warning.Trim();
        if (text.Equals(SystemTranscriptEmptyWarning, StringComparison.OrdinalIgnoreCase) ||
            text.Contains("System transcript was empty", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }
        if (text.Equals(DiarizationNoSegmentsWarning, StringComparison.OrdinalIgnoreCase) ||
            text.Contains("returned no speaker segments", StringComparison.OrdinalIgnoreCase))
        {
            return diarizationSucceeded ? null : DiarizationNoSegmentsWarning;
        }
        if (text.Equals(DiarizationFailedWarning, StringComparison.OrdinalIgnoreCase) ||
            text.Contains("Speaker diarization failed", StringComparison.OrdinalIgnoreCase) ||
            text.Equals("Diarization failed.", StringComparison.OrdinalIgnoreCase))
        {
            return diarizationSucceeded ? null : DiarizationFailedWarning;
        }
        if (text.StartsWith("ASR engine:", StringComparison.OrdinalIgnoreCase) ||
            text.StartsWith("Diarization segments:", StringComparison.OrdinalIgnoreCase) ||
            text.StartsWith("Timing diarization ", StringComparison.OrdinalIgnoreCase) ||
            text.StartsWith("Worker stderr:", StringComparison.OrdinalIgnoreCase) ||
            text.StartsWith("Input path:", StringComparison.OrdinalIgnoreCase) ||
            text.StartsWith("Input bytes:", StringComparison.OrdinalIgnoreCase) ||
            text.StartsWith("Model cache:", StringComparison.OrdinalIgnoreCase) ||
            text.StartsWith("Backend:", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("torchvision", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("site-packages", StringComparison.OrdinalIgnoreCase) ||
            text.Contains(".py:", StringComparison.OrdinalIgnoreCase) ||
            text.Contains('\\') ||
            text.Contains('/'))
        {
            return null;
        }
        return text.StartsWith("Microphone", StringComparison.OrdinalIgnoreCase) ||
               text.StartsWith("System audio", StringComparison.OrdinalIgnoreCase) ||
               text.StartsWith("Windows", StringComparison.OrdinalIgnoreCase) ||
               text.StartsWith("Muesli", StringComparison.OrdinalIgnoreCase) ||
               text.StartsWith("Recording", StringComparison.OrdinalIgnoreCase) ||
               text.StartsWith("Meeting", StringComparison.OrdinalIgnoreCase) ||
               text.StartsWith("Speaker", StringComparison.OrdinalIgnoreCase)
            ? text
            : null;
    }

    private static bool HasSuccessfulDiarizationSegmentCount(string warning)
    {
        if (!warning.StartsWith("Diarization segments:", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        var parts = warning.Split(':', 2);
        return parts.Length == 2 && int.TryParse(parts[1].Trim(), out var count) && count > 0;
    }

    private static bool TranscriptHasDiarizedSpeakerLabels(string? transcript) =>
        !string.IsNullOrWhiteSpace(transcript) &&
        System.Text.RegularExpressions.Regex.IsMatch(
            transcript,
            @"\[\d{2}:\d{2}:\d{2}\]\s+[^:\r\n]+:\s");

    private sealed record CapturePairStartResult(
        SystemAudioCaptureMode? SystemCaptureMode,
        string? Warning,
        Exception? MicrophoneFailure,
        Exception? SystemFailure);
}

public sealed record MeetingStartResult(
    string MeetingId,
    MeetingSessionState State,
    SystemAudioCaptureMode? SystemCaptureMode,
    string? Warning,
    bool MicrophoneStarted,
    bool SystemAudioStarted);

public sealed record RecordedMeetingResult(
    string MeetingId,
    string Title,
    DateTime StartedAt,
    int DurationMs,
    string Transcript,
    string? MicAudioPath,
    string? SystemAudioPath,
    List<string>? HealthWarnings = null,
    MeetingSessionState SessionState = MeetingSessionState.Completed,
    string SystemCaptureMode = "unknown",
    bool RecoveredFromInterruption = false,
    string? LivePreviewModelId = null,
    string LiveTranscriptOwnership = "off",
    string FinalTranscriptOwnerModelId = "",
    string? GapRecoveryModelId = null);

public sealed class MeetingSessionStateChangedEventArgs(MeetingSessionTransition transition) : EventArgs
{
    public MeetingSessionTransition Transition { get; } = transition;
}

public sealed class MeetingAudioHealthChangedEventArgs(MeetingAudioHealthSnapshot snapshot) : EventArgs
{
    public MeetingAudioHealthSnapshot Snapshot { get; } = snapshot;
}

public sealed class MeetingSessionRecoverableException(
    string message,
    string sessionId,
    Exception innerException) : Exception(message, innerException)
{
    public string SessionId { get; } = sessionId;
}
