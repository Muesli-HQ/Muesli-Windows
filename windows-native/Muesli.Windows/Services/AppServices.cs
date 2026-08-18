using System.Net.Http;
using System.Windows;

namespace Muesli.Windows.Services;

/// <summary>
/// Narrow composition boundary for services owned by the application shell.
/// Feature code receives the contracts it needs without constructing shell services itself.
/// </summary>
public sealed class AppServices : IDisposable
{
    private FeatureServiceScope? _featureScope;
    private bool _disposed;

    public AppServices(INavigationService navigation, IDialogService dialogs)
    {
        Navigation = navigation ?? throw new ArgumentNullException(nameof(navigation));
        Dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
    }

    public INavigationService Navigation { get; }
    public IDialogService Dialogs { get; }

    /// <summary>
    /// Creates the production feature dependency scope exactly once. The shell owns the
    /// resulting scope through this composition root; feature code only consumes its contracts.
    /// </summary>
    public FeatureServiceScope CreateFeatureScope(FeatureServiceCallbacks callbacks)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_featureScope is not null)
        {
            throw new InvalidOperationException("The application feature service scope has already been created.");
        }

        _featureScope = FeatureServiceScope.CreateProduction(callbacks);
        return _featureScope;
    }

    public FeatureServiceScope? FeatureScope => _featureScope;

    public static AppServices ForWindow(Window owner) =>
        new(new NavigationService(), new WindowDialogService(owner));

    public static AppServices ForPreview() =>
        new(new NavigationService(), NullDialogService.Instance);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _featureScope?.Dispose();
        _featureScope = null;
    }
}

/// <summary>
/// Callbacks that let lifecycle services remain feature-aware without taking a dependency on
/// MainWindow. They are supplied by the feature runtime and invoked only by the corresponding
/// lifecycle service.
/// </summary>
public sealed record FeatureServiceCallbacks(
    Func<IReadOnlySet<string>> SelectedTranscriptionModelIds,
    Func<string, CancellationToken, Task> ReleaseTranscriptionModelAsync,
    Func<string?> SelectedStreamingModelId,
    Func<CancellationToken, Task> ReleaseStreamingModelAsync);

/// <summary>
/// Production-only dependencies used by feature presenters/controllers. Construction and
/// disposal live here so individual feature files never create global services or stores.
/// </summary>
public sealed class FeatureServiceScope : IDisposable
{
    private bool _disposed;

    private FeatureServiceScope(
        AppLogService logService,
        SettingsStore settingsStore,
        AppDataStore dataStore,
        GlobalHotkeyService globalHotkeyService,
        ToastNotificationService toastNotificationService,
        ActiveAppPasteService activeAppPasteService,
        NativeTranscriptionClient dictationTranscriptionClient,
        DictationCoordinator dictationCoordinator,
        NativeTranscriptionClient meetingTranscriptionClient,
        MeetingRecordingCoordinator meetingRecordingCoordinator,
        MeetingRecordingPlaybackService meetingPlaybackService,
        TranscriptionModelLifecycleService modelLifecycle,
        StreamingModelLifecycleService streamingModelLifecycle,
        MeetingDetectionService meetingDetectionService,
        MeetingPromptService meetingPromptService,
        TrayIconService trayIconService,
        OnboardingProgressStore onboardingProgressStore,
        WindowsMicrophoneAccessService microphoneAccessService,
        RuntimeDiagnosticsService runtimeDiagnosticsService,
        PostMeetingAutomationService postMeetingAutomationService,
        CaptureStorageService captureStorageService,
        TranscriptionBenchmarkService transcriptionBenchmarkService,
        NativeTextCleanupService textCleanupService,
        TranscriptionPipelineService transcriptionPipelineService,
        HttpClient computerUseHttpClient,
        ComputerUseTraceStore computerUseTraceStore)
    {
        LogService = logService;
        SettingsStore = settingsStore;
        DataStore = dataStore;
        GlobalHotkeyService = globalHotkeyService;
        ToastNotificationService = toastNotificationService;
        ActiveAppPasteService = activeAppPasteService;
        DictationTranscriptionClient = dictationTranscriptionClient;
        DictationCoordinator = dictationCoordinator;
        MeetingTranscriptionClient = meetingTranscriptionClient;
        MeetingRecordingCoordinator = meetingRecordingCoordinator;
        MeetingPlaybackService = meetingPlaybackService;
        ModelLifecycle = modelLifecycle;
        StreamingModelLifecycle = streamingModelLifecycle;
        MeetingDetectionService = meetingDetectionService;
        MeetingPromptService = meetingPromptService;
        TrayIconService = trayIconService;
        OnboardingProgressStore = onboardingProgressStore;
        MicrophoneAccessService = microphoneAccessService;
        RuntimeDiagnosticsService = runtimeDiagnosticsService;
        PostMeetingAutomationService = postMeetingAutomationService;
        CaptureStorageService = captureStorageService;
        TranscriptionBenchmarkService = transcriptionBenchmarkService;
        TextCleanupService = textCleanupService;
        TranscriptionPipelineService = transcriptionPipelineService;
        ComputerUseHttpClient = computerUseHttpClient;
        ComputerUseTraceStore = computerUseTraceStore;
    }

    public AppLogService LogService { get; }
    public SettingsStore SettingsStore { get; }
    public AppDataStore DataStore { get; }
    public GlobalHotkeyService GlobalHotkeyService { get; }
    public ToastNotificationService ToastNotificationService { get; }
    public ActiveAppPasteService ActiveAppPasteService { get; }
    public NativeTranscriptionClient DictationTranscriptionClient { get; }
    public DictationCoordinator DictationCoordinator { get; }
    public NativeTranscriptionClient MeetingTranscriptionClient { get; }
    public MeetingRecordingCoordinator MeetingRecordingCoordinator { get; }
    public MeetingRecordingPlaybackService MeetingPlaybackService { get; }
    public TranscriptionModelLifecycleService ModelLifecycle { get; }
    public StreamingModelLifecycleService StreamingModelLifecycle { get; }
    public MeetingDetectionService MeetingDetectionService { get; }
    public MeetingPromptService MeetingPromptService { get; }
    public TrayIconService TrayIconService { get; }
    public OnboardingProgressStore OnboardingProgressStore { get; }
    public WindowsMicrophoneAccessService MicrophoneAccessService { get; }
    public RuntimeDiagnosticsService RuntimeDiagnosticsService { get; }
    public PostMeetingAutomationService PostMeetingAutomationService { get; }
    public CaptureStorageService CaptureStorageService { get; }
    public TranscriptionBenchmarkService TranscriptionBenchmarkService { get; }
    public NativeTextCleanupService TextCleanupService { get; }
    public TranscriptionPipelineService TranscriptionPipelineService { get; }
    public HttpClient ComputerUseHttpClient { get; }
    public ComputerUseTraceStore ComputerUseTraceStore { get; }

    internal static FeatureServiceScope CreateProduction(FeatureServiceCallbacks callbacks)
    {
        ArgumentNullException.ThrowIfNull(callbacks);

        var logService = new AppLogService();
        var settingsStore = new SettingsStore();
        var dataStore = new AppDataStore();
        var globalHotkeyService = new GlobalHotkeyService();
        var toastNotificationService = new ToastNotificationService();
        var activeAppPasteService = new ActiveAppPasteService();
        var dictationTranscriptionClient = new NativeTranscriptionClient();
        var dictationCoordinator = new DictationCoordinator(dictationTranscriptionClient);
        var meetingTranscriptionClient = new NativeTranscriptionClient();
        var meetingRecordingCoordinator = new MeetingRecordingCoordinator(meetingTranscriptionClient, logService);
        var meetingPlaybackService = new MeetingRecordingPlaybackService();
        var modelLifecycle = new TranscriptionModelLifecycleService(
            callbacks.SelectedTranscriptionModelIds,
            callbacks.ReleaseTranscriptionModelAsync);
        var streamingModelLifecycle = new StreamingModelLifecycleService(
            callbacks.SelectedStreamingModelId,
            callbacks.ReleaseStreamingModelAsync);
        var meetingDetectionService = new MeetingDetectionService();
        var meetingPromptService = new MeetingPromptService();
        var trayIconService = new TrayIconService();
        var onboardingProgressStore = new OnboardingProgressStore();
        var microphoneAccessService = new WindowsMicrophoneAccessService();
        var runtimeDiagnosticsService = new RuntimeDiagnosticsService();
        var postMeetingAutomationService = new PostMeetingAutomationService();
        var captureStorageService = new CaptureStorageService();
        var transcriptionBenchmarkService = new TranscriptionBenchmarkService(logService);
        var textCleanupService = new NativeTextCleanupService(logService);
        var transcriptionPipelineService = new TranscriptionPipelineService(textCleanupService, logService);
        var computerUseHttpClient = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        var computerUseTraceStore = new ComputerUseTraceStore();

        return new FeatureServiceScope(
            logService,
            settingsStore,
            dataStore,
            globalHotkeyService,
            toastNotificationService,
            activeAppPasteService,
            dictationTranscriptionClient,
            dictationCoordinator,
            meetingTranscriptionClient,
            meetingRecordingCoordinator,
            meetingPlaybackService,
            modelLifecycle,
            streamingModelLifecycle,
            meetingDetectionService,
            meetingPromptService,
            trayIconService,
            onboardingProgressStore,
            microphoneAccessService,
            runtimeDiagnosticsService,
            postMeetingAutomationService,
            captureStorageService,
            transcriptionBenchmarkService,
            textCleanupService,
            transcriptionPipelineService,
            computerUseHttpClient,
            computerUseTraceStore);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        ComputerUseHttpClient.Dispose();
        GlobalHotkeyService.Dispose();
        MeetingDetectionService.Dispose();
        ModelLifecycle.Dispose();
        StreamingModelLifecycle.Dispose();
        DictationCoordinator.Dispose();
        MeetingRecordingCoordinator.Dispose();
        MeetingTranscriptionClient.Dispose();
        MeetingPlaybackService.Dispose();
        TextCleanupService.Dispose();
        TrayIconService.Dispose();
        ToastNotificationService.Dispose();
    }
}

public interface IDialogService
{
    void ShowInfo(string message, string title);
    void ShowWarning(string message, string title);
    MessageBoxResult Confirm(string message, string title);
}

public sealed class WindowDialogService : IDialogService
{
    private readonly Window _owner;

    public WindowDialogService(Window owner) => _owner = owner;

    public void ShowInfo(string message, string title) =>
        System.Windows.MessageBox.Show(_owner, message, title, MessageBoxButton.OK, MessageBoxImage.Information);

    public void ShowWarning(string message, string title) =>
        System.Windows.MessageBox.Show(_owner, message, title, MessageBoxButton.OK, MessageBoxImage.Warning);

    public MessageBoxResult Confirm(string message, string title) =>
        System.Windows.MessageBox.Show(_owner, message, title, MessageBoxButton.YesNoCancel, MessageBoxImage.Warning);
}

public sealed class NullDialogService : IDialogService
{
    public static NullDialogService Instance { get; } = new();

    private NullDialogService()
    {
    }

    public void ShowInfo(string message, string title)
    {
    }

    public void ShowWarning(string message, string title)
    {
    }

    public MessageBoxResult Confirm(string message, string title) => MessageBoxResult.Cancel;
}

/// <summary>
/// Immutable value boundary used by feature tests and future settings presenters.
/// The persisted settings record remains the source of truth and its schema is unchanged.
/// </summary>
public sealed record SettingsSnapshot(MuesliSettings Value)
{
    public string Theme => Value.Theme;
    public string DictationModelId => Value.DictationModelId;
    public string FinalMeetingModelId => Value.FinalMeetingModelId;
    public string? LiveMeetingModelId => Value.LiveMeetingModelId;
    public bool IsLightTheme => Value.Theme.Equals("light", StringComparison.OrdinalIgnoreCase);

    public static SettingsSnapshot Capture(MuesliSettings settings) =>
        new((settings ?? throw new ArgumentNullException(nameof(settings))) with { });
}

public enum WorkflowEntryPoint
{
    HoldToDictate,
    GlobalHotkey,
    ManualMeeting,
    DetectedMeeting,
    MediaImport,
    ComputerUseVoice
}

public static class WorkflowEntryPoints
{
    public static IReadOnlyList<WorkflowEntryPoint> All { get; } =
    [
        WorkflowEntryPoint.HoldToDictate,
        WorkflowEntryPoint.GlobalHotkey,
        WorkflowEntryPoint.ManualMeeting,
        WorkflowEntryPoint.DetectedMeeting,
        WorkflowEntryPoint.MediaImport,
        WorkflowEntryPoint.ComputerUseVoice
    ];

    public static bool CanStartDictation(bool computerUseVoiceCaptureActive, bool computerUseRunning) =>
        !computerUseVoiceCaptureActive && !computerUseRunning;

    public static bool CanToggleMeeting(bool coordinatorBusy) => !coordinatorBusy;

    public static bool CanStartComputerUse(
        bool enabled,
        bool configurationReady,
        bool computerUseRunning,
        bool dictationRecording,
        bool dictationBusy,
        bool meetingRecording) =>
        enabled && configurationReady &&
        !computerUseRunning && !dictationRecording && !dictationBusy && !meetingRecording;
}
