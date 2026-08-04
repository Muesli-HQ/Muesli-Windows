using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using Muesli.Windows.Services;
using Velopack;
using Velopack.Sources;
using WpfButton = System.Windows.Controls.Button;
using WpfListBox = System.Windows.Controls.ListBox;
using WpfOrientation = System.Windows.Controls.Orientation;
using WpfTextBox = System.Windows.Controls.TextBox;

namespace Muesli.Windows;

public partial class MainWindow : Window, INotifyPropertyChanged
{
    private readonly NativeTranscriptionClient _dictationTranscriptionClient;
    private readonly DictationCoordinator _dictationCoordinator;
    private readonly GlobalHotkeyService _globalHotkeyService;
    private readonly SettingsStore _settingsStore;
    private readonly AppDataStore _dataStore;
    private readonly ToastNotificationService _toastNotificationService;
    private readonly ActiveAppPasteService _activeAppPasteService;
    private readonly NativeTranscriptionClient _meetingTranscriptionClient;
    private readonly MeetingRecordingCoordinator _meetingRecordingCoordinator;
    private readonly MeetingRecordingPlaybackService _meetingPlaybackService;
    private readonly TranscriptionModelLifecycleService _modelLifecycle;
    private readonly StreamingModelLifecycleService _streamingModelLifecycle;
    private readonly MeetingDetectionService _meetingDetectionService;
    private readonly MeetingPromptService _meetingPromptService;
    private readonly TrayIconService _trayIconService;
    private readonly OnboardingProgressStore _onboardingProgressStore;
    private readonly WindowsMicrophoneAccessService _microphoneAccessService;
    private readonly RuntimeDiagnosticsService _runtimeDiagnosticsService;
    private readonly PostMeetingAutomationService _postMeetingAutomationService;
    private readonly AppLogService _logService;
    private readonly CaptureStorageService _captureStorageService;
    private readonly TranscriptionBenchmarkService _transcriptionBenchmarkService;
    private readonly NativeTextCleanupService _textCleanupService;
    private readonly TranscriptionPipelineService _transcriptionPipelineService;
    private readonly DictationHotkeyStateMachine _dictationHotkeyState = new();
    private readonly Dictionary<string, DateTime> _ignoredMeetingPrompts = new(StringComparer.OrdinalIgnoreCase);
    private readonly System.Windows.Threading.DispatcherTimer _meetingAutoStopTimer = new()
    {
        Interval = TimeSpan.FromSeconds(4)
    };
    private readonly System.Windows.Threading.DispatcherTimer _aliasSaveDebounceTimer = new()
    {
        Interval = TimeSpan.FromMilliseconds(400)
    };
    private readonly System.Windows.Threading.DispatcherTimer _hotkeyReleaseTimer = new()
    {
        Interval = TimeSpan.FromMilliseconds(320)
    };
    private readonly System.Windows.Threading.DispatcherTimer _meetingPlaybackTimer = new()
    {
        Interval = TimeSpan.FromMilliseconds(250)
    };
    private readonly SemaphoreSlim _summaryGate = new(1, 1);

    private string _dictationStatus = "Ready";
    private string? _selectedMicrophone;
    private string _selectedHotkey = "F8";
    private string _selectedPasteBehavior = "active-app";
    private TranscriptionModelDefinition _selectedTranscriptionModel = TranscriptionModelCatalog.Models[0];
    private TranscriptionModelDefinition _selectedFinalMeetingModel = TranscriptionModelCatalog.Models[0];
    private LiveModelChoice _selectedLiveMeetingModel = LiveModelChoice.Off;
    private string _selectedLiveTranscriptOwnership = "Preview-only";
    private bool _showLiveWaveformOnHover;
    private MeetingLiveTranscriptWindow? _liveTranscriptWindow;
    private string _selectedSummaryProvider = "local";
    private bool _isSummarizing;
    private bool _summaryRetryAvailable;
    private CancellationTokenSource? _summaryCancellation;
    private string _ollamaEndpoint = "http://localhost:11434";
    private string _ollamaModel = "llama3.1:8b";
    private string _selectedSummaryTemplate = "standard";
    private string _userName = "";
    private string _openAIApiKey = "";
    private string _openAIModel = "gpt-5.4-mini";
    private string _openRouterApiKey = "";
    private string _openRouterModel = "stepfun/step-3.5-flash:free";
    private bool _updatingSecretBoxes;
    private string _theme = "dark";
    private bool _enableDoubleTapDictation;
    private bool _removeFillerWords = true;
    private bool _enableLocalCleanup;
    private bool _startAtLogin;
    private bool _openDashboardOnLaunch = true;
    private bool _saveMeetingRecordings = true;
    private bool _postMeetingHookEnabled;
    private string _postMeetingHookExecutablePath = "";
    private string _selectedHookTranscriptPolicy = "Metadata only";
    private int _postMeetingHookTimeoutSeconds = 30;
    private int _postMeetingHookMaxAttempts = 2;
    private bool _autoExportMarkdownEnabled;
    private string _autoExportMarkdownDirectory = "";
    private string _selectedAutoExportContent = "Notes";
    private string _postMeetingAutomationStatus = "Automation is disabled.";
    private bool _computerUseEnabled;
    private string _selectedComputerUsePlannerProvider = "None";
    private string _computerUsePlannerModel = "";
    private int _computerUsePlannerTimeoutSeconds = 30;
    private int _computerUsePerActionTimeoutSeconds = 10;
    private int _computerUseMaximumActionCount = 5;
    private string _computerUseAllowedApplications = "";
    private string _computerUseAllowedBrowserDomains = "";
    private bool _computerUseIncludeWindowText;
    private bool _computerUseIncludeScreenshots;
    private bool _computerUseIncludeBrowserPageText;
    private string _selectedComputerUseBrowserInterface = "Disabled";
    private string _computerUseBrowserEndpoint = "http://127.0.0.1:9222";
    private string _computerUseStatus = "Computer Use is disabled.";
    private bool _computerUseVoiceCaptureActive;
    private bool _computerUseIsRunning;
    private ComputerUseActivationToken? _computerUseActivationToken;
    private ComputerUsePlannerService? _computerUsePlannerService;
    private ComputerUseWindowTarget? _computerUseApprovedTarget;
    private readonly HttpClient _computerUseHttpClient;
    private readonly ComputerUseTraceStore _computerUseTraceStore;
    private CancellationTokenSource? _computerUseCancellation;
    private readonly CancellationTokenSource _applicationShutdownCancellation = new();
    private bool _showFloatingIndicator = true;
    private string _selectedIndicatorPosition = "Top Center";
    private bool _autoMeetingDetectionEnabled = true;
    private bool _onboardingCompleted;
    private int _lastCompletedFeatureTourVersion;
    private OnboardingWindow? _onboardingWindow;
    private MeetingDetectionScan? _lastMeetingDetectionScan;
    private IntPtr _pasteTargetWindow = IntPtr.Zero;
    private PasteTargetInfo _pasteTargetInfo = PasteTargetInfo.Unknown;
    private bool _shouldPasteToActiveApp;
    private bool _meetingsExpanded = true;
    private bool _meetingSortNewestFirst = true;
    private bool _isCapturingHotkey;
    private CancellationTokenSource? _dictationOperationCancellation;
    private bool _runtimeStarted;
    private readonly bool _isVisualPreview;
    private string _previewPageStateMessage = "";
    private bool _isParkedForBackground;
    private bool _isWorkAreaMaximized;
    private bool _isCompactLayout;
    private Rect _restoreBounds;
    private string _dictationDateFilter = "all";
    private string _meetingDateFilter = "all";
    private string _searchQuery = "";
    private UIElement? _lastNonSearchPage;
    private System.Windows.Controls.Button? _lastNonSearchNav;
    private string? _selectedMeetingFolderId;
    private string _selectedMeetingTemplate = "Standard Meeting Notes";
    private MeetingItem? _selectedMeeting;
    private Dictionary<string, string> _activeSpeakerAliases = new();
    private bool _lastMeetingDetailShowTranscript = false;
    private bool _isMeetingRecording;
    private string _meetingSessionStatus = "Idle";
    private CancellationTokenSource? _meetingOperationCancellation;
    private MeetingAutoStopTracker? _meetingAutoStopTracker;
    private readonly List<RecoverableMeetingSession> _recoverableMeetingSessions = [];
    private MeetingPlaybackTrack? _selectedMeetingPlaybackTrack;
    private double _meetingPlaybackPosition;
    private double _meetingPlaybackDuration;
    private bool _updatingMeetingPlaybackPosition;
    private bool _lastSummaryUsedLocalFallback;
    private string? _currentMeetingTitle;
    private CancellationTokenSource? _importCancellation;
    private bool _isImportingMeeting;
    private double _importProgressPercent;
    private string _importProgressLabel = "";
    private bool _importProgressIsIndeterminate = true;
    private string _runtimeDiagnostics = "Not checked yet.";
    private string _modelCacheDirectory = "";
    private string _modelCacheSize = "0 B";
    private string _benchmarkSummary = "Benchmark not run yet.";
    private string _setupReadiness = "Setup not checked yet.";
    private string _nativeRuntimeStatus = "Not checked";
    private string _speakerDiarizationStatusLabel = "Not checked";
    private string _dictationModelRuntimeStatus = "Needs model";
    private string _qwenCleanupRuntimeStatus = "Disabled";
    private string _gpuRuntimeStatus = "Not checked";
    private string _diarizationDependencyStatus = "Not checked yet.";
    private string _diarizationTokenStatus = "Not checked yet.";
    private string _meetingDetectionStatus = "Meeting detection has not scanned yet.";
    private string _runtimeSetupStatus = "";
    private double? _indicatorLeft;
    private double? _indicatorTop;
    private bool _crashReportingEnabled;
    private bool _crashReportingPromptShown;
    private bool _crashReportingStartupValue;
    private UpdateManager? _updateManager;
    private UpdateInfo? _pendingUpdate;
    private bool _isUpdateReady;
    private bool _updateCheckInFlight;

    public event PropertyChangedEventHandler? PropertyChanged;

    public bool IsCompactLayout
    {
        get => _isCompactLayout;
        private set
        {
            if (!SetField(ref _isCompactLayout, value)) return;
            UpdateSidebarColumnWidth();
        }
    }

    private void UpdateSidebarColumnWidth()
    {
        if (SidebarColumn is null) return;
        SidebarColumn.Width = new GridLength(IsCompactLayout ? 72 : 260);
    }

    public ObservableCollection<DictationItem> Dictations { get; } = [];
    public ObservableCollection<MeetingItem> Meetings { get; } = [];
    public ObservableCollection<MeetingFolderItem> MeetingFolders { get; } = [];
    public ObservableCollection<MeetingTemplateItem> CustomMeetingTemplates { get; } = [];
    public ObservableCollection<DictionaryEntryItem> DictionaryEntries { get; } = [];
    public ObservableCollection<TranscriptionModelItem> TranscriptionModelItems { get; } = [];
    public ObservableCollection<StreamingModelItem> StreamingModelItems { get; } = [];
    public ObservableCollection<MeetingPlaybackTrack> MeetingPlaybackTracks { get; } = [];
    public ObservableCollection<string> MicrophoneDevices { get; } = ["System default microphone"];
    public ObservableCollection<string> HotkeyOptions { get; } =
    [
        "F6",
        "F7",
        "F8",
        "F9",
        "F10",
        "F11",
        "F12",
        "Ctrl+Shift+Space",
        "Ctrl+Alt+Space",
        "Ctrl+Shift+D",
        "Ctrl+Alt+D"
    ];
    public ObservableCollection<string> PasteBehaviors { get; } = ["active-app", "clipboard"];
    public IReadOnlyList<TranscriptionModelDefinition> TranscriptionModels { get; } = TranscriptionModelCatalog.Models;
    public IReadOnlyList<LiveModelChoice> LiveMeetingModels { get; } =
        [LiveModelChoice.Off, .. StreamingModelCatalog.Models.Select(model => new LiveModelChoice(model.Id, model.PickerLabel))];
    public IReadOnlyList<string> LiveTranscriptOwnershipModes { get; } = LiveTranscriptOwnershipDescriptor.DisplayNames;
    public ObservableCollection<string> IndicatorPositions { get; } = ["Top Left", "Top Center", "Top Right", "Bottom Left", "Bottom Center", "Bottom Right", "Custom"];
    public ObservableCollection<string> ThemeOptions { get; } = ["Light", "Dark"];
    public ObservableCollection<string> SummaryProviders { get; } = [.. SummaryProviderDisclosure.AvailableIds];
    public ObservableCollection<string> HookTranscriptPolicies { get; } = ["Metadata only", "Inline transcript", "Auto-export path"];
    public ObservableCollection<string> AutoExportContentOptions { get; } = ["Notes", "Transcript", "Full meeting"];
    public ObservableCollection<string> ComputerUsePlannerProviders { get; } = ["None", "OpenAI"];
    public ObservableCollection<string> ComputerUseBrowserInterfaces { get; } = ["Disabled", "Loopback DevTools"];
    /// <summary>Shown next to the provider picker so the user knows before recording whether the transcript leaves the machine.</summary>
    public string SummaryProviderDisclosureText =>
        SummaryProviderDisclosure.DisclosureFor(SelectedSummaryProvider, _ollamaEndpoint);

    public string OllamaEndpoint
    {
        get => _ollamaEndpoint;
        set
        {
            if (!SetField(ref _ollamaEndpoint, string.IsNullOrWhiteSpace(value) ? "http://localhost:11434" : value.Trim())) return;
            // Pointing Ollama at a remote host changes whether the transcript leaves the machine.
            OnPropertyChanged(nameof(SummaryProviderDisclosureText));
            SaveSettings();
        }
    }

    public string OllamaModel
    {
        get => _ollamaModel;
        set
        {
            if (SetField(ref _ollamaModel, string.IsNullOrWhiteSpace(value) ? "llama3.1:8b" : value.Trim()))
            {
                SaveSettings();
            }
        }
    }
    public ObservableCollection<string> SummaryTemplates { get; } = new(MeetingSummaryService.BuiltInTemplateNames);
    public ICollectionView FilteredDictations { get; }
    public ICollectionView FilteredMeetings { get; }
    public ICollectionView SearchDictationResults { get; }
    public ICollectionView SearchMeetingResults { get; }

    public string UserGreeting
    {
        get
        {
            var name = UserName.Trim();
            return string.IsNullOrWhiteSpace(name) ? "" : $"Hi, {name}";
        }
    }

    public string UserName
    {
        get => _userName;
        set
        {
            if (SetField(ref _userName, value?.Trim() ?? ""))
            {
                OnPropertyChanged(nameof(UserGreeting));
                SaveSettings();
            }
        }
    }

    public int DayStreak => ComputeDayStreak();
    public int WordsDictated => Dictations.Sum(item => item.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length);
    public string WordsDictatedDisplay => WordsDictated >= 1000 ? $"{WordsDictated / 1000.0:0.0}k" : WordsDictated.ToString();
    public int AverageWpm
    {
        get
        {
            var totalMs = Dictations.Sum(item => Math.Max(item.DurationMs, 0));
            if (totalMs <= 0)
            {
                return 0;
            }

            var minutes = totalMs / 60000.0;
            return minutes <= 0 ? 0 : (int)Math.Round(WordsDictated / minutes);
        }
    }
    public int MeetingCount => Meetings.Count;
    public int VisibleMeetingCount => FilteredMeetings?.Cast<MeetingItem>().Count() ?? Meetings.Count;
    public string MeetingsChevron => _meetingsExpanded ? "⌄" : "›";
    public string MeetingSortLabel => _meetingSortNewestFirst ? "Newest first⌄" : "Oldest first⌄";
    public string CurrentMeetingFolderName => _selectedMeetingFolderId is null
        ? "All Meetings"
        : MeetingFolders.FirstOrDefault(folder => folder.Id == _selectedMeetingFolderId)?.Name ?? "All Meetings";
    public string DictationHeaderLabel => "DICTATIONS";
    public string DictationFilterLabel => _dictationDateFilter == "all" ? "" : FilterLabel(_dictationDateFilter);
    public string MeetingFilterLabel => _meetingDateFilter == "all" ? "" : FilterLabel(_meetingDateFilter);
    public string SearchResultsTitle => string.IsNullOrWhiteSpace(SearchQuery) ? "Search" : $"Search results for \"{SearchQuery.Trim()}\"";
    public int SearchDictationCount => SearchDictationResults?.Cast<DictationItem>().Count() ?? 0;
    public int SearchMeetingCount => SearchMeetingResults?.Cast<MeetingItem>().Count() ?? 0;
    public string SearchResultsSummary => $"{SearchDictationCount} dictations · {SearchMeetingCount} meetings";
    public bool HasDictations => FilteredDictations?.Cast<DictationItem>().Any() ?? false;
    public bool HasMeetings => FilteredMeetings?.Cast<MeetingItem>().Any() ?? false;
    public bool HasSearchResults => SearchDictationCount > 0 || SearchMeetingCount > 0;
    public bool HasDictionaryEntries => DictionaryEntries.Count > 0;
    public string SearchQuery
    {
        get => _searchQuery;
        set
        {
            if (SetField(ref _searchQuery, value ?? ""))
            {
                FilteredDictations.Refresh();
                RefreshMeetingViews();
                SearchDictationResults.Refresh();
                SearchMeetingResults.Refresh();
                OnPropertyChanged(nameof(SearchResultsTitle));
                OnPropertyChanged(nameof(SearchDictationCount));
                OnPropertyChanged(nameof(SearchMeetingCount));
                OnPropertyChanged(nameof(SearchResultsSummary));
                OnPropertyChanged(nameof(HasDictations));
                OnPropertyChanged(nameof(HasMeetings));
                OnPropertyChanged(nameof(HasSearchResults));
                UpdateSearchPageVisibility();
            }
        }
    }
    public string MeetingRecordingButtonText => _meetingRecordingCoordinator.State switch
    {
        MeetingSessionState.Preparing => "Preparing…",
        MeetingSessionState.Recording or MeetingSessionState.DegradedRecording => "Stop recording",
        MeetingSessionState.Stopping => "Stopping…",
        MeetingSessionState.Finalizing => "Finalizing…",
        _ => "Record meeting"
    };
    public string MeetingSessionStatus
    {
        get => _meetingSessionStatus;
        private set => SetField(ref _meetingSessionStatus, value);
    }
    public int RecoverableMeetingCount => _recoverableMeetingSessions.Count;
    public bool HasRecoverableMeetings => RecoverableMeetingCount > 0;
    public string RecoverInterruptedButtonText => $"Recover interrupted ({RecoverableMeetingCount})";
    public bool HasMeetingPlayback => MeetingPlaybackTracks.Count > 0;
    public MeetingPlaybackTrack? SelectedMeetingPlaybackTrack
    {
        get => _selectedMeetingPlaybackTrack;
        set
        {
            if (!SetField(ref _selectedMeetingPlaybackTrack, value) || value is null)
            {
                return;
            }
            try
            {
                _meetingPlaybackService.Load(value);
                MeetingPlaybackPosition = 0;
                MeetingPlaybackDuration = _meetingPlaybackService.Duration.TotalSeconds;
                OnPropertyChanged(nameof(MeetingPlaybackButtonText));
                OnPropertyChanged(nameof(MeetingPlaybackTimeLabel));
            }
            catch (Exception exception)
            {
                DictationStatus = $"Recording playback failed: {exception.Message}";
                _toastNotificationService.Show("Playback failed", "The recording could not be opened", ToastState.Error, 3600);
            }
        }
    }
    public string MeetingPlaybackButtonText =>
        _meetingPlaybackService.State == MeetingPlaybackState.Playing ? "Pause" : "Play";
    public double MeetingPlaybackPosition
    {
        get => _meetingPlaybackPosition;
        set => SetField(ref _meetingPlaybackPosition, value);
    }
    public double MeetingPlaybackDuration
    {
        get => _meetingPlaybackDuration;
        private set => SetField(ref _meetingPlaybackDuration, value);
    }
    public string MeetingPlaybackTimeLabel =>
        $"{FormatPlaybackTime(TimeSpan.FromSeconds(Math.Max(0, MeetingPlaybackPosition)))} / {FormatPlaybackTime(TimeSpan.FromSeconds(Math.Max(0, MeetingPlaybackDuration)))}";
    public string ActiveModelLabel => SelectedTranscriptionModel.DisplayName;
    public string SelectedModelDescription => SelectedTranscriptionModel.Summary;
    public string SelectedModelLanguages => SelectedTranscriptionModel.Languages;
    public string SelectedModelDownloadSize => SelectedTranscriptionModel.SizeLabel;
    public string SelectedModelCacheStatus => _isVisualPreview ? "Preview-only: no model cache inspected." : _modelLifecycle.Snapshot(SelectedTranscriptionModel.Id).StatusText;
    public TranscriptionModelDefinition SelectedTranscriptionModel
    {
        get => _selectedTranscriptionModel;
        set
        {
            var next = TranscriptionModelCatalog.Get(value?.Id);
            if (!SetField(ref _selectedTranscriptionModel, next))
            {
                return;
            }

            _ = SwitchDictationModelAsync(next.Id);
            OnPropertyChanged(nameof(ActiveModelLabel));
            OnPropertyChanged(nameof(SelectedModelDescription));
            OnPropertyChanged(nameof(SelectedModelLanguages));
            OnPropertyChanged(nameof(SelectedModelDownloadSize));
            OnPropertyChanged(nameof(SelectedModelCacheStatus));
            DictationModelRuntimeStatus = _modelLifecycle.Snapshot(next.Id).StatusText;
            DictationStatus = TranscriptionModelReadiness.IsVerified(next)
                ? $"{next.DisplayName} selected"
                : $"{next.DisplayName} selected — prepare the model before transcription";
            SaveSettings();
            _ = RefreshRuntimeDiagnosticsAsync();
        }
    }
    public TranscriptionModelDefinition SelectedFinalMeetingModel
    {
        get => _selectedFinalMeetingModel;
        set
        {
            var next = TranscriptionModelCatalog.Get(value?.Id);
            if (!SetField(ref _selectedFinalMeetingModel, next))
            {
                return;
            }

            _ = SwitchFinalMeetingModelAsync(next.Id);
            OnPropertyChanged(nameof(FinalMeetingModelStatus));
            OnPropertyChanged(nameof(FinalTranscriptOwnerLabel));
            OnPropertyChanged(nameof(GapRecoveryOwnerLabel));
            SaveSettings();
            RefreshModelItems();
        }
    }
    public string FinalMeetingModelStatus => _isVisualPreview ? "Preview-only: no model cache inspected." : _modelLifecycle.Snapshot(SelectedFinalMeetingModel.Id).StatusText;
    public LiveModelChoice SelectedLiveMeetingModel
    {
        get => _selectedLiveMeetingModel;
        set
        {
            var next = LiveMeetingModels.FirstOrDefault(choice => choice.Id == value?.Id) ?? LiveModelChoice.Off;
            if (!SetField(ref _selectedLiveMeetingModel, next)) return;
            OnPropertyChanged(nameof(LiveMeetingModelStatus));
            OnPropertyChanged(nameof(LivePreviewOwnerLabel));
            OnPropertyChanged(nameof(FinalTranscriptOwnerLabel));
            OnPropertyChanged(nameof(GapRecoveryOwnerLabel));
            SaveSettings();
            RefreshStreamingModelItems();
        }
    }
    public string SelectedLiveTranscriptOwnership
    {
        get => _selectedLiveTranscriptOwnership;
        set
        {
            var next = LiveTranscriptOwnershipModes.Contains(value) ? value : LiveTranscriptOwnershipDescriptor.PreviewOnlyDisplayName;
            if (!SetField(ref _selectedLiveTranscriptOwnership, next)) return;
            OnPropertyChanged(nameof(LivePreviewOwnerLabel));
            OnPropertyChanged(nameof(FinalTranscriptOwnerLabel));
            OnPropertyChanged(nameof(GapRecoveryOwnerLabel));
            SaveSettings();
        }
    }
    public bool ShowLiveWaveformOnHover
    {
        get => _showLiveWaveformOnHover;
        set { if (SetField(ref _showLiveWaveformOnHover, value)) SaveSettings(); }
    }
    private LiveTranscriptOwnershipMode SelectedOwnershipMode =>
        LiveTranscriptOwnershipDescriptor.ModeFromDisplayName(SelectedLiveTranscriptOwnership);
    private LiveTranscriptOwnershipDescriptor OwnershipDescriptor => LiveTranscriptOwnershipDescriptor.Create(
        SelectedLiveMeetingModel.Id,
        SelectedLiveMeetingModel.Label,
        SelectedOwnershipMode,
        SelectedFinalMeetingModel.DisplayName);
    public string LiveMeetingModelStatus => SelectedLiveMeetingModel.Id is null
        ? "Off by default · select a verified live model explicitly"
        : _streamingModelLifecycle.Snapshot(SelectedLiveMeetingModel.Id).StatusText;
    public string LivePreviewOwnerLabel => OwnershipDescriptor.LivePreviewOwner;
    public string FinalTranscriptOwnerLabel => OwnershipDescriptor.FinalTranscriptOwner;
    public string GapRecoveryOwnerLabel => OwnershipDescriptor.GapRecoveryOwner;
    public string DictationModelStatusLabel => DictationModelRuntimeStatus;
    public string QwenCleanupStatusLabel => QwenCleanupRuntimeStatus;
    public string ShortcutModeLabel => EnableDoubleTapDictation ? "Hold to talk, or double-tap to lock recording" : "Hold to record, release to transcribe";
    public bool SetupNeedsResume => _isVisualPreview || !_onboardingCompleted || _onboardingProgressStore.Load().Deferred;
    public string SetupResumeLabel => SetupNeedsResume ? "Setup is paused or incomplete" : "Setup complete";
    public string StartupRegistrationLabel => _isVisualPreview ? "Preview-only: startup registration was not inspected." : StartupRegistrationService.DescribeState();
    public string PreviewPageStateMessage => _previewPageStateMessage;
    public string CaptureHotkeyButtonText => _isCapturingHotkey ? "Press shortcut..." : "Record shortcut";
    public string ShortcutCaptureLabel => _isCapturingHotkey
        ? "Press a function key or a modifier shortcut such as Ctrl+Shift+Space. Press Esc to cancel."
        : "Choose a shortcut or record one that is free on this Windows laptop.";
    public string AppVersion => $"v{Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.2.0"}";
    public string SelectedMeetingTitle
    {
        get => _selectedMeeting?.Title ?? "";
        set
        {
            if (_selectedMeeting is null || !MeetingTitleService.IsAcceptableManualTitle(value)) return;
            if (string.Equals(_selectedMeeting.Title, value.Trim(), StringComparison.Ordinal)) return;
            // Editing the title claims it: regeneration must never overwrite it afterwards.
            UpdateSelectedMeeting(meeting => meeting with { Title = value.Trim(), TitleIsManual = true });
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedMeetingTitleOwnership));
        }
    }
    public string SelectedMeetingTitleOwnership => _selectedMeeting?.TitleIsManual == true
        ? "Your title · kept when notes are regenerated"
        : "Generated title · replaced when notes are regenerated";
    public string SelectedMeetingManualNotes
    {
        get => _selectedMeeting?.ManualNotes ?? "";
        set
        {
            if (_selectedMeeting is null) return;
            var trimmed = value?.Trim() ?? "";
            if (string.Equals(_selectedMeeting.ManualNotes, trimmed, StringComparison.Ordinal)) return;
            UpdateSelectedMeeting(meeting => meeting with { ManualNotes = trimmed });
            OnPropertyChanged();
        }
    }
    public string SelectedMeetingMetadata => _selectedMeeting?.Metadata ?? "";

    /// <summary>Applies an edit to the selected meeting and persists it, keeping list and detail in step.</summary>
    private void UpdateSelectedMeeting(Func<MeetingItem, MeetingItem> edit)
    {
        if (_selectedMeeting is null) return;
        var index = Meetings.IndexOf(_selectedMeeting);
        var updated = edit(_selectedMeeting);
        if (index >= 0) Meetings[index] = updated;
        _selectedMeeting = updated;
        SaveMeetings();
        RefreshSearchResults();
    }
    public string SelectedMeetingNotes => string.IsNullOrWhiteSpace(_selectedMeeting?.Summary)
        ? ""
        : ApplySpeakerAliasesToNotes(_selectedMeeting.Summary, _activeSpeakerAliases);
    public string SelectedMeetingTemplate
    {
        get => _selectedMeetingTemplate;
        set
        {
            var normalized = NormalizeSummaryTemplateName(value);
            if (SetField(ref _selectedMeetingTemplate, normalized))
            {
                OnPropertyChanged(nameof(SelectedMeetingNotesActionLabel));
            }
        }
    }
    public string SelectedMeetingNotesActionLabel => string.IsNullOrWhiteSpace(_selectedMeeting?.Summary) ? "Generate Notes" : "Regenerate Notes";

    /// <summary>True while a notes request is running, which drives the progress and Cancel affordances.</summary>
    public bool IsSummarizing
    {
        get => _isSummarizing;
        private set
        {
            if (!SetField(ref _isSummarizing, value)) return;
            OnPropertyChanged(nameof(IsNotSummarizing));
            OnPropertyChanged(nameof(CanRetrySummary));
        }
    }
    public bool IsNotSummarizing => !_isSummarizing;
    public bool CanRetrySummary => _summaryRetryAvailable && !_isSummarizing;

    private void CancelSummary_Click(object sender, RoutedEventArgs e)
    {
        _summaryCancellation?.Cancel();
        DictationStatus = "Cancelling notes generation…";
    }

    /// <summary>True while a media import is running, which drives the progress and Cancel affordances.</summary>
    public bool IsImportingMeeting
    {
        get => _isImportingMeeting;
        private set
        {
            if (!SetField(ref _isImportingMeeting, value)) return;
            OnPropertyChanged(nameof(IsNotImportingMeeting));
        }
    }
    public bool IsNotImportingMeeting => !_isImportingMeeting;

    /// <summary>Overall import completion, 0-100, across decode, transcription, cleanup and notes.</summary>
    public double ImportProgressPercent
    {
        get => _importProgressPercent;
        private set => SetField(ref _importProgressPercent, value);
    }

    public string ImportProgressLabel
    {
        get => _importProgressLabel;
        private set => SetField(ref _importProgressLabel, value);
    }

    /// <summary>
    /// True for stages that report no fraction. Parakeet decodes a whole file in one native call,
    /// so the bar must say "working" rather than invent a percentage that never moves.
    /// </summary>
    public bool ImportProgressIsIndeterminate
    {
        get => _importProgressIsIndeterminate;
        private set => SetField(ref _importProgressIsIndeterminate, value);
    }

    private void CancelImport_Click(object sender, RoutedEventArgs e)
    {
        _importCancellation?.Cancel();
        DictationStatus = "Cancelling import…";
        ApplyImportProgress(MeetingImportProgressMapper.Cancelling(ImportProgressPercent));
    }

    private void ReportImportProgress(MeetingImportStage stage, double? fraction) =>
        ApplyImportProgress(MeetingImportProgressMapper.Map(stage, fraction));

    private void ApplyImportProgress(MeetingImportProgress progress)
    {
        ImportProgressLabel = progress.Label;
        ImportProgressPercent = progress.Percent;
        ImportProgressIsIndeterminate = progress.IsIndeterminate;
    }

    private async void RetrySummary_Click(object sender, RoutedEventArgs e)
    {
        _summaryRetryAvailable = false;
        OnPropertyChanged(nameof(CanRetrySummary));
        await GenerateSelectedMeetingNotesAsync();
    }
    public string SelectedMeetingTranscript => ApplySpeakerAliases(_selectedMeeting?.Transcript ?? "", _activeSpeakerAliases);
    public string RuntimeDiagnostics
    {
        get => _runtimeDiagnostics;
        private set => SetField(ref _runtimeDiagnostics, value);
    }
    public string NativeRuntimeStatus
    {
        get => _nativeRuntimeStatus;
        private set => SetField(ref _nativeRuntimeStatus, value);
    }
    public string SpeakerDiarizationStatusLabel
    {
        get => _speakerDiarizationStatusLabel;
        private set => SetField(ref _speakerDiarizationStatusLabel, value);
    }
    public string DictationModelRuntimeStatus
    {
        get => _dictationModelRuntimeStatus;
        private set
        {
            if (SetField(ref _dictationModelRuntimeStatus, value))
            {
                OnPropertyChanged(nameof(DictationModelStatusLabel));
            }
        }
    }
    public string QwenCleanupRuntimeStatus
    {
        get => _qwenCleanupRuntimeStatus;
        private set
        {
            if (SetField(ref _qwenCleanupRuntimeStatus, value))
            {
                OnPropertyChanged(nameof(QwenCleanupStatusLabel));
            }
        }
    }
    public string GpuRuntimeStatus
    {
        get => _gpuRuntimeStatus;
        private set => SetField(ref _gpuRuntimeStatus, value);
    }
    public string ModelCacheDirectory
    {
        get => _modelCacheDirectory;
        private set => SetField(ref _modelCacheDirectory, value);
    }
    public string RuntimeSetupStatus
    {
        get => _runtimeSetupStatus;
        private set => SetField(ref _runtimeSetupStatus, value);
    }
    public string ModelCacheSize
    {
        get => _modelCacheSize;
        private set => SetField(ref _modelCacheSize, value);
    }
    public string BenchmarkSummary
    {
        get => _benchmarkSummary;
        private set => SetField(ref _benchmarkSummary, value);
    }
    public string DiarizationDependencyStatus
    {
        get => _diarizationDependencyStatus;
        private set => SetField(ref _diarizationDependencyStatus, value);
    }
    public string DiarizationTokenStatus
    {
        get => _diarizationTokenStatus;
        private set => SetField(ref _diarizationTokenStatus, value);
    }

    public string MeetingDetectionStatus
    {
        get => _meetingDetectionStatus;
        private set => SetField(ref _meetingDetectionStatus, value);
    }

    public string DictationStatus
    {
        get => _dictationStatus;
        private set => SetField(ref _dictationStatus, value);
    }

    public string SelectedTheme
    {
        get => _theme.Equals("light", StringComparison.OrdinalIgnoreCase) ? "Light" : "Dark";
        set
        {
            var next = value.Equals("Light", StringComparison.OrdinalIgnoreCase) ? "light" : "dark";
            if (_theme.Equals(next, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            SetTheme(next);
            OnPropertyChanged();
        }
    }

    public string? SelectedMicrophone
    {
        get => _selectedMicrophone;
        set
        {
            if (SetField(ref _selectedMicrophone, value))
            {
                SaveSettings();
            }
        }
    }

    public string SelectedHotkey
    {
        get => _selectedHotkey;
        set
        {
            var nextHotkey = NormalizeHotkey(value, allowCustom: true);
            if (_isVisualPreview)
            {
                if (SetField(ref _selectedHotkey, nextHotkey))
                    DictationStatus = "Preview-only: shortcut changes are disabled; no hook was registered.";
                return;
            }
            var previousHotkey = _selectedHotkey;
            if (!SetField(ref _selectedHotkey, nextHotkey))
            {
                return;
            }

            AddHotkeyOptionIfMissing(nextHotkey);
            if (!RegisterGlobalHotkey())
            {
                _selectedHotkey = previousHotkey;
                OnPropertyChanged(nameof(SelectedHotkey));
                RegisterGlobalHotkey();
                return;
            }

            DictationStatus = EnableDoubleTapDictation
                ? $"Hold {_selectedHotkey} to dictate, or double-tap for hands-free"
                : $"Hold {_selectedHotkey} to dictate";
            SaveSettings();
            OnPropertyChanged(nameof(ShortcutModeLabel));
            _toastNotificationService.ShowIdle(_selectedHotkey);
        }
    }

    public string SelectedPasteBehavior
    {
        get => _selectedPasteBehavior;
        set
        {
            if (SetField(ref _selectedPasteBehavior, value))
            {
                SaveSettings();
            }
        }
    }

    public bool EnableDoubleTapDictation
    {
        get => _enableDoubleTapDictation;
        set
        {
            if (SetField(ref _enableDoubleTapDictation, value))
            {
                ResetHotkeyDictationState();
                SaveSettings();
                OnPropertyChanged(nameof(ShortcutModeLabel));
                DictationStatus = value
                    ? $"Hold {SelectedHotkey} to dictate, or double-tap for hands-free"
                    : $"Hold {SelectedHotkey} to dictate";
            }
        }
    }

    public bool RemoveFillerWords
    {
        get => _removeFillerWords;
        set
        {
            if (SetField(ref _removeFillerWords, value))
            {
                SaveSettings();
            }
        }
    }

    public bool EnableLocalCleanup
    {
        get => _enableLocalCleanup;
        set
        {
            if (SetField(ref _enableLocalCleanup, value))
            {
                SaveSettings();
                QwenCleanupRuntimeStatus = NativeTextCleanupService.Status(_enableLocalCleanup);
                OnPropertyChanged(nameof(QwenCleanupStatusLabel));
                _ = RefreshRuntimeDiagnosticsAsync();
            }
        }
    }

    public bool StartAtLogin
    {
        get => _startAtLogin;
        set
        {
            if (!SetField(ref _startAtLogin, value))
            {
                return;
            }

            if (_isVisualPreview)
            {
                DictationStatus = "Preview-only: startup registration is disabled; no registry state was changed.";
                return;
            }

            try
            {
                StartupRegistrationService.SetEnabled(value);
                SaveSettings();
                DictationStatus = value ? "Muesli will start at login" : "Start at login disabled";
            }
            catch (Exception exception)
            {
                _startAtLogin = !value;
                OnPropertyChanged(nameof(StartAtLogin));
                DictationStatus = $"Could not update startup setting: {exception.Message}";
                _toastNotificationService.Show("Startup setting failed", exception.Message, ToastState.Error, 4200);
            }
        }
    }

    public string SelectedSummaryProvider
    {
        get => _selectedSummaryProvider;
        set
        {
            if (SetField(ref _selectedSummaryProvider, value))
            {
                OnPropertyChanged(nameof(SummaryProviderDisclosureText));
                SaveSettings();
            }
        }
    }

    public string SelectedSummaryTemplate
    {
        get => _selectedSummaryTemplate;
        set
        {
            var normalized = NormalizeSummaryTemplateName(value);
            if (SetField(ref _selectedSummaryTemplate, normalized))
            {
                SaveSettings();
            }
        }
    }

    public bool OpenDashboardOnLaunch
    {
        get => _openDashboardOnLaunch;
        set
        {
            if (SetField(ref _openDashboardOnLaunch, value))
            {
                SaveSettings();
            }
        }
    }

    public bool SaveMeetingRecordings
    {
        get => _saveMeetingRecordings;
        set
        {
            if (SetField(ref _saveMeetingRecordings, value))
            {
                SaveSettings();
            }
        }
    }

    public bool PostMeetingHookEnabled
    {
        get => _postMeetingHookEnabled;
        set
        {
            if (value && !IsValidHookExecutable(PostMeetingHookExecutablePath))
            {
                PostMeetingAutomationStatusText = "Choose an existing .exe before enabling the hook.";
                value = false;
            }
            if (SetField(ref _postMeetingHookEnabled, value))
            {
                SaveSettings();
            }
        }
    }

    public string PostMeetingHookExecutablePath
    {
        get => _postMeetingHookExecutablePath;
        set
        {
            var normalized = value?.Trim() ?? "";
            if (!SetField(ref _postMeetingHookExecutablePath, normalized)) return;
            if (_postMeetingHookEnabled && !IsValidHookExecutable(normalized))
            {
                _postMeetingHookEnabled = false;
                OnPropertyChanged(nameof(PostMeetingHookEnabled));
                PostMeetingAutomationStatusText = "The hook was disabled because its executable is unavailable.";
            }
            SaveSettings();
        }
    }

    public string SelectedHookTranscriptPolicy
    {
        get => _selectedHookTranscriptPolicy;
        set
        {
            var normalized = HookTranscriptPolicies.Contains(value) ? value : "Metadata only";
            if (SetField(ref _selectedHookTranscriptPolicy, normalized)) SaveSettings();
        }
    }

    public int PostMeetingHookTimeoutSeconds
    {
        get => _postMeetingHookTimeoutSeconds;
        set
        {
            if (SetField(ref _postMeetingHookTimeoutSeconds, Math.Clamp(value, 1, 600))) SaveSettings();
        }
    }

    public int PostMeetingHookMaxAttempts
    {
        get => _postMeetingHookMaxAttempts;
        set
        {
            if (SetField(ref _postMeetingHookMaxAttempts, Math.Clamp(value, 1, 3))) SaveSettings();
        }
    }

    public bool AutoExportMarkdownEnabled
    {
        get => _autoExportMarkdownEnabled;
        set
        {
            if (value && !IsValidAutoExportDirectory(AutoExportMarkdownDirectory))
            {
                PostMeetingAutomationStatusText = "Choose an absolute export folder before enabling automatic Markdown export.";
                value = false;
            }
            if (SetField(ref _autoExportMarkdownEnabled, value)) SaveSettings();
        }
    }

    public string AutoExportMarkdownDirectory
    {
        get => _autoExportMarkdownDirectory;
        set
        {
            var normalized = value?.Trim() ?? "";
            if (!SetField(ref _autoExportMarkdownDirectory, normalized)) return;
            if (_autoExportMarkdownEnabled && !IsValidAutoExportDirectory(normalized))
            {
                _autoExportMarkdownEnabled = false;
                OnPropertyChanged(nameof(AutoExportMarkdownEnabled));
                PostMeetingAutomationStatusText = "Automatic Markdown export was disabled because its destination is invalid.";
            }
            SaveSettings();
        }
    }

    public string SelectedAutoExportContent
    {
        get => _selectedAutoExportContent;
        set
        {
            var normalized = AutoExportContentOptions.Contains(value) ? value : "Notes";
            if (SetField(ref _selectedAutoExportContent, normalized)) SaveSettings();
        }
    }

    public string PostMeetingAutomationStatusText
    {
        get => _postMeetingAutomationStatus;
        private set => SetField(ref _postMeetingAutomationStatus, value);
    }

    public bool ComputerUseEnabled
    {
        get => _computerUseEnabled;
        set
        {
            if (value && !ComputerUseConfigurationIsReady(out var error))
            {
                ComputerUseStatusText = error;
                value = false;
            }
            if (SetField(ref _computerUseEnabled, value))
            {
                ComputerUseStatusText = value
                    ? "Computer Use is enabled for explicit planner voice sessions only."
                    : "Computer Use is disabled.";
                SaveSettings();
            }
        }
    }

    public string SelectedComputerUsePlannerProvider
    {
        get => _selectedComputerUsePlannerProvider;
        set
        {
            var normalized = ComputerUsePlannerProviders.Contains(value) ? value : "None";
            if (!SetField(ref _selectedComputerUsePlannerProvider, normalized)) return;
            DisableComputerUseIfConfigurationBecameInvalid();
            SaveSettings();
        }
    }

    public string ComputerUsePlannerModel
    {
        get => _computerUsePlannerModel;
        set
        {
            if (!SetField(ref _computerUsePlannerModel, value?.Trim() ?? "")) return;
            DisableComputerUseIfConfigurationBecameInvalid();
            SaveSettings();
        }
    }

    public int ComputerUsePlannerTimeoutSeconds
    {
        get => _computerUsePlannerTimeoutSeconds;
        set { if (SetField(ref _computerUsePlannerTimeoutSeconds, Math.Clamp(value, 5, 120))) SaveSettings(); }
    }

    public int ComputerUsePerActionTimeoutSeconds
    {
        get => _computerUsePerActionTimeoutSeconds;
        set { if (SetField(ref _computerUsePerActionTimeoutSeconds, Math.Clamp(value, 1, 30))) SaveSettings(); }
    }

    public int ComputerUseMaximumActionCount
    {
        get => _computerUseMaximumActionCount;
        set { if (SetField(ref _computerUseMaximumActionCount, Math.Clamp(value, 1, 20))) SaveSettings(); }
    }

    public string ComputerUseAllowedApplications
    {
        get => _computerUseAllowedApplications;
        set
        {
            if (!SetField(ref _computerUseAllowedApplications, NormalizeAllowlistText(value))) return;
            DisableComputerUseIfConfigurationBecameInvalid();
            SaveSettings();
        }
    }

    public string ComputerUseAllowedBrowserDomains
    {
        get => _computerUseAllowedBrowserDomains;
        set { if (SetField(ref _computerUseAllowedBrowserDomains, NormalizeAllowlistText(value))) SaveSettings(); }
    }

    public bool ComputerUseIncludeWindowText
    {
        get => _computerUseIncludeWindowText;
        set { if (SetField(ref _computerUseIncludeWindowText, value)) SaveSettings(); }
    }

    public bool ComputerUseIncludeScreenshots
    {
        get => _computerUseIncludeScreenshots;
        set { if (SetField(ref _computerUseIncludeScreenshots, value)) SaveSettings(); }
    }

    public bool ComputerUseIncludeBrowserPageText
    {
        get => _computerUseIncludeBrowserPageText;
        set { if (SetField(ref _computerUseIncludeBrowserPageText, value)) SaveSettings(); }
    }

    public string SelectedComputerUseBrowserInterface
    {
        get => _selectedComputerUseBrowserInterface;
        set
        {
            var normalized = ComputerUseBrowserInterfaces.Contains(value) ? value : "Disabled";
            if (SetField(ref _selectedComputerUseBrowserInterface, normalized)) SaveSettings();
        }
    }

    public string ComputerUseBrowserEndpoint
    {
        get => _computerUseBrowserEndpoint;
        set { if (SetField(ref _computerUseBrowserEndpoint, value?.Trim() ?? "")) SaveSettings(); }
    }

    public string ComputerUseStatusText
    {
        get => _computerUseStatus;
        private set => SetField(ref _computerUseStatus, value);
    }

    public string ComputerUseVoiceButtonText => _computerUseVoiceCaptureActive
        ? "Stop listening and plan"
        : "Speak planner command";

    public bool ComputerUseStopEnabled => _computerUseVoiceCaptureActive || _computerUseIsRunning;

    public bool ShowFloatingIndicator
    {
        get => _showFloatingIndicator;
        set
        {
            if (SetField(ref _showFloatingIndicator, value))
            {
                _toastNotificationService.SetIdleIndicatorVisible(value);
                SaveSettings();
            }
        }
    }

    public string SelectedIndicatorPosition
    {
        get => _selectedIndicatorPosition;
        set
        {
            var next = IndicatorPositions.Contains(value) ? value : "Top Center";
            if (SetField(ref _selectedIndicatorPosition, next))
            {
                var clearCustomPosition = !next.Equals("Custom", StringComparison.OrdinalIgnoreCase);
                if (clearCustomPosition)
                {
                    _indicatorLeft = null;
                    _indicatorTop = null;
                }

                _toastNotificationService.SetIndicatorAnchor(next, clearCustomPosition);
                SaveSettings();
            }
        }
    }

    public string OpenAIApiKey
    {
        get => _openAIApiKey;
    }

    public string OpenAIApiKeyStatus => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OPENAI_API_KEY"))
        ? "Configured via environment"
        : string.IsNullOrWhiteSpace(_openAIApiKey) ? "Not configured" : "Configured securely";

    public string OpenAIModel
    {
        get => _openAIModel;
        set
        {
            if (SetField(ref _openAIModel, value))
            {
                SaveSettings();
            }
        }
    }

    public string OpenRouterApiKey
    {
        get => _openRouterApiKey;
    }

    public string OpenRouterApiKeyStatus => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OPENROUTER_API_KEY"))
        ? "Configured via environment"
        : string.IsNullOrWhiteSpace(_openRouterApiKey) ? "Not configured" : "Configured securely";

    public string OpenRouterModel
    {
        get => _openRouterModel;
        set
        {
            if (SetField(ref _openRouterModel, value))
            {
                SaveSettings();
            }
        }
    }

    public bool AutoMeetingDetectionEnabled
    {
        get => _autoMeetingDetectionEnabled;
        set
        {
            if (SetField(ref _autoMeetingDetectionEnabled, value))
            {
                SaveSettings();
                if (value)
                {
                    _meetingDetectionService.Start();
                }
                else
                {
                    _meetingDetectionService.Stop();
                    _meetingPromptService.Close();
                }
            }
        }
    }

    public MainWindow()
    {
        _globalHotkeyService = new GlobalHotkeyService();
        _settingsStore = new SettingsStore();
        _dataStore = new AppDataStore();
        _toastNotificationService = new ToastNotificationService();
        _activeAppPasteService = new ActiveAppPasteService();
        _meetingTranscriptionClient = new NativeTranscriptionClient();
        _meetingPlaybackService = new MeetingRecordingPlaybackService();
        _meetingDetectionService = new MeetingDetectionService();
        _meetingPromptService = new MeetingPromptService();
        _trayIconService = new TrayIconService();
        _onboardingProgressStore = new OnboardingProgressStore();
        _microphoneAccessService = new WindowsMicrophoneAccessService();
        _runtimeDiagnosticsService = new RuntimeDiagnosticsService();
        _postMeetingAutomationService = new PostMeetingAutomationService();
        _logService = new AppLogService();
        _captureStorageService = new CaptureStorageService();
        _computerUseHttpClient = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        _computerUseTraceStore = new ComputerUseTraceStore();
        _dictationTranscriptionClient = new NativeTranscriptionClient();
        _dictationCoordinator = new DictationCoordinator(_dictationTranscriptionClient);
        if (_dictationCoordinator.StartupCaptureCleanup.DeletedCount > 0)
        {
            _logService.Info($"Removed interrupted dictation temporary audio at startup. count={_dictationCoordinator.StartupCaptureCleanup.DeletedCount}");
        }
        if (_dictationCoordinator.StartupCaptureCleanup.FailedPaths.Count > 0)
        {
            _logService.Info($"Interrupted dictation temporary audio needs cleanup. failedCount={_dictationCoordinator.StartupCaptureCleanup.FailedPaths.Count}; pathsLogged=false");
        }
        _meetingRecordingCoordinator = new(_meetingTranscriptionClient, _logService);
        _meetingRecordingCoordinator.StateChanged += OnMeetingSessionStateChanged;
        _meetingRecordingCoordinator.HealthChanged += OnMeetingAudioHealthChanged;
        _meetingRecordingCoordinator.LevelChanged += OnMeetingRecordingLevelChanged;
        _meetingRecordingCoordinator.LiveTranscriptChanged += OnLiveTranscriptChanged;
        _meetingRecordingCoordinator.LiveTranscriptionFailed += OnLiveTranscriptionFailed;
        _meetingPlaybackService.StateChanged += OnMeetingPlaybackStateChanged;
        _modelLifecycle = new TranscriptionModelLifecycleService(
            () => new HashSet<string>(
                [SelectedTranscriptionModel.Id, SelectedFinalMeetingModel.Id],
                StringComparer.OrdinalIgnoreCase),
            ReleaseTranscriptionModelAsync);
        _modelLifecycle.ModelChanged += OnTranscriptionModelChanged;
        _streamingModelLifecycle = new StreamingModelLifecycleService(
            () => SelectedLiveMeetingModel.Id,
            _ => Task.CompletedTask);
        _streamingModelLifecycle.ModelChanged += OnStreamingModelChanged;
        foreach (var model in TranscriptionModels)
        {
            TranscriptionModelItems.Add(new TranscriptionModelItem(_modelLifecycle.Snapshot(model.Id)));
        }
        foreach (var model in StreamingModelCatalog.Models)
        {
            StreamingModelItems.Add(new StreamingModelItem(_streamingModelLifecycle.Snapshot(model.Id)));
        }
        _transcriptionBenchmarkService = new(_logService);
        _textCleanupService = new(_logService);
        _transcriptionPipelineService = new(_textCleanupService, _logService);
        InitializeComponent();
        IsCompactLayout = ActualWidth < 900;
        FilteredDictations = CollectionViewSource.GetDefaultView(Dictations);
        FilteredDictations.Filter = item => PassesDateFilter(item, _dictationDateFilter) && PassesSearch(item);
        if (FilteredDictations is ListCollectionView dictationView)
        {
            dictationView.SortDescriptions.Clear();
            dictationView.SortDescriptions.Add(new SortDescription(nameof(DictationItem.Timestamp), ListSortDirection.Descending));
            dictationView.GroupDescriptions.Clear();
            dictationView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(DictationItem.DateGroupLabel)));
        }
        FilteredMeetings = CollectionViewSource.GetDefaultView(Meetings);
        FilteredMeetings.Filter = item => PassesDateFilter(item, _meetingDateFilter) && PassesMeetingFolder(item) && PassesSearch(item);
        SearchDictationResults = new ListCollectionView(Dictations);
        SearchDictationResults.Filter = item => PassesSearch(item) && !string.IsNullOrWhiteSpace(_searchQuery);
        SearchMeetingResults = new ListCollectionView(Meetings);
        SearchMeetingResults.Filter = item => PassesSearch(item) && !string.IsNullOrWhiteSpace(_searchQuery);
        DataContext = this;
        _hotkeyReleaseTimer.Tick += HotkeyReleaseTimer_Tick;

        var settings = _settingsStore.Load();
        LoadPersistedData();
        var persistenceWarning = _dataStore.LastWarning ?? _settingsStore.LastWarning;
        if (!string.IsNullOrWhiteSpace(persistenceWarning))
        {
            _dictationStatus = persistenceWarning;
            _logService.Info($"Persistence recovery notice: {persistenceWarning}");
        }
        foreach (var microphone in _dictationCoordinator.ListMicrophones())
        {
            if (!MicrophoneDevices.Contains(microphone))
            {
                MicrophoneDevices.Add(microphone);
            }
        }
        var savedMicrophone = settings.MicrophoneName;
        _selectedMicrophone = ShouldUseSavedMicrophone(savedMicrophone) && savedMicrophone is not null && MicrophoneDevices.Contains(savedMicrophone)
            ? savedMicrophone
            : _dictationCoordinator.PickPreferredMicrophone();
        _selectedHotkey = NormalizeHotkey(settings.Hotkey, allowCustom: true);
        AddHotkeyOptionIfMissing(_selectedHotkey);
        _selectedPasteBehavior = PasteBehaviors.Contains(settings.PasteBehavior) ? settings.PasteBehavior : "active-app";
        _selectedTranscriptionModel = TranscriptionModelCatalog.Get(settings.DictationModelId);
        _selectedFinalMeetingModel = TranscriptionModelCatalog.Get(settings.FinalMeetingModelId);
        _selectedLiveMeetingModel = LiveMeetingModels.FirstOrDefault(choice => choice.Id == settings.LiveMeetingModelId) ?? LiveModelChoice.Off;
        _selectedLiveTranscriptOwnership = LiveTranscriptOwnershipDescriptor.DisplayNameFor(
            LiveTranscriptOwnershipDescriptor.ModeFromSettingValue(settings.LiveTranscriptOwnership));
        _showLiveWaveformOnHover = settings.ShowLiveWaveformOnHover;
        _dictationTranscriptionClient.SwitchModelAsync(_selectedTranscriptionModel.Id).GetAwaiter().GetResult();
        _meetingTranscriptionClient.SwitchModelAsync(_selectedFinalMeetingModel.Id).GetAwaiter().GetResult();
        _userName = string.IsNullOrWhiteSpace(settings.UserName) ? Environment.UserName.Trim() : settings.UserName.Trim();
        _onboardingCompleted = settings.OnboardingCompleted;
        _lastCompletedFeatureTourVersion = settings.LastCompletedFeatureTourVersion;
        _selectedSummaryProvider = SummaryProviders.Contains(settings.MeetingSummaryProvider) ? settings.MeetingSummaryProvider : "local";
        _ollamaEndpoint = settings.OllamaEndpoint;
        _ollamaModel = settings.OllamaModel;
        _selectedSummaryTemplate = NormalizeSummaryTemplateName(settings.MeetingSummaryTemplate);
        _openAIApiKey = settings.ResolvedOpenAIApiKey;
        _openAIModel = string.IsNullOrWhiteSpace(settings.OpenAIModel) ? "gpt-5.4-mini" : settings.OpenAIModel;
        _openRouterApiKey = settings.ResolvedOpenRouterApiKey;
        _openRouterModel = string.IsNullOrWhiteSpace(settings.OpenRouterModel) ? "stepfun/step-3.5-flash:free" : settings.OpenRouterModel;
        _theme = settings.Theme.Equals("light", StringComparison.OrdinalIgnoreCase) ? "light" : "dark";
        _enableDoubleTapDictation = settings.EnableDoubleTapDictation;
        _removeFillerWords = settings.RemoveFillerWords;
        _enableLocalCleanup = settings.EnableLocalCleanup;
        _qwenCleanupRuntimeStatus = NativeTextCleanupService.Status(_enableLocalCleanup);
        _startAtLogin = settings.StartAtLogin && StartupRegistrationService.IsEnabled();
        _openDashboardOnLaunch = settings.OpenDashboardOnLaunch;
        _saveMeetingRecordings = settings.SaveMeetingRecordings;
        _postMeetingHookExecutablePath = settings.PostMeetingHookExecutablePath;
        _selectedHookTranscriptPolicy = HookTranscriptPolicyDisplay(settings.PostMeetingHookTranscriptPolicy);
        _postMeetingHookTimeoutSeconds = settings.PostMeetingHookTimeoutSeconds;
        _postMeetingHookMaxAttempts = settings.PostMeetingHookMaxAttempts;
        _postMeetingHookEnabled = settings.PostMeetingHookEnabled && IsValidHookExecutable(_postMeetingHookExecutablePath);
        _autoExportMarkdownDirectory = settings.AutoExportMarkdownDirectory;
        _selectedAutoExportContent = AutoExportContentDisplay(settings.AutoExportMarkdownContent);
        _autoExportMarkdownEnabled = settings.AutoExportMarkdownEnabled && IsValidAutoExportDirectory(_autoExportMarkdownDirectory);
        _postMeetingAutomationStatus = settings.PostMeetingHookEnabled && !_postMeetingHookEnabled
            ? "The saved hook was disabled because its executable is unavailable."
            : settings.AutoExportMarkdownEnabled && !_autoExportMarkdownEnabled
                ? "Automatic Markdown export was disabled because its saved destination is invalid."
                : _postMeetingHookEnabled || _autoExportMarkdownEnabled
                    ? "Automation is ready and runs only after a newly completed meeting is saved."
                    : "Automation is disabled.";
        _selectedComputerUsePlannerProvider = ComputerUseProviderDisplay(settings.ComputerUsePlannerProvider);
        _computerUsePlannerModel = settings.ComputerUsePlannerModel;
        _computerUsePlannerTimeoutSeconds = settings.ComputerUsePlannerTimeoutSeconds;
        _computerUsePerActionTimeoutSeconds = settings.ComputerUsePerActionTimeoutSeconds;
        _computerUseMaximumActionCount = settings.ComputerUseMaximumActionCount;
        _computerUseAllowedApplications = settings.ComputerUseAllowedApplications;
        _computerUseAllowedBrowserDomains = settings.ComputerUseAllowedBrowserDomains;
        _computerUseIncludeWindowText = settings.ComputerUseIncludeWindowText;
        _computerUseIncludeScreenshots = settings.ComputerUseIncludeScreenshots;
        _computerUseIncludeBrowserPageText = settings.ComputerUseIncludeBrowserPageText;
        _selectedComputerUseBrowserInterface = ComputerUseBrowserInterfaceDisplay(settings.ComputerUseBrowserInterface);
        _computerUseBrowserEndpoint = settings.ComputerUseBrowserEndpoint;
        _computerUseEnabled = settings.ComputerUseEnabled && ComputerUseConfigurationIsReady(out _);
        _computerUseStatus = settings.ComputerUseEnabled && !_computerUseEnabled
            ? "Computer Use was disabled because its saved provider, model, key, or application allowlist is unavailable."
            : _computerUseEnabled
                ? "Computer Use is ready for an explicit planner voice session."
                : "Computer Use is disabled.";
        _showFloatingIndicator = settings.ShowFloatingIndicator;
        _selectedIndicatorPosition = IndicatorPositions.Contains(settings.IndicatorAnchor) ? settings.IndicatorAnchor : "Top Center";
        _autoMeetingDetectionEnabled = settings.AutoMeetingDetectionEnabled;
        _indicatorLeft = settings.IndicatorLeft;
        _indicatorTop = settings.IndicatorTop;
        _crashReportingEnabled = settings.CrashReportingEnabled;
        _crashReportingPromptShown = settings.CrashReportingPromptShown;
        _crashReportingStartupValue = _crashReportingEnabled;
        _toastNotificationService.SetSavedPosition(_indicatorLeft, _indicatorTop);
        _toastNotificationService.SetIndicatorAnchor(_selectedIndicatorPosition, clearCustomPosition: false);
        _toastNotificationService.SetIdleIndicatorVisible(_showFloatingIndicator, showNow: false);
        _toastNotificationService.ConfigureActions(StopActiveRecordingFromIndicatorAsync, CancelActiveRecordingFromIndicatorAsync);
        _toastNotificationService.PositionChanged += OnIndicatorPositionChanged;
        _dictationCoordinator.DeviceListChanged += OnDictationDeviceListChanged;
        _dictationCoordinator.RouteChanged += OnDictationRouteChanged;
        _dictationCoordinator.LevelChanged += OnDictationLevelChanged;
        _meetingDetectionService.ScanCompleted += OnMeetingDetectionScanCompleted;
        _meetingAutoStopTimer.Tick += MeetingAutoStopTimer_Tick;
        _meetingPlaybackTimer.Tick += MeetingPlaybackTimer_Tick;
        _aliasSaveDebounceTimer.Tick += AliasSaveDebounceTimer_Tick;
        SystemEvents.PowerModeChanged += SystemEvents_PowerModeChanged;
        RefreshRecoverableMeetingSessions();
        _meetingPromptService.Reset();
OnPropertyChanged(nameof(SelectedMicrophone));
    OnPropertyChanged(nameof(SelectedHotkey));
    OnPropertyChanged(nameof(SelectedPasteBehavior));
    OnPropertyChanged(nameof(SelectedTranscriptionModel));
    OnPropertyChanged(nameof(SelectedFinalMeetingModel));
    OnPropertyChanged(nameof(FinalMeetingModelStatus));
    OnPropertyChanged(nameof(LiveMeetingModelStatus));
    OnPropertyChanged(nameof(SelectedLiveMeetingModel));
    OnPropertyChanged(nameof(SelectedLiveTranscriptOwnership));
    OnPropertyChanged(nameof(ShowLiveWaveformOnHover));
    OnPropertyChanged(nameof(LivePreviewOwnerLabel));
    OnPropertyChanged(nameof(FinalTranscriptOwnerLabel));
    OnPropertyChanged(nameof(GapRecoveryOwnerLabel));
    OnPropertyChanged(nameof(ActiveModelLabel));
    OnPropertyChanged(nameof(SelectedModelDescription));
    OnPropertyChanged(nameof(SelectedModelLanguages));
    OnPropertyChanged(nameof(SelectedModelDownloadSize));
    OnPropertyChanged(nameof(SelectedModelCacheStatus));
    OnPropertyChanged(nameof(UserName));
    OnPropertyChanged(nameof(UserGreeting));
    OnPropertyChanged(nameof(SelectedSummaryProvider));
    OnPropertyChanged(nameof(SelectedSummaryTemplate));
    OnPropertyChanged(nameof(OpenAIApiKey));
    OnPropertyChanged(nameof(OpenAIApiKeyStatus));
    OnPropertyChanged(nameof(OpenAIModel));
    OnPropertyChanged(nameof(OpenRouterApiKey));
    OnPropertyChanged(nameof(OpenRouterApiKeyStatus));
    OnPropertyChanged(nameof(OpenRouterModel));
    OnPropertyChanged(nameof(EnableDoubleTapDictation));
    OnPropertyChanged(nameof(RemoveFillerWords));
    OnPropertyChanged(nameof(EnableLocalCleanup));
    OnPropertyChanged(nameof(SelectedTheme));
    OnPropertyChanged(nameof(StartAtLogin));
    OnPropertyChanged(nameof(OpenDashboardOnLaunch));
    OnPropertyChanged(nameof(SaveMeetingRecordings));
    OnPropertyChanged(nameof(PostMeetingHookEnabled));
    OnPropertyChanged(nameof(PostMeetingHookExecutablePath));
    OnPropertyChanged(nameof(SelectedHookTranscriptPolicy));
    OnPropertyChanged(nameof(PostMeetingHookTimeoutSeconds));
    OnPropertyChanged(nameof(PostMeetingHookMaxAttempts));
    OnPropertyChanged(nameof(AutoExportMarkdownEnabled));
    OnPropertyChanged(nameof(AutoExportMarkdownDirectory));
    OnPropertyChanged(nameof(SelectedAutoExportContent));
    OnPropertyChanged(nameof(PostMeetingAutomationStatusText));
    OnPropertyChanged(nameof(ComputerUseEnabled));
    OnPropertyChanged(nameof(SelectedComputerUsePlannerProvider));
    OnPropertyChanged(nameof(ComputerUsePlannerModel));
    OnPropertyChanged(nameof(ComputerUsePlannerTimeoutSeconds));
    OnPropertyChanged(nameof(ComputerUsePerActionTimeoutSeconds));
    OnPropertyChanged(nameof(ComputerUseMaximumActionCount));
    OnPropertyChanged(nameof(ComputerUseAllowedApplications));
    OnPropertyChanged(nameof(ComputerUseAllowedBrowserDomains));
    OnPropertyChanged(nameof(ComputerUseIncludeWindowText));
    OnPropertyChanged(nameof(ComputerUseIncludeScreenshots));
    OnPropertyChanged(nameof(ComputerUseIncludeBrowserPageText));
    OnPropertyChanged(nameof(SelectedComputerUseBrowserInterface));
    OnPropertyChanged(nameof(ComputerUseBrowserEndpoint));
    OnPropertyChanged(nameof(ComputerUseStatusText));
    OnPropertyChanged(nameof(ComputerUseVoiceButtonText));
    OnPropertyChanged(nameof(ComputerUseStopEnabled));
    if (settings.PostMeetingHookEnabled != _postMeetingHookEnabled ||
        settings.AutoExportMarkdownEnabled != _autoExportMarkdownEnabled ||
        settings.ComputerUseEnabled != _computerUseEnabled)
    {
        SaveSettings();
    }
    OnPropertyChanged(nameof(ShowFloatingIndicator));
    OnPropertyChanged(nameof(SelectedIndicatorPosition));
    OnPropertyChanged(nameof(AutoMeetingDetectionEnabled));
    OnPropertyChanged(nameof(MeetingDetectionStatus));
    ApplyTheme(_theme);
    ShowPage(DictationsPage, DictationsNav);
    Loaded += (_, _) =>
    {
        StartRuntime(showOnboarding: !_isParkedForBackground);
        if (!string.IsNullOrWhiteSpace(persistenceWarning))
        {
            System.Windows.MessageBox.Show(
                persistenceWarning,
                "Muesli data recovery",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    };
    Closing += (_, _) =>
    {
        _logService.Info("Main window closing.");
        _aliasSaveDebounceTimer.Stop();
        SaveActiveSpeakerAliases();
        _meetingAutoStopTimer.Stop();
        _meetingPlaybackTimer.Stop();
        SystemEvents.PowerModeChanged -= SystemEvents_PowerModeChanged;
        _applicationShutdownCancellation.Cancel();
        _computerUseCancellation?.Cancel();
        _computerUseHttpClient.Dispose();
        _meetingOperationCancellation?.Cancel();
        if (_meetingRecordingCoordinator.IsRecording)
        {
            _meetingRecordingCoordinator.PreserveForShutdownAsync().GetAwaiter().GetResult();
        }
        _meetingDetectionService.ScanCompleted -= OnMeetingDetectionScanCompleted;
        _globalHotkeyService.Dispose();
        _dictationOperationCancellation?.Cancel();
        _dictationCoordinator.DeviceListChanged -= OnDictationDeviceListChanged;
        _dictationCoordinator.RouteChanged -= OnDictationRouteChanged;
        _dictationCoordinator.LevelChanged -= OnDictationLevelChanged;
        _meetingDetectionService.Dispose();
        _meetingPromptService.Close();
        _modelLifecycle.ModelChanged -= OnTranscriptionModelChanged;
        _modelLifecycle.Dispose();
        _streamingModelLifecycle.ModelChanged -= OnStreamingModelChanged;
        _streamingModelLifecycle.Dispose();
        _dictationCoordinator.Dispose();
        _dictationOperationCancellation?.Dispose();
        _dictationOperationCancellation = null;
        _meetingRecordingCoordinator.StateChanged -= OnMeetingSessionStateChanged;
        _meetingRecordingCoordinator.HealthChanged -= OnMeetingAudioHealthChanged;
        _meetingRecordingCoordinator.LevelChanged -= OnMeetingRecordingLevelChanged;
        _meetingRecordingCoordinator.LiveTranscriptChanged -= OnLiveTranscriptChanged;
        _meetingRecordingCoordinator.LiveTranscriptionFailed -= OnLiveTranscriptionFailed;
        _meetingRecordingCoordinator.Dispose();
        _liveTranscriptWindow?.Close();
        _liveTranscriptWindow = null;
        _meetingTranscriptionClient.Dispose();
        _meetingPlaybackService.StateChanged -= OnMeetingPlaybackStateChanged;
        _meetingPlaybackService.Dispose();
        _meetingOperationCancellation?.Dispose();
        _meetingOperationCancellation = null;
        _textCleanupService.Dispose();
        _trayIconService.Dispose();
        _toastNotificationService.Dispose();
    };
}

    /// <summary>
    /// Creates the actual MainWindow XAML tree for a presentation-only Phase 12 check.
    /// This constructor deliberately assigns no-op null sentinels instead of constructing
    /// production services.  It must remain free of stores, logs, native clients, tray,
    /// hooks, registry, device, cache, network, and SystemEvents subscriptions.
    /// </summary>
    internal static MainWindow CreateVisualPreview(Phase12PreviewMode mode)
        => new(mode);

    private MainWindow(Phase12PreviewMode mode)
    {
        _isVisualPreview = true;
        _dictationTranscriptionClient = null!;
        _dictationCoordinator = null!;
        _globalHotkeyService = null!;
        _settingsStore = null!;
        _dataStore = null!;
        _toastNotificationService = null!;
        _activeAppPasteService = null!;
        _meetingTranscriptionClient = null!;
        _meetingRecordingCoordinator = null!;
        _meetingPlaybackService = null!;
        _modelLifecycle = null!;
        _streamingModelLifecycle = null!;
        _meetingDetectionService = null!;
        _meetingPromptService = null!;
        _trayIconService = null!;
        _onboardingProgressStore = null!;
        _microphoneAccessService = null!;
        _runtimeDiagnosticsService = null!;
        _postMeetingAutomationService = null!;
        _logService = null!;
        _captureStorageService = null!;
        _transcriptionBenchmarkService = null!;
        _textCleanupService = null!;
        _transcriptionPipelineService = null!;
        _computerUseHttpClient = null!;
        _computerUseTraceStore = null!;

        _theme = mode.Theme;
        _previewPageStateMessage = mode.PageStateMessage;
        _userName = "Visual verification";
        _dictationStatus = mode.PresentationStatus;
        _runtimeDiagnostics = "Preview-only diagnostics. No production diagnostic service was created.";
        _setupReadiness = mode.PresentationStatus;
        _runtimeSetupStatus = mode.PresentationStatus;
        _modelCacheDirectory = "Preview isolation: no model cache inspected.";
        _modelCacheSize = "0 B";
        _meetingDetectionStatus = "Preview isolation: meeting detection is not running.";
        _selectedHotkey = mode.Page == "shortcuts" ? "Ctrl+Shift+Space" : "F8";
        if (mode.Page == "shortcuts" && mode.Case == "conflict")
            _dictationStatus = "Preview-only shortcut conflict: Ctrl+Shift+Space is unavailable. Choose another shortcut; preview cannot register or test it.";
        if (mode.Page == "settings" && mode.Case == "startup-unavailable")
            _dictationStatus = "Preview-only startup guidance: Windows registration was not inspected or changed in this isolated process.";
        if ((mode.Page == "dashboard" || mode.Page == "meetings") && mode.Case == "long-text")
            _dictationStatus = "Preview-only wrapping verification: this deliberately long message is not a dictation, meeting, transcript, insight, statistic, or persisted user activity. Resize the real page to review readable wrapping while all history remains empty.";
        _openDashboardOnLaunch = true;

        InitializeComponent();
        FilteredDictations = CollectionViewSource.GetDefaultView(Dictations);
        FilteredMeetings = CollectionViewSource.GetDefaultView(Meetings);
        SearchDictationResults = new ListCollectionView(Dictations);
        SearchMeetingResults = new ListCollectionView(Meetings);
        PopulatePreviewModels(mode);
        DataContext = this;
        ApplyTheme(_theme);
        VisualVerificationBannerText.Text = mode.Banner;
        VisualVerificationBanner.Visibility = Visibility.Visible;
        IsCompactLayout = mode.Size == "narrow";
        ShowPreviewPage(mode.Page);
        DisableVisualPreviewActions();
    }

    private void PopulatePreviewModels(Phase12PreviewMode mode)
    {
        if (mode.Page != "models") return;
        var model = TranscriptionModels[0];
        var snapshot = mode.Case switch
        {
            "ready" => new TranscriptionModelSnapshot(model, TranscriptionModelStatus.Ready, "Preview-only ready; no cache was inspected.", 0, "0 B", "No production model service was created.", false, false, false, false, false, false),
            "downloading" => new TranscriptionModelSnapshot(model, TranscriptionModelStatus.Downloading, "Preview-only downloading: 42% (no download started).", 0, "0 B", "Cancel is visibly disabled in preview.", true, false, false, false, false, false),
            "failure" => new TranscriptionModelSnapshot(model, TranscriptionModelStatus.Failed, "Preview-only failure: retry and diagnostics are guidance only.", 0, "0 B", "No retry, log, or network action is available.", false, false, false, false, false, false),
            _ => new TranscriptionModelSnapshot(model, TranscriptionModelStatus.RuntimeUnavailable, "Preview-only offline/unavailable: connect before preparing.", 0, "0 B", "No network or model cache was inspected.", false, false, false, false, false, false)
        };
        TranscriptionModelItems.Add(new TranscriptionModelItem(snapshot));
        _selectedTranscriptionModel = model;
        _selectedFinalMeetingModel = model;
        _dictationModelRuntimeStatus = snapshot.StatusText;
    }

    private void ShowPreviewPage(string page)
    {
        switch (page)
        {
            case "meetings": ShowPage(MeetingsPage, MeetingsNav); break;
            case "search": ShowPage(SearchPage, DictationsNav); break;
            case "dictionary": ShowPage(DictionaryPage, DictionaryNav); break;
            case "models": ShowPage(ModelsPage, ModelsNav); break;
            case "shortcuts": ShowPage(ShortcutsPage, ShortcutsNav); break;
            case "settings": ShowPage(SettingsPage, SettingsNav); break;
            case "about": ShowPage(AboutPage, AboutNav); break;
            default: ShowPage(DictationsPage, DictationsNav); break;
        }
    }

    private void DisableVisualPreviewActions()
    {
        // Preview windows use the real XAML pages, but every interactive control is inert.
        // This is defence in depth alongside individual side-effect guards.
        DisablePreviewControls(this);
    }

    private static void DisablePreviewControls(DependencyObject parent)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is System.Windows.Controls.Control control)
                control.IsEnabled = false;
            DisablePreviewControls(child);
        }
    }
public void StartRuntime(bool showOnboarding)
{
    if (_runtimeStarted)
    {
        if (showOnboarding)
        {
            ShowOnboardingIfNeeded();
        }
        _meetingPromptService.Reset();
        return;
    }
    _runtimeStarted = true;
    _logService.Info("Main window runtime starting.");
    _trayIconService.Initialize(this, CreateProductExperienceState, NavigateFromTray);
    _meetingDetectionService.MeetingDetected += OnMeetingDetected;
    if (AutoMeetingDetectionEnabled)
    {
        _meetingDetectionService.Start();
    }
    if (RegisterGlobalHotkey())
    {
        _toastNotificationService.ShowIdle(SelectedHotkey);
    }
    if (showOnboarding)
    {
        ShowOnboardingIfNeeded();
    }
    _ = RefreshRuntimeDiagnosticsAsync();
    StartBackgroundUpdateCheck();
    _ = EnsureTranscriptionReadyAsync();
}
public void SetBackgroundStatus()
{
    DictationStatus = "Running in background";
}
public void ParkForBackgroundLaunch()
{
    _isParkedForBackground = true;
    ShowInTaskbar = false;
    WindowStartupLocation = WindowStartupLocation.Manual;
    WindowState = WindowState.Normal;
    Opacity = 0;
    Left = -32000;
    Top = -32000;
}
public void ShowDashboardFromBackground()
{
    _isParkedForBackground = false;
    Opacity = 1;
    ShowInTaskbar = true;
    Show();
    // Show first so placement has a real HWND/PresentationSource instead of NaN or the
    // parked (-32000) coordinates. This also keeps per-monitor DPI conversion truthful.
    FitDashboardToWorkArea();
    WindowState = WindowState.Normal;
    Activate();
    ShowOnboardingIfNeeded();
}
private void FitDashboardToWorkArea()
{
    WindowPlacementService.FitToWorkArea(this);
}
private async void HoldToDictate_MouseDown(object sender, MouseButtonEventArgs e)
{
    await StartDictationAsync(shouldPasteToActiveApp: false);
}
private void ShowOnboardingIfNeeded(bool explicitResume = false)
{
    if (_onboardingCompleted)
    {
        return;
    }

    if (_onboardingWindow is { IsVisible: true })
    {
        _onboardingWindow.Activate();
        return;
    }

    var savedProgress = _onboardingProgressStore.Load();
    if (savedProgress.Deferred && !explicitResume)
    {
        return;
    }
    if (savedProgress.Deferred)
    {
        savedProgress = savedProgress with { Deferred = false, LastStatus = "Setup resumed. Continue from the saved step." };
        _onboardingProgressStore.Save(savedProgress);
    }
    var progress = OnboardingProgressReconciler.Reconcile(
        savedProgress,
        new OnboardingSelection(SelectedMicrophone, SelectedTranscriptionModel.Id, SelectedFinalMeetingModel.Id, SelectedLiveMeetingModel.Id, SelectedHotkey),
        id => _modelLifecycle.Snapshot(id).Status is TranscriptionModelStatus.Ready or TranscriptionModelStatus.Selected,
        id => _streamingModelLifecycle.Snapshot(id).Status is TranscriptionModelStatus.Ready or TranscriptionModelStatus.Selected);
    var context = new OnboardingContext(
        MicrophoneDevices, HotkeyOptions, TranscriptionModels, LiveMeetingModels,
        new OnboardingDraft(UserName, SelectedMicrophone, SelectedHotkey, SelectedTranscriptionModel.Id, SelectedFinalMeetingModel.Id, SelectedLiveMeetingModel.Id, StartAtLogin, ShowFloatingIndicator, SelectedIndicatorPosition, SelectedSummaryProvider), _ollamaEndpoint,
        microphone => _microphoneAccessService.ProbeAsync(microphone),
        (id, progress, token) => _modelLifecycle.PrepareAsync(id, progress, token),
        (id, progress, token) => _modelLifecycle.VerifyAsync(id, progress, token),
        id => _modelLifecycle.Cancel(id),
        (id, progress, token) => _modelLifecycle.RetryAsync(id, token),
        OnboardingOfflineModelSnapshot,
        (id, progress, token) => _streamingModelLifecycle.PrepareAsync(id, progress, token),
        (id, progress, token) => _streamingModelLifecycle.VerifyAsync(id, progress, token),
        id => _streamingModelLifecycle.Cancel(id),
        (id, progress, token) => _streamingModelLifecycle.RetryAsync(id, token),
        OnboardingLiveModelSnapshot,
        RunOnboardingPipelineTestForWindowAsync,
        gesture => TestOnboardingHotkey(gesture),
        !OpenAIApiKeyStatus.Equals("Not configured", StringComparison.OrdinalIgnoreCase),
        !OpenRouterApiKeyStatus.Equals("Not configured", StringComparison.OrdinalIgnoreCase),
        IndicatorPositions,
        PreviewOnboardingIndicator,
        ResetOnboardingIndicatorPosition,
        OpenOnboardingSummaryProviderSettings,
        ApplyOnboardingDraft,
        value => _onboardingProgressStore.Save(value),
        CompleteOnboarding);
    _onboardingWindow = new OnboardingWindow(context, progress) { Owner = this };
    _onboardingWindow.Closed += (_, _) => { _onboardingWindow = null; _trayIconService.Refresh(); };
    _onboardingWindow.Show();
}

private OnboardingModelSnapshot OnboardingOfflineModelSnapshot(string modelId)
{
    var snapshot = _modelLifecycle.Snapshot(modelId);
    return new OnboardingModelSnapshot(snapshot.StatusText,
        snapshot.Status is TranscriptionModelStatus.Ready or TranscriptionModelStatus.Selected,
        snapshot.IsBusy, snapshot.CanPrepare, snapshot.CanCancel, snapshot.CanRetry, snapshot.CanVerify);
}

private OnboardingModelSnapshot OnboardingLiveModelSnapshot(string modelId)
{
    var snapshot = _streamingModelLifecycle.Snapshot(modelId);
    return new OnboardingModelSnapshot(snapshot.StatusText,
        snapshot.Status is TranscriptionModelStatus.Ready or TranscriptionModelStatus.Selected,
        snapshot.IsBusy, snapshot.CanPrepare, snapshot.CanCancel, snapshot.CanRetry, snapshot.CanVerify);
}

private void PreviewOnboardingIndicator()
{
    _toastNotificationService.SetIndicatorAnchor(SelectedIndicatorPosition, clearCustomPosition: false);
    _toastNotificationService.ShowIdle(SelectedHotkey);
}

private void ResetOnboardingIndicatorPosition()
{
    SelectedIndicatorPosition = "Top Center";
    _indicatorLeft = null;
    _indicatorTop = null;
    _toastNotificationService.SetSavedPosition(null, null);
    _toastNotificationService.SetIndicatorAnchor(SelectedIndicatorPosition, clearCustomPosition: true);
}

private void OpenOnboardingSummaryProviderSettings()
{
    _onboardingWindow?.Close();
    ShowPage(SettingsPage, SettingsNav);
}

private bool TestOnboardingHotkey(string gesture)
{
    var previous = SelectedHotkey;
    var advisoryAvailable = false;
    try { advisoryAvailable = HotkeyConflictProbe.IsAdvisoryRegistrationAvailable(gesture); }
    catch (Exception exception)
    {
        _logService.Error("Onboarding shortcut advisory probe failed; continuing with the decisive hook test.", exception);
    }

    // RegisterGlobalHotkey is the same low-level hook used by dictation.  The advisory
    // RegisterHotKey probe is deliberately supplemental: it cannot veto that real test.
    _selectedHotkey = gesture;
    OnPropertyChanged(nameof(SelectedHotkey));
    var result = HotkeyCandidateHookTest.Run(
        RegisterGlobalHotkey,
        () =>
        {
            _selectedHotkey = previous;
            OnPropertyChanged(nameof(SelectedHotkey));
            return RegisterGlobalHotkey();
        });

    if (!result.PreviousHookRestored)
    {
        var exception = result.RestorationException ?? new InvalidOperationException("The previous shortcut hook could not be restored.");
        _logService.Error("Onboarding shortcut hook restoration failed after candidate test.", exception);
    }
    if (result.CandidateRegistered)
        DictationStatus = advisoryAvailable
            ? $"Shortcut {gesture} passed the actual dictation-hook test; the advisory probe also found no common conflict."
            : $"Shortcut {gesture} passed the actual dictation-hook test. Windows advisory registration was unavailable, but did not veto the real hook.";
    return result.CandidateRegistered;
}

private async Task<string> RunOnboardingPipelineTestForWindowAsync(CancellationToken cancellationToken)
{
    cancellationToken.ThrowIfCancellationRequested();
    if (_dictationCoordinator.IsBusy || _dictationCoordinator.IsRecording)
        throw new InvalidOperationException("Dictation is already active.");
    if (!_dictationCoordinator.IsModelReady)
        throw new InvalidOperationException($"{ActiveModelLabel} must be prepared before this test.");
    try
    {
        DictationStatus = "Recording a local setup test";
        await _dictationCoordinator.StartAsync(SelectedMicrophone);
        await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken);
        var result = await _dictationCoordinator.StopForOnboardingTestAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return result.Text?.Trim() ?? "";
    }
    catch
    {
        if (_dictationCoordinator.IsRecording) await _dictationCoordinator.CancelAsync();
        throw;
    }
}

private void ApplyOnboardingDraft(OnboardingDraft draft)
{
    UserName = draft.UserName;
    SelectedMicrophone = draft.Microphone;
    SelectedHotkey = draft.Hotkey;
    SelectedTranscriptionModel = TranscriptionModelCatalog.Get(draft.DictationModelId);
    SelectedFinalMeetingModel = TranscriptionModelCatalog.Get(draft.FinalModelId);
    SelectedLiveMeetingModel = LiveMeetingModels.FirstOrDefault(item => item.Id == draft.LiveModelId) ?? LiveModelChoice.Off;
    StartAtLogin = draft.StartAtLogin;
    ShowFloatingIndicator = draft.ShowIndicator;
    SelectedIndicatorPosition = draft.IndicatorPosition;
    SelectedSummaryProvider = draft.SummaryProvider;
    SaveSettings(); // Settings are authoritative; progress is saved after this delegate returns.
}

private void CompleteOnboarding()
{
    _onboardingCompleted = true;
    SaveSettings();
    _onboardingProgressStore.Clear();
    DictationStatus = "Setup completed";
    _trayIconService.Refresh();
    OnPropertyChanged(nameof(SetupNeedsResume));
    OnPropertyChanged(nameof(SetupResumeLabel));
}

private ProductExperienceState CreateProductExperienceState()
{
    var progress = _onboardingProgressStore.Load();
    return new ProductExperienceState(
        !_onboardingCompleted || progress.Deferred,
        _onboardingCompleted ? "Setup complete" : "Resume setup",
        StartupRegistrationState.FromWindows(),
        Dictations.OrderByDescending(item => item.Timestamp).Take(6).Select(item => new ProductHistoryEntry("Dictation", item.Timestamp)).ToList(),
        Meetings.OrderByDescending(item => item.CreatedAt).Take(6).Select(item => new ProductHistoryEntry(item.Title, new DateTimeOffset(item.CreatedAt))).ToList(),
        _lastMeetingDetectionScan?.Found == true ? _lastMeetingDetectionScan.DetectedMeeting?.Platform ?? "Meeting detected" : "No meeting detected");
}

private void NavigateFromTray(string destination)
{
    switch (destination)
    {
        case "resume": ShowOnboardingIfNeeded(explicitResume: true); break;
        case "tour": ShowFeatureTour(); break;
        case "dictations": ShowPage(DictationsPage, DictationsNav); break;
        case "meetings": ShowPage(MeetingsPage, MeetingsNav); break;
        case "settings": ShowPage(SettingsPage, SettingsNav); break;
        case "about": ShowPage(AboutPage, AboutNav); break;
    }
}

private async void HoldToDictate_MouseUp(object sender, MouseButtonEventArgs e)
{
    await StopDictationAsync();
}
private void CaptureHotkey_Click(object sender, RoutedEventArgs e)
{
    _isCapturingHotkey = true;
    OnPropertyChanged(nameof(CaptureHotkeyButtonText));
    OnPropertyChanged(nameof(ShortcutCaptureLabel));
    DictationStatus = "Press a new dictation shortcut";
    Focus();
}
private void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
{
    if (!_isCapturingHotkey)
    {
        if (e.Key == Key.Escape && !string.IsNullOrWhiteSpace(SearchQuery))
        {
            SearchQuery = "";
            e.Handled = true;
        }
        return;
    }
    e.Handled = true;
    if (e.Key == Key.Escape)
    {
        StopHotkeyCapture("Shortcut capture cancelled");
        return;
    }
    var label = BuildCapturedHotkeyLabel(e);
    if (label is null)
    {
        DictationStatus = "Press a function key or include Ctrl, Alt, Shift, or Win";
        return;
    }
    AddHotkeyOptionIfMissing(label);
    SelectedHotkey = label;
    StopHotkeyCapture($"Shortcut set to {label}");
}
private void StopHotkeyCapture(string status)
{
    _isCapturingHotkey = false;
    DictationStatus = status;
    OnPropertyChanged(nameof(CaptureHotkeyButtonText));
    OnPropertyChanged(nameof(ShortcutCaptureLabel));
}
private static string? BuildCapturedHotkeyLabel(System.Windows.Input.KeyEventArgs e)
{
    var key = e.Key == Key.System ? e.SystemKey : e.Key == Key.ImeProcessed ? e.ImeProcessedKey : e.Key;
    if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
    {
        return null;
    }
    var modifiers = Keyboard.Modifiers;
    var parts = new List<string>();
    if (modifiers.HasFlag(ModifierKeys.Control))
    {
        parts.Add("Ctrl");
    }
    if (modifiers.HasFlag(ModifierKeys.Alt))
    {
        parts.Add("Alt");
    }
    if (modifiers.HasFlag(ModifierKeys.Shift))
    {
        parts.Add("Shift");
    }
    if (modifiers.HasFlag(ModifierKeys.Windows))
    {
        parts.Add("Win");
    }
    if (key is not (>= Key.F1 and <= Key.F24) && parts.Count == 0)
    {
        return null;
    }
    parts.Add(key == Key.Space ? "Space" : key.ToString());
    return string.Join("+", parts);
}
private Task StartHotkeyDictationAsync()
{
    return StartDictationAsync(shouldPasteToActiveApp: true);
}
private bool RegisterGlobalHotkey()
{
    try
    {
        _globalHotkeyService.Register(
            SelectedHotkey,
            () => Dispatcher.InvokeAsync(HandleHotkeyDownAsync),
            () => Dispatcher.InvokeAsync(HandleHotkeyUpAsync),
            () => _dictationCoordinator.IsRecording || _dictationCoordinator.IsTranscribing || _dictationOperationCancellation is not null,
            () => Dispatcher.InvokeAsync(CancelDictationAsync));
        return true;
    }
    catch (Exception exception)
    {
        DictationStatus = $"Hotkey failed: {exception.Message}";
        _logService.Error("Global hotkey registration failed.", exception);
        _toastNotificationService.Show("Hotkey failed", exception.Message, ToastState.Error, 4200);
        return false;
    }
}
private string NormalizeHotkey(string? value, bool allowCustom = false)
{
    if (string.IsNullOrWhiteSpace(value))
    {
        return "F8";
    }
    var compact = value.Replace(" ", "", StringComparison.OrdinalIgnoreCase);
    var preset = HotkeyOptions.FirstOrDefault(option =>
        option.Replace(" ", "", StringComparison.OrdinalIgnoreCase)
            .Equals(compact, StringComparison.OrdinalIgnoreCase));
    if (preset is not null)
    {
        return preset;
    }
    return allowCustom && TryNormalizeCustomHotkey(value, out var custom) ? custom : "F8";
}
private static bool TryNormalizeCustomHotkey(string value, out string normalized)
{
    normalized = "";
    try
    {
        var gesture = HotkeyGesture.Parse(value);
        if (gesture.Key == Key.Escape ||
            (gesture.Key is not (>= Key.F1 and <= Key.F24) && gesture.Modifiers == ModifierKeys.None))
        {
            return false;
        }
        normalized = gesture.Label;
        return true;
    }
    catch (InvalidOperationException)
    {
        return false;
    }
}
private void AddHotkeyOptionIfMissing(string hotkey)
{
    if (!HotkeyOptions.Any(option => option.Equals(hotkey, StringComparison.OrdinalIgnoreCase)))
    {
        HotkeyOptions.Add(hotkey);
    }
}
private async Task HandleHotkeyDownAsync()
{
    await ExecuteHotkeyActionAsync(_dictationHotkeyState.KeyDown(EnableDoubleTapDictation));
}
private async Task HandleHotkeyUpAsync()
{
    await ExecuteHotkeyActionAsync(_dictationHotkeyState.KeyUp(EnableDoubleTapDictation));
}

private async Task ExecuteHotkeyActionAsync(DictationHotkeyAction action)
{
    // Planner voice capture has a separate explicit activation provenance. The ordinary dictation
    // hotkey must never stop, transcribe, persist, paste, or execute planner audio.
    if (_computerUseVoiceCaptureActive || _computerUseIsRunning)
    {
        return;
    }
    switch (action)
    {
        case DictationHotkeyAction.StartRecording:
            await StartHotkeyDictationAsync();
            if (!_dictationCoordinator.IsRecording)
            {
                ResetHotkeyDictationState();
            }
            break;
        case DictationHotkeyAction.StopRecording:
            _hotkeyReleaseTimer.Stop();
            await StopDictationAsync();
            break;
        case DictationHotkeyAction.StartDoubleTapTimer:
            _hotkeyReleaseTimer.Stop();
            _hotkeyReleaseTimer.Start();
            break;
        case DictationHotkeyAction.EnterHandsFree:
            _hotkeyReleaseTimer.Stop();
            DictationStatus = "Hands-free dictation active";
            _toastNotificationService.Show("Recording", "Click the square or tap the shortcut to stop", ToastState.Recording, 0);
            break;
    }
}
private async Task StartDictationAsync(bool shouldPasteToActiveApp)
{
    if (_dictationCoordinator.IsRecording || _dictationCoordinator.IsBusy)
    {
        return;
    }
    try
    {
        _shouldPasteToActiveApp = shouldPasteToActiveApp;
        _pasteTargetWindow = shouldPasteToActiveApp
            ? _activeAppPasteService.CaptureForegroundWindow()
            : IntPtr.Zero;
        _pasteTargetInfo = shouldPasteToActiveApp
            ? _activeAppPasteService.DescribeWindow(_pasteTargetWindow)
            : PasteTargetInfo.Unknown;
        DictationStatus = "Listening";
        _toastNotificationService.Show("Recording", $"Hold {SelectedHotkey} or the button while speaking", ToastState.Recording, 0);
        await _dictationCoordinator.StartAsync(SelectedMicrophone);
        DictationStatus = _dictationCoordinator.IsRecording ? "Listening" : "Ready";
    }
    catch (Exception exception)
    {
        DictationStatus = $"Could not start microphone: {exception.Message}";
        _logService.Error("Could not start dictation microphone.", exception);
        _toastNotificationService.Show("Microphone failed", exception.Message, ToastState.Error);
    }
}
private async Task StopDictationAsync()
{
    if (_computerUseVoiceCaptureActive || _computerUseIsRunning)
    {
        return;
    }
    if (!_dictationCoordinator.IsRecording || _dictationCoordinator.IsBusy)
    {
        return;
    }

    ResetHotkeyDictationState();
    var traceId = Guid.NewGuid().ToString("N")[..12];
    var releaseToPasteStarted = Stopwatch.StartNew();
    DictationStopResult? stopResult = null;
    var operationCancellation = new CancellationTokenSource();
    _dictationOperationCancellation = operationCancellation;

    TranscriptionResult result;
    try
    {
        DictationStatus = "Transcribing";
        _toastNotificationService.Show("Transcribing", "Processing local audio", ToastState.Transcribing, 0);
        stopResult = await _dictationCoordinator.StopAsync(traceId, operationCancellation.Token);
        result = stopResult.Transcription;
        _transcriptionPipelineService.LogTranscriptionResult(
            "dictation", result, _dictationCoordinator.EngineId, _dictationCoordinator.ModelId);
    }
    catch (OperationCanceledException) when (operationCancellation.IsCancellationRequested)
    {
        CompleteDictationOperationCancellation(operationCancellation);
        _pasteTargetWindow = IntPtr.Zero;
        _shouldPasteToActiveApp = false;
        DictationStatus = "Dictation cancelled";
        _logService.Info($"Dictation cancelled. trace={traceId}; stage=transcription; transcriptPersisted=false; transcriptDelivered=false");
        _toastNotificationService.ShowIdle(SelectedHotkey);
        return;
    }
    catch (Exception exception)
    {
        CompleteDictationOperationCancellation(operationCancellation);
        if (_dictationCoordinator.IsRecording)
        {
            await _dictationCoordinator.CancelAsync();
        }

        DictationStatus = $"Dictation failed: {exception.Message}";
        _logService.Error("Dictation transcription failed.", exception);
        _toastNotificationService.Show("Dictation failed", exception.Message, ToastState.Error);
        return;
    }
    CompleteDictationOperationCancellation(operationCancellation);
    var textToUse = "";
    var blankAudioDetected = false;
    var cleanupStarted = Stopwatch.StartNew();
    if (!string.IsNullOrWhiteSpace(result.Text))
    {
        if (result.Text.Contains("[BLANK_AUDIO]", StringComparison.OrdinalIgnoreCase) ||
            result.Text.Equals("BLANK_AUDIO", StringComparison.OrdinalIgnoreCase))
        {
            blankAudioDetected = true;
            _logService.Info($"Blank audio detected. Diagnostic: {result.Diagnostic}");
        }
        else
        {
            textToUse = await _transcriptionPipelineService.PrepareDictationTextAsync(
                result.Text,
                enableCleanup: false,
                removeFillerWords: RemoveFillerWords,
                DictionaryEntries.Select(entry => entry.Record));
        }
    }
    cleanupStarted.Stop();
    if (textToUse.Length > 0)
    {
        // Commit the transcript before interacting with another process or the clipboard.
        // A focus/input/clipboard failure can then never make successful ASR text unrecoverable.
        var persistenceStarted = Stopwatch.StartNew();
        Dictations.Insert(0, new DictationItem(
            $"dict_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}",
            DateTime.Now,
            DateTime.Now.ToString("hh:mm tt"),
            textToUse,
            _dictationCoordinator.ModelId,
            result.DurationMs));
        var persistenceSucceeded = true;
        try
        {
            SaveDictations();
        }
        catch (Exception persistenceException)
        {
            persistenceSucceeded = false;
            _logService.Error("Dictation history save failed; keeping the transcript in the current dashboard and continuing delivery.", persistenceException);
        }
        OnPropertyChanged(nameof(DayStreak));
        OnPropertyChanged(nameof(WordsDictated));
        OnPropertyChanged(nameof(WordsDictatedDisplay));
        OnPropertyChanged(nameof(AverageWpm));
        RefreshSearchResults();
        persistenceStarted.Stop();

        var deliveryStarted = Stopwatch.StartNew();
        PasteOperationResult pasteResult;
        var deliveryMode = "clipboard";
        var deliverySucceeded = true;
        try
        {
            if (_shouldPasteToActiveApp && SelectedPasteBehavior == "active-app")
            {
                deliveryMode = "active-app";
                pasteResult = await _activeAppPasteService.PasteTextAsync(textToUse, _pasteTargetWindow);
                DictationStatus = persistenceSucceeded ? "Pasted" : "Pasted; history save failed";
                _toastNotificationService.Show(
                    persistenceSucceeded ? "Pasted" : "Pasted; history save failed",
                    persistenceSucceeded ? "Transcript delivered to the original app" : "The delivered text remains visible in this dashboard",
                    persistenceSucceeded ? ToastState.Success : ToastState.Error);
            }
            else
            {
                var clipboardMs = await _activeAppPasteService.CopyTextAsync(textToUse);
                pasteResult = new PasteOperationResult(
                    clipboardMs,
                    clipboardMs,
                    0,
                    0,
                    false);
                DictationStatus = persistenceSucceeded ? "Copied" : "Copied; history save failed";
                _toastNotificationService.Show(
                    persistenceSucceeded ? "Copied" : "Copied; history save failed",
                    persistenceSucceeded ? "Transcript copied to clipboard" : "The copied text remains visible in this dashboard",
                    persistenceSucceeded ? ToastState.Success : ToastState.Error);
            }

            deliveryStarted.Stop();
        }
        catch (Exception exception)
        {
            deliveryStarted.Stop();
            deliverySucceeded = false;
            var fallbackCopied = false;
            long fallbackClipboardMs = 0;
            try
            {
                fallbackClipboardMs = await _activeAppPasteService.CopyTextAsync(textToUse);
                fallbackCopied = true;
            }
            catch (Exception clipboardException)
            {
                _logService.Error("Clipboard fallback failed; the transcript remains in dictation history.", clipboardException);
            }
            pasteResult = new PasteOperationResult(
                deliveryStarted.ElapsedMilliseconds,
                fallbackClipboardMs,
                0,
                0,
                false);
            DictationStatus = fallbackCopied
                ? persistenceSucceeded
                    ? "Paste failed; transcript copied and saved in history"
                    : "Paste failed; transcript copied and visible in the dashboard"
                : persistenceSucceeded
                    ? "Paste and clipboard failed; transcript saved in history"
                    : "Delivery and history save failed; transcript remains visible in the dashboard";
            _logService.Error("Active-app paste failed after successful dictation.", exception);
            _toastNotificationService.Show(
                "Dictation saved",
                fallbackCopied ? "Paste failed; copied to clipboard" : "Open dictation history to recover it",
                ToastState.Error);
        }

        var releaseToPasteMs = releaseToPasteStarted.ElapsedMilliseconds;
        releaseToPasteStarted.Stop();

        var latency = stopResult?.Latency ??
                      new DictationLatencyMetrics(traceId, 0, 0, 0, 0, "unavailable", 0, 0);
        _logService.Info(
            $"Dictation latency trace. trace={traceId}; status={(deliverySucceeded && persistenceSucceeded ? "success" : !persistenceSucceeded ? "history-failed" : "paste-failed")}; engine={_dictationCoordinator.EngineId}; model={_dictationCoordinator.ModelId}; audioDurationMs={result.DurationMs}; chars={textToUse.Length}; releaseToPasteMs={releaseToPasteMs}; releaseToUiSettledMs={releaseToPasteStarted.ElapsedMilliseconds}; captureTotalMs={latency.CaptureTotalMs}; captureStopDisposeMs={latency.CaptureStopDisposeMs}; captureFlushWaitMs={latency.CaptureFlushWaitMs}; capturePreparationMs={latency.CapturePreparationMs}; capturePreparation={TraceValue(latency.CapturePreparation)}; transcriptionWallMs={latency.TranscriptionWallMs}; coordinatorTotalMs={latency.CoordinatorTotalMs}; cleanupDictionaryMs={cleanupStarted.ElapsedMilliseconds}; fillerRemoval={RemoveFillerWords}; deliveryMode={deliveryMode}; deliveryMs={deliveryStarted.ElapsedMilliseconds}; clipboardMs={pasteResult.ClipboardMs}; focusWaitMs={pasteResult.FocusWaitMs}; inputMs={pasteResult.InputMs}; targetForeground={pasteResult.TargetWasForeground}; targetProcess={TraceValue(_pasteTargetInfo.ProcessName)}; targetProcessId={_pasteTargetInfo.ProcessId}; historyPersisted={persistenceSucceeded}; persistenceUiMs={persistenceStarted.ElapsedMilliseconds}");
    }
    else
    {
        releaseToPasteStarted.Stop();
        var diagnostic = blankAudioDetected
            ? "Switch microphone to System default or your headset mic."
            : FirstDiagnosticLine(result.Diagnostic);
        DictationStatus = blankAudioDetected
            ? "No voice detected. Check microphone input."
            : string.IsNullOrWhiteSpace(diagnostic) ? "No speech detected" : $"No speech detected. {diagnostic}";
        _logService.Info($"No speech detected. Diagnostic: {result.Diagnostic}");
        _logService.Info(
            $"Dictation latency trace. trace={traceId}; status=no-speech; engine={_dictationCoordinator.EngineId}; model={_dictationCoordinator.ModelId}; audioDurationMs={result.DurationMs}; chars=0; releaseToPasteMs=0; releaseToUiSettledMs={releaseToPasteStarted.ElapsedMilliseconds}; captureTotalMs={stopResult?.Latency.CaptureTotalMs ?? 0}; transcriptionWallMs={stopResult?.Latency.TranscriptionWallMs ?? 0}; cleanupDictionaryMs={cleanupStarted.ElapsedMilliseconds}");
        _toastNotificationService.Show(blankAudioDetected ? "No voice detected" : "No speech detected", diagnostic, ToastState.Error, 3600);
    }
}
private async Task StopActiveRecordingFromIndicatorAsync()
{
    await Dispatcher.InvokeAsync(async () =>
    {
        if (_computerUseVoiceCaptureActive)
        {
            await StopComputerUseVoiceCaptureAndRunAsync();
            return;
        }
        if (_isMeetingRecording)
        {
            await ToggleMeetingRecordingAsync(null);
            return;
        }
        await StopDictationAsync();
    }).Task.Unwrap();
}
private async Task CancelActiveRecordingFromIndicatorAsync()
{
    await Dispatcher.InvokeAsync(async () =>
    {
        if (_computerUseVoiceCaptureActive || _computerUseIsRunning)
        {
            await CancelComputerUseAsync();
            return;
        }
        if (_isMeetingRecording)
        {
            await CancelMeetingRecordingAsync();
            return;
        }
        if (_meetingRecordingCoordinator.State is MeetingSessionState.Stopping or MeetingSessionState.Finalizing)
        {
            _meetingOperationCancellation?.Cancel();
            return;
        }
        await CancelDictationAsync();
    }).Task.Unwrap();
}

private async void ComputerUseVoice_Click(object sender, RoutedEventArgs e)
{
    if (_computerUseVoiceCaptureActive)
    {
        await StopComputerUseVoiceCaptureAndRunAsync();
        return;
    }
    await StartComputerUseVoiceCaptureAsync();
}

private async Task StartComputerUseVoiceCaptureAsync()
{
    var configurationReady = ComputerUseConfigurationIsReady(out var error);
    if (!ComputerUseEnabled || !configurationReady)
    {
        ComputerUseStatusText = error.Length == 0 ? "Enable Computer Use before starting a planner session." : error;
        return;
    }
    if (_computerUseIsRunning || _dictationCoordinator.IsRecording || _dictationCoordinator.IsBusy || _isMeetingRecording)
    {
        ComputerUseStatusText = "Finish the active recording or planner operation first.";
        return;
    }

    _computerUseApprovedTarget = null;
    _computerUsePlannerService = CreateComputerUsePlannerService();
    _computerUseActivationToken = _computerUsePlannerService.BeginExplicitVoicePlannerActivation();
    _computerUseCancellation?.Dispose();
    _computerUseCancellation = CancellationTokenSource.CreateLinkedTokenSource(_applicationShutdownCancellation.Token);
    try
    {
        await _dictationCoordinator.StartAsync(SelectedMicrophone);
        _computerUseVoiceCaptureActive = _dictationCoordinator.IsRecording;
        NotifyComputerUseActivityChanged();
        if (!_computerUseVoiceCaptureActive)
        {
            throw new InvalidOperationException("The microphone did not enter the recording state.");
        }
        ComputerUseStatusText = "Planner listening. Focus an allowed target app, then use the floating stop control.";
        _toastNotificationService.Show("Computer Use listening", "Focus an allowed app, then click the square to plan", ToastState.Recording, 0);
        WindowState = WindowState.Minimized;
    }
    catch (Exception exception)
    {
        ResetComputerUseSession();
        ComputerUseStatusText = $"Could not start planner voice capture ({exception.GetType().Name}).";
        _logService.Info("Computer Use voice capture could not start; commandLogged=false.");
        _toastNotificationService.Show("Computer Use unavailable", "Voice capture could not start", ToastState.Error, 4200);
    }
}

private async Task StopComputerUseVoiceCaptureAndRunAsync()
{
    if (!_computerUseVoiceCaptureActive || _computerUsePlannerService is null || _computerUseActivationToken is null)
        return;

    // Capture only the single foreground target selected by the user while Muesli is minimized.
    // No clipboard, window enumeration, title, or field value is acquired here.
    var targetHandle = _activeAppPasteService.CaptureForegroundWindow();
    var targetInfo = _activeAppPasteService.DescribeWindow(targetHandle);
    var applicationId = targetInfo.ProcessName?.Trim().ToLowerInvariant() ?? "";
    var allowedApplicationsAtSelection = ParseAllowlist(ComputerUseAllowedApplications);
    var targetWasAllowlistedAtSelection = targetHandle != IntPtr.Zero && applicationId.Length > 0 &&
        allowedApplicationsAtSelection.Contains(applicationId, StringComparer.OrdinalIgnoreCase);
    ComputerUseWindowTarget? capturedTarget = null;
    var targetIdentityCapturedAtSelection = targetWasAllowlistedAtSelection &&
        ComputerUseWindowTarget.TryCapture(targetHandle, applicationId, targetInfo.ProcessId, out capturedTarget);
    _computerUseVoiceCaptureActive = false;
    _computerUseIsRunning = true;
    NotifyComputerUseActivityChanged();

    try
    {
        ComputerUseStatusText = "Transcribing the explicit planner command locally.";
        _toastNotificationService.Show("Computer Use", "Transcribing the explicit command", ToastState.Transcribing, 0);
        var stopResult = await _dictationCoordinator.StopAsync(
            $"computer-use-{Guid.NewGuid():N}",
            _computerUseCancellation?.Token ?? _applicationShutdownCancellation.Token);
        var transcript = stopResult.Transcription.Text?.Trim() ?? "";
        if (transcript.Length == 0)
        {
            ComputerUseStatusText = "No planner command was detected; nothing was executed.";
            return;
        }
        if (!targetWasAllowlistedAtSelection)
        {
            ComputerUseStatusText = "The selected foreground application is not allowlisted; nothing was sent to the planner.";
            return;
        }
        if (!targetIdentityCapturedAtSelection || capturedTarget is null || !capturedTarget.IsCurrentOwner())
        {
            ComputerUseStatusText = "The selected application window changed before it could be approved; nothing was sent to the planner.";
            return;
        }
        _computerUseApprovedTarget = capturedTarget;
        if (!_computerUsePlannerService.TryCreateExplicitVoiceCommand(_computerUseActivationToken, transcript, out var command) || command is null)
        {
            ComputerUseStatusText = "The explicit planner activation expired or was already consumed; nothing was executed.";
            return;
        }

        ComputerUseStatusText = "Planning from the approved foreground window.";
        var result = await _computerUsePlannerService.RunAsync(
            command,
            CurrentComputerUseOptions(),
            _computerUseCancellation?.Token ?? _applicationShutdownCancellation.Token);
        try
        {
            _computerUseTraceStore.Append(ComputerUseTracePath(), result);
        }
        catch (Exception)
        {
            _logService.Info($"Computer Use trace persistence failed. run={result.RunId}; commandLogged=false; valuesLogged=false.");
        }
        ComputerUseStatusText = DescribeComputerUseResult(result);
        _logService.Info($"Computer Use completed. run={result.RunId}; status={result.Status}; actions={result.Trace.Count}; commandLogged=false; valuesLogged=false.");
    }
    catch (OperationCanceledException)
    {
        ComputerUseStatusText = "Computer Use was stopped; no further actions were sent.";
    }
    catch (Exception exception)
    {
        ComputerUseStatusText = $"Computer Use failed safely ({exception.GetType().Name}); no further actions were sent.";
        _logService.Info("Computer Use failed safely; commandLogged=false; valuesLogged=false.");
    }
    finally
    {
        ResetComputerUseSession();
        _toastNotificationService.ShowIdle(SelectedHotkey);
        if (IsVisible)
        {
            WindowState = WindowState.Normal;
            Activate();
        }
    }
}

private async void StopComputerUse_Click(object sender, RoutedEventArgs e) => await CancelComputerUseAsync();

private async Task CancelComputerUseAsync()
{
    _computerUseCancellation?.Cancel();
    if (_computerUseVoiceCaptureActive && _dictationCoordinator.IsRecording && !_dictationCoordinator.IsBusy)
    {
        await _dictationCoordinator.CancelAsync();
    }
    ResetComputerUseSession();
    ComputerUseStatusText = "Computer Use stopped; the planner session was discarded.";
    _toastNotificationService.ShowIdle(SelectedHotkey);
}

private void OpenComputerUseDiagnostics_Click(object sender, RoutedEventArgs e)
{
    var document = _computerUseTraceStore.Load(ComputerUseTracePath());
    var rows = document.Runs.Count == 0
        ? "No Computer Use runs are recorded."
        : string.Join(Environment.NewLine, document.Runs.Reverse().Take(20).Select(run =>
            $"{run.CompletedAtUtc.LocalDateTime:g}  {run.Status}  actions={run.Actions.Count}  run={run.RunId}"));
    System.Windows.MessageBox.Show(
        $"Schema: {document.SchemaVersion}\nStored runs: {document.Runs.Count}\n\n{rows}\n\nCommands, typed values, titles, URLs, element names, screenshots, provider bodies, and errors are never stored.",
        "Computer Use diagnostics",
        MessageBoxButton.OK,
        MessageBoxImage.Information);
}

private void ResetComputerUseSession()
{
    _computerUseVoiceCaptureActive = false;
    _computerUseIsRunning = false;
    _computerUseActivationToken = null;
    _computerUsePlannerService = null;
    _computerUseApprovedTarget = null;
    _computerUseCancellation?.Dispose();
    _computerUseCancellation = null;
    NotifyComputerUseActivityChanged();
}

private void NotifyComputerUseActivityChanged()
{
    OnPropertyChanged(nameof(ComputerUseVoiceButtonText));
    OnPropertyChanged(nameof(ComputerUseStopEnabled));
}

private async Task CancelMeetingRecordingAsync()
{
    StopMeetingAutoStopMonitor();
    _meetingOperationCancellation?.Cancel();
    await _meetingRecordingCoordinator.CancelAsync();
    _currentMeetingTitle = null;
    _isMeetingRecording = false;
    _liveTranscriptWindow?.Hide();
    _meetingAutoStopTracker = null;
    OnPropertyChanged(nameof(MeetingRecordingButtonText));
    DictationStatus = "Meeting recording cancelled";
    _toastNotificationService.ShowIdle(SelectedHotkey);
    RefreshRecoverableMeetingSessions();
}

private async Task CancelDictationAsync()
{
    if (_computerUseVoiceCaptureActive || _computerUseIsRunning)
    {
        await CancelComputerUseAsync();
        return;
    }
    var operationCancellation = _dictationOperationCancellation;
    var hadRecording = _dictationCoordinator.IsRecording;
    if (operationCancellation is not null)
    {
        operationCancellation.Cancel();
    }

    if (_dictationCoordinator.IsRecording && !_dictationCoordinator.IsBusy)
    {
        await _dictationCoordinator.CancelAsync();
    }

    if (operationCancellation is null && !hadRecording)
    {
        return;
    }

    ResetHotkeyDictationState();
    _pasteTargetWindow = IntPtr.Zero;
    _pasteTargetInfo = PasteTargetInfo.Unknown;
    _shouldPasteToActiveApp = false;
    DictationStatus = "Dictation cancelled";
    _toastNotificationService.ShowIdle(SelectedHotkey);
}

private void CompleteDictationOperationCancellation(CancellationTokenSource cancellation)
{
    if (ReferenceEquals(_dictationOperationCancellation, cancellation))
    {
        _dictationOperationCancellation = null;
    }
    cancellation.Dispose();
}

private async void HotkeyReleaseTimer_Tick(object? sender, EventArgs e)
{
    _hotkeyReleaseTimer.Stop();
    await ExecuteHotkeyActionAsync(_dictationHotkeyState.DoubleTapWindowElapsed());
}
private void ResetHotkeyDictationState()
{
    _hotkeyReleaseTimer.Stop();
    _dictationHotkeyState.Reset();
}

private void OnDictationDeviceListChanged(object? sender, EventArgs e)
{
    Dispatcher.BeginInvoke(() =>
    {
        var selected = SelectedMicrophone;
        var devices = _dictationCoordinator.ListMicrophones();
        MicrophoneDevices.Clear();
        foreach (var device in devices)
        {
            MicrophoneDevices.Add(device);
        }
        if (!string.IsNullOrWhiteSpace(selected) && !MicrophoneDevices.Contains(selected))
        {
            MicrophoneDevices.Add(selected);
        }
        _selectedMicrophone = selected ?? AudioCaptureService.SystemDefaultMicrophone;
        OnPropertyChanged(nameof(SelectedMicrophone));
    });
}

private void OnDictationRouteChanged(object? sender, AudioRouteChangedEventArgs e)
{
    Dispatcher.BeginInvoke(() =>
    {
        DictationStatus = e.Message;
        if (e.Kind == AudioRouteChangeKind.Failed)
        {
            _logService.Error(
                $"Dictation microphone route recovery failed. previous={TraceValue(e.PreviousDevice)}; current={TraceValue(e.CurrentDevice)}",
                e.Exception ?? new InvalidOperationException(e.Message));
            _toastNotificationService.Show("Microphone disconnected", "Recording stopped; release the shortcut to recover captured audio", ToastState.Error, 0);
            return;
        }

        _logService.Info(
            $"Dictation microphone route changed. kind={e.Kind}; previous={TraceValue(e.PreviousDevice)}; current={TraceValue(e.CurrentDevice)}");
        _toastNotificationService.Show("Microphone route changed", e.Message, ToastState.Recording, 0);
    });
}

private void OnDictationLevelChanged(object? sender, AudioLevelEventArgs e) =>
    _toastNotificationService.UpdateRecordingLevel(e.Peak);

private async void TestMic_Click(object sender, RoutedEventArgs e)
{
    if (_dictationCoordinator.IsBusy || _dictationCoordinator.IsRecording)
    {
        return;
    }
    try
    {
        DictationStatus = "Testing microphone for 2 seconds";
        _toastNotificationService.Show("Testing microphone", SelectedMicrophone ?? "Selected microphone", ToastState.Recording, 0);
        await _dictationCoordinator.StartAsync(SelectedMicrophone);
        await Task.Delay(2000);
        var result = await _dictationCoordinator.StopAsync();
        var diagnostic = FirstDiagnosticLine(result.Diagnostic);
        DictationStatus = string.IsNullOrWhiteSpace(diagnostic) ? "Mic test completed" : diagnostic;
        _toastNotificationService.Show("Mic test completed", DictationStatus, ToastState.Success, 3600);
    }
    catch (Exception exception)
    {
        DictationStatus = $"Mic test failed: {exception.Message}";
        _toastNotificationService.Show("Mic test failed", exception.Message, ToastState.Error);
    }
}
private void CopyDictation_Click(object sender, RoutedEventArgs e)
{
    if (sender is FrameworkElement { DataContext: DictationItem item })
    {
        System.Windows.Clipboard.SetText(item.Text);
        DictationStatus = "Copied";
        _toastNotificationService.Show("Copied", item.Text, ToastState.Success);
    }
}
private async void DictationRow_Click(object sender, MouseButtonEventArgs e)
{
    // Ignore clicks on buttons (Copy/Delete icons)
    if (e.OriginalSource is System.Windows.Controls.Button or System.Windows.Controls.Image)
        return;
    if (sender is not FrameworkElement { DataContext: DictationItem item })
        return;
    if (sender is not System.Windows.DependencyObject dep)
        return;

    // Visual feedback: highlight text
    if (FindVisualChild<System.Windows.Controls.TextBox>(dep) is { } textBox)
    {
        textBox.Focus();
        textBox.SelectAll();
    }

    // Copy to clipboard
    System.Windows.Clipboard.SetText(item.Text);
    DictationStatus = "Copied to clipboard";
    _toastNotificationService.Show("Copied", "Dictation copied to clipboard", ToastState.Success, 2000);

    // Remove highlight after brief delay
    await Task.Delay(300);
    if (FindVisualChild<System.Windows.Controls.TextBox>(dep) is { } tb)
    {
        tb.Select(0, 0);
    }
}
private void DeleteDictation_Click(object sender, RoutedEventArgs e)
{
    if (sender is FrameworkElement { DataContext: DictationItem item })
    {
        Dictations.Remove(item);
        SaveDictations(afterExplicitDeletion: true);
        OnPropertyChanged(nameof(DayStreak));
        OnPropertyChanged(nameof(WordsDictated));
        OnPropertyChanged(nameof(WordsDictatedDisplay));
        OnPropertyChanged(nameof(AverageWpm));
        RefreshSearchResults();
        DictationStatus = "Deleted dictation";
    }
}
private void CopyMeeting_Click(object sender, RoutedEventArgs e)
{
    if (sender is FrameworkElement { DataContext: MeetingItem item })
    {
        CopyMeetingToClipboard(item);
    }
}
private void OpenMeetingAudio_Click(object sender, RoutedEventArgs e)
{
    if (sender is not FrameworkElement { DataContext: MeetingItem item })
    {
        return;
    }
    OpenMeetingAudio(item);
}
private void CopyMeetingToClipboard(MeetingItem item)
{
    var text = string.IsNullOrWhiteSpace(item.Summary)
        ? item.Transcript
        : $"{item.Summary}{Environment.NewLine}{Environment.NewLine}## Transcript{Environment.NewLine}{item.Transcript}";
    System.Windows.Clipboard.SetText(text);
    DictationStatus = "Copied meeting";
    _toastNotificationService.Show("Copied", item.Title, ToastState.Success);
}
private void OpenMeetingDetail(MeetingItem item)
{
    _selectedMeeting = item;
    _selectedMeetingTemplate = NormalizeSummaryTemplateName(string.IsNullOrWhiteSpace(item.TemplateName) ? SelectedSummaryTemplate : item.TemplateName);
    _activeSpeakerAliases = new Dictionary<string, string>(item.SpeakerAliases ?? new Dictionary<string, string>());
    BuildSpeakerAliasPanel();
    BuildMeetingWarningsPanel(item);
    BuildMeetingNotesContent();
    RefreshMeetingPlaybackTracks(item);
    OnPropertyChanged(nameof(SelectedMeetingTitle));
    OnPropertyChanged(nameof(SelectedMeetingTitleOwnership));
    OnPropertyChanged(nameof(SelectedMeetingManualNotes));
    OnPropertyChanged(nameof(SelectedMeetingMetadata));
    OnPropertyChanged(nameof(SelectedMeetingNotes));
    OnPropertyChanged(nameof(SelectedMeetingTemplate));
    OnPropertyChanged(nameof(SelectedMeetingNotesActionLabel));
    OnPropertyChanged(nameof(SelectedMeetingTranscript));
    MeetingsBrowserView.Visibility = Visibility.Collapsed;
    MeetingDetailView.Visibility = Visibility.Visible;
    var showTranscript = string.IsNullOrWhiteSpace(item.Summary) && !string.IsNullOrWhiteSpace(item.Transcript)
        ? true
        : _lastMeetingDetailShowTranscript;
    ShowMeetingDetailTab(showTranscript);
}
private void OpenMeetingAudio(MeetingItem item)
{
    OpenMeetingDetail(item);
    if (!HasMeetingPlayback)
    {
        DictationStatus = "Meeting audio file not found";
        _toastNotificationService.Show("Audio not found", item.Title, ToastState.Error);
        return;
    }
    ShowPage(MeetingsPage, MeetingsNav);
    DictationStatus = "Meeting recording ready to play";
}
private void DeleteMeeting(MeetingItem item)
{
    var audioPaths = item.SourcePath
        .Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
        .Where(path => _captureStorageService.IsOwnedMeetingAudioPath(item.Id, path))
        .ToList();
    if (audioPaths.Count > 0)
    {
        var choice = System.Windows.MessageBox.Show(
            "Delete the audio files saved for this meeting too?\n\nYes deletes this meeting's owned recordings. No keeps the audio files. Imported media is never deleted.",
            "Delete meeting",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Warning);
        if (choice == MessageBoxResult.Cancel)
        {
            return;
        }
        if (choice == MessageBoxResult.Yes)
        {
            var cleanup = _captureStorageService.DeleteOwnedMeetingAudio(item.Id, audioPaths);
            if (cleanup.FailedPaths.Count > 0)
            {
                _logService.Info($"Meeting removed, but {cleanup.FailedPaths.Count} owned audio file(s) could not be deleted.");
            }
        }
    }

    Meetings.Remove(item);
    SaveMeetings(afterExplicitDeletion: true);
    RefreshMeetingViews();
    RefreshSearchResults();
    DictationStatus = "Deleted meeting";
}
private void DeleteMeeting_Click(object sender, RoutedEventArgs e)
{
    if (sender is FrameworkElement { DataContext: MeetingItem item })
    {
        DeleteMeeting(item);
    }
}
private void OpenMeetingDetail_Click(object sender, RoutedEventArgs e)
{
    if (sender is FrameworkElement { DataContext: MeetingItem item })
    {
        OpenMeetingDetail(item);
        ShowPage(MeetingsPage, MeetingsNav);
    }
}
private void BackToMeetings_Click(object sender, RoutedEventArgs e)
{
    SaveActiveSpeakerAliases();
    _meetingPlaybackTimer.Stop();
    _meetingPlaybackService.Close();
    _selectedMeeting = null;
    _selectedMeetingTemplate = NormalizeSummaryTemplateName(SelectedSummaryTemplate);
    MeetingsBrowserView.Visibility = Visibility.Visible;
    MeetingDetailView.Visibility = Visibility.Collapsed;
    MeetingWarningsPanel.Visibility = Visibility.Collapsed;
    MeetingWarningsItems.ItemsSource = null;
}
private void ShowMeetingNotesTab_Click(object sender, MouseButtonEventArgs e)
{
    _lastMeetingDetailShowTranscript = false;
    ShowMeetingDetailTab(showTranscript: false);
}
private void ShowMeetingTranscriptTab_Click(object sender, MouseButtonEventArgs e)
{
    _lastMeetingDetailShowTranscript = true;
    ShowMeetingDetailTab(showTranscript: true);
}
private void ShowMeetingDetailTab(bool showTranscript)
{
    MeetingNotesPanel.Visibility = showTranscript ? Visibility.Collapsed : Visibility.Visible;
    MeetingTranscriptPanel.Visibility = showTranscript ? Visibility.Visible : Visibility.Collapsed;
    MeetingNotesTab.Background = showTranscript
        ? System.Windows.Media.Brushes.Transparent
        : (System.Windows.Media.Brush)FindResource("SurfaceSelectedBrush");
    MeetingTranscriptTab.Background = showTranscript
        ? (System.Windows.Media.Brush)FindResource("SurfaceSelectedBrush")
        : System.Windows.Media.Brushes.Transparent;
    MeetingNotesTabLabel.Foreground = showTranscript
        ? (System.Windows.Media.Brush)FindResource("TextSecondaryBrush")
        : (System.Windows.Media.Brush)FindResource("TextPrimaryBrush");
    MeetingTranscriptTabLabel.Foreground = showTranscript
        ? (System.Windows.Media.Brush)FindResource("TextPrimaryBrush")
        : (System.Windows.Media.Brush)FindResource("TextSecondaryBrush");
}
private void BuildSpeakerAliasPanel()
{
    SpeakerAliasPanel.Children.Clear();
    if (_selectedMeeting is null || string.IsNullOrWhiteSpace(_selectedMeeting.Transcript))
        return;

    var labels = DetectSpeakerLabels(_selectedMeeting.Transcript);
    if (labels.Count == 0)
        return;

    var header = new TextBlock
    {
        Text = "Speakers",
        Style = (Style)FindResource("SectionLabel"),
        Margin = new Thickness(0, 0, 0, 8)
    };
    SpeakerAliasPanel.Children.Add(header);

    var rows = new StackPanel { Orientation = System.Windows.Controls.Orientation.Vertical };
    foreach (var label in labels)
    {
        var row = new StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 6)
        };

        var labelText = new TextBlock
        {
            Text = label,
            FontSize = 13,
            Foreground = (System.Windows.Media.Brush)FindResource("TextPrimaryBrush"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
            Width = 80
        };

        var aliasBox = new System.Windows.Controls.TextBox
        {
            Text = _activeSpeakerAliases.TryGetValue(label, out var alias) ? alias : "",
            FontSize = 13,
            Padding = new Thickness(8, 4, 8, 4),
            Background = (System.Windows.Media.Brush)FindResource("BackgroundHoverBrush"),
            Foreground = (System.Windows.Media.Brush)FindResource("TextPrimaryBrush"),
            BorderBrush = (System.Windows.Media.Brush)FindResource("BorderBrushSoft"),
            BorderThickness = new Thickness(1),
            VerticalAlignment = VerticalAlignment.Center,
            Width = 200,
            Tag = label
        };
        aliasBox.TextChanged += (_, _) =>
        {
            _activeSpeakerAliases[(string)aliasBox.Tag] = aliasBox.Text;
            OnPropertyChanged(nameof(SelectedMeetingTranscript));
            OnPropertyChanged(nameof(SelectedMeetingNotes));
            BuildMeetingNotesContent();
            _aliasSaveDebounceTimer.Stop();
            _aliasSaveDebounceTimer.Start();
        };

        row.Children.Add(labelText);
        row.Children.Add(aliasBox);
        rows.Children.Add(row);
    }

    SpeakerAliasPanel.Children.Add(rows);
}

private void SaveActiveSpeakerAliases()
{
    if (_selectedMeeting is null)
        return;

    var filtered = _activeSpeakerAliases
        .Where(p => !string.IsNullOrWhiteSpace(p.Value) && p.Key != p.Value.Trim())
        .ToDictionary(p => p.Key, p => p.Value.Trim());

    if (filtered.Count == 0 && (_selectedMeeting.SpeakerAliases is null || _selectedMeeting.SpeakerAliases.Count == 0))
        return;

    var index = Meetings.IndexOf(_selectedMeeting);
    if (index < 0)
        return;

    var updated = _selectedMeeting with { SpeakerAliases = filtered };
    Meetings[index] = updated;
    _selectedMeeting = updated;
    SaveMeetings();
}

private void BuildMeetingWarningsPanel(MeetingItem item)
{
    var warnings = MeetingRecordingCoordinator.CleanupHealthWarnings(item.HealthWarnings, item.Transcript);
    if (warnings.Count == 0)
    {
        MeetingWarningsPanel.Visibility = Visibility.Collapsed;
        MeetingWarningsItems.ItemsSource = null;
        return;
    }

    MeetingWarningsItems.ItemsSource = warnings;
    MeetingWarningsPanel.Visibility = Visibility.Visible;
}

private static System.Windows.Controls.Grid CreateWrappedNoteRow(UIElement leading, TextBlock content)
{
    var row = new System.Windows.Controls.Grid
    {
        Margin = new Thickness(0, 2, 0, 2),
        HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch
    };
    row.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition
    {
        Width = System.Windows.GridLength.Auto
    });
    row.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition
    {
        Width = new System.Windows.GridLength(1, System.Windows.GridUnitType.Star)
    });

    content.TextWrapping = TextWrapping.Wrap;
    content.HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch;

    System.Windows.Controls.Grid.SetColumn(leading, 0);
    System.Windows.Controls.Grid.SetColumn(content, 1);
    row.Children.Add(leading);
    row.Children.Add(content);
    return row;
}

private static void SetMarkdownInlineText(TextBlock target, string text)
{
    target.Inlines.Clear();
    var matches = System.Text.RegularExpressions.Regex.Matches(text, @"\*\*(.+?)\*\*");
    if (matches.Count == 0)
    {
        target.Text = text;
        return;
    }

    target.Text = "";
    var index = 0;
    foreach (System.Text.RegularExpressions.Match match in matches)
    {
        if (match.Index > index)
        {
            target.Inlines.Add(new System.Windows.Documents.Run(text[index..match.Index]));
        }

        target.Inlines.Add(new System.Windows.Documents.Bold(new System.Windows.Documents.Run(match.Groups[1].Value)));
        index = match.Index + match.Length;
    }

    if (index < text.Length)
    {
        target.Inlines.Add(new System.Windows.Documents.Run(text[index..]));
    }
}

private void BuildMeetingNotesContent()
{
    MeetingNotesContent.Children.Clear();

    var text = SelectedMeetingNotes;
    if (string.IsNullOrWhiteSpace(text))
    {
        var emptyState = new StackPanel
        {
            Margin = new Thickness(0, 10, 0, 4)
        };
        emptyState.Children.Add(new TextBlock
        {
            Text = "\uE70F",
            FontFamily = new System.Windows.Media.FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
            FontSize = 28,
            Foreground = (System.Windows.Media.Brush)FindResource("TextTertiaryBrush"),
            Margin = new Thickness(0, 0, 0, 10)
        });
        emptyState.Children.Add(new TextBlock
        {
            Text = "No notes yet",
            FontSize = 18,
            FontWeight = FontWeights.SemiBold,
            Foreground = (System.Windows.Media.Brush)FindResource("TextPrimaryBrush"),
            Margin = new Thickness(0, 0, 0, 6)
        });
        emptyState.Children.Add(new TextBlock
        {
            Text = "Generate structured notes from this meeting transcript using the selected template.",
            FontSize = 14,
            Foreground = (System.Windows.Media.Brush)FindResource("TextSecondaryBrush"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 14)
        });
        var generateButton = new WpfButton
        {
            Content = "Generate Notes",
            Style = (Style)FindResource("PrimaryButton"),
            HorizontalAlignment = System.Windows.HorizontalAlignment.Left
        };
        generateButton.Click += GenerateSelectedMeetingNotes_Click;
        emptyState.Children.Add(generateButton);
        MeetingNotesContent.Children.Add(emptyState);
        return;
    }

    var lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.None);
    foreach (var rawLine in lines)
    {
        var line = rawLine.TrimEnd();
        if (string.IsNullOrWhiteSpace(line))
        {
            MeetingNotesContent.Children.Add(new System.Windows.Controls.Grid { Height = 8 });
            continue;
        }

        var headingMatch = System.Text.RegularExpressions.Regex.Match(line, @"^(#{1,3})\s+(.+)$");
        if (headingMatch.Success)
        {
            var level = headingMatch.Groups[1].Value.Length;
            var headingText = headingMatch.Groups[2].Value.Trim();
            MeetingNotesContent.Children.Add(new TextBlock
            {
                Text = headingText,
                FontWeight = FontWeights.Bold,
                FontSize = level == 1 ? 22 : (level == 2 ? 17 : 14),
                Foreground = (System.Windows.Media.Brush)FindResource(level <= 2 ? "TextPrimaryBrush" : "TextSecondaryBrush"),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, level == 1 ? 4 : 18, 0, level == 3 ? 6 : 10)
            });
            continue;
        }

        if (System.Text.RegularExpressions.Regex.IsMatch(line, @"^---+\s*$"))
        {
            MeetingNotesContent.Children.Add(new Border
            {
                Height = 1,
                Background = (System.Windows.Media.Brush)FindResource("BorderBrushSoft"),
                Margin = new Thickness(0, 8, 0, 8)
            });
            continue;
        }

        var checkboxMatch = System.Text.RegularExpressions.Regex.Match(line, @"^-\s+\[([ xX])\]\s*(.*)$");
        if (checkboxMatch.Success)
        {
            var isChecked = checkboxMatch.Groups[1].Value.Trim().Equals("x", StringComparison.OrdinalIgnoreCase);
            var itemText = checkboxMatch.Groups[2].Value.Trim();
            var icon = new TextBlock
            {
                Text = isChecked ? "\uE73D" : "\uE739",
                FontFamily = new System.Windows.Media.FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
                FontSize = 14,
                Foreground = (System.Windows.Media.Brush)FindResource(isChecked ? "AccentBlueBrush" : "TextSecondaryBrush"),
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 1, 8, 0)
            };
            var content = new TextBlock
            {
                FontSize = 14,
                Foreground = (System.Windows.Media.Brush)FindResource("TextPrimaryBrush"),
                VerticalAlignment = VerticalAlignment.Top,
                TextWrapping = TextWrapping.Wrap
            };
            SetMarkdownInlineText(content, itemText);
            MeetingNotesContent.Children.Add(CreateWrappedNoteRow(icon, content));
            continue;
        }

        var bulletMatch = System.Text.RegularExpressions.Regex.Match(line, @"^[-\u2022]\s+(.+)$");
        if (bulletMatch.Success)
        {
            var bulletText = bulletMatch.Groups[1].Value.Trim();
            var bullet = new TextBlock
            {
                Text = "\u2022",
                FontSize = 14,
                Foreground = (System.Windows.Media.Brush)FindResource("TextSecondaryBrush"),
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 0, 10, 0)
            };
            var content = new TextBlock
            {
                FontSize = 14,
                Foreground = (System.Windows.Media.Brush)FindResource("TextPrimaryBrush"),
                TextWrapping = TextWrapping.Wrap
            };
            SetMarkdownInlineText(content, bulletText);
            MeetingNotesContent.Children.Add(CreateWrappedNoteRow(bullet, content));
            continue;
        }

        var numberedMatch = System.Text.RegularExpressions.Regex.Match(line, @"^(\d+)\.\s+(.+)$");
        if (numberedMatch.Success)
        {
            var number = numberedMatch.Groups[1].Value.Trim();
            var numText = numberedMatch.Groups[2].Value.Trim();
            var index = new TextBlock
            {
                Text = number + ".",
                FontSize = 14,
                Foreground = (System.Windows.Media.Brush)FindResource("TextSecondaryBrush"),
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 0, 10, 0),
                Width = 20
            };
            var content = new TextBlock
            {
                FontSize = 14,
                Foreground = (System.Windows.Media.Brush)FindResource("TextPrimaryBrush"),
                TextWrapping = TextWrapping.Wrap
            };
            SetMarkdownInlineText(content, numText);
            MeetingNotesContent.Children.Add(CreateWrappedNoteRow(index, content));
            continue;
        }

        var paragraph = new TextBlock
        {
            FontSize = 14,
            Foreground = (System.Windows.Media.Brush)FindResource("TextPrimaryBrush"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 4),
            LineHeight = 22
        };
        SetMarkdownInlineText(paragraph, line);
        MeetingNotesContent.Children.Add(paragraph);
    }
}
private async void GenerateSelectedMeetingNotes_Click(object sender, RoutedEventArgs e)
{
    await GenerateSelectedMeetingNotesAsync();
}

private async Task GenerateSelectedMeetingNotesAsync()
{
    if (_selectedMeeting is null || string.IsNullOrWhiteSpace(_selectedMeeting.Transcript))
    {
        return;
    }

    if (!string.IsNullOrWhiteSpace(_selectedMeeting.Summary))
    {
        var result = System.Windows.MessageBox.Show(
            $"Regenerate notes for \"{_selectedMeeting.Title}\" using the \"{SelectedMeetingTemplate}\" template?",
            "Regenerate notes",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (result != MessageBoxResult.Yes)
        {
            return;
        }
    }

    _summaryCancellation?.Dispose();
    _summaryCancellation = new CancellationTokenSource();
    var summaryToken = _summaryCancellation.Token;
    IsSummarizing = true;
    try
    {
        DictationStatus = "Generating meeting notes";
        _toastNotificationService.Show("Generating notes", SelectedMeetingTemplate, ToastState.Transcribing, 0);
        var summary = await CreateMeetingSummaryWithSettingsAsync(
            _selectedMeeting.Transcript,
            _selectedMeeting.Title,
            CurrentSettingsSnapshot() with
            {
                MeetingSummaryTemplate = SelectedMeetingTemplate,
                MeetingSummaryPromptOverride = CustomMeetingTemplates.FirstOrDefault(template =>
                    template.Name.Equals(SelectedMeetingTemplate, StringComparison.OrdinalIgnoreCase))?.Prompt ?? ""
            },
            summaryToken);

        var index = Meetings.IndexOf(_selectedMeeting);
        if (index < 0)
        {
            return;
        }

        // Regeneration replaces generated notes only. Manual notes and a manually chosen title are
        // the user's own writing and are carried across untouched.
        var updated = _selectedMeeting with
        {
            Summary = summary,
            TemplateName = SelectedMeetingTemplate,
            ManualNotes = _selectedMeeting.ManualNotes,
            TitleIsManual = _selectedMeeting.TitleIsManual
        };
        Meetings[index] = updated;
        _selectedMeeting = updated;
        SaveMeetings();
        RefreshSearchResults();
        BuildMeetingWarningsPanel(updated);
        BuildMeetingNotesContent();
        OnPropertyChanged(nameof(SelectedMeetingNotes));
        OnPropertyChanged(nameof(SelectedMeetingNotesActionLabel));
        if (!_lastSummaryUsedLocalFallback)
        {
            DictationStatus = "Meeting notes ready";
            _toastNotificationService.Show("Notes ready", updated.Title, ToastState.Success, 2800);
        }
    }
    catch (OperationCanceledException)
    {
        // Cancelling must leave the existing notes exactly as they were.
        DictationStatus = "Notes generation cancelled; existing notes are unchanged";
        _toastNotificationService.Show("Cancelled", "Existing notes were left unchanged", ToastState.Idle, 2600);
    }
    catch (Exception exception)
    {
        _summaryRetryAvailable = true;
        DictationStatus = $"Notes generation failed: {exception.Message}";
        _toastNotificationService.Show("Notes generation failed", $"{exception.Message} · use Retry", ToastState.Error, 4200);
        OnPropertyChanged(nameof(CanRetrySummary));
    }
    finally
    {
        IsSummarizing = false;
    }
}

private void MoreMeetingActions_Click(object sender, RoutedEventArgs e)
{
    if (sender is System.Windows.Controls.Button button && button.ContextMenu is not null)
    {
        button.ContextMenu.PlacementTarget = button;
        button.ContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        button.ContextMenu.IsOpen = true;
    }
}
private void ExportMeetingNotes_Click(object sender, RoutedEventArgs e) => RunExport(MeetingExportMode.Notes);
private void ExportMeetingTranscript_Click(object sender, RoutedEventArgs e) => RunExport(MeetingExportMode.Transcript);
private void ExportFullMeeting_Click(object sender, RoutedEventArgs e) => RunExport(MeetingExportMode.FullMeeting);

private void ShowMeetingAutomationDiagnostics_Click(object sender, RoutedEventArgs e)
{
    var result = _selectedMeeting?.AutomationResult;
    if (result is null)
    {
        System.Windows.MessageBox.Show(
            this,
            "No post-meeting automation result is recorded. Existing meetings are never run retroactively.",
            "Automation diagnostics",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
        return;
    }

    var lines = new List<string>
    {
        $"Status: {result.Status}",
        $"Started: {result.StartedAtUtc:u}",
        $"Finished: {result.CompletedAtUtc:u}",
        $"Attempts: {result.Attempts}",
        $"Exit code: {result.ExitCode?.ToString() ?? "none"}",
        $"Markdown export: {(result.Export.Completed ? "completed" : result.Export.Requested ? "failed" : "not requested")}",
        $"Destination ownership: {result.Export.DestinationOwnership}"
    };
    if (!string.IsNullOrWhiteSpace(result.Export.DestinationPath)) lines.Add($"Export path: {result.Export.DestinationPath}");
    if (!string.IsNullOrWhiteSpace(result.Error)) lines.Add($"Result: {result.Error}");
    if (!string.IsNullOrWhiteSpace(result.StandardOutput))
        lines.Add($"\nstdout{(result.StandardOutputTruncated ? " (truncated)" : "")}\n{result.StandardOutput}");
    if (!string.IsNullOrWhiteSpace(result.StandardError))
        lines.Add($"\nstderr{(result.StandardErrorTruncated ? " (truncated)" : "")}\n{result.StandardError}");

    System.Windows.MessageBox.Show(
        this,
        string.Join(Environment.NewLine, lines),
        "Automation diagnostics",
        MessageBoxButton.OK,
        result.Completed ? MessageBoxImage.Information : MessageBoxImage.Warning);
}

/// <summary>
/// Exports and reports the outcome. Export only reads the saved meeting, so a failure cannot
/// corrupt it, but it must never fail silently either.
/// </summary>
private void RunExport(MeetingExportMode mode)
{
    if (_selectedMeeting is null) return;
    var result = MeetingExporter.Export(_selectedMeeting, mode, _activeSpeakerAliases);
    if (result is { Completed: false, Error: null })
    {
        return;
    }
    if (!result.Completed)
    {
        DictationStatus = $"Export failed: {result.Error}";
        _logService.Info($"Meeting export failed. mode={mode}; transcriptLogged=false");
        _toastNotificationService.Show("Export failed", result.Error ?? "Unknown error", ToastState.Error, 4600);
        System.Windows.MessageBox.Show(
            $"The export could not be written.\n\n{result.Error}\n\nYour saved meeting is unchanged.",
            "Export failed",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
        return;
    }
    DictationStatus = result.Error is null ? "Export ready" : result.Error;
    _toastNotificationService.Show(
        result.Error is null ? "Exported" : "Exported with a warning",
        System.IO.Path.GetFileName(result.Path) ?? "",
        result.Error is null ? ToastState.Success : ToastState.Error,
        3200);
}
private void CopySelectedMeetingNotes_Click(object sender, RoutedEventArgs e)
{
    if (_selectedMeeting is null)
    {
        return;
    }
    if (string.IsNullOrWhiteSpace(SelectedMeetingNotes))
    {
        DictationStatus = "No notes to copy";
        _toastNotificationService.Show("No notes yet", "Generate notes first", ToastState.Error, 2600);
        return;
    }
    System.Windows.Clipboard.SetText(SelectedMeetingNotes);
    DictationStatus = "Copied meeting notes";
    _toastNotificationService.Show("Copied notes", _selectedMeeting.Title, ToastState.Success);
}
private void CopySelectedMeetingTranscript_Click(object sender, RoutedEventArgs e)
{
    if (_selectedMeeting is null)
    {
        return;
    }
    System.Windows.Clipboard.SetText(SelectedMeetingTranscript);
    DictationStatus = "Copied transcript";
    _toastNotificationService.Show("Copied transcript", _selectedMeeting.Title, ToastState.Success);
}
private void OpenSelectedMeetingAudio_Click(object sender, RoutedEventArgs e)
{
    if (_selectedMeeting is not null)
    {
        OpenMeetingAudio(_selectedMeeting);
    }
}
private void DeleteSelectedMeeting_Click(object sender, RoutedEventArgs e)
{
    if (_selectedMeeting is null)
    {
        return;
    }
    var item = _selectedMeeting;
    BackToMeetings_Click(sender, e);
    DeleteMeeting(item);
}
private void MoveMeeting_Click(object sender, RoutedEventArgs e)
{
    if (sender is not FrameworkElement { DataContext: MeetingItem item } element)
    {
        return;
    }
    OpenMoveMeetingMenu(element, item);
}
private void MoveSelectedMeeting_Click(object sender, RoutedEventArgs e)
{
    if (_selectedMeeting is null || sender is not FrameworkElement element)
    {
        return;
    }
    OpenMoveMeetingMenu(element, _selectedMeeting);
}
private void OpenMoveMeetingMenu(FrameworkElement placementTarget, MeetingItem meeting)
{
    var menu = new ContextMenu();
    AddMoveMenuItem(menu, "All Meetings", meeting, null);
    if (MeetingFolders.Count > 0)
    {
        menu.Items.Add(new Separator());
    }
    foreach (var folder in MeetingFolders)
    {
        AddMoveMenuItem(menu, folder.Name, meeting, folder.Id);
    }
    menu.PlacementTarget = placementTarget;
    menu.IsOpen = true;
}
private void AddMoveMenuItem(ContextMenu menu, string header, MeetingItem meeting, string? folderId)
{
    var item = new MenuItem
    {
        Header = header,
        Tag = new MoveMeetingRequest(meeting.Id, folderId)
    };
    item.Click += MoveMeetingToFolder_Click;
    menu.Items.Add(item);
}
private void MoveMeetingToFolder_Click(object sender, RoutedEventArgs e)
{
    if (sender is not MenuItem { Tag: MoveMeetingRequest request })
    {
        return;
    }
    var index = Meetings.ToList().FindIndex(meeting => meeting.Id == request.MeetingId);
    if (index < 0)
    {
        return;
    }
    var updated = Meetings[index] with { FolderId = request.FolderId };
    Meetings[index] = updated;
    if (_selectedMeeting?.Id == updated.Id)
    {
        _selectedMeeting = updated;
        OnPropertyChanged(nameof(SelectedMeetingMetadata));
    }
    SaveMeetings();
    RefreshMeetingViews();
    DictationStatus = "Moved meeting";
}
private async void ImportMeeting_Click(object sender, RoutedEventArgs e)
{
    // The button is disabled while an import runs; this guards the keyboard and automation paths.
    if (IsImportingMeeting)
    {
        return;
    }
    var dialog = new Microsoft.Win32.OpenFileDialog
    {
        Title = "Import meeting audio or video",
        // Only formats whose decode path is actually qualified; "All files" is kept so a user can
        // still pick anything, and then gets explicit conversion guidance instead of a decode crash.
        Filter = $"{MediaImportFormats.DialogFilter}|All files|*.*"
    };
    if (dialog.ShowDialog(this) != true)
    {
        return;
    }
    if (!MediaImportFormats.IsSupported(dialog.FileName))
    {
        var guidance = MediaImportFormats.ConversionGuidanceFor(dialog.FileName);
        DictationStatus = "Unsupported media format";
        _logService.Info($"Import rejected before decode. extension={System.IO.Path.GetExtension(dialog.FileName)}");
        System.Windows.MessageBox.Show(guidance, "Cannot import this file", MessageBoxButton.OK, MessageBoxImage.Warning);
        return;
    }
    var displayName = System.IO.Path.GetFileNameWithoutExtension(dialog.FileName);
    _importCancellation?.Dispose();
    _importCancellation = new CancellationTokenSource();
    var importToken = _importCancellation.Token;
    IsImportingMeeting = true;
    ReportImportProgress(MeetingImportStage.Preparing, null);

    // Set once the transcript exists. Cancelling after that point keeps the transcript rather than
    // throwing away inference the user already waited for.
    string? recoveredTranscript = null;
    TranscriptionResult? recoveredResult = null;
    try
    {
        DictationStatus = "Transcribing meeting";
        _toastNotificationService.Show("Transcribing meeting", System.IO.Path.GetFileName(dialog.FileName), ToastState.Transcribing, 0);
        // Progress created on the UI thread, so reports marshal back here off the worker.
        var progress = new Progress<TranscriptionProgress>(report =>
            ReportImportProgress(MeetingImportProgressMapper.From(report.Stage), report.Fraction));
        var result = await _meetingTranscriptionClient.TranscribeFileAsync(
            displayName,
            dialog.FileName,
            progress,
            importToken);
        _transcriptionPipelineService.LogTranscriptionResult(
            "imported media", result, _meetingTranscriptionClient.EngineId, _meetingTranscriptionClient.ModelId);
        ReportImportProgress(MeetingImportStage.CleaningUp, null);
        var transcript = await _transcriptionPipelineService.PrepareImportedTranscriptAsync(
            result.Text,
            EnableLocalCleanup,
            DictionaryEntries.Select(entry => entry.Record),
            importToken);
        if (string.IsNullOrWhiteSpace(transcript))
        {
            DictationStatus = "No speech detected in imported meeting";
            _toastNotificationService.Show("No speech detected", System.IO.Path.GetFileName(dialog.FileName), ToastState.Error, 3600);
            return;
        }
        recoveredTranscript = transcript;
        recoveredResult = result;
        ReportImportProgress(MeetingImportStage.GeneratingNotes, null);
        var summary = await CreateMeetingSummaryAsync(transcript, displayName, importToken);
        SaveImportedMeeting(dialog.FileName, displayName, transcript, summary, result.DurationMs);
        if (!_lastSummaryUsedLocalFallback)
        {
            DictationStatus = "Meeting transcribed";
            _toastNotificationService.Show("Meeting ready", displayName, ToastState.Success);
        }
        ShowPage(MeetingsPage, MeetingsNav);
    }
    catch (OperationCanceledException)
    {
        // Cancelling before a transcript exists leaves nothing behind. Cancelling once one exists
        // keeps it: discarding a finished transcript because notes were interrupted would destroy
        // the expensive half of the work. The source file is never touched either way.
        if (recoveredTranscript is not null)
        {
            SaveImportedMeeting(
                dialog.FileName,
                displayName,
                recoveredTranscript,
                summary: "",
                recoveredResult?.DurationMs ?? 0);
            DictationStatus = "Import cancelled during notes; the transcript was saved";
            _logService.Info("Import cancelled after transcription; transcript retained without notes.");
            _toastNotificationService.Show(
                "Transcript saved",
                "Notes were cancelled — use Generate Notes when ready",
                ToastState.Idle,
                4200);
            ShowPage(MeetingsPage, MeetingsNav);
        }
        else
        {
            DictationStatus = "Import cancelled; nothing was saved";
            _logService.Info("Import cancelled before a transcript existed; no meeting created.");
            _toastNotificationService.Show(
                "Import cancelled",
                "Your original file is unchanged",
                ToastState.Idle,
                2600);
        }
    }
    catch (Exception exception)
    {
        DictationStatus = $"Meeting import failed: {exception.Message}";
        _toastNotificationService.Show("Meeting import failed", exception.Message, ToastState.Error, 4200);
    }
    finally
    {
        IsImportingMeeting = false;
        ImportProgressPercent = 0;
        ImportProgressLabel = "";
    }
}

/// <summary>
/// Persists an imported meeting. The imported file is recorded as the source path only; it is
/// never copied into the meeting directory and never becomes owned audio.
/// </summary>
private void SaveImportedMeeting(
    string sourcePath,
    string title,
    string transcript,
    string summary,
    int durationMs)
{
    var meeting = new MeetingItem(
        $"meet_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}",
        title,
        DateTime.Now,
        transcript,
        summary,
        sourcePath,
        _meetingTranscriptionClient.ModelId,
        durationMs,
        _selectedMeetingFolderId,
        CountWords(transcript),
        SelectedSummaryTemplate,
        FinalTranscriptOwnerModelId: _meetingTranscriptionClient.ModelId);
    Meetings.Insert(0, meeting);
    try
    {
        SaveMeetings();
    }
    catch
    {
        Meetings.Remove(meeting);
        throw;
    }
    RefreshMeetingViews();
    RefreshSearchResults();
}
private async void ToggleMeetingRecording_Click(object sender, RoutedEventArgs e)
{
    await ToggleMeetingRecordingAsync(null);
}
private async Task ToggleMeetingRecordingAsync(DetectedMeeting? detectedMeeting)
{
    if (_meetingRecordingCoordinator.IsBusy)
    {
        return;
    }
    if (!_isMeetingRecording)
    {
        try
        {
            _meetingOperationCancellation?.Dispose();
            _meetingOperationCancellation = new CancellationTokenSource();
            DictationStatus = "Preparing meeting capture";
            _toastNotificationService.Show("Preparing meeting", "Starting microphone and meeting audio", ToastState.Transcribing, 0);
            var start = await _meetingRecordingCoordinator.StartAsync(
                SelectedMicrophone,
                detectedMeeting?.Title,
                SaveMeetingRecordings,
                detectedMeeting?.ProcessId,
                _meetingOperationCancellation.Token,
                SelectedLiveMeetingModel.Id is { } liveModelId
                    ? new LiveTranscriptionConfiguration(liveModelId, SelectedOwnershipMode, ShowLiveWaveformOnHover)
                    : null);
            _currentMeetingTitle = detectedMeeting?.Title;
            _isMeetingRecording = start.State is MeetingSessionState.Recording or MeetingSessionState.DegradedRecording;
            _meetingAutoStopTracker = new MeetingAutoStopTracker(
                detectedMeeting is null
                    ? MeetingRecordingStartOrigin.Manual
                    : MeetingRecordingStartOrigin.DetectedMeeting,
                detectedMeeting?.Key);
            if (detectedMeeting is not null)
            {
                _meetingAutoStopTracker.Observe(detectedMeeting.Key, DateTimeOffset.UtcNow);
            }
            StartMeetingAutoStopMonitor();
            DictationStatus = start.State == MeetingSessionState.DegradedRecording
                ? "Meeting recording started with a missing channel"
                : "Recording meeting";
            _toastNotificationService.Show(
                start.State == MeetingSessionState.DegradedRecording ? "Recording degraded" : "Recording meeting",
                start.Warning ?? (start.SystemCaptureMode == SystemAudioCaptureMode.ProcessTreeLoopback
                    ? "Capturing microphone and the meeting process"
                    : "Capturing microphone and default Windows output"),
                start.State == MeetingSessionState.DegradedRecording ? ToastState.Error : ToastState.Recording,
                start.State == MeetingSessionState.DegradedRecording ? 4200 : 0);
            OnPropertyChanged(nameof(MeetingRecordingButtonText));
            ShowPage(MeetingsPage, MeetingsNav);
        }
        catch (OperationCanceledException)
        {
            ResetMeetingRecordingUi("Meeting recording cancelled");
        }
        catch (Exception exception)
        {
            DictationStatus = $"Meeting recording failed: {exception.Message}";
            _toastNotificationService.Show("Meeting recording failed", exception.Message, ToastState.Error);
            RefreshRecoverableMeetingSessions();
        }
        return;
    }
    try
    {
        StopMeetingAutoStopMonitor();
        DictationStatus = "Transcribing meeting";
        _toastNotificationService.Show("Transcribing meeting", "Processing local meeting audio", ToastState.Transcribing, 0);
        _isMeetingRecording = false;
        OnPropertyChanged(nameof(MeetingRecordingButtonText));
        var title = string.IsNullOrWhiteSpace(_currentMeetingTitle)
            ? $"Meeting {DateTime.Now:yyyy-MM-dd HH-mm}"
            : _currentMeetingTitle;
        _meetingOperationCancellation?.Dispose();
        _meetingOperationCancellation = new CancellationTokenSource();
        var result = await _meetingRecordingCoordinator.StopAsync(
            title,
            SaveMeetingRecordings,
            _meetingOperationCancellation.Token);
        _liveTranscriptWindow?.Hide();
        _currentMeetingTitle = null;
        var meeting = await PersistRecordedMeetingAsync(result);
        DictationStatus = result.SessionState == MeetingSessionState.Completed
            ? "Meeting ready"
            : "Meeting audio saved; transcript needs recovery";
        _toastNotificationService.Show(
            result.SessionState == MeetingSessionState.Completed ? "Meeting ready" : "Meeting needs attention",
            result.SessionState == MeetingSessionState.Completed ? meeting.Title : "Audio was retained without a final transcript",
            result.SessionState == MeetingSessionState.Completed ? ToastState.Success : ToastState.Error,
            result.SessionState == MeetingSessionState.Completed ? 2200 : 4200);
        ShowPage(MeetingsPage, MeetingsNav);
    }
    catch (OperationCanceledException)
    {
        RefreshRecoverableMeetingSessions();
        ResetMeetingRecordingUi("Meeting finalization cancelled; audio retained for recovery");
    }
    catch (MeetingSessionRecoverableException)
    {
        RefreshRecoverableMeetingSessions();
        ResetMeetingRecordingUi("Meeting finalization failed; audio retained for recovery");
        _toastNotificationService.Show(
            "Meeting retained for recovery",
            "Use Recover interrupted in Meetings to retry",
            ToastState.Error,
            5200);
    }
    catch (Exception exception)
    {
        StopMeetingAutoStopMonitor();
        _currentMeetingTitle = null;
        _isMeetingRecording = false;
        OnPropertyChanged(nameof(MeetingRecordingButtonText));
        DictationStatus = $"Meeting recording failed: {exception.Message}";
        _toastNotificationService.Show("Meeting recording failed", exception.Message, ToastState.Error, 4200);
    }
}
private void StartMeetingAutoStopMonitor()
{
    _meetingAutoStopTimer.Stop();
    if (_meetingAutoStopTracker?.IsArmed == true)
    {
        _meetingAutoStopTimer.Start();
    }
}
private void StopMeetingAutoStopMonitor()
{
    _meetingAutoStopTimer.Stop();
}
private async void MeetingAutoStopTimer_Tick(object? sender, EventArgs e)
{
    if (!_isMeetingRecording)
    {
        StopMeetingAutoStopMonitor();
        return;
    }
    if (!_meetingRecordingCoordinator.IsRecording && !_meetingRecordingCoordinator.IsBusy)
    {
        ResetMeetingRecordingUi("Meeting recording ended");
        return;
    }
    var scan = _meetingDetectionService.CheckNow(publish: false);
    var shouldStop = _meetingAutoStopTracker?.Observe(
        scan.DetectedMeeting?.Key,
        DateTimeOffset.UtcNow) == true;
    if (!shouldStop || _meetingRecordingCoordinator.IsBusy)
    {
        return;
    }
    _logService.Info("Qualified detected-meeting signal disappeared after the auto-stop grace period; stopping recording.");
    await ToggleMeetingRecordingAsync(null);
}
private void AliasSaveDebounceTimer_Tick(object? sender, EventArgs e)
{
    _aliasSaveDebounceTimer.Stop();
    SaveActiveSpeakerAliases();
}
private void ResetMeetingRecordingUi(string status)
{
    StopMeetingAutoStopMonitor();
    _currentMeetingTitle = null;
    _isMeetingRecording = false;
    _liveTranscriptWindow?.Hide();
    OnPropertyChanged(nameof(MeetingRecordingButtonText));
    DictationStatus = status;
    _toastNotificationService.ShowIdle(SelectedHotkey);
}

private async Task<MeetingItem> PersistRecordedMeetingAsync(RecordedMeetingResult result)
{
    var transcript = string.IsNullOrWhiteSpace(result.Transcript)
        ? ""
        : await _transcriptionPipelineService.PrepareMeetingTranscriptAsync(
            result.Transcript,
            EnableLocalCleanup,
            DictionaryEntries.Select(entry => entry.Record));
    var summary = string.IsNullOrWhiteSpace(transcript)
        ? ""
        : await CreateMeetingSummaryAsync(transcript, result.Title);
    var sourceAudioPath = string.Join(
        "; ",
        new[] { result.MicAudioPath, result.SystemAudioPath }
            .Where(path => !string.IsNullOrWhiteSpace(path)));
    var meeting = new MeetingItem(
        result.MeetingId,
        result.Title,
        result.StartedAt,
        transcript,
        summary,
        sourceAudioPath,
        _meetingTranscriptionClient.ModelId,
        result.DurationMs,
        _selectedMeetingFolderId,
        CountWords(transcript),
        SelectedSummaryTemplate,
        HealthWarnings: result.HealthWarnings ?? [],
        SessionState: result.SessionState,
        MicrophoneAudioPath: result.MicAudioPath,
        SystemAudioPath: result.SystemAudioPath,
        SystemCaptureMode: result.SystemCaptureMode,
        RecoveredFromInterruption: result.RecoveredFromInterruption,
        LivePreviewModelId: result.LivePreviewModelId,
        LiveTranscriptOwnership: result.LiveTranscriptOwnership,
        FinalTranscriptOwnerModelId: result.FinalTranscriptOwnerModelId,
        GapRecoveryModelId: result.GapRecoveryModelId);
    Meetings.Insert(0, meeting);
    try
    {
        SaveMeetings();
    }
    catch
    {
        Meetings.Remove(meeting);
        throw;
    }
    _meetingRecordingCoordinator.AcknowledgePersisted(result.MeetingId);
    RefreshMeetingViews();
    RefreshSearchResults();
    RefreshRecoverableMeetingSessions();
    if (result.SessionState == MeetingSessionState.Completed)
    {
        meeting = await RunPostMeetingAutomationAsync(
            meeting,
            result.RecoveredFromInterruption
                ? PostMeetingCompletionEvent.RecoveryCompleted
                : PostMeetingCompletionEvent.RecordingCompleted);
    }
    return meeting;
}

private async Task<MeetingItem> RunPostMeetingAutomationAsync(
    MeetingItem meeting,
    PostMeetingCompletionEvent completionEvent)
{
    PostMeetingAutomationResult result;
    try
    {
        PostMeetingAutomationStatusText = PostMeetingHookEnabled || AutoExportMarkdownEnabled
            ? "Running post-meeting automation…"
            : "Automation is disabled; the meeting was saved without launching a process or writing an export.";
        result = await _postMeetingAutomationService.RunAsync(
            meeting,
            CurrentPostMeetingAutomationOptions(),
            completionEvent,
            _applicationShutdownCancellation.Token);
    }
    catch (Exception exception)
    {
        // The meeting was already durably saved and acknowledged. This fallback is diagnostics
        // only; optional automation is never allowed to escape and invalidate completion.
        result = new PostMeetingAutomationResult(
            Guid.NewGuid(),
            PostMeetingAutomationStatus.Failed,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            0,
            null,
            "",
            "",
            false,
            false,
            $"Automation failed ({exception.GetType().Name}).",
            PostMeetingExportDiagnostic.NotRequested);
    }

    var index = Meetings.ToList().FindIndex(candidate => candidate.Id == meeting.Id);
    var updated = meeting with { AutomationResult = result };
    if (index >= 0)
    {
        Meetings[index] = updated;
        try
        {
            SaveMeetings();
        }
        catch (Exception exception)
        {
            _logService.Info($"Automation diagnostics persistence failed. category={exception.GetType().Name}");
        }
    }

    PostMeetingAutomationStatusText = DescribeAutomationResult(result);
    _logService.Info(
        $"Post-meeting automation finished. status={result.Status}; attempts={result.Attempts}; exitCode={result.ExitCode?.ToString() ?? "none"}; exportRequested={result.Export.Requested}; exportCompleted={result.Export.Completed}; contentLogged=false");
    RefreshMeetingViews();
    RefreshSearchResults();
    return updated;
}

private void RefreshRecoverableMeetingSessions()
{
    _recoverableMeetingSessions.Clear();
    _recoverableMeetingSessions.AddRange(_meetingRecordingCoordinator.DiscoverRecoverableSessions());
    OnPropertyChanged(nameof(RecoverableMeetingCount));
    OnPropertyChanged(nameof(HasRecoverableMeetings));
    OnPropertyChanged(nameof(RecoverInterruptedButtonText));
    if (_recoverableMeetingSessions.Count > 0)
    {
        MeetingSessionStatus = $"{_recoverableMeetingSessions.Count} interrupted recording(s) ready for recovery";
    }
}

private async void RecoverInterruptedMeetings_Click(object sender, RoutedEventArgs e)
{
    if (_meetingRecordingCoordinator.IsBusy || _meetingRecordingCoordinator.IsRecording)
    {
        return;
    }
    var pending = _recoverableMeetingSessions.ToList();
    foreach (var recovery in pending)
    {
        _meetingOperationCancellation?.Dispose();
        _meetingOperationCancellation = new CancellationTokenSource();
        try
        {
            DictationStatus = "Recovering interrupted meeting";
            _toastNotificationService.Show("Recovering meeting", "Finalizing retained local audio", ToastState.Transcribing, 0);
            var result = await _meetingRecordingCoordinator.FinalizeRecoverableAsync(
                recovery,
                _meetingOperationCancellation.Token);
            await PersistRecordedMeetingAsync(result);
        }
        catch (OperationCanceledException)
        {
            break;
        }
        catch (Exception exception)
        {
            _logService.Info($"Interrupted meeting recovery failed. category={exception.GetType().Name}");
            _toastNotificationService.Show(
                "Recovery needs attention",
                "The retained audio remains available for another retry",
                ToastState.Error,
                4600);
            break;
        }
    }
    RefreshRecoverableMeetingSessions();
    if (_recoverableMeetingSessions.Count == 0)
    {
        DictationStatus = "Interrupted meeting recovery complete";
        _toastNotificationService.Show("Meeting recovery complete", "Recovered recordings are in Meetings", ToastState.Success);
    }
}

private void OnMeetingSessionStateChanged(object? sender, MeetingSessionStateChangedEventArgs e)
{
    Dispatcher.BeginInvoke(() =>
    {
        var state = e.Transition.To;
        _isMeetingRecording = state is MeetingSessionState.Recording or MeetingSessionState.DegradedRecording;
        MeetingSessionStatus = state switch
        {
            MeetingSessionState.Idle => "Idle",
            MeetingSessionState.Preparing => "Preparing microphone and meeting audio",
            MeetingSessionState.Recording => "Recording microphone and meeting audio",
            MeetingSessionState.DegradedRecording => "Recording with an audio warning",
            MeetingSessionState.Stopping => "Stopping capture safely",
            MeetingSessionState.Finalizing => "Finalizing local transcript",
            MeetingSessionState.Completed => "Meeting completed",
            MeetingSessionState.Failed => "Meeting needs attention",
            MeetingSessionState.Cancelled => "Meeting cancelled",
            MeetingSessionState.RecoverableInterruption => "Recording retained for recovery",
            _ => state.ToString()
        };
        OnPropertyChanged(nameof(MeetingRecordingButtonText));
    });
}

private void OnMeetingAudioHealthChanged(object? sender, MeetingAudioHealthChangedEventArgs e)
{
    if (!e.Snapshot.IsDegraded)
    {
        return;
    }
    Dispatcher.BeginInvoke(() =>
    {
        MeetingSessionStatus = e.Snapshot.Warnings.FirstOrDefault() ?? "Recording with an audio warning";
    });
}

private void OnMeetingRecordingLevelChanged(object? sender, AudioLevelEventArgs e) =>
    Dispatcher.BeginInvoke(() => _toastNotificationService.UpdateRecordingLevel(e.Peak));

private void OnLiveTranscriptChanged(object? sender, LiveTranscriptSnapshot snapshot)
{
    Dispatcher.BeginInvoke(() =>
    {
        _liveTranscriptWindow ??= new MeetingLiveTranscriptWindow(ShowLiveWaveformOnHover) { Owner = this };
        _liveTranscriptWindow.Update(snapshot);
        if (!_liveTranscriptWindow.IsVisible) _liveTranscriptWindow.Show();
    });
}

private void OnLiveTranscriptionFailed(object? sender, Exception exception)
{
    Dispatcher.BeginInvoke(() =>
    {
        MeetingSessionStatus = "Live preview stopped; retained audio is still recording";
        _toastNotificationService.Show("Live transcript stopped", "Retained audio will still be finalized locally", ToastState.Error, 4200);
        _liveTranscriptWindow?.Hide();
    });
}

private async void SystemEvents_PowerModeChanged(object sender, PowerModeChangedEventArgs e)
{
    try
    {
        if (e.Mode == PowerModes.Suspend)
        {
            await _meetingRecordingCoordinator.SuspendAsync();
            return;
        }
        if (e.Mode == PowerModes.Resume &&
            _meetingRecordingCoordinator.State == MeetingSessionState.RecoverableInterruption)
        {
            await _meetingRecordingCoordinator.ResumeAsync();
            _ = Dispatcher.BeginInvoke(() =>
            {
                _isMeetingRecording = _meetingRecordingCoordinator.IsRecording;
                OnPropertyChanged(nameof(MeetingRecordingButtonText));
                _toastNotificationService.Show(
                    _isMeetingRecording ? "Meeting recording resumed" : "Meeting retained for recovery",
                    _isMeetingRecording ? "Microphone and meeting audio restarted" : "Capture could not restart automatically",
                    _isMeetingRecording ? ToastState.Recording : ToastState.Error,
                    _isMeetingRecording ? 0 : 4200);
            });
        }
    }
    catch (Exception exception)
    {
        _logService.Info($"Meeting power-transition handling failed. category={exception.GetType().Name}");
        RefreshRecoverableMeetingSessions();
    }
}

private void RefreshMeetingPlaybackTracks(MeetingItem item)
{
    _meetingPlaybackTimer.Stop();
    _meetingPlaybackService.Close();
    MeetingPlaybackTracks.Clear();
    foreach (var track in MeetingRecordingPlaybackService.SelectTracks(
                 item.MicrophoneAudioPath,
                 item.SystemAudioPath,
                 item.SourcePath))
    {
        MeetingPlaybackTracks.Add(track);
    }
    OnPropertyChanged(nameof(HasMeetingPlayback));
    SelectedMeetingPlaybackTrack = MeetingPlaybackTracks.FirstOrDefault();
    if (SelectedMeetingPlaybackTrack is null)
    {
        MeetingPlaybackPosition = 0;
        MeetingPlaybackDuration = 0;
        OnPropertyChanged(nameof(MeetingPlaybackTimeLabel));
    }
}

private void ToggleMeetingPlayback_Click(object sender, RoutedEventArgs e)
{
    try
    {
        if (_meetingPlaybackService.State == MeetingPlaybackState.Playing)
        {
            _meetingPlaybackService.Pause();
            _meetingPlaybackTimer.Stop();
        }
        else
        {
            _meetingPlaybackService.Play();
            _meetingPlaybackTimer.Start();
        }
        OnPropertyChanged(nameof(MeetingPlaybackButtonText));
    }
    catch (Exception exception)
    {
        DictationStatus = $"Playback failed: {exception.Message}";
        _toastNotificationService.Show("Playback failed", "The selected track could not play", ToastState.Error, 3600);
    }
}

private void MeetingPlaybackSeek_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
{
    if (_updatingMeetingPlaybackPosition || _meetingPlaybackService.State == MeetingPlaybackState.Empty)
    {
        return;
    }
    _meetingPlaybackService.Seek(TimeSpan.FromSeconds(Math.Max(0, e.NewValue)));
    OnPropertyChanged(nameof(MeetingPlaybackTimeLabel));
}

private void MeetingPlaybackTimer_Tick(object? sender, EventArgs e)
{
    _updatingMeetingPlaybackPosition = true;
    MeetingPlaybackPosition = _meetingPlaybackService.Position.TotalSeconds;
    MeetingPlaybackDuration = _meetingPlaybackService.Duration.TotalSeconds;
    _updatingMeetingPlaybackPosition = false;
    OnPropertyChanged(nameof(MeetingPlaybackTimeLabel));
    if (_meetingPlaybackService.State != MeetingPlaybackState.Playing)
    {
        _meetingPlaybackTimer.Stop();
    }
}

private void OnMeetingPlaybackStateChanged(object? sender, MeetingPlaybackStateChangedEventArgs e)
{
    Dispatcher.BeginInvoke(() =>
    {
        OnPropertyChanged(nameof(MeetingPlaybackButtonText));
        if (e.State == MeetingPlaybackState.Failed)
        {
            _meetingPlaybackTimer.Stop();
            _toastNotificationService.Show("Playback stopped", "Windows audio playback failed", ToastState.Error, 3600);
        }
    });
}

private static string FormatPlaybackTime(TimeSpan value) =>
    value.TotalHours >= 1 ? value.ToString(@"h\:mm\:ss") : value.ToString(@"m\:ss");
private void AddDictionaryEntry_Click(object sender, RoutedEventArgs e)
{
    var phrase = DictionaryPhraseBox.Text.Trim();
    var replacement = DictionaryReplacementBox.Text.Trim();
    if (string.IsNullOrWhiteSpace(phrase) || string.IsNullOrWhiteSpace(replacement))
    {
        DictationStatus = "Dictionary entry needs both fields";
        return;
    }
    DictionaryEntries.Insert(0, new DictionaryEntryItem(
        $"dictentry_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}",
        phrase,
        replacement,
        DictionaryThresholdSlider.Value));
    DictionaryPhraseBox.Text = "";
    DictionaryReplacementBox.Text = "";
    SaveDictionary();
    OnPropertyChanged(nameof(HasDictionaryEntries));
}
private void SaveDictionaryEntry_Click(object sender, RoutedEventArgs e)
{
    SaveDictionary();
    DictationStatus = "Dictionary saved";
    OnPropertyChanged(nameof(HasDictionaryEntries));
}
private async void RefreshRuntimeDiagnostics_Click(object sender, RoutedEventArgs e)
{
    await RefreshRuntimeDiagnosticsAsync();
}
private async void RunBenchmark_Click(object sender, RoutedEventArgs e)
{
    try
    {
        BenchmarkSummary = "Running benchmark...";
        DictationStatus = "Running transcription benchmark";
        _toastNotificationService.Show("Running benchmark", "Using captured local audio", ToastState.Transcribing, 0);
        var report = await _transcriptionBenchmarkService.RunAsync();
        BenchmarkSummary = string.IsNullOrWhiteSpace(report.LaunchRecommendation)
            ? report.Summary
            : $"{report.Summary}{Environment.NewLine}{report.LaunchRecommendation}";
        DictationStatus = "Benchmark complete";
        _toastNotificationService.Show("Benchmark complete", report.Summary, ToastState.Success, 5200);
    }
    catch (Exception exception)
    {
        var error = ConciseUiError(exception);
        BenchmarkSummary = $"Benchmark failed: {error}";
        DictationStatus = $"Benchmark failed: {error}";
        _logService.Error("Transcription benchmark failed.", exception);
        _toastNotificationService.Show("Benchmark failed", error, ToastState.Error, 5200);
    }
}
private void OpenModelCache_Click(object sender, RoutedEventArgs e)
{
    try
    {
        _runtimeDiagnosticsService.OpenModelCacheDirectory();
    }
    catch (Exception exception)
    {
        DictationStatus = $"Could not open model cache: {ConciseUiError(exception)}";
        _logService.Error("Could not open model cache.", exception);
    }
}
private void OpenCleanupModelCache_Click(object sender, RoutedEventArgs e)
{
    try
    {
        NativeTextCleanupService.OpenModelCacheDirectory();
    }
    catch (Exception exception)
    {
        DictationStatus = $"Could not open cleanup cache: {ConciseUiError(exception)}";
        _logService.Error("Could not open cleanup model cache.", exception);
    }
}
private void OpenTranscriptionModelCache_Click(object sender, RoutedEventArgs e)
{
    try
    {
        NativeTranscriptionClient.OpenModelCacheDirectory();
    }
    catch (Exception exception)
    {
        DictationStatus = $"Could not open transcription cache: {ConciseUiError(exception)}";
        _logService.Error("Could not open transcription model cache.", exception);
    }
}
private void OpenDiarizationModelCache_Click(object sender, RoutedEventArgs e)
{
    try
    {
        NativeDiarizationClient.OpenModelCacheDirectory();
    }
    catch (Exception exception)
    {
        DictationStatus = $"Could not open speaker label cache: {ConciseUiError(exception)}";
        _logService.Error("Could not open diarization model cache.", exception);
    }
}
private void OpenAIApiKeyBox_PasswordChanged(object sender, RoutedEventArgs e)
{
    if (_updatingSecretBoxes || sender is not PasswordBox passwordBox)
    {
        return;
    }
    SaveProtectedSecret(SettingsStore.OpenAISecretKey, passwordBox.Password, isOpenAi: true);
}

private void OpenRouterApiKeyBox_PasswordChanged(object sender, RoutedEventArgs e)
{
    if (_updatingSecretBoxes || sender is not PasswordBox passwordBox)
    {
        return;
    }
    SaveProtectedSecret(SettingsStore.OpenRouterSecretKey, passwordBox.Password, isOpenAi: false);
}

private void ClearOpenAIApiKey_Click(object sender, RoutedEventArgs e) =>
    ClearProtectedSecret(SettingsStore.OpenAISecretKey, OpenAIApiKeyBox, isOpenAi: true);

private void ClearOpenRouterApiKey_Click(object sender, RoutedEventArgs e) =>
    ClearProtectedSecret(SettingsStore.OpenRouterSecretKey, OpenRouterApiKeyBox, isOpenAi: false);

private void BrowseHookExecutable_Click(object sender, RoutedEventArgs e)
{
    var dialog = new Microsoft.Win32.OpenFileDialog
    {
        Title = "Choose a post-meeting executable",
        Filter = "Windows executable (*.exe)|*.exe",
        CheckFileExists = true,
        Multiselect = false
    };
    if (dialog.ShowDialog(this) == true)
    {
        PostMeetingHookExecutablePath = Path.GetFullPath(dialog.FileName);
        PostMeetingAutomationStatusText = "Hook executable selected. It remains disabled until you enable it.";
    }
}

private void BrowseAutoExportDirectory_Click(object sender, RoutedEventArgs e)
{
    using var dialog = new System.Windows.Forms.FolderBrowserDialog
    {
        Description = "Choose the user-owned folder for automatic Markdown exports",
        UseDescriptionForTitle = true,
        ShowNewFolderButton = true,
        SelectedPath = IsValidAutoExportDirectory(AutoExportMarkdownDirectory)
            ? AutoExportMarkdownDirectory
            : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
    };
    if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
    {
        AutoExportMarkdownDirectory = Path.GetFullPath(dialog.SelectedPath);
        PostMeetingAutomationStatusText = "Export destination selected. It remains disabled until you enable it.";
    }
}

private async void TestPostMeetingHook_Click(object sender, RoutedEventArgs e)
{
    var validation = PostMeetingAutomationService.ValidateExecutable(PostMeetingHookExecutablePath);
    if (validation is not null)
    {
        PostMeetingAutomationStatusText = validation;
        System.Windows.MessageBox.Show(this, validation, "Post-meeting hook", MessageBoxButton.OK, MessageBoxImage.Warning);
        return;
    }

    PostMeetingAutomationStatusText = "Running a metadata-only synthetic hook test…";
    try
    {
        var result = await _postMeetingAutomationService.TestHookAsync(
            CurrentPostMeetingAutomationOptions(),
            _applicationShutdownCancellation.Token);
        PostMeetingAutomationStatusText = DescribeAutomationResult(result);
        var detail = $"Status: {result.Status}\nAttempts: {result.Attempts}\nExit code: {result.ExitCode?.ToString() ?? "none"}";
        if (!string.IsNullOrWhiteSpace(result.StandardOutput))
            detail += $"\n\nstdout (redacted, bounded):\n{result.StandardOutput}";
        if (!string.IsNullOrWhiteSpace(result.StandardError))
            detail += $"\n\nstderr (redacted, bounded):\n{result.StandardError}";
        if (!string.IsNullOrWhiteSpace(result.Error)) detail += $"\n\n{result.Error}";
        System.Windows.MessageBox.Show(
            this,
            detail,
            "Post-meeting hook test",
            MessageBoxButton.OK,
            result.Completed ? MessageBoxImage.Information : MessageBoxImage.Warning);
    }
    catch (Exception exception)
    {
        PostMeetingAutomationStatusText = $"Hook test failed ({exception.GetType().Name}).";
        System.Windows.MessageBox.Show(
            this,
            PostMeetingAutomationStatusText,
            "Post-meeting hook test",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
    }
}

private void SaveProtectedSecret(string key, string value, bool isOpenAi)
{
    try
    {
        _settingsStore.SaveSecret(key, value);
        if (isOpenAi)
        {
            _openAIApiKey = value;
            OnPropertyChanged(nameof(OpenAIApiKeyStatus));
        }
        else
        {
            _openRouterApiKey = value;
            OnPropertyChanged(nameof(OpenRouterApiKeyStatus));
        }
        SaveSettings();
    }
    catch (Exception exception)
    {
        _logService.Error("Could not update a protected summary-provider credential.", exception);
        _toastNotificationService.Show("Key not saved", "Windows Credential Manager was unavailable.", ToastState.Error, 4200);
    }
}

private void ClearProtectedSecret(string key, PasswordBox passwordBox, bool isOpenAi)
{
    _updatingSecretBoxes = true;
    try
    {
        passwordBox.Clear();
        _settingsStore.SaveSecret(key, null);
        if (isOpenAi)
        {
            _openAIApiKey = "";
            OnPropertyChanged(nameof(OpenAIApiKeyStatus));
        }
        else
        {
            _openRouterApiKey = "";
            OnPropertyChanged(nameof(OpenRouterApiKeyStatus));
        }
        SaveSettings();
        DictationStatus = "Stored API key cleared";
    }
    catch (Exception exception)
    {
        _logService.Error("Could not clear a protected summary-provider credential.", exception);
        _toastNotificationService.Show("Key not cleared", "Windows Credential Manager was unavailable.", ToastState.Error, 4200);
    }
    finally
    {
        _updatingSecretBoxes = false;
    }
}

private void OpenLogs_Click(object sender, RoutedEventArgs e)
{
    try
    {
        _logService.OpenLogDirectory();
    }
    catch (Exception exception)
    {
        DictationStatus = $"Could not open logs: {ConciseUiError(exception)}";
    }
}
private void CleanupCaptures_Click(object sender, RoutedEventArgs e)
{
    var inventory = _captureStorageService.InspectLegacyAndTransientCaptures();
    if (inventory.FileCount == 0)
    {
        System.Windows.MessageBox.Show(
            "No legacy or interrupted capture files were found. Saved meetings, imports, transcripts, settings, models, and last-dictation.wav were not inspected for deletion.",
            "Audio storage",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
        return;
    }

    var confirmed = System.Windows.MessageBox.Show(
        $"Muesli found {inventory.FileCount} legacy or interrupted capture file(s), totaling {FormatByteCount(inventory.TotalBytes)}.\n\nDelete these files? Saved meeting recordings, imported media, transcripts, settings, models, and last-dictation.wav are excluded.",
        "Clean legacy captures",
        MessageBoxButton.YesNo,
        MessageBoxImage.Warning);
    if (confirmed != MessageBoxResult.Yes)
    {
        return;
    }

    var result = _captureStorageService.DeleteLegacyAndTransientCaptures(inventory);
    DictationStatus = result.FailedPaths.Count == 0
        ? $"Deleted {result.DeletedCount} legacy capture file(s)"
        : $"Deleted {result.DeletedCount}; {result.FailedPaths.Count} could not be removed";
    System.Windows.MessageBox.Show(
        result.FailedPaths.Count == 0
            ? $"Deleted {result.DeletedCount} legacy or interrupted capture file(s)."
            : $"Deleted {result.DeletedCount} file(s). {result.FailedPaths.Count} file(s) were busy or inaccessible and were left in place.",
        "Audio storage",
        MessageBoxButton.OK,
        result.FailedPaths.Count == 0 ? MessageBoxImage.Information : MessageBoxImage.Warning);
}

private void OpenPrivacy_Click(object sender, RoutedEventArgs e)
{
    var privacyPath = System.IO.Path.Combine(AppContext.BaseDirectory, "WINDOWS-PRIVACY.md");
    if (!System.IO.File.Exists(privacyPath))
    {
        DictationStatus = "Privacy document is missing from this build";
        return;
    }
    Process.Start(new ProcessStartInfo { FileName = privacyPath, UseShellExecute = true });
}

private static string FormatByteCount(long bytes) => bytes switch
{
    >= 1_073_741_824 => $"{bytes / 1_073_741_824.0:0.0} GB",
    >= 1_048_576 => $"{bytes / 1_048_576.0:0.0} MB",
    >= 1024 => $"{bytes / 1024.0:0.0} KB",
    _ => $"{bytes} B"
};
private async void CheckForUpdates_Click(object sender, RoutedEventArgs e)
{
    if (_isUpdateReady && _pendingUpdate is not null)
    {
        await ApplyPendingUpdateAsync(manualPrompt: true);
        return;
    }

    var manager = EnsureUpdateManager();
    if (manager is null || !manager.IsInstalled)
    {
        DictationStatus = "Opening release page";
        OpenExternalUrl("https://github.com/Muesli-HQ/Muesli-Windows/releases", "Could not open release page");
        return;
    }

    if (_updateCheckInFlight)
    {
        return;
    }

    _updateCheckInFlight = true;
    try
    {
        DictationStatus = "Checking for updates";
        _toastNotificationService.Show("Checking for updates", "Contacting GitHub", ToastState.Transcribing, 0);
        var info = await manager.CheckForUpdatesAsync();
        if (info is null)
        {
            DictationStatus = "Muesli is up to date";
            _toastNotificationService.Show("Up to date", "You're on the latest release.", ToastState.Success, 3600);
            return;
        }

        _toastNotificationService.Show("Downloading update", info.TargetFullRelease.Version.ToString(), ToastState.Transcribing, 0);
        await manager.DownloadUpdatesAsync(info);
        _pendingUpdate = info;
        IsUpdateReady = true;
        _toastNotificationService.Show("Update ready", "Click 'Restart and update' to apply.", ToastState.Success, 4200);
        await ApplyPendingUpdateAsync(manualPrompt: true);
    }
    catch (Exception exception)
    {
        DictationStatus = $"Update check failed: {exception.Message}";
        _logService.Error("Update check failed.", exception);
        _toastNotificationService.Show("Update check failed", exception.Message, ToastState.Error, 5200);
    }
    finally
    {
        _updateCheckInFlight = false;
    }
}

public bool IsUpdateReady
{
    get => _isUpdateReady;
    private set
    {
        if (SetField(ref _isUpdateReady, value))
        {
            OnPropertyChanged(nameof(UpdateButtonLabel));
        }
    }
}

public string UpdateButtonLabel => _isUpdateReady ? "Restart and update" : "Check Now";

private UpdateManager? EnsureUpdateManager()
{
    if (_updateManager is not null)
    {
        return _updateManager;
    }
    try
    {
        var source = new GithubSource("https://github.com/Muesli-HQ/Muesli-Windows", null, false);
        _updateManager = new UpdateManager(source);
        return _updateManager;
    }
    catch (Exception exception)
    {
        _logService.Error("Could not initialize update manager.", exception);
        return null;
    }
}

private void StartBackgroundUpdateCheck()
{
    if (_updateCheckInFlight) return;
    var manager = EnsureUpdateManager();
    if (manager is null || !manager.IsInstalled) return;
    _ = Task.Run(async () =>
    {
        try
        {
            var info = await manager.CheckForUpdatesAsync();
            if (info is null) return;
            await manager.DownloadUpdatesAsync(info);
            await Dispatcher.InvokeAsync(() =>
            {
                _pendingUpdate = info;
                IsUpdateReady = true;
                _toastNotificationService.Show("Update ready",
                    $"v{info.TargetFullRelease.Version} is ready. Restart Muesli to apply.",
                    ToastState.Success, 5200);
            });
        }
        catch (Exception exception)
        {
            _logService.Error("Background update check failed.", exception);
        }
    });
}

private Task ApplyPendingUpdateAsync(bool manualPrompt)
{
    if (_pendingUpdate is null) return Task.CompletedTask;
    if (_updateManager is null) return Task.CompletedTask;

    if (_meetingRecordingCoordinator.IsRecording || _dictationCoordinator.IsBusy)
    {
        if (manualPrompt)
        {
            _toastNotificationService.Show("Update deferred",
                "Stop the active recording or dictation first.", ToastState.Error, 4200);
        }
        return Task.CompletedTask;
    }

    if (manualPrompt)
    {
        var version = _pendingUpdate.TargetFullRelease.Version.ToString();
        var result = System.Windows.MessageBox.Show(
            $"Restart Muesli now to install v{version}?",
            "Update ready",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Question);
        if (result != System.Windows.MessageBoxResult.Yes) return Task.CompletedTask;
    }

    try
    {
        _updateManager.ApplyUpdatesAndRestart(_pendingUpdate);
    }
    catch (Exception exception)
    {
        _logService.Error("Apply update failed.", exception);
        _toastNotificationService.Show("Update failed", exception.Message, ToastState.Error, 5200);
    }
    return Task.CompletedTask;
}
private void Donate_Click(object sender, RoutedEventArgs e)
{
    OpenExternalUrl("https://buymeacoffee.com/phequals7", "Could not open donation link");
}
private void ViewGitHub_Click(object sender, RoutedEventArgs e)
{
    OpenExternalUrl("https://github.com/Muesli-HQ/Muesli-Windows", "Could not open GitHub");
}
private void OpenExternalUrl(string url, string failurePrefix)
{
    try
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = url,
            UseShellExecute = true
        });
    }
    catch (Exception exception)
    {
        DictationStatus = $"{failurePrefix}: {exception.Message}";
        _logService.Error(failurePrefix, exception);
    }
}
private async void ClearModelCache_Click(object sender, RoutedEventArgs e)
{
    var result = System.Windows.MessageBox.Show(
        this,
        "Delete downloaded local model files from the Muesli cache? They will download again when needed.",
        "Clear model cache",
        MessageBoxButton.YesNo,
        MessageBoxImage.Warning);
    if (result != MessageBoxResult.Yes)
    {
        return;
    }
    try
    {
        foreach (var model in TranscriptionModels)
        {
            if (_modelLifecycle.Snapshot(model.Id).DiskSizeBytes > 0)
            {
                await _modelLifecycle.DeleteAsync(model.Id);
            }
        }
        foreach (var model in StreamingModelCatalog.Models)
        {
            if (_streamingModelLifecycle.Snapshot(model.Id).DiskSizeBytes > 0)
            {
                await _streamingModelLifecycle.DeleteAsync(model.Id);
            }
        }
        NativeDiarizationClient.ClearModelCache();
        NativeTextCleanupService.ClearModelCache();
        DictationStatus = "Model cache cleared";
        await RefreshRuntimeDiagnosticsAsync();
    }
    catch (Exception exception)
    {
        DictationStatus = $"Could not clear model cache: {ConciseUiError(exception)}";
        _logService.Error("Could not clear model cache.", exception);
    }
}
private async void DownloadSelectedModel_Click(object sender, RoutedEventArgs e)
{
    await DownloadSelectedModelAsync();
}

private async void PrepareModelItem_Click(object sender, RoutedEventArgs e)
{
    if (sender is FrameworkElement { DataContext: TranscriptionModelItem item })
    {
        await RunModelItemOperationAsync(item, progress => _modelLifecycle.PrepareAsync(item.Id, progress));
    }
}

private void CancelModelItem_Click(object sender, RoutedEventArgs e)
{
    if (sender is FrameworkElement { DataContext: TranscriptionModelItem item })
    {
        _modelLifecycle.Cancel(item.Id);
        DictationStatus = $"Cancelling {item.DisplayName}…";
    }
}

private async void RetryModelItem_Click(object sender, RoutedEventArgs e)
{
    if (sender is FrameworkElement { DataContext: TranscriptionModelItem item })
    {
        await RunModelItemOperationAsync(item, _ => _modelLifecycle.RetryAsync(item.Id));
    }
}

private async void VerifyModelItem_Click(object sender, RoutedEventArgs e)
{
    if (sender is FrameworkElement { DataContext: TranscriptionModelItem item })
    {
        await RunModelItemOperationAsync(item, progress => _modelLifecycle.VerifyAsync(item.Id, progress));
    }
}

private async void DeleteModelItem_Click(object sender, RoutedEventArgs e)
{
    if (sender is not FrameworkElement { DataContext: TranscriptionModelItem item })
    {
        return;
    }

    var confirmation = System.Windows.MessageBox.Show(
        this,
        $"Delete the downloaded {item.DisplayName} files? Role selections will be preserved, but transcription using this model will fail closed until you prepare it again.",
        "Delete transcription model",
        MessageBoxButton.YesNo,
        MessageBoxImage.Warning);
    if (confirmation != MessageBoxResult.Yes)
    {
        return;
    }

    await RunModelItemOperationAsync(item, _ => _modelLifecycle.DeleteAsync(item.Id));
}

private void ModelDiagnosticsItem_Click(object sender, RoutedEventArgs e)
{
    if (sender is FrameworkElement { DataContext: TranscriptionModelItem item })
    {
        System.Windows.MessageBox.Show(this, item.Diagnostics, $"{item.DisplayName} diagnostics", MessageBoxButton.OK, MessageBoxImage.Information);
    }
}

private async Task RunModelItemOperationAsync(
    TranscriptionModelItem item,
    Func<IProgress<ModelDownloadProgress>, Task> operation)
{
    try
    {
        var progress = new Progress<ModelDownloadProgress>(value =>
        {
            item.ProgressText = value.DisplayText;
            DictationStatus = value.DisplayText;
        });
        await operation(progress);
        DictationStatus = $"{item.DisplayName}: {_modelLifecycle.Snapshot(item.Id).StatusText}";
    }
    catch (OperationCanceledException)
    {
        DictationStatus = $"{item.DisplayName} operation cancelled";
    }
    catch (Exception exception)
    {
        var error = ConciseUiError(exception);
        DictationStatus = $"{item.DisplayName}: {error}";
        _logService.Error($"Transcription model operation failed. model={item.Id}", exception);
    }
    finally
    {
        RefreshModelItems();
        await RefreshRuntimeDiagnosticsAsync();
    }
}
private async void DownloadAllModels_Click(object sender, RoutedEventArgs e)
{
    try
    {
        var progress = new Progress<ModelDownloadProgress>(value => DictationStatus = value.DisplayText);
        await _modelLifecycle.PrepareAllSequentialAsync(progress);

        DictationStatus = $"All {TranscriptionModels.Count} transcription models are ready";
        _toastNotificationService.Show("Models ready", $"{TranscriptionModels.Count} native transcription models verified", ToastState.Success, 4200);
        OnPropertyChanged(nameof(SelectedModelCacheStatus));
        await RefreshRuntimeDiagnosticsAsync();
    }
    catch (Exception exception)
    {
        var error = ConciseUiError(exception);
        DictationStatus = $"Model setup stopped: {error}";
        _logService.Error("Prepare all transcription models failed.", exception);
        _toastNotificationService.Show("Model setup stopped", error, ToastState.Error, 5200);
    }
}
private async Task DownloadSelectedModelAsync()
{
    var selected = SelectedTranscriptionModel;
    try
    {
        DictationStatus = $"Preparing {selected.DisplayName}";
        _toastNotificationService.Show("Preparing transcription", selected.DisplayName, ToastState.Transcribing, 0);
        var progress = new Progress<ModelDownloadProgress>(value => DictationStatus = value.DisplayText);
        await _modelLifecycle.PrepareAsync(selected.Id, progress);
        DictationStatus = $"{selected.DisplayName} downloaded and verified; role selections were unchanged";
        _toastNotificationService.Show("Transcription ready", $"{selected.DisplayName} verified", ToastState.Success, 3600);
        OnPropertyChanged(nameof(SelectedModelCacheStatus));
        await RefreshRuntimeDiagnosticsAsync();
    }
    catch (Exception exception)
    {
        var error = ConciseUiError(exception);
        DictationStatus = $"{selected.DisplayName} download failed: {error}";
        _logService.Error($"{selected.DisplayName} model download failed.", exception);
        _toastNotificationService.Show("Model download failed", error, ToastState.Error, 5200);
    }
}
private async Task EnsureBaseModelDownloadedAsync()
{
    if (string.Equals(Environment.GetEnvironmentVariable("MUESLI_SKIP_AUTODOWNLOAD"), "1", StringComparison.Ordinal))
    {
        return;
    }
    var selected = SelectedTranscriptionModel;
    if (_modelLifecycle.Snapshot(selected.Id).Status is TranscriptionModelStatus.Ready or TranscriptionModelStatus.Selected)
    {
        return;
    }
    try
    {
        DictationStatus = $"Downloading {selected.DisplayName}";
        _toastNotificationService.Show("Downloading model", "First-run setup", ToastState.Transcribing, 0);
        var progress = new Progress<ModelDownloadProgress>(value => DictationStatus = value.DisplayText);
        await _modelLifecycle.PrepareAsync(selected.Id, progress);
        DictationStatus = $"{selected.DisplayName} ready";
        _toastNotificationService.Show("Model ready", selected.DisplayName, ToastState.Success, 3600);
        OnPropertyChanged(nameof(SelectedModelCacheStatus));
        await RefreshRuntimeDiagnosticsAsync();
    }
    catch (Exception exception)
    {
        DictationStatus = $"Model download failed: {exception.Message}";
        _logService.Error("Base model auto-download failed.", exception);
        _toastNotificationService.Show("Model download failed", "Retry from Models → Download.", ToastState.Error, 5200);
    }
}
public string SetupReadiness
{
    get => _setupReadiness;
    private set => SetField(ref _setupReadiness, value);
}
private void DeleteDictionaryEntry_Click(object sender, RoutedEventArgs e)
{
    if (sender is FrameworkElement { DataContext: DictionaryEntryItem item })
    {
        DictionaryEntries.Remove(item);
        SaveDictionary();
        OnPropertyChanged(nameof(HasDictionaryEntries));
    }
}
private void DictationsList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
{
    if (sender is System.Windows.Controls.ListBox { SelectedItem: DictationItem item })
    {
        System.Windows.Clipboard.SetText(item.Text);
        DictationStatus = "Copied";
        _toastNotificationService.Show("Copied", item.Text, ToastState.Success);
    }
}
private void MeetingCard_Click(object sender, MouseButtonEventArgs e)
{
    if (sender is FrameworkElement { DataContext: MeetingItem item })
    {
        OpenMeetingDetail(item);
        ShowPage(MeetingsPage, MeetingsNav);
    }
}
private void ShowDictations_Click(object sender, RoutedEventArgs e) => ShowPage(DictationsPage, DictationsNav);
private void ClearSearch_Click(object sender, RoutedEventArgs e) => SearchQuery = "";
private void ShowMeetings_Click(object sender, RoutedEventArgs e)
{
    SaveActiveSpeakerAliases();
    _selectedMeetingFolderId = null;
    _selectedMeeting = null;
    MeetingsBrowserView.Visibility = Visibility.Visible;
    MeetingDetailView.Visibility = Visibility.Collapsed;
    RefreshMeetingViews();
    ShowPage(MeetingsPage, MeetingsNav);
}
private void ShowDictionary_Click(object sender, RoutedEventArgs e) => ShowPage(DictionaryPage, DictionaryNav);
private void ShowModels_Click(object sender, RoutedEventArgs e) => ShowPage(ModelsPage, ModelsNav);
private void ShowShortcuts_Click(object sender, RoutedEventArgs e) => ShowPage(ShortcutsPage, ShortcutsNav);
private void ShowSettings_Click(object sender, RoutedEventArgs e) { OnPropertyChanged(nameof(StartupRegistrationLabel)); OnPropertyChanged(nameof(SetupNeedsResume)); OnPropertyChanged(nameof(SetupResumeLabel)); ShowPage(SettingsPage, SettingsNav); _trayIconService.Refresh(); }
private void ShowAbout_Click(object sender, RoutedEventArgs e) { ShowPage(AboutPage, AboutNav); _trayIconService.Refresh(); }
private void ResumeSetup_Click(object sender, RoutedEventArgs e) => ShowOnboardingIfNeeded(explicitResume: true);
private void RepairStartup_Click(object sender, RoutedEventArgs e)
{
    if (_isVisualPreview)
    {
        DictationStatus = "Preview-only: startup repair is disabled and no registry state was changed.";
        return;
    }
    try { StartupRegistrationService.SetEnabled(true); StartAtLogin = true; DictationStatus = "Startup registration repaired"; }
    catch (Exception exception) { DictationStatus = $"Startup repair failed: {ConciseUiError(exception)}"; }
    finally { OnPropertyChanged(nameof(StartupRegistrationLabel)); }
}
private void FeatureTour_Click(object sender, RoutedEventArgs e) => ShowFeatureTour();
private void ShowFeatureTour() => new FeatureTourWindow(() => { _lastCompletedFeatureTourVersion = FeatureTourWindow.CurrentVersion; SaveSettings(); }) { Owner = this }.Show();
private void SetLightTheme_Click(object sender, RoutedEventArgs e)
{
    SetTheme("light");
}
private void SetDarkTheme_Click(object sender, RoutedEventArgs e)
{
    SetTheme("dark");
}
private void SetTheme(string theme)
{
    try
    {
        _theme = theme;
        ApplyTheme(_theme);
        RefreshNavButtonStyles();
        SaveSettings();
        DictationStatus = theme.Equals("light", StringComparison.OrdinalIgnoreCase)
            ? "Light mode enabled"
            : "Dark mode enabled";
        OnPropertyChanged(nameof(SelectedTheme));
    }
    catch (Exception exception)
    {
        _logService.Error("Theme switch failed.", exception);
        DictationStatus = $"Theme switch failed: {exception.Message}";
        _toastNotificationService.Show("Theme switch failed", exception.Message, ToastState.Error, 4200);
    }
}
private void ToggleMeetings_Click(object sender, RoutedEventArgs e)
{
    _meetingsExpanded = !_meetingsExpanded;
    MeetingsChildren.Visibility = _meetingsExpanded ? Visibility.Visible : Visibility.Collapsed;
    OnPropertyChanged(nameof(MeetingsChevron));
    ShowPage(MeetingsPage, MeetingsNav);
}
private void AddMeetingFolder_Click(object sender, RoutedEventArgs e)
{
    var baseName = "New Folder";
    var index = 1;
    var name = baseName;
    while (MeetingFolders.Any(folder => folder.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
    {
        index++;
        name = $"{baseName} {index}";
    }
    var folder = new MeetingFolderItem($"folder_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}", name);
    MeetingFolders.Add(folder);
    SaveActiveSpeakerAliases();
    _selectedMeetingFolderId = folder.Id;
    _selectedMeeting = null;
    MeetingsBrowserView.Visibility = Visibility.Visible;
    MeetingDetailView.Visibility = Visibility.Collapsed;
    SaveMeetingFolders();
    RefreshMeetingViews();
    ShowPage(MeetingsPage, MeetingsNav);
}
private void ManageTemplates_Click(object sender, RoutedEventArgs e)
{
    var window = new Window
    {
        Owner = this,
        Title = "Manage Templates",
        Width = 760,
        Height = 560,
        MinWidth = 680,
        MinHeight = 480,
        WindowStartupLocation = WindowStartupLocation.CenterOwner,
        Background = (System.Windows.Media.Brush)FindResource("BackgroundBaseBrush"),
        Foreground = (System.Windows.Media.Brush)FindResource("TextPrimaryBrush"),
        FontFamily = FontFamily,
        Content = BuildTemplatesManagerContent()
    };
    window.ShowDialog();
}
private FrameworkElement BuildTemplatesManagerContent()
{
    var root = new DockPanel { Margin = new Thickness(24) };
    var header = new DockPanel { Margin = new Thickness(0, 0, 0, 20) };
    DockPanel.SetDock(header, Dock.Top);
    root.Children.Add(header);
    var done = new WpfButton
    {
        Content = "Done",
        Style = (Style)FindResource("SecondaryButton"),
        Width = 88,
        HorizontalAlignment = System.Windows.HorizontalAlignment.Right
    };
    done.Click += (_, _) => Window.GetWindow(done)?.Close();
    DockPanel.SetDock(done, Dock.Right);
    header.Children.Add(done);
    var create = new WpfButton
    {
        Content = "+ New template",
        Style = (Style)FindResource("SecondaryButton"),
        Width = 128,
        Margin = new Thickness(0, 0, 8, 0),
        HorizontalAlignment = System.Windows.HorizontalAlignment.Right
    };
    DockPanel.SetDock(create, Dock.Right);
    header.Children.Add(create);
    var titleStack = new StackPanel();
    titleStack.Children.Add(new TextBlock
    {
        Text = "Manage Templates",
        FontSize = 22,
        FontWeight = FontWeights.SemiBold,
        Foreground = (System.Windows.Media.Brush)FindResource("TextPrimaryBrush")
    });
    titleStack.Children.Add(new TextBlock
    {
        Text = "Create reusable prompt-based note formats for meetings.",
        Margin = new Thickness(0, 4, 0, 0),
        FontSize = 13,
        Foreground = (System.Windows.Media.Brush)FindResource("TextSecondaryBrush")
    });
    header.Children.Add(titleStack);
    var grid = new Grid();
    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(320) });
    root.Children.Add(grid);
    var templates = new WpfListBox
    {
        ItemsSource = CustomMeetingTemplates,
        Background = System.Windows.Media.Brushes.Transparent,
        Foreground = (System.Windows.Media.Brush)FindResource("TextPrimaryBrush"),
        BorderThickness = new Thickness(0),
        MinHeight = 360,
        ItemContainerStyle = (Style)FindResource("MuesliListBoxItem")
    };
    templates.ItemTemplate = BuildMeetingTemplateItemTemplate();
    var listWrap = new Border
    {
        Background = (System.Windows.Media.Brush)FindResource("BackgroundRaisedBrush"),
        BorderBrush = (System.Windows.Media.Brush)FindResource("BorderBrushSoft"),
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(10),
        Child = templates
    };
    Grid.SetColumn(listWrap, 0);
    grid.Children.Add(listWrap);
    var form = new StackPanel { Margin = new Thickness(16, 0, 0, 0) };
    Grid.SetColumn(form, 1);
    grid.Children.Add(form);
    form.Children.Add(new TextBlock
    {
        Text = "TEMPLATE",
        Style = (Style)FindResource("SectionLabel")
    });
    var editorCard = new Border
    {
        Background = (System.Windows.Media.Brush)FindResource("BackgroundRaisedBrush"),
        BorderBrush = (System.Windows.Media.Brush)FindResource("BorderBrushSoft"),
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(10),
        Padding = new Thickness(16)
    };
    form.Children.Add(editorCard);
    var editor = new StackPanel();
    editorCard.Child = editor;
    var nameBox = new WpfTextBox
    {
        Style = (Style)FindResource("MuesliTextBox"),
        Height = 34
    };
    var promptBox = new WpfTextBox
    {
        Style = (Style)FindResource("MuesliTextBox"),
        Margin = new Thickness(0, 6, 0, 0),
        MinHeight = 180,
        Padding = new Thickness(10),
        AcceptsReturn = true,
        TextWrapping = TextWrapping.Wrap,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto
    };
    editor.Children.Add(new TextBlock { Text = "Name", FontSize = 12, Foreground = (System.Windows.Media.Brush)FindResource("TextSecondaryBrush") });
    editor.Children.Add(nameBox);
    editor.Children.Add(new TextBlock { Text = "Prompt", Margin = new Thickness(0, 12, 0, 0), FontSize = 12, Foreground = (System.Windows.Media.Brush)FindResource("TextSecondaryBrush") });
    editor.Children.Add(promptBox);
    templates.SelectionChanged += (_, _) =>
    {
        if (templates.SelectedItem is not MeetingTemplateItem template)
        {
            return;
        }
        nameBox.Text = template.Name;
        promptBox.Text = template.Prompt;
    };
    var actions = new StackPanel { Orientation = WpfOrientation.Horizontal, Margin = new Thickness(0, 14, 0, 0), HorizontalAlignment = System.Windows.HorizontalAlignment.Right };
    editor.Children.Add(actions);
    create.Click += (_, _) =>
    {
        nameBox.Text = "";
        promptBox.Text = "";
        templates.SelectedItem = null;
        nameBox.Focus();
    };
    var cancel = new WpfButton { Content = "Cancel", Style = (Style)FindResource("GhostButton") };
    cancel.Click += (_, _) =>
    {
        nameBox.Text = "";
        promptBox.Text = "";
        templates.SelectedItem = null;
    };
    actions.Children.Add(cancel);
    var save = new WpfButton { Content = "Save changes", Margin = new Thickness(8, 0, 0, 0), Style = (Style)FindResource("SecondaryButton") };
    save.Click += (_, _) =>
    {
        var name = nameBox.Text.Trim();
        var prompt = promptBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(prompt))
        {
            _toastNotificationService.Show("Template needs name and prompt", "Enter both fields", ToastState.Error, 3200);
            return;
        }
        if (templates.SelectedItem is MeetingTemplateItem existing)
        {
            existing.Name = name;
            existing.Prompt = prompt;
            templates.Items.Refresh();
        }
        else
        {
            var item = new MeetingTemplateItem(name, prompt);
            CustomMeetingTemplates.Add(item);
            templates.SelectedItem = item;
        }
        SaveMeetingTemplates();
        DictationStatus = "Template saved";
    };
    actions.Children.Add(save);
    var delete = new WpfButton { Content = "Delete", Margin = new Thickness(8, 0, 0, 0), Style = (Style)FindResource("GhostButton"), Foreground = System.Windows.Media.Brushes.IndianRed };
    delete.Click += (_, _) =>
    {
        if (templates.SelectedItem is not MeetingTemplateItem selected)
        {
            return;
        }
        CustomMeetingTemplates.Remove(selected);
        SummaryTemplates.Remove(selected.Name);
        SaveMeetingTemplates();
        nameBox.Text = "";
        promptBox.Text = "";
    };
    actions.Children.Add(delete);
    if (CustomMeetingTemplates.Count == 0)
    {
        nameBox.Text = "";
        promptBox.Text = "";
    }
    else
    {
        templates.SelectedIndex = 0;
    }
    return root;
}
private static DataTemplate BuildMeetingTemplateItemTemplate()
{
    var template = new DataTemplate(typeof(MeetingTemplateItem));
    var border = new FrameworkElementFactory(typeof(Border));
    border.SetValue(Border.PaddingProperty, new Thickness(12));
    border.SetValue(Border.MarginProperty, new Thickness(0, 0, 0, 8));
    border.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
    border.SetResourceReference(Border.BackgroundProperty, "BackgroundRaisedBrush");
    border.SetResourceReference(Border.BorderBrushProperty, "BorderBrushSoft");
    border.SetValue(Border.BorderThicknessProperty, new Thickness(1));
    var stack = new FrameworkElementFactory(typeof(StackPanel));
    stack.SetValue(StackPanel.OrientationProperty, WpfOrientation.Vertical);
    border.AppendChild(stack);
    var title = new FrameworkElementFactory(typeof(TextBlock));
    title.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding(nameof(MeetingTemplateItem.Name)));
    title.SetValue(TextBlock.FontSizeProperty, 12.0);
    title.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);
    title.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");
    stack.AppendChild(title);
    var prompt = new FrameworkElementFactory(typeof(TextBlock));
    prompt.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding(nameof(MeetingTemplateItem.Prompt)));
    prompt.SetValue(TextBlock.MarginProperty, new Thickness(0, 4, 0, 0));
    prompt.SetValue(TextBlock.FontSizeProperty, 12.0);
    prompt.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap);
    prompt.SetValue(TextBlock.MaxHeightProperty, 38.0);
    prompt.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
    prompt.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
    stack.AppendChild(prompt);
    template.VisualTree = border;
    return template;
}
private void SelectMeetingFolder_Click(object sender, RoutedEventArgs e)
{
    if (sender is not FrameworkElement { DataContext: MeetingFolderItem folder })
    {
        return;
    }
    _selectedMeetingFolderId = folder.Id;
    _selectedMeeting = null;
    MeetingsBrowserView.Visibility = Visibility.Visible;
    MeetingDetailView.Visibility = Visibility.Collapsed;
    RefreshMeetingViews();
    ShowPage(MeetingsPage, MeetingsNav);
}
private void RenameMeetingFolder_Click(object sender, RoutedEventArgs e)
{
    if (sender is not FrameworkElement { DataContext: MeetingFolderItem folder })
    {
        return;
    }
    folder.IsRenaming = true;
}
private void FolderNameBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
{
    if (sender is not FrameworkElement { DataContext: MeetingFolderItem folder })
    {
        return;
    }
    if (e.Key == Key.Enter)
    {
        CommitFolderRename(folder);
        e.Handled = true;
    }
    else if (e.Key == Key.Escape)
    {
        folder.IsRenaming = false;
        e.Handled = true;
    }
}
private void FolderNameBox_LostFocus(object sender, RoutedEventArgs e)
{
    if (sender is FrameworkElement { DataContext: MeetingFolderItem folder } && folder.IsRenaming)
    {
        CommitFolderRename(folder);
    }
}
private void CommitFolderRename(MeetingFolderItem folder)
{
    folder.Name = string.IsNullOrWhiteSpace(folder.Name) ? "New Folder" : folder.Name.Trim();
    folder.IsRenaming = false;
    SaveMeetingFolders();
    RefreshMeetingViews();
}
private void MoveMeetingFolderUp_Click(object sender, RoutedEventArgs e)
{
    if (sender is FrameworkElement { DataContext: MeetingFolderItem folder })
    {
        MoveMeetingFolder(folder, -1);
    }
}
private void MoveMeetingFolderDown_Click(object sender, RoutedEventArgs e)
{
    if (sender is FrameworkElement { DataContext: MeetingFolderItem folder })
    {
        MoveMeetingFolder(folder, 1);
    }
}
private void MoveMeetingFolder(MeetingFolderItem folder, int direction)
{
    var oldIndex = MeetingFolders.IndexOf(folder);
    var newIndex = oldIndex + direction;
    if (oldIndex < 0 || newIndex < 0 || newIndex >= MeetingFolders.Count)
    {
        return;
    }
    MeetingFolders.Move(oldIndex, newIndex);
    SaveMeetingFolders();
    RefreshMeetingViews();
}
private void DeleteMeetingFolder_Click(object sender, RoutedEventArgs e)
{
    if (sender is not FrameworkElement { DataContext: MeetingFolderItem folder })
    {
        return;
    }
    var result = System.Windows.MessageBox.Show(
        this,
        "Delete this folder? Meetings inside it will stay in All Meetings.",
        "Delete folder",
        MessageBoxButton.YesNo,
        MessageBoxImage.Question);
    if (result != MessageBoxResult.Yes)
    {
        return;
    }
    MeetingFolders.Remove(folder);
    for (var index = 0; index < Meetings.Count; index++)
    {
        if (string.Equals(Meetings[index].FolderId, folder.Id, StringComparison.Ordinal))
        {
            Meetings[index] = Meetings[index] with { FolderId = null };
        }
    }
    if (string.Equals(_selectedMeetingFolderId, folder.Id, StringComparison.Ordinal))
    {
        _selectedMeetingFolderId = null;
    }
    SaveMeetingFolders();
    SaveMeetings();
    RefreshMeetingViews();
}
private void ToggleMeetingSort_Click(object sender, RoutedEventArgs e)
{
    _meetingSortNewestFirst = !_meetingSortNewestFirst;
    var sorted = _meetingSortNewestFirst
        ? Meetings.OrderByDescending(item => item.CreatedAt).ToList()
        : Meetings.OrderBy(item => item.CreatedAt).ToList();
    Meetings.Clear();
    foreach (var item in sorted)
    {
        Meetings.Add(item);
    }
    OnPropertyChanged(nameof(MeetingSortLabel));
}
private void OpenButtonContextMenu_Click(object sender, RoutedEventArgs e)
{
    if (sender is System.Windows.Controls.Button button && button.ContextMenu is not null)
    {
        button.ContextMenu.PlacementTarget = button;
        button.ContextMenu.IsOpen = true;
    }
}
private void SetDictationFilter_Click(object sender, RoutedEventArgs e)
{
    if (sender is not System.Windows.Controls.MenuItem { Tag: string filter })
    {
        return;
    }
    _dictationDateFilter = filter;
    FilteredDictations.Refresh();
    OnPropertyChanged(nameof(DictationHeaderLabel));
    OnPropertyChanged(nameof(DictationFilterLabel));
    OnPropertyChanged(nameof(HasDictations));
}
private void SetMeetingFilter_Click(object sender, RoutedEventArgs e)
{
    if (sender is not System.Windows.Controls.MenuItem { Tag: string filter })
    {
        return;
    }
    _meetingDateFilter = filter;
    RefreshMeetingViews();
    OnPropertyChanged(nameof(MeetingFilterLabel));
}
private bool PassesMeetingFolder(object item)
{
    return _selectedMeetingFolderId is null ||
           item is MeetingItem meeting &&
           string.Equals(meeting.FolderId, _selectedMeetingFolderId, StringComparison.Ordinal);
}
private bool PassesSearch(object item)
{
    var query = _searchQuery.Trim();
    if (query.Length == 0)
    {
        return true;
    }
    return item switch
    {
        DictationItem dictation => ContainsSearch(dictation.Text, query) ||
                                  ContainsSearch(dictation.ModelProfile, query),
        MeetingItem meeting => ContainsSearch(meeting.Title, query) ||
                               ContainsSearch(meeting.Summary, query) ||
                               ContainsSearch(meeting.Transcript, query) ||
                               ContainsSearch(meeting.Metadata, query),
        _ => true
    };
}
private static bool ContainsSearch(string value, string query)
{
    return value.Contains(query, StringComparison.OrdinalIgnoreCase);
}
private void RefreshMeetingViews()
{
    UpdateMeetingFolderCounts();
    FilteredMeetings.Refresh();
    OnPropertyChanged(nameof(MeetingCount));
    OnPropertyChanged(nameof(VisibleMeetingCount));
    OnPropertyChanged(nameof(CurrentMeetingFolderName));
    OnPropertyChanged(nameof(HasMeetings));
}
private void UpdateMeetingFolderCounts()
{
    foreach (var folder in MeetingFolders)
    {
        folder.Count = Meetings.Count(meeting => string.Equals(meeting.FolderId, folder.Id, StringComparison.Ordinal));
    }
}
private static bool PassesDateFilter(object item, string filter)
{
    if (filter == "all")
    {
        return true;
    }
    var date = item switch
    {
        DictationItem dictation => dictation.Timestamp,
        MeetingItem meeting => meeting.CreatedAt,
        _ => DateTime.MinValue
    };
    return date >= DateTime.Now - FilterWindow(filter);
}
private static TimeSpan FilterWindow(string filter)
{
    return filter switch
    {
        "last2Days" => TimeSpan.FromDays(2),
        "lastWeek" => TimeSpan.FromDays(7),
        "last2Weeks" => TimeSpan.FromDays(14),
        "lastMonth" => TimeSpan.FromDays(31),
        "last3Months" => TimeSpan.FromDays(93),
        _ => TimeSpan.MaxValue
    };
}
private static string FilterLabel(string filter)
{
    return filter switch
    {
        "last2Days" => "Last 2 days",
        "lastWeek" => "Last week",
        "last2Weeks" => "Last 2 weeks",
        "lastMonth" => "Last month",
        "last3Months" => "Last 3 months",
        _ => "All time"
    };
}
private string NormalizeSummaryTemplateName(string? value)
{
    var normalized = MeetingSummaryService.NormalizeTemplateName(value);
    if (MeetingSummaryService.IsBuiltInTemplate(normalized))
    {
        return normalized;
    }

    var custom = SummaryTemplates.FirstOrDefault(template => template.Equals(normalized, StringComparison.OrdinalIgnoreCase));
    return custom ?? "Standard Meeting Notes";
}
private void ShowPage(UIElement activePage, System.Windows.Controls.Button activeNav)
{
    foreach (var page in new UIElement[]
             {
                 DictationsPage,
                 SearchPage,
                 MeetingsPage,
                 DictionaryPage,
                 ModelsPage,
                 ShortcutsPage,
                 SettingsPage,
                 AboutPage
             })
    {
        page.Visibility = page == activePage ? Visibility.Visible : Visibility.Collapsed;
    }
    if (activePage != SearchPage)
    {
        _lastNonSearchPage = activePage;
        _lastNonSearchNav = activeNav;
    }
    foreach (var button in new[]
             {
                 DictationsNav,
                 MeetingsNav,
                 AllMeetingsNav,
                 DictionaryNav,
                 ModelsNav,
                 ShortcutsNav,
                 SettingsNav,
                 AboutNav
             })
    {
        var isActive = button == activeNav;
        button.Background = isActive
            ? (System.Windows.Media.Brush)FindResource("SurfaceSelectedBrush")
            : System.Windows.Media.Brushes.Transparent;
        button.Foreground = isActive
            ? (System.Windows.Media.Brush)FindResource("TextPrimaryBrush")
            : (System.Windows.Media.Brush)FindResource("TextSecondaryBrush");
    }
}
private void RefreshNavButtonStyles()
{
    if (DictationsPage.Visibility == Visibility.Visible)
        ShowPage(DictationsPage, DictationsNav);
    else if (MeetingsPage.Visibility == Visibility.Visible)
        ShowPage(MeetingsPage, MeetingsNav);
    else if (DictionaryPage.Visibility == Visibility.Visible)
        ShowPage(DictionaryPage, DictionaryNav);
    else if (ModelsPage.Visibility == Visibility.Visible)
        ShowPage(ModelsPage, ModelsNav);
    else if (ShortcutsPage.Visibility == Visibility.Visible)
        ShowPage(ShortcutsPage, ShortcutsNav);
    else if (SettingsPage.Visibility == Visibility.Visible)
        ShowPage(SettingsPage, SettingsNav);
    else if (AboutPage.Visibility == Visibility.Visible)
        ShowPage(AboutPage, AboutNav);
    else if (SearchPage.Visibility == Visibility.Visible)
        ShowPage(SearchPage, _lastNonSearchNav ?? DictationsNav);
}
private void UpdateSearchPageVisibility()
{
    if (!string.IsNullOrWhiteSpace(SearchQuery))
    {
        if (SearchPage.Visibility != Visibility.Visible)
        {
            ShowPage(SearchPage, _lastNonSearchNav ?? DictationsNav);
        }
        return;
    }
    if (SearchPage.Visibility == Visibility.Visible)
    {
        ShowPage(_lastNonSearchPage ?? DictationsPage, _lastNonSearchNav ?? DictationsNav);
    }
}
private void OpenSearchFromCompactRail_Click(object sender, RoutedEventArgs e)
{
    ShowPage(SearchPage, _lastNonSearchNav ?? DictationsNav);
    Dispatcher.BeginInvoke(() =>
    {
        MainSearchInput.Focus();
        Keyboard.Focus(MainSearchInput);
    });
}
private void RefreshSearchResults()
{
    SearchDictationResults.Refresh();
    SearchMeetingResults.Refresh();
    OnPropertyChanged(nameof(SearchDictationCount));
    OnPropertyChanged(nameof(SearchMeetingCount));
    OnPropertyChanged(nameof(SearchResultsSummary));
    OnPropertyChanged(nameof(HasSearchResults));
}
private void Minimize_Click(object sender, RoutedEventArgs e)
{
    WindowState = WindowState.Minimized;
}
private void MaximizeRestore_Click(object sender, RoutedEventArgs e)
{
    if (_isWorkAreaMaximized)
    {
        RestoreFromWorkAreaMaximize();
        return;
    }
    MaximizeToWorkArea();
}
private void MaximizeToWorkArea()
{
    if (WindowState == WindowState.Minimized)
    {
        WindowState = WindowState.Normal;
    }
    _restoreBounds = new Rect(Left, Top, Width, Height);
    var area = WindowPlacementService.GetWorkAreaForWindow(this);
    WindowState = WindowState.Normal;
    Left = area.Left;
    Top = area.Top;
    Width = area.Width;
    Height = area.Height;
    _isWorkAreaMaximized = true;
}
private void RestoreFromWorkAreaMaximize()
{
    WindowState = WindowState.Normal;
    var restored = WindowPlacementService.ClampToVisibleWorkArea(this, _restoreBounds);
    Left = restored.Left;
    Top = restored.Top;
    Width = restored.Width;
    Height = restored.Height;
    _isWorkAreaMaximized = false;
}
private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
{
    IsCompactLayout = e.NewSize.Width < 900;
}
private void Window_Activated(object? sender, EventArgs e)
{
    if (_isVisualPreview) return;
    // Activation is a read-only reconciliation: external Windows changes must never trigger a registry write.
    var registered = StartupRegistrationService.IsEnabled();
    if (_startAtLogin != registered)
    {
        _startAtLogin = registered;
        OnPropertyChanged(nameof(StartAtLogin));
    }
    OnPropertyChanged(nameof(StartupRegistrationLabel));
    OnPropertyChanged(nameof(SetupNeedsResume));
    OnPropertyChanged(nameof(SetupResumeLabel));
    _trayIconService.Refresh();
}
private void Close_Click(object sender, RoutedEventArgs e)
{
    if (_isVisualPreview) { Close(); return; }
    Hide();
}
private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
{
    if (e.ButtonState == MouseButtonState.Pressed && e.GetPosition(this).Y < 48)
    {
        DragMove();
    }
}
private string? PromptForText(string title, string label, string initialValue)
{
    var dialog = new Window
    {
        Owner = this,
        Title = title,
        Width = 360,
        Height = 170,
        WindowStartupLocation = WindowStartupLocation.CenterOwner,
        ResizeMode = ResizeMode.NoResize,
        Background = (System.Windows.Media.Brush)FindResource("BackgroundBaseBrush")
    };
    var input = new System.Windows.Controls.TextBox
    {
        Text = initialValue,
        Height = 36,
        Padding = new Thickness(10, 7, 10, 7),
        Foreground = (System.Windows.Media.Brush)FindResource("TextPrimaryBrush"),
        Background = (System.Windows.Media.Brush)FindResource("BackgroundHoverBrush"),
        BorderBrush = (System.Windows.Media.Brush)FindResource("BorderBrushSoft")
    };
    var save = new System.Windows.Controls.Button
    {
        Content = "Save",
        Width = 90,
        Height = 32,
        Margin = new Thickness(8, 0, 0, 0),
        Style = (Style)FindResource("PrimaryButton")
    };
    var cancel = new System.Windows.Controls.Button
    {
        Content = "Cancel",
        Width = 90,
        Height = 32,
        Style = (Style)FindResource("GhostButton")
    };
    save.Click += (_, _) => dialog.DialogResult = true;
    cancel.Click += (_, _) => dialog.DialogResult = false;
    var buttons = new StackPanel
    {
        Orientation = System.Windows.Controls.Orientation.Horizontal,
        HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
        Margin = new Thickness(0, 16, 0, 0)
    };
    buttons.Children.Add(cancel);
    buttons.Children.Add(save);
    var content = new StackPanel
    {
        Margin = new Thickness(18)
    };
    content.Children.Add(new TextBlock
    {
        Text = label,
        Foreground = (System.Windows.Media.Brush)FindResource("TextSecondaryBrush"),
        Margin = new Thickness(0, 0, 0, 8)
    });
    content.Children.Add(input);
    content.Children.Add(buttons);
    dialog.Content = content;
    input.SelectAll();
    input.Focus();
    return dialog.ShowDialog() == true ? input.Text : null;
}
private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
{
    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
private static string FirstDiagnosticLine(string? diagnostic)
{
    if (string.IsNullOrWhiteSpace(diagnostic))
    {
        return "";
    }
    var lines = diagnostic
        .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Where(line => line.StartsWith("Native input RMS", StringComparison.OrdinalIgnoreCase) ||
                       line.StartsWith("Native input peak", StringComparison.OrdinalIgnoreCase) ||
                       line.StartsWith("Captured audio bytes", StringComparison.OrdinalIgnoreCase))
        .ToList();
    if (lines.Count == 0)
    {
        return diagnostic.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? "";
    }
    return string.Join(" | ", lines);
}

private static string TraceValue(string? value)
{
    return (value ?? "")
        .Replace(';', ',')
        .Replace('\r', ' ')
        .Replace('\n', ' ')
        .Trim();
}
private static string ConciseUiError(Exception exception)
{
    var message = exception.Message
        .Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .FirstOrDefault() ?? "Something went wrong.";
    return message.Length <= 180 ? message : $"{message[..177]}...";
}
private async Task<string> CreateMeetingSummaryAsync(
    string transcript,
    string title,
    CancellationToken cancellationToken = default)
{
    DictationStatus = SelectedSummaryProvider == "local" ? "Creating local summary" : "Creating AI summary";
    _toastNotificationService.Show("Summarizing meeting", SelectedSummaryProvider, ToastState.Transcribing, 0);
    return await CreateMeetingSummaryWithSettingsAsync(transcript, title, CurrentSettingsSnapshot(), cancellationToken);
}

private async Task<string> CreateMeetingSummaryWithSettingsAsync(
    string transcript,
    string title,
    MuesliSettings settings,
    CancellationToken cancellationToken = default)
{
    if (!await _summaryGate.WaitAsync(0, cancellationToken))
    {
        throw new InvalidOperationException("A meeting summary request is already in progress.");
    }
    try
    {
        _lastSummaryUsedLocalFallback = false;
        var result = await MeetingSummaryService.CreateSummaryResultAsync(
            transcript,
            title,
            settings,
            cancellationToken);
        if (result.UsedLocalFallback)
        {
            _lastSummaryUsedLocalFallback = true;
            DictationStatus = $"{result.Provider} failed; local summary used";
            _toastNotificationService.Show(
                "Local summary used",
                $"{result.Provider} could not create notes. Your transcript was preserved.",
                ToastState.Error,
                5200);
            _logService.Info(
                $"Cloud summary fallback. provider={result.Provider}; reason={result.SafeFailureReason ?? "provider-error"}; localFallback=true");
        }
        return result.Summary;
    }
    finally
    {
        _summaryGate.Release();
    }
}
private async Task RefreshRuntimeDiagnosticsAsync()
{
    try
    {
        RuntimeDiagnostics = "Checking runtime...";
        SetupReadiness = "Checking setup...";
        RuntimeSetupStatus = "";
        var diagnostics = await _runtimeDiagnosticsService.InspectAsync(
            EnableLocalCleanup,
            SelectedTranscriptionModel.Id,
            SelectedFinalMeetingModel.Id,
            SelectedLiveMeetingModel.Id);
        RuntimeDiagnostics = diagnostics.Summary;
        ModelCacheDirectory = diagnostics.ModelCacheDirectory;
        ModelCacheSize = diagnostics.ModelCacheSize;
        ApplyRuntimeDiagnostics(diagnostics);
        _logService.Info($"Runtime diagnostics refreshed. {SetupReadiness.Replace(Environment.NewLine, " | ")}");
        _logService.Info($"Runtime diagnostics details. {diagnostics.Summary.Replace(Environment.NewLine, " | ")}");
    }
    catch (Exception exception)
    {
        var failureStatus = RuntimeStatusMapper.Map(null, exception);
        RuntimeDiagnostics = "Setup check failed. Open logs for details.";
        SetupReadiness = "Setup check failed. Open logs for details.";
        RuntimeSetupStatus = failureStatus.Readiness;
        NativeRuntimeStatus = failureStatus.RuntimeStatus;
        SpeakerDiarizationStatusLabel = "Unknown (diagnostics failed)";
        DictationModelRuntimeStatus = SelectedModelCacheStatus;
        QwenCleanupRuntimeStatus = NativeTextCleanupService.Status(EnableLocalCleanup);
        GpuRuntimeStatus = failureStatus.ProviderStatus;
        DiarizationDependencyStatus = "Unknown (diagnostics failed)";
        DiarizationTokenStatus = "Not required";
        _logService.Error("Runtime diagnostics failed.", exception);
    }
}

private async Task SwitchDictationModelAsync(string modelId)
{
    try
    {
        await _dictationCoordinator.SwitchModelAsync(modelId);
        _logService.Info($"Dictation model role changed. model={modelId}; activation=selection-only; downloadStarted=false");
    }
    catch (Exception exception)
    {
        _logService.Error($"Could not switch the dictation model role to {modelId}.", exception);
        DictationStatus = $"Could not switch dictation model: {ConciseUiError(exception)}";
    }
}

private async Task SwitchFinalMeetingModelAsync(string modelId)
{
    try
    {
        await _meetingTranscriptionClient.SwitchModelAsync(modelId);
        _logService.Info($"Final meeting model role changed. model={modelId}; activation=selection-only; downloadStarted=false");
    }
    catch (Exception exception)
    {
        _logService.Error($"Could not switch the final meeting model role to {modelId}.", exception);
        DictationStatus = $"Could not switch final meeting model: {ConciseUiError(exception)}";
    }
}

private async Task ReleaseTranscriptionModelAsync(string modelId, CancellationToken cancellationToken)
{
    await _dictationCoordinator.ReleaseModelAsync(modelId, cancellationToken);
    await _meetingTranscriptionClient.ReleaseModelAsync(modelId, cancellationToken);
}

private void OnTranscriptionModelChanged(object? sender, string modelId)
{
    if (!Dispatcher.CheckAccess())
    {
        Dispatcher.BeginInvoke(() => OnTranscriptionModelChanged(sender, modelId));
        return;
    }

    OnPropertyChanged(nameof(SelectedModelCacheStatus));
    OnPropertyChanged(nameof(FinalMeetingModelStatus));
    RefreshModelItems();
}

private void RefreshModelItems()
{
    foreach (var item in TranscriptionModelItems)
    {
        item.Apply(_modelLifecycle.Snapshot(item.Id));
    }
    OnPropertyChanged(nameof(SelectedModelCacheStatus));
    OnPropertyChanged(nameof(FinalMeetingModelStatus));
}

private void OnStreamingModelChanged(object? sender, string modelId)
{
    if (!Dispatcher.CheckAccess())
    {
        Dispatcher.BeginInvoke(() => OnStreamingModelChanged(sender, modelId));
        return;
    }
    RefreshStreamingModelItems();
}

private void RefreshStreamingModelItems()
{
    foreach (var item in StreamingModelItems) item.Apply(_streamingModelLifecycle.Snapshot(item.Id));
    OnPropertyChanged(nameof(LiveMeetingModelStatus));
}

private async void PrepareStreamingModelItem_Click(object sender, RoutedEventArgs e)
{
    if (sender is FrameworkElement { DataContext: StreamingModelItem item })
        await RunStreamingModelOperationAsync(item, progress => _streamingModelLifecycle.PrepareAsync(item.Id, progress));
}

private void CancelStreamingModelItem_Click(object sender, RoutedEventArgs e)
{
    if (sender is FrameworkElement { DataContext: StreamingModelItem item }) _streamingModelLifecycle.Cancel(item.Id);
}

private async void RetryStreamingModelItem_Click(object sender, RoutedEventArgs e)
{
    if (sender is FrameworkElement { DataContext: StreamingModelItem item })
        await RunStreamingModelOperationAsync(item, _ => _streamingModelLifecycle.RetryAsync(item.Id));
}

private async void VerifyStreamingModelItem_Click(object sender, RoutedEventArgs e)
{
    if (sender is FrameworkElement { DataContext: StreamingModelItem item })
        await RunStreamingModelOperationAsync(item, progress => _streamingModelLifecycle.VerifyAsync(item.Id, progress));
}

private async void DeleteStreamingModelItem_Click(object sender, RoutedEventArgs e)
{
    if (sender is not FrameworkElement { DataContext: StreamingModelItem item }) return;
    if (System.Windows.MessageBox.Show(this, $"Delete {item.DisplayName}? The live role remains selected but will fail closed until prepared again.", "Delete live model", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
        await RunStreamingModelOperationAsync(item, _ => _streamingModelLifecycle.DeleteAsync(item.Id));
}

private void StreamingModelDiagnosticsItem_Click(object sender, RoutedEventArgs e)
{
    if (sender is FrameworkElement { DataContext: StreamingModelItem item })
        System.Windows.MessageBox.Show(this, item.Diagnostics, $"{item.DisplayName} diagnostics", MessageBoxButton.OK, MessageBoxImage.Information);
}

private async Task RunStreamingModelOperationAsync(StreamingModelItem item, Func<IProgress<ModelDownloadProgress>, Task> operation)
{
    try
    {
        var progress = new Progress<ModelDownloadProgress>(value => { item.ProgressText = value.DisplayText; DictationStatus = value.DisplayText; });
        await operation(progress);
        DictationStatus = $"{item.DisplayName}: {_streamingModelLifecycle.Snapshot(item.Id).StatusText}. Live selection was unchanged.";
    }
    catch (OperationCanceledException) { DictationStatus = $"{item.DisplayName} operation cancelled"; }
    catch (Exception exception)
    {
        DictationStatus = $"{item.DisplayName}: {ConciseUiError(exception)}";
        _logService.Error($"Streaming model operation failed. model={item.Id}", exception);
    }
    finally { RefreshStreamingModelItems(); }
}

private async Task EnsureTranscriptionReadyAsync()
{
    try
    {
        if (!_dictationCoordinator.IsModelReady)
        {
            DictationStatus = $"{_dictationCoordinator.ModelDisplayName} needs preparation in Models";
            _logService.Info($"Dictation model is not ready. model={_dictationCoordinator.ModelId}; automaticDownload=false");
            return;
        }
        var warmup = await _dictationCoordinator.InitializeModelAsync();
        DictationStatus = "Ready";
        _logService.Info(
            $"Transcription warmup complete. engine={_dictationCoordinator.EngineId}; model={_dictationCoordinator.ModelId}; {warmup.Text}; {warmup.Diagnostic?.Replace(Environment.NewLine, " | ")}");
        await RefreshRuntimeDiagnosticsAsync();
    }
    catch (Exception exception)
    {
        DictationStatus = $"Transcription setup failed: {ConciseUiError(exception)}";
        _logService.Error($"{_dictationCoordinator.ModelDisplayName} warmup failed without fallback.", exception);
    }
}

private void ApplyRuntimeDiagnostics(RuntimeDiagnostics diagnostics)
{
    var status = RuntimeStatusMapper.Map(diagnostics);
    NativeRuntimeStatus = status.RuntimeStatus;
    SpeakerDiarizationStatusLabel = diagnostics.DiarizationStatus;
    DictationModelRuntimeStatus = SelectedModelCacheStatus;
    OnPropertyChanged(nameof(DictationModelStatusLabel));
    OnPropertyChanged(nameof(SelectedModelCacheStatus));
    QwenCleanupRuntimeStatus = diagnostics.QwenCleanupStatus;
    OnPropertyChanged(nameof(QwenCleanupStatusLabel));
    GpuRuntimeStatus = status.ProviderStatus;
    DiarizationDependencyStatus = SpeakerDiarizationStatusLabel;
    DiarizationTokenStatus = "Not required";
    RuntimeSetupStatus = status.Readiness;
    SetupReadiness = BuildSetupReadiness(diagnostics);
}
private string BuildSetupReadiness(RuntimeDiagnostics diagnostics)
{
    var lines = new List<string>();
    lines.Add(diagnostics.RuntimeReady
        ? "Native transcription runtime is ready."
        : "Native transcription runtime needs attention.");
    lines.Add(diagnostics.ModelReady
        ? $"{ActiveModelLabel} is cached for offline use."
        : $"{ActiveModelLabel} is not ready. Prepare it explicitly from Models.");
    lines.Add($"Execution provider: {diagnostics.Acceleration} (selected automatically). ");

    lines.Add("Native speaker diarization uses ONNX models and downloads them on first meeting transcription if missing.");
    lines.Add(EnableLocalCleanup
        ? $"Local Qwen cleanup: {NativeTextCleanupService.Status(true)}."
        : "Local Qwen cleanup is disabled. Turn it on in Settings after placing a GGUF model in the native-cleanup cache.");

    return string.Join(Environment.NewLine, lines);
}
private static bool ShouldUseSavedMicrophone(string? microphoneName)
{
    return !string.IsNullOrWhiteSpace(microphoneName);
}

private static bool IsValidHookExecutable(string? path)
{
    if (string.IsNullOrWhiteSpace(path) || !Path.IsPathRooted(path)) return false;
    return string.Equals(Path.GetExtension(path), ".exe", StringComparison.OrdinalIgnoreCase) && File.Exists(path);
}

private static bool IsValidAutoExportDirectory(string? path) =>
    !string.IsNullOrWhiteSpace(path) && Path.IsPathRooted(path);

private static string HookTranscriptPolicyDisplay(string? value) => value?.Trim().ToLowerInvariant() switch
{
    "inline" => "Inline transcript",
    "auto-export-path" => "Auto-export path",
    _ => "Metadata only"
};

private static string HookTranscriptPolicySetting(string? value) => value switch
{
    "Inline transcript" => "inline",
    "Auto-export path" => "auto-export-path",
    _ => "metadata-only"
};

private static string AutoExportContentDisplay(string? value) => value?.Trim().ToLowerInvariant() switch
{
    "transcript" => "Transcript",
    "full-meeting" => "Full meeting",
    _ => "Notes"
};

private static string AutoExportContentSetting(string? value) => value switch
{
    "Transcript" => "transcript",
    "Full meeting" => "full-meeting",
    _ => "notes"
};

private static string ComputerUseProviderDisplay(string? value) => value?.Trim().ToLowerInvariant() switch
{
    "openai" => "OpenAI",
    _ => "None"
};

private static string ComputerUseProviderSetting(string? value) => value switch
{
    "OpenAI" => "openai",
    _ => "none"
};

private static string ComputerUseBrowserInterfaceDisplay(string? value) => value?.Trim().ToLowerInvariant() switch
{
    "loopback-devtools" => "Loopback DevTools",
    _ => "Disabled"
};

private static string ComputerUseBrowserInterfaceSetting(string? value) => value switch
{
    "Loopback DevTools" => "loopback-devtools",
    _ => "none"
};

private static string NormalizeAllowlistText(string? value) => string.Join("; ",
    (value ?? "")
    .Split([';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
    .Select(entry => entry.ToLowerInvariant())
    .Distinct(StringComparer.OrdinalIgnoreCase));

private static string[] ParseAllowlist(string? value) => (value ?? "")
    .Split([';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
    .Select(entry => entry.ToLowerInvariant())
    .Distinct(StringComparer.OrdinalIgnoreCase)
    .ToArray();

private bool ComputerUseConfigurationIsReady(out string error)
{
    if (!SelectedComputerUsePlannerProvider.Equals("OpenAI", StringComparison.Ordinal))
    {
        error = "Choose the OpenAI planner provider explicitly.";
        return false;
    }
    if (string.IsNullOrWhiteSpace(ComputerUsePlannerModel))
    {
        error = "Choose a planner model explicitly.";
        return false;
    }
    if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OPENAI_API_KEY")) &&
        string.IsNullOrWhiteSpace(OpenAIApiKey))
    {
        error = "Configure the OpenAI key before enabling Computer Use.";
        return false;
    }
    if (ParseAllowlist(ComputerUseAllowedApplications).Length == 0)
    {
        error = "Allow at least one application before enabling Computer Use.";
        return false;
    }
    if (ComputerUseIncludeWindowText || ComputerUseIncludeBrowserPageText || ComputerUseIncludeScreenshots)
    {
        error = "Text, page-text, and screenshot observation remain unavailable until scoped masking is verified. Leave those privacy options off.";
        return false;
    }
    error = "";
    return true;
}

private void DisableComputerUseIfConfigurationBecameInvalid()
{
    if (!_computerUseEnabled || ComputerUseConfigurationIsReady(out var error)) return;
    _computerUseEnabled = false;
    OnPropertyChanged(nameof(ComputerUseEnabled));
    ComputerUseStatusText = error;
}

private ComputerUsePlannerService CreateComputerUsePlannerService()
{
    Func<ComputerUseWindowTarget?> target = () => _computerUseApprovedTarget;
    var browserSessions = new List<KeyValuePair<string, IExplicitBrowserSession>>();
    var browserContextSessions = new List<LoopbackDevToolsBrowserSession>();
    if (SelectedComputerUseBrowserInterface == "Loopback DevTools" &&
        Uri.TryCreate(ComputerUseBrowserEndpoint, UriKind.Absolute, out var devToolsEndpoint) &&
        devToolsEndpoint.Scheme == Uri.UriSchemeHttp && devToolsEndpoint.IsLoopback)
    {
        foreach (var domain in ParseAllowlist(ComputerUseAllowedBrowserDomains))
        {
            try
            {
                var session = new LoopbackDevToolsBrowserSession(_computerUseHttpClient, devToolsEndpoint.Port, domain);
                browserSessions.Add(new KeyValuePair<string, IExplicitBrowserSession>(
                    domain,
                    session));
                browserContextSessions.Add(session);
            }
            catch (ArgumentException)
            {
                // Invalid domains are omitted from the capability registry and fail closed.
            }
        }
    }
    async Task<string?> CaptureBrowserContextAsync(CancellationToken cancellationToken)
    {
        var approvedTarget = target();
        if (approvedTarget is null || approvedTarget.ApplicationId is not ("chrome" or "msedge")) return null;
        foreach (var session in browserContextSessions)
        {
            var fingerprint = await session.CaptureContextFingerprintAsync(cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrEmpty(fingerprint)) return fingerprint;
        }
        return null;
    }
    var observations = new WindowsUiAutomationObservationSource(
        target,
        browserContextSessions.Count == 0 ? null : CaptureBrowserContextAsync);
    var local = new WindowsUiAutomationLocalAdapter(target);
    IBrowserAutomationAdapter browser = new RegisteredBrowserAutomationAdapter(browserSessions);
    var executor = new WindowsUiAutomationExecutor(browser, local);
    var planner = new OpenAiResponsesPlannerProvider(
        _computerUseHttpClient,
        () => Environment.GetEnvironmentVariable("OPENAI_API_KEY") is { Length: > 0 } environmentKey
            ? environmentKey
            : OpenAIApiKey);
    var status = new DelegateComputerUseStatusSink(value => Dispatcher.BeginInvoke(() =>
    {
        ComputerUseStatusText = value.ActionCount > 0
            ? $"{value.Message} Action {value.ActionNumber} of {value.ActionCount}."
            : value.Message;
    }));
    var confirmation = new DelegateComputerUseConfirmation((action, cancellationToken) =>
        Dispatcher.InvokeAsync(() => ComputerUseConfirmationPrompt.ShowAsync(this, action, cancellationToken)).Task.Unwrap());
    var visualizer = new DelegateComputerUseActionVisualizer((action, targetElement, cancellationToken) =>
        Dispatcher.InvokeAsync(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var location = targetElement is null
                ? ""
                : $" at ({targetElement.Left}, {targetElement.Top})";
            ComputerUseStatusText = $"Proposed {action.Kind}{location}; validating policy and confirmation.";
            _toastNotificationService.Show("Computer Use action", $"{action.Kind}{location}", ToastState.Transcribing, 0);
        }).Task);
    return new ComputerUsePlannerService(observations, planner, executor, confirmation, status, visualizer);
}

private ComputerUseOptions CurrentComputerUseOptions()
{
    var plannerTimeout = TimeSpan.FromSeconds(Math.Clamp(ComputerUsePlannerTimeoutSeconds, 5, 120));
    var perActionTimeout = TimeSpan.FromSeconds(Math.Clamp(ComputerUsePerActionTimeoutSeconds, 1, 30));
    var maximumActions = Math.Clamp(ComputerUseMaximumActionCount, 1, 20);
    var overallSeconds = Math.Clamp(
        (int)plannerTimeout.TotalSeconds + maximumActions * (int)perActionTimeout.TotalSeconds + 30,
        10,
        300);
    return new ComputerUseOptions
    {
        Enabled = ComputerUseEnabled,
        Provider = ComputerUsePlannerProvider.OpenAI,
        Model = ComputerUsePlannerModel,
        PlannerTimeout = plannerTimeout,
        OverallTimeout = TimeSpan.FromSeconds(overallSeconds),
        PerActionTimeout = perActionTimeout,
        MaximumActionCount = maximumActions,
        AllowedApplications = ParseAllowlist(ComputerUseAllowedApplications),
        AllowedBrowserDomains = ParseAllowlist(ComputerUseAllowedBrowserDomains),
        Privacy = new ComputerUsePrivacyOptions
        {
            IncludeWindowText = ComputerUseIncludeWindowText,
            IncludeBrowserPageText = ComputerUseIncludeBrowserPageText,
            IncludeScreenshots = false,
            UserApprovedScreenCapture = false
        }
    };
}

private static string ComputerUseTracePath() => Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
    "muesli",
    "computer-use",
    "trace.json");

private static string DescribeComputerUseResult(ComputerUseRunResult result) => result.Status switch
{
    ComputerUseRunStatus.Completed => $"Computer Use completed {result.Trace.Count} validated action(s).",
    ComputerUseRunStatus.Disabled => "Computer Use is disabled; nothing was planned or executed.",
    ComputerUseRunStatus.Rejected => result.Error ?? "The planner response or context was rejected.",
    ComputerUseRunStatus.Cancelled => "Computer Use was stopped or a required confirmation was declined.",
    ComputerUseRunStatus.TimedOut => "Computer Use timed out; no further actions were sent.",
    ComputerUseRunStatus.StaleObservation => "The target changed; no further actions were sent.",
    _ => result.Error ?? "Computer Use failed safely; no further actions were sent."
};

private MuesliSettings CurrentSettingsSnapshot()
{
    return new MuesliSettings
    {
        Hotkey = SelectedHotkey,
        UserName = UserName,
        PasteBehavior = SelectedPasteBehavior,
        DictationModelId = SelectedTranscriptionModel.Id,
        FinalMeetingModelId = SelectedFinalMeetingModel.Id,
        LiveMeetingModelId = SelectedLiveMeetingModel.Id,
        LiveTranscriptOwnership = LiveTranscriptOwnershipDescriptor.SettingValueFor(SelectedOwnershipMode),
        ShowLiveWaveformOnHover = ShowLiveWaveformOnHover,
        OnboardingCompleted = _onboardingCompleted,
        LastCompletedFeatureTourVersion = _lastCompletedFeatureTourVersion,
        EnableDoubleTapDictation = EnableDoubleTapDictation,
        RemoveFillerWords = RemoveFillerWords,
        EnableLocalCleanup = EnableLocalCleanup,
        StartAtLogin = StartAtLogin,
        AutoMeetingDetectionEnabled = AutoMeetingDetectionEnabled,
        MeetingSummaryProvider = SelectedSummaryProvider,
        OllamaEndpoint = _ollamaEndpoint,
        OllamaModel = _ollamaModel,
        MeetingSummaryTemplate = SelectedSummaryTemplate,
        MeetingSummaryPromptOverride = CustomMeetingTemplates.FirstOrDefault(template =>
            template.Name.Equals(SelectedSummaryTemplate, StringComparison.OrdinalIgnoreCase))?.Prompt ?? "",
        OpenDashboardOnLaunch = OpenDashboardOnLaunch,
        SaveMeetingRecordings = SaveMeetingRecordings,
        PostMeetingHookEnabled = PostMeetingHookEnabled,
        PostMeetingHookExecutablePath = PostMeetingHookExecutablePath,
        PostMeetingHookTranscriptPolicy = HookTranscriptPolicySetting(SelectedHookTranscriptPolicy),
        PostMeetingHookTimeoutSeconds = PostMeetingHookTimeoutSeconds,
        PostMeetingHookMaxAttempts = PostMeetingHookMaxAttempts,
        AutoExportMarkdownEnabled = AutoExportMarkdownEnabled,
        AutoExportMarkdownDirectory = AutoExportMarkdownDirectory,
        AutoExportMarkdownContent = AutoExportContentSetting(SelectedAutoExportContent),
        ComputerUseEnabled = ComputerUseEnabled,
        ComputerUsePlannerProvider = ComputerUseProviderSetting(SelectedComputerUsePlannerProvider),
        ComputerUsePlannerModel = ComputerUsePlannerModel,
        ComputerUsePlannerTimeoutSeconds = ComputerUsePlannerTimeoutSeconds,
        ComputerUsePerActionTimeoutSeconds = ComputerUsePerActionTimeoutSeconds,
        ComputerUseMaximumActionCount = ComputerUseMaximumActionCount,
        ComputerUseAllowedApplications = ComputerUseAllowedApplications,
        ComputerUseAllowedBrowserDomains = ComputerUseAllowedBrowserDomains,
        ComputerUseIncludeWindowText = ComputerUseIncludeWindowText,
        ComputerUseIncludeScreenshots = ComputerUseIncludeScreenshots,
        ComputerUseIncludeBrowserPageText = ComputerUseIncludeBrowserPageText,
        ComputerUseBrowserInterface = ComputerUseBrowserInterfaceSetting(SelectedComputerUseBrowserInterface),
        ComputerUseBrowserEndpoint = ComputerUseBrowserEndpoint,
        ShowFloatingIndicator = ShowFloatingIndicator,
        IndicatorAnchor = SelectedIndicatorPosition,
        ResolvedOpenAIApiKey = OpenAIApiKey,
        OpenAIModel = OpenAIModel,
        ResolvedOpenRouterApiKey = OpenRouterApiKey,
        OpenRouterModel = OpenRouterModel,
        Theme = _theme,
        MicrophoneName = SelectedMicrophone,
        IndicatorLeft = _indicatorLeft,
        IndicatorTop = _indicatorTop,
        CrashReportingEnabled = _crashReportingEnabled,
        CrashReportingPromptShown = _crashReportingPromptShown
    };
}

public bool CrashReportingEnabled
{
    get => _crashReportingEnabled;
    set
    {
        if (SetField(ref _crashReportingEnabled, value))
        {
            OnPropertyChanged(nameof(CrashReportingRestartHintVisible));
            SaveSettings();
        }
    }
}

public System.Windows.Visibility CrashReportingRestartHintVisible =>
    _crashReportingEnabled != _crashReportingStartupValue
        ? System.Windows.Visibility.Visible
        : System.Windows.Visibility.Collapsed;

private PostMeetingAutomationOptions CurrentPostMeetingAutomationOptions() => new()
{
    HookEnabled = PostMeetingHookEnabled,
    HookExecutablePath = PostMeetingHookExecutablePath,
    AutoExportEnabled = AutoExportMarkdownEnabled,
    AutoExportDirectory = AutoExportMarkdownDirectory,
    AutoExportMode = AutoExportContentSetting(SelectedAutoExportContent) switch
    {
        "transcript" => MeetingExportMode.Transcript,
        "full-meeting" => MeetingExportMode.FullMeeting,
        _ => MeetingExportMode.Notes
    },
    TranscriptPolicy = HookTranscriptPolicySetting(SelectedHookTranscriptPolicy) switch
    {
        "inline" => PostMeetingTranscriptPolicy.Inline,
        "auto-export-path" => PostMeetingTranscriptPolicy.AutoExportPath,
        _ => PostMeetingTranscriptPolicy.MetadataOnly
    },
    Timeout = TimeSpan.FromSeconds(Math.Clamp(PostMeetingHookTimeoutSeconds, 1, 600)),
    RetryPolicy = new PostMeetingRetryPolicy
    {
        MaxAttempts = Math.Clamp(PostMeetingHookMaxAttempts, 1, 3),
        Delay = TimeSpan.FromMilliseconds(500)
    }
};

private static string DescribeAutomationResult(PostMeetingAutomationResult result)
{
    var export = result.Export.Completed
        ? "Markdown exported. "
        : result.Export.Requested
            ? "Markdown export failed. "
            : "";
    return result.Status switch
    {
        PostMeetingAutomationStatus.Disabled => "Automation is disabled; no process was launched and no export was written.",
        PostMeetingAutomationStatus.Succeeded => $"{export}Post-meeting automation completed safely.",
        PostMeetingAutomationStatus.TimedOut => $"{export}The hook timed out and its process tree was terminated.",
        PostMeetingAutomationStatus.Cancelled => $"{export}Post-meeting automation was cancelled and its process tree was terminated.",
        PostMeetingAutomationStatus.InvalidConfiguration => $"{export}{result.Error ?? "Automation is disabled until its path is fixed."}",
        _ => $"{export}{result.Error ?? "Post-meeting automation failed; the meeting remains saved."}"
    };
}

private PostMeetingAutomationOptions CurrentPostMeetingAutomationOptions() => new()
{
    HookEnabled = PostMeetingHookEnabled,
    HookExecutablePath = PostMeetingHookExecutablePath,
    AutoExportEnabled = AutoExportMarkdownEnabled,
    AutoExportDirectory = AutoExportMarkdownDirectory,
    AutoExportMode = AutoExportContentSetting(SelectedAutoExportContent) switch
    {
        "transcript" => MeetingExportMode.Transcript,
        "full-meeting" => MeetingExportMode.FullMeeting,
        _ => MeetingExportMode.Notes
    },
    TranscriptPolicy = HookTranscriptPolicySetting(SelectedHookTranscriptPolicy) switch
    {
        "inline" => PostMeetingTranscriptPolicy.Inline,
        "auto-export-path" => PostMeetingTranscriptPolicy.AutoExportPath,
        _ => PostMeetingTranscriptPolicy.MetadataOnly
    },
    Timeout = TimeSpan.FromSeconds(Math.Clamp(PostMeetingHookTimeoutSeconds, 1, 600)),
    RetryPolicy = new PostMeetingRetryPolicy
    {
        MaxAttempts = Math.Clamp(PostMeetingHookMaxAttempts, 1, 3),
        Delay = TimeSpan.FromMilliseconds(500)
    }
};

private static string DescribeAutomationResult(PostMeetingAutomationResult result)
{
    var export = result.Export.Completed
        ? "Markdown exported. "
        : result.Export.Requested
            ? "Markdown export failed. "
            : "";
    return result.Status switch
    {
        PostMeetingAutomationStatus.Disabled => "Automation is disabled; no process was launched and no export was written.",
        PostMeetingAutomationStatus.Succeeded => $"{export}Post-meeting automation completed safely.",
        PostMeetingAutomationStatus.TimedOut => $"{export}The hook timed out and its process tree was terminated.",
        PostMeetingAutomationStatus.Cancelled => $"{export}Post-meeting automation was cancelled and its process tree was terminated.",
        PostMeetingAutomationStatus.InvalidConfiguration => $"{export}{result.Error ?? "Automation is disabled until its path is fixed."}",
        _ => $"{export}{result.Error ?? "Post-meeting automation failed; the meeting remains saved."}"
    };
}
private void OnIndicatorPositionChanged(object? sender, IndicatorPositionChangedEventArgs e)
{
    _indicatorLeft = e.Left;
    _indicatorTop = e.Top;
    _selectedIndicatorPosition = "Custom";
    OnPropertyChanged(nameof(SelectedIndicatorPosition));
    _toastNotificationService.SetIndicatorAnchor("Custom", clearCustomPosition: false);
    SaveSettings();
}
private void ApplyTheme(string theme)
{
    var light = theme.Equals("light", StringComparison.OrdinalIgnoreCase);
    SetColor("BackgroundDeep", light ? "#F5F5F7" : "#111214");
    SetColor("BackgroundBase", light ? "#FFFFFF" : "#161719");
    SetColor("BackgroundRaised", light ? "#F0F0F2" : "#1C1D20");
    SetColor("BackgroundHover", light ? "#E8E8EC" : "#232528");
    SetColor("SurfacePrimary", light ? "#E5E5EA" : "#262830");
    SetColor("SurfaceSelected", light ? "#D6DFFE" : "#2E3340");
    SetColor("AccentBlue", light ? "#2563EB" : "#6BA3F7");
    SetColor("TextPrimary", light ? "#E0000000" : "#EBFFFFFF");
    SetColor("TextSecondary", light ? "#A6000000" : "#9EFFFFFF");
    SetColor("TextTertiary", light ? "#73000000" : "#66FFFFFF");
    SetBrush("BorderBrushSoft", light ? "#14000000" : "#12FFFFFF");
    SetBrush("BorderBrushMedium", light ? "#1F000000" : "#1CFFFFFF");
    SetBrush("PrimaryButtonBackgroundBrush", light ? "#D6DFFE" : "#26364F");
    SetBrush("PrimaryButtonBorderBrush", light ? "#9BB7F5" : "#3C5D8E");
    SetBrush("AccentBadgeBackgroundBrush", light ? "#D6E4FF" : "#226BA3F7");
    SetBrush("SuccessBadgeBackgroundBrush", light ? "#D4F5E0" : "#24342E");
    if (System.Windows.Application.Current?.MainWindow is not null)
    {
        System.Windows.Application.Current.MainWindow.Background = (System.Windows.Media.Brush)System.Windows.Application.Current.Resources["BackgroundDeepBrush"];
    }
    UpdateThemeToggleVisuals(light);
}
private void UpdateThemeToggleVisuals(bool light)
{
    var selected = (System.Windows.Media.Brush)FindResource("SurfaceSelectedBrush");
    var clear = System.Windows.Media.Brushes.Transparent;
    var accent = (System.Windows.Media.Brush)FindResource("AccentBlueBrush");
    var tertiary = (System.Windows.Media.Brush)FindResource("TextTertiaryBrush");
    LightThemeButton.Background = light ? selected : clear;
    LightThemeButton.Foreground = light ? accent : tertiary;
    DarkThemeButton.Background = light ? clear : selected;
    DarkThemeButton.Foreground = light ? tertiary : accent;
}
private static void SetColor(string key, string value)
{
    var color = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(value);
    System.Windows.Application.Current.Resources[key] = color;
    var brushKey = $"{key}Brush";
    SetBrush(brushKey, color);
}
private static void SetBrush(string key, string value)
{
    var color = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(value);
    SetBrush(key, color);
}
private static void SetBrush(string key, System.Windows.Media.Color color)
{
    if (System.Windows.Application.Current.Resources[key] is SolidColorBrush existing)
    {
        if (existing.IsFrozen)
        {
            System.Windows.Application.Current.Resources[key] = new SolidColorBrush(color);
            return;
        }
        existing.Color = color;
        return;
    }
    System.Windows.Application.Current.Resources[key] = new SolidColorBrush(color);
}
private void OnMeetingDetected(object? sender, DetectedMeeting meeting)
{
    if (!AutoMeetingDetectionEnabled)
    {
        _logService.Info($"Meeting detected ({meeting.Platform}) but AutoDetection is disabled.");
        return;
    }
    if (_isMeetingRecording)
    {
        _logService.Info($"Meeting detected ({meeting.Platform}) but already recording.");
        return;
    }
    if (_meetingRecordingCoordinator.IsBusy)
    {
        _logService.Info($"Meeting detected ({meeting.Platform}) but coordinator is busy.");
        return;
    }
    if (_meetingPromptService.IsVisible)
    {
        _logService.Info($"Meeting detected ({meeting.Platform}) but prompt is already visible — resetting.");
        _meetingPromptService.Reset();
        return;
    }
    if (_ignoredMeetingPrompts.TryGetValue(meeting.Key, out var ignoredUntil) && ignoredUntil > DateTime.Now)
    {
        var remaining = (ignoredUntil - DateTime.Now).TotalSeconds;
        _logService.Info($"Meeting detected ({meeting.Platform}) but prompt ignored for {remaining:F0}s more.");
        return;
    }

    _meetingPromptService.Show(
        meeting,
        () => Dispatcher.InvokeAsync(() => ToggleMeetingRecordingAsync(meeting)),
        () =>
        {
            _ignoredMeetingPrompts[meeting.Key] = DateTime.Now.AddMinutes(30);
            // Tell the detector too, otherwise it keeps re-confirming this candidate and the
            // 30-minute window is the only thing suppressing a prompt the user already refused.
            _meetingDetectionService.DismissCandidate(meeting.Key);
            DictationStatus = "Meeting prompt dismissed";
        });
}
    private void OnMeetingDetectionScanCompleted(object? sender, MeetingDetectionScan scan)
    {
        Dispatcher.Invoke(() =>
        {
            _lastMeetingDetectionScan = scan;
            MeetingDetectionStatus = scan.Found
                ? scan.Summary
                : $"No meeting found. {scan.Summary}";
        });
    }

    private void CheckMeetingDetection_Click(object sender, RoutedEventArgs e)
    {
        AutoMeetingDetectionEnabled = true;
        var scan = _meetingDetectionService.CheckNow();
        _lastMeetingDetectionScan = scan;
        MeetingDetectionStatus = scan.Found
            ? scan.Summary
            : $"No meeting found. {scan.Summary}";
        DictationStatus = scan.Found ? "Meeting detected" : "No meeting detected";
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void SaveSettings()
    {
        if (_isVisualPreview) return;
        _settingsStore.Save(CurrentSettingsSnapshot());
    }

    private void LoadPersistedData()
    {
        foreach (var folder in _dataStore.LoadMeetingFolders())
        {
            MeetingFolders.Add(new MeetingFolderItem(folder.Id, folder.Name));
        }

        foreach (var dictation in _dataStore.LoadDictations().OrderByDescending(item => item.Timestamp))
        {
            Dictations.Add(new DictationItem(
                dictation.Id,
                dictation.Timestamp,
                dictation.Timestamp.ToString("hh:mm tt"),
                dictation.Text,
                dictation.ModelProfile,
                dictation.DurationMs));
        }

        foreach (var meeting in _dataStore.LoadMeetings().OrderByDescending(item => item.CreatedAt))
        {
            Meetings.Add(new MeetingItem(
                meeting.Id,
                meeting.Title,
                meeting.CreatedAt,
                meeting.Transcript,
                meeting.Summary,
                meeting.SourcePath,
                meeting.ModelProfile,
                meeting.DurationMs,
                meeting.FolderId,
                meeting.WordCount,
                meeting.TemplateName,
                meeting.SpeakerAliases,
                MeetingRecordingCoordinator.CleanupHealthWarnings(meeting.HealthWarnings, meeting.Transcript),
                meeting.SessionState,
                meeting.MicrophoneAudioPath,
                meeting.SystemAudioPath,
                meeting.SystemCaptureMode,
                meeting.RecoveredFromInterruption,
                meeting.LivePreviewModelId,
                meeting.LiveTranscriptOwnership,
                meeting.FinalTranscriptOwnerModelId,
                meeting.GapRecoveryModelId,
                meeting.ManualNotes,
                meeting.TitleIsManual,
                meeting.AutomationResult));
        }

        foreach (var entry in _dataStore.LoadDictionary())
        {
            DictionaryEntries.Add(new DictionaryEntryItem(entry));
        }
        OnPropertyChanged(nameof(HasDictionaryEntries));

        foreach (var template in _dataStore.LoadMeetingTemplates())
        {
            CustomMeetingTemplates.Add(new MeetingTemplateItem(template));
            if (!SummaryTemplates.Contains(template.Name))
            {
                SummaryTemplates.Add(template.Name);
            }
        }

        UpdateMeetingFolderCounts();
        OnPropertyChanged(nameof(DayStreak));
    }

    private void SaveDictations(bool afterExplicitDeletion = false)
    {
        var dictations = Dictations.Select(item => new PersistedDictation(
            item.Id,
            item.Timestamp,
            item.Text,
            item.DurationMs,
            item.ModelProfile));
        if (afterExplicitDeletion)
        {
            _dataStore.SaveDictationsAfterDeletion(dictations);
        }
        else
        {
            _dataStore.SaveDictations(dictations);
        }
    }

    private void SaveMeetings(bool afterExplicitDeletion = false)
    {
        var meetings = Meetings.Select(item => new PersistedMeeting
        {
            SchemaVersion = AppDataStore.CurrentMeetingSchemaVersion,
            Id = item.Id,
            Title = item.Title,
            CreatedAt = item.CreatedAt,
            DurationMs = item.DurationMs,
            Transcript = item.Transcript,
            Summary = item.Summary,
            SourcePath = item.SourcePath,
            ModelProfile = item.ModelProfile,
            FolderId = item.FolderId,
            WordCount = item.WordCount,
            TemplateName = item.TemplateName,
            SpeakerAliases = item.SpeakerAliases ?? new Dictionary<string, string>(),
            HealthWarnings = MeetingRecordingCoordinator.CleanupHealthWarnings(item.HealthWarnings, item.Transcript),
            SessionState = item.SessionState,
            MicrophoneAudioPath = item.MicrophoneAudioPath,
            SystemAudioPath = item.SystemAudioPath,
            SystemCaptureMode = item.SystemCaptureMode,
            RecoveredFromInterruption = item.RecoveredFromInterruption,
            LivePreviewModelId = item.LivePreviewModelId,
            LiveTranscriptOwnership = item.LiveTranscriptOwnership,
            FinalTranscriptOwnerModelId = item.FinalTranscriptOwnerModelId,
            GapRecoveryModelId = item.GapRecoveryModelId,
            ManualNotes = item.ManualNotes,
            TitleIsManual = item.TitleIsManual,
            AutomationResult = item.AutomationResult
        });
        if (afterExplicitDeletion)
        {
            _dataStore.SaveMeetingsAfterDeletion(meetings);
        }
        else
        {
            _dataStore.SaveMeetings(meetings);
        }
    }

    private static int CountWords(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return 0;
        return text.Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries).Length;
    }

    private int ComputeDayStreak()
    {
        if (Dictations.Count == 0)
            return 0;

        var dates = Dictations
            .Select(d => d.Timestamp.Date)
            .Distinct()
            .OrderBy(d => d)
            .ToList();

        var today = DateTime.Today;
        var anchor = today;
        var lastDate = dates.Last();

        if (lastDate == today)
        {
            anchor = today;
        }
        else if (lastDate == today.AddDays(-1))
        {
            anchor = today.AddDays(-1);
        }
        else
        {
            return 0;
        }

        var dateSet = new HashSet<DateTime>(dates);
        var streak = 0;
        var cursor = anchor;
        while (dateSet.Contains(cursor))
        {
            streak++;
            cursor = cursor.AddDays(-1);
        }

        return streak;
    }

    private static string ApplySpeakerAliases(string transcript, Dictionary<string, string> aliases)
    {
        return SpeakerAliasService.Apply(transcript, aliases);
    }

    private static string ApplySpeakerAliasesToNotes(string notes, Dictionary<string, string> aliases)
    {
        return SpeakerAliasService.Apply(notes, aliases);
    }

    private static List<string> DetectSpeakerLabels(string transcript)
    {
        if (string.IsNullOrWhiteSpace(transcript))
            return new List<string>();

        var labels = new System.Collections.Generic.HashSet<string>();
        var pattern = new System.Text.RegularExpressions.Regex(@"\bSpeaker \d+\b");
        foreach (System.Text.RegularExpressions.Match match in pattern.Matches(transcript))
        {
            labels.Add(match.Value);
        }

        return labels.OrderBy(l => l).ToList();
    }

    private void SaveMeetingFolders()
    {
        _dataStore.SaveMeetingFolders(MeetingFolders.Select(folder => new PersistedMeetingFolder(folder.Id, folder.Name)));
    }

    private void SaveDictionary()
    {
        _dataStore.SaveDictionary(DictionaryEntries.Select(item => item.Record));
    }

    private void SaveMeetingTemplates()
    {
        _dataStore.SaveMeetingTemplates(CustomMeetingTemplates.Select(item => item.Record));
        foreach (var template in CustomMeetingTemplates)
        {
            if (!SummaryTemplates.Contains(template.Name))
            {
                SummaryTemplates.Add(template.Name);
            }
        }
    }

    private static T? FindVisualChild<T>(System.Windows.DependencyObject parent) where T : System.Windows.DependencyObject
    {
        for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
            if (child is T typed)
                return typed;
            var result = FindVisualChild<T>(child);
            if (result != null)
                return result;
        }
        return null;
    }
}

public sealed record DictationItem(
    string Id,
    DateTime Timestamp,
    string Time,
    string Text,
    string ModelProfile,
    int DurationMs)
{
    private DateTime LocalTimestamp => Timestamp.Kind == DateTimeKind.Utc ? Timestamp.ToLocalTime() : Timestamp;

    public string DateGroupLabel => LocalTimestamp.Date switch
    {
        var date when date == DateTime.Today => "TODAY",
        var date when date == DateTime.Today.AddDays(-1) => "YESTERDAY",
        _ => LocalTimestamp.ToString("MMMM d, yyyy")
    };
}

public sealed record MeetingItem(
    string Id,
    string Title,
    DateTime CreatedAt,
    string Transcript,
    string Summary,
    string SourcePath,
    string ModelProfile,
    int DurationMs,
    string? FolderId,
    int WordCount = 0,
    string TemplateName = "",
    Dictionary<string, string>? SpeakerAliases = null,
    List<string>? HealthWarnings = null,
    MeetingSessionState SessionState = MeetingSessionState.Completed,
    string? MicrophoneAudioPath = null,
    string? SystemAudioPath = null,
    string SystemCaptureMode = "legacy-unknown",
    bool RecoveredFromInterruption = false,
    string? LivePreviewModelId = null,
    string LiveTranscriptOwnership = "off",
    string FinalTranscriptOwnerModelId = "",
    string? GapRecoveryModelId = null,
    string ManualNotes = "",
    bool TitleIsManual = false,
    PostMeetingAutomationResult? AutomationResult = null)
{
    public string Metadata => $"{CreatedAt:yyyy-MM-dd HH:mm} • {DurationLabel} • {SessionStateLabel}";
    public string SessionStateLabel => SessionState switch
    {
        MeetingSessionState.Completed => RecoveredFromInterruption ? "Recovered" : "Completed",
        MeetingSessionState.Failed => "Needs attention",
        MeetingSessionState.RecoverableInterruption => "Recoverable",
        MeetingSessionState.Cancelled => "Cancelled",
        _ => SessionState.ToString()
    };

    public string DurationLabel
    {
        get
        {
            var seconds = Math.Max(0, (int)Math.Round(DurationMs / 1000.0));
            if (seconds >= 3600)
            {
                return $"{seconds / 3600}h {(seconds % 3600) / 60}m";
            }

            if (seconds >= 60)
            {
                var minutes = seconds / 60;
                var remainingSeconds = seconds % 60;
                return remainingSeconds == 0 ? $"{minutes}m" : $"{minutes}m {remainingSeconds}s";
            }

            return $"{seconds}s";
        }
    }

    public string PreviewText
    {
        get
        {
            var text = string.IsNullOrWhiteSpace(Summary) ? Transcript : Summary;
            if (string.IsNullOrWhiteSpace(text))
                return "";
            text = text.Trim();
            return text.Length > 200 ? text[..200] + "…" : text;
        }
    }
}

public sealed class MeetingFolderItem : INotifyPropertyChanged
{
    private int _count;
    private bool _isRenaming;
    private string _name;

    public MeetingFolderItem(string id, string name)
    {
        Id = id;
        _name = name;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Id { get; }
    public string Name
    {
        get => _name;
        set
        {
            if (_name == value)
            {
                return;
            }

            _name = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Name)));
        }
    }

    public int Count
    {
        get => _count;
        set
        {
            if (_count == value)
            {
                return;
            }

            _count = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Count)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CountDisplay)));
        }
    }

    public string CountDisplay => Count < 1000 ? Count.ToString() : Count < 10000 ? $"{Count / 1000.0:0.0}k" : $"{Count / 1000}k";

    public bool IsRenaming
    {
        get => _isRenaming;
        set
        {
            if (_isRenaming == value)
            {
                return;
            }

            _isRenaming = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsRenaming)));
        }
    }
}

public sealed record MoveMeetingRequest(string MeetingId, string? FolderId);

public sealed class MeetingTemplateItem
{
    public MeetingTemplateItem(PersistedMeetingTemplate record)
    {
        Id = string.IsNullOrWhiteSpace(record.Id) ? $"template_{Guid.NewGuid():N}" : record.Id;
        Name = record.Name;
        Prompt = record.Prompt;
        Icon = string.IsNullOrWhiteSpace(record.Icon) ? "square.and.pencil" : record.Icon;
    }

    public MeetingTemplateItem(string name, string prompt, string icon = "square.and.pencil")
    {
        Id = $"template_{Guid.NewGuid():N}";
        Name = name;
        Prompt = prompt;
        Icon = icon;
    }

    public string Id { get; }
    public string Name { get; set; }
    public string Prompt { get; set; }
    public string Icon { get; set; }

    public PersistedMeetingTemplate Record => new()
    {
        Id = Id,
        Name = Name,
        Prompt = Prompt,
        Icon = Icon
    };
}

public sealed class DictionaryEntryItem : INotifyPropertyChanged
{
    private string _phrase;
    private string _replacement;
    private double _matchingThreshold;

    public DictionaryEntryItem(DictionaryEntryRecord record)
        : this(record.Id, record.Phrase, record.Replacement, record.MatchingThreshold)
    {
    }

    public DictionaryEntryItem(string id, string phrase, string replacement, double matchingThreshold)
    {
        Id = id;
        _phrase = phrase;
        _replacement = replacement;
        _matchingThreshold = matchingThreshold <= 0 ? 0.85 : matchingThreshold;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Id { get; }

    public string Phrase
    {
        get => _phrase;
        set
        {
            if (_phrase == value)
            {
                return;
            }

            _phrase = value;
            NotifyDictionaryChanged();
        }
    }

    public string Replacement
    {
        get => _replacement;
        set
        {
            if (_replacement == value)
            {
                return;
            }

            _replacement = value;
            NotifyDictionaryChanged();
        }
    }

    public double MatchingThreshold
    {
        get => _matchingThreshold;
        set
        {
            var next = Math.Round(Math.Clamp(value, 0.70, 0.95), 2);
            if (Math.Abs(_matchingThreshold - next) < 0.001)
            {
                return;
            }

            _matchingThreshold = next;
            NotifyDictionaryChanged();
        }
    }

    public string ThresholdDisplay => MatchingThreshold.ToString("0.00");
    public string Display => $"{Phrase} → {Replacement}";

    public DictionaryEntryRecord Record => new()
    {
        Id = Id,
        Phrase = Phrase,
        Replacement = Replacement,
        MatchingThreshold = MatchingThreshold
    };

    private void NotifyDictionaryChanged()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Display)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Record)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ThresholdDisplay)));
    }
}

public sealed class TranscriptionModelItem : INotifyPropertyChanged
{
    private TranscriptionModelSnapshot _snapshot;
    private string _progressText = "";

    public TranscriptionModelItem(TranscriptionModelSnapshot snapshot)
    {
        _snapshot = snapshot;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public string Id => _snapshot.Model.Id;
    public string DisplayName => _snapshot.Model.DisplayName;
    public string Summary => _snapshot.Model.Summary;
    public string Languages => _snapshot.Model.Languages;
    public string DownloadSize => _snapshot.Model.SizeLabel;
    public string StatusText => string.IsNullOrWhiteSpace(ProgressText) ? _snapshot.StatusText : ProgressText;
    public string DiskSize => _snapshot.DiskSize;
    public string Diagnostics => _snapshot.Diagnostics;
    public bool CanPrepare => _snapshot.CanPrepare;
    public bool CanCancel => _snapshot.CanCancel;
    public bool CanRetry => _snapshot.CanRetry;
    public bool CanVerify => _snapshot.CanVerify;
    public bool CanDelete => _snapshot.CanDelete;

    public string ProgressText
    {
        get => _progressText;
        set
        {
            if (_progressText == value) return;
            _progressText = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ProgressText)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StatusText)));
        }
    }

    public void Apply(TranscriptionModelSnapshot snapshot)
    {
        _snapshot = snapshot;
        if (!snapshot.IsBusy)
        {
            _progressText = "";
        }
        foreach (var property in new[]
                 {
                     nameof(StatusText), nameof(DiskSize), nameof(Diagnostics), nameof(CanPrepare),
                     nameof(CanCancel), nameof(CanRetry), nameof(CanVerify), nameof(CanDelete), nameof(ProgressText)
                 })
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
        }
    }
}

public sealed record LiveModelChoice(string? Id, string Label)
{
    public static LiveModelChoice Off { get; } = new(null, LiveTranscriptOwnershipDescriptor.OffOwnerLabel);

    /// <summary>
    /// The shared ComboBox template renders <c>SelectionBoxItem</c> without
    /// <c>SelectionBoxItemTemplate</c>, so the closed picker falls back to <c>ToString()</c>.
    /// Picker records in this app override it for that reason.
    /// </summary>
    public override string ToString() => Label;
}

public sealed class StreamingModelItem : INotifyPropertyChanged
{
    private StreamingModelSnapshot _snapshot;
    private string _progressText = "";
    public StreamingModelItem(StreamingModelSnapshot snapshot) => _snapshot = snapshot;
    public event PropertyChangedEventHandler? PropertyChanged;
    public string Id => _snapshot.Model.Id;
    public string DisplayName => _snapshot.Model.DisplayName;
    public string Summary => _snapshot.Model.Summary;
    public string Languages => _snapshot.Model.Languages;
    public string DownloadSize => _snapshot.Model.SizeLabel;
    public string StatusText => string.IsNullOrWhiteSpace(ProgressText) ? _snapshot.StatusText : ProgressText;
    public string DiskSize => _snapshot.DiskSize;
    public string Diagnostics => _snapshot.Diagnostics;
    public bool CanPrepare => _snapshot.CanPrepare;
    public bool CanCancel => _snapshot.CanCancel;
    public bool CanRetry => _snapshot.CanRetry;
    public bool CanVerify => _snapshot.CanVerify;
    public bool CanDelete => _snapshot.CanDelete;
    public string ProgressText
    {
        get => _progressText;
        set
        {
            if (_progressText == value) return;
            _progressText = value;
            PropertyChanged?.Invoke(this, new(nameof(ProgressText)));
            PropertyChanged?.Invoke(this, new(nameof(StatusText)));
        }
    }
    public void Apply(StreamingModelSnapshot snapshot)
    {
        _snapshot = snapshot;
        if (!snapshot.IsBusy) _progressText = "";
        foreach (var property in new[] { nameof(StatusText), nameof(DiskSize), nameof(Diagnostics), nameof(CanPrepare), nameof(CanCancel), nameof(CanRetry), nameof(CanVerify), nameof(CanDelete), nameof(ProgressText) })
            PropertyChanged?.Invoke(this, new(property));
    }
}
