using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using Muesli.Windows.Services;
using WpfButton = System.Windows.Controls.Button;
using WpfListBox = System.Windows.Controls.ListBox;
using WpfOrientation = System.Windows.Controls.Orientation;
using WpfTextBox = System.Windows.Controls.TextBox;

namespace Muesli.Windows;

public partial class MainWindow : Window, INotifyPropertyChanged
{
    private readonly DictationCoordinator _dictationCoordinator = new();
    private readonly GlobalHotkeyService _globalHotkeyService = new();
    private readonly SettingsStore _settingsStore = new();
    private readonly AppDataStore _dataStore = new();
    private readonly ToastNotificationService _toastNotificationService = new();
    private readonly ActiveAppPasteService _activeAppPasteService = new();
    private readonly TranscriptionWorkerClient _meetingTranscriptionClient = new();
    private readonly MeetingRecordingCoordinator _meetingRecordingCoordinator = new();
    private readonly MeetingDetectionService _meetingDetectionService = new();
    private readonly MeetingPromptService _meetingPromptService = new();
    private readonly TrayIconService _trayIconService = new();
    private readonly RuntimeDiagnosticsService _runtimeDiagnosticsService = new();
    private readonly AppLogService _logService = new();
    private readonly Dictionary<string, DateTime> _ignoredMeetingPrompts = new(StringComparer.OrdinalIgnoreCase);
    private readonly System.Windows.Threading.DispatcherTimer _meetingAutoStopTimer = new()
    {
        Interval = TimeSpan.FromSeconds(4)
    };

    private string _dictationStatus = "Ready";
    private string? _selectedMicrophone;
    private string _selectedAsrEngine = "whisper";
    private string _selectedModelProfile = "base";
    private string _selectedHotkey = "F8";
    private string _selectedPasteBehavior = "active-app";
    private string _selectedSummaryProvider = "local";
    private string _selectedSummaryTemplate = "standard";
    private string _openAIApiKey = "";
    private string _openAIModel = "gpt-5.4-mini";
    private string _openRouterApiKey = "";
    private string _openRouterModel = "stepfun/step-3.5-flash:free";
    private string _theme = "dark";
    private bool _postProcessingEnabled;
    private bool _enableDoubleTapDictation;
    private string _postProcessingPrompt = "";
    private bool _startAtLogin;
    private bool _openDashboardOnLaunch = true;
    private bool _saveMeetingRecordings = true;
    private bool _showFloatingIndicator = true;
    private string _selectedIndicatorPosition = "Top Center";
    private bool _autoMeetingDetectionEnabled = true;
    private bool _onboardingCompleted;
    private IntPtr _pasteTargetWindow = IntPtr.Zero;
    private bool _shouldPasteToActiveApp;
    private bool _meetingsExpanded = true;
    private bool _meetingSortNewestFirst = true;
    private bool _isCapturingHotkey;
    private bool _runtimeStarted;
    private bool _isParkedForBackground;
    private bool _isWorkAreaMaximized;
    private Rect _restoreBounds;
    private DateTime _lastHotkeyTapUtc = DateTime.MinValue;
    private string _dictationDateFilter = "all";
    private string _meetingDateFilter = "all";
    private string _searchQuery = "";
    private UIElement? _lastNonSearchPage;
    private System.Windows.Controls.Button? _lastNonSearchNav;
    private string? _selectedMeetingFolderId;
    private MeetingItem? _selectedMeeting;
    private bool _isMeetingRecording;
    private int _meetingMissingScanCount;
    private string? _currentMeetingTitle;
    private string _runtimeDiagnostics = "Not checked yet.";
    private string _modelCacheDirectory = "";
    private string _modelCacheSize = "0 B";
    private string _setupReadiness = "Setup not checked yet.";
    private string _meetingDetectionStatus = "Meeting detection has not scanned yet.";
    private double? _indicatorLeft;
    private double? _indicatorTop;

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<DictationItem> Dictations { get; } = [];
    public ObservableCollection<MeetingItem> Meetings { get; } = [];
    public ObservableCollection<MeetingFolderItem> MeetingFolders { get; } = [];
    public ObservableCollection<MeetingTemplateItem> CustomMeetingTemplates { get; } = [];
    public ObservableCollection<DictionaryEntryItem> DictionaryEntries { get; } = [];
    public ObservableCollection<string> MicrophoneDevices { get; } = ["System default microphone"];
    public ObservableCollection<string> AsrEngines { get; } = ["whisper", "parakeet-v3"];
    public ObservableCollection<string> ModelProfiles { get; } = ["tiny", "base", "small", "medium", "large-v3-turbo"];
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
    public ObservableCollection<string> IndicatorPositions { get; } = ["Top Left", "Top Center", "Top Right", "Bottom Left", "Bottom Center", "Bottom Right", "Custom"];
    public ObservableCollection<string> ThemeOptions { get; } = ["Light", "Dark"];
    public ObservableCollection<string> SummaryProviders { get; } = ["local", "openai", "openrouter"];
    public ObservableCollection<string> SummaryTemplates { get; } = ["auto", "1 to 1", "customer discovery", "hiring", "stand-up", "weekly team meeting", "standard", "standup", "customer"];
    public ICollectionView FilteredDictations { get; }
    public ICollectionView FilteredMeetings { get; }
    public ICollectionView SearchDictationResults { get; }
    public ICollectionView SearchMeetingResults { get; }

    public string UserGreeting
    {
        get
        {
            var name = Environment.UserName.Trim();
            return string.IsNullOrWhiteSpace(name) ? "" : $"Hi, {name}";
        }
    }

    public int DayStreak => Dictations.Count > 0 ? 1 : 0;
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
    public string DictationHeaderLabel => _dictationDateFilter == "all" ? "TODAY" : FilterLabel(_dictationDateFilter).ToUpperInvariant();
    public string DictationFilterLabel => _dictationDateFilter == "all" ? "" : FilterLabel(_dictationDateFilter);
    public string MeetingFilterLabel => _meetingDateFilter == "all" ? "" : FilterLabel(_meetingDateFilter);
    public string SearchResultsTitle => string.IsNullOrWhiteSpace(SearchQuery) ? "Search" : $"Search results for \"{SearchQuery.Trim()}\"";
    public int SearchDictationCount => SearchDictationResults?.Cast<DictationItem>().Count() ?? 0;
    public int SearchMeetingCount => SearchMeetingResults?.Cast<MeetingItem>().Count() ?? 0;
    public string SearchResultsSummary => $"{SearchDictationCount} dictations · {SearchMeetingCount} meetings";
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
                UpdateSearchPageVisibility();
            }
        }
    }
    public string MeetingRecordingButtonText => _isMeetingRecording ? "Stop recording" : "Record meeting";
    public string ActiveModelLabel => SelectedAsrEngine == "parakeet-v3" ? "Parakeet v3" : $"Whisper {SelectedModelProfile}";
    public string WhisperStatusLabel => SelectedAsrEngine == "whisper" ? "Active" : "Downloaded";
    public string ParakeetStatusLabel => SelectedAsrEngine == "parakeet-v3" ? "Active" : "Optional";
    public string ShortcutModeLabel => EnableDoubleTapDictation ? "Double-tap to start, tap again to stop" : "Hold to record, release to transcribe";
    public string CaptureHotkeyButtonText => _isCapturingHotkey ? "Press shortcut..." : "Record shortcut";
    public string ShortcutCaptureLabel => _isCapturingHotkey
        ? "Press a function key or a modifier shortcut such as Ctrl+Shift+Space. Press Esc to cancel."
        : "Choose a shortcut or record one that is free on this Windows laptop.";
    public string AppVersion => $"v{Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.2.0"}";
    public string SelectedMeetingTitle => _selectedMeeting?.Title ?? "";
    public string SelectedMeetingMetadata => _selectedMeeting?.Metadata ?? "";
    public string SelectedMeetingNotes => string.IsNullOrWhiteSpace(_selectedMeeting?.Summary)
        ? "No generated notes yet. The raw transcript is available below."
        : _selectedMeeting.Summary;
    public string SelectedMeetingTranscript => _selectedMeeting?.Transcript ?? "";
    public string RuntimeDiagnostics
    {
        get => _runtimeDiagnostics;
        private set => SetField(ref _runtimeDiagnostics, value);
    }
    public string ModelCacheDirectory
    {
        get => _modelCacheDirectory;
        private set => SetField(ref _modelCacheDirectory, value);
    }
    public string ModelCacheSize
    {
        get => _modelCacheSize;
        private set => SetField(ref _modelCacheSize, value);
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

    public string SelectedModelProfile
    {
        get => _selectedModelProfile;
        set
        {
            if (SetField(ref _selectedModelProfile, value))
            {
                SaveSettings();
                OnPropertyChanged(nameof(ActiveModelLabel));
            }
        }
    }

    public string SelectedAsrEngine
    {
        get => _selectedAsrEngine;
        set
        {
            if (SetField(ref _selectedAsrEngine, AsrEngines.Contains(value) ? value : "whisper"))
            {
                SaveSettings();
                OnPropertyChanged(nameof(ActiveModelLabel));
                OnPropertyChanged(nameof(WhisperStatusLabel));
                OnPropertyChanged(nameof(ParakeetStatusLabel));
            }
        }
    }

    public string SelectedHotkey
    {
        get => _selectedHotkey;
        set
        {
            var nextHotkey = NormalizeHotkey(value, allowCustom: true);
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

            DictationStatus = EnableDoubleTapDictation ? $"Double-tap {_selectedHotkey} to dictate" : $"Hold {_selectedHotkey} to dictate";
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

    public bool PostProcessingEnabled
    {
        get => _postProcessingEnabled;
        set
        {
            if (SetField(ref _postProcessingEnabled, value))
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
                _lastHotkeyTapUtc = DateTime.MinValue;
                SaveSettings();
                OnPropertyChanged(nameof(ShortcutModeLabel));
                DictationStatus = value ? "Hands-Free Mode enabled" : $"Hold {SelectedHotkey} to dictate";
            }
        }
    }

    public string PostProcessingPrompt
    {
        get => _postProcessingPrompt;
        set
        {
            if (SetField(ref _postProcessingPrompt, value ?? ""))
            {
                SaveSettings();
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
                SaveSettings();
            }
        }
    }

    public string SelectedSummaryTemplate
    {
        get => _selectedSummaryTemplate;
        set
        {
            if (SetField(ref _selectedSummaryTemplate, SummaryTemplates.Contains(value) ? value : "standard"))
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
        set
        {
            if (SetField(ref _openAIApiKey, value))
            {
                SaveSettings();
            }
        }
    }

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
        set
        {
            if (SetField(ref _openRouterApiKey, value))
            {
                SaveSettings();
            }
        }
    }

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
        InitializeComponent();
        FilteredDictations = CollectionViewSource.GetDefaultView(Dictations);
        FilteredDictations.Filter = item => PassesDateFilter(item, _dictationDateFilter) && PassesSearch(item);
        FilteredMeetings = CollectionViewSource.GetDefaultView(Meetings);
        FilteredMeetings.Filter = item => PassesDateFilter(item, _meetingDateFilter) && PassesMeetingFolder(item) && PassesSearch(item);
        SearchDictationResults = new ListCollectionView(Dictations);
        SearchDictationResults.Filter = item => PassesSearch(item) && !string.IsNullOrWhiteSpace(_searchQuery);
        SearchMeetingResults = new ListCollectionView(Meetings);
        SearchMeetingResults.Filter = item => PassesSearch(item) && !string.IsNullOrWhiteSpace(_searchQuery);
        DataContext = this;

        var settings = _settingsStore.Load();
        LoadPersistedData();
        foreach (var microphone in _dictationCoordinator.ListMicrophones())
        {
            if (!MicrophoneDevices.Contains(microphone))
            {
                MicrophoneDevices.Add(microphone);
            }
        }
        _selectedMicrophone = settings.MicrophoneName ?? _dictationCoordinator.PickPreferredMicrophone();
        _selectedAsrEngine = AsrEngines.Contains(settings.AsrEngine) ? settings.AsrEngine : "whisper";
        _selectedModelProfile = ModelProfiles.Contains(settings.ModelProfile) ? settings.ModelProfile : "base";
        _selectedHotkey = NormalizeHotkey(settings.Hotkey, allowCustom: true);
        AddHotkeyOptionIfMissing(_selectedHotkey);
        _selectedPasteBehavior = PasteBehaviors.Contains(settings.PasteBehavior) ? settings.PasteBehavior : "active-app";
        _onboardingCompleted = settings.OnboardingCompleted;
        _selectedSummaryProvider = SummaryProviders.Contains(settings.MeetingSummaryProvider) ? settings.MeetingSummaryProvider : "local";
        _selectedSummaryTemplate = SummaryTemplates.Contains(settings.MeetingSummaryTemplate) ? settings.MeetingSummaryTemplate : "standard";
        _openAIApiKey = settings.OpenAIApiKey;
        _openAIModel = string.IsNullOrWhiteSpace(settings.OpenAIModel) ? "gpt-5.4-mini" : settings.OpenAIModel;
        _openRouterApiKey = settings.OpenRouterApiKey;
        _openRouterModel = string.IsNullOrWhiteSpace(settings.OpenRouterModel) ? "stepfun/step-3.5-flash:free" : settings.OpenRouterModel;
        _theme = settings.Theme.Equals("light", StringComparison.OrdinalIgnoreCase) ? "light" : "dark";
        _postProcessingEnabled = settings.PostProcessingEnabled;
        _enableDoubleTapDictation = settings.EnableDoubleTapDictation;
        _postProcessingPrompt = settings.PostProcessingPrompt;
        _startAtLogin = settings.StartAtLogin && StartupRegistrationService.IsEnabled();
        _openDashboardOnLaunch = settings.OpenDashboardOnLaunch;
        _saveMeetingRecordings = settings.SaveMeetingRecordings;
        _showFloatingIndicator = settings.ShowFloatingIndicator;
        _selectedIndicatorPosition = IndicatorPositions.Contains(settings.IndicatorAnchor) ? settings.IndicatorAnchor : "Top Center";
        _autoMeetingDetectionEnabled = settings.AutoMeetingDetectionEnabled;
        _indicatorLeft = settings.IndicatorLeft;
        _indicatorTop = settings.IndicatorTop;
        _toastNotificationService.SetSavedPosition(_indicatorLeft, _indicatorTop);
        _toastNotificationService.SetIndicatorAnchor(_selectedIndicatorPosition, clearCustomPosition: false);
        _toastNotificationService.SetIdleIndicatorVisible(_showFloatingIndicator, showNow: false);
        _toastNotificationService.ConfigureActions(StopActiveRecordingFromIndicatorAsync, CancelActiveRecordingFromIndicatorAsync);
        _toastNotificationService.PositionChanged += OnIndicatorPositionChanged;
        _meetingDetectionService.ScanCompleted += OnMeetingDetectionScanCompleted;
        _meetingAutoStopTimer.Tick += MeetingAutoStopTimer_Tick;
        _meetingPromptService.Reset();
OnPropertyChanged(nameof(SelectedMicrophone));
    OnPropertyChanged(nameof(SelectedAsrEngine));
    OnPropertyChanged(nameof(SelectedModelProfile));
    OnPropertyChanged(nameof(SelectedHotkey));
    OnPropertyChanged(nameof(SelectedPasteBehavior));
    OnPropertyChanged(nameof(SelectedSummaryProvider));
    OnPropertyChanged(nameof(SelectedSummaryTemplate));
    OnPropertyChanged(nameof(OpenAIApiKey));
    OnPropertyChanged(nameof(OpenAIModel));
    OnPropertyChanged(nameof(OpenRouterApiKey));
    OnPropertyChanged(nameof(OpenRouterModel));
    OnPropertyChanged(nameof(PostProcessingEnabled));
    OnPropertyChanged(nameof(EnableDoubleTapDictation));
    OnPropertyChanged(nameof(PostProcessingPrompt));
    OnPropertyChanged(nameof(SelectedTheme));
    OnPropertyChanged(nameof(StartAtLogin));
    OnPropertyChanged(nameof(OpenDashboardOnLaunch));
    OnPropertyChanged(nameof(SaveMeetingRecordings));
    OnPropertyChanged(nameof(ShowFloatingIndicator));
    OnPropertyChanged(nameof(SelectedIndicatorPosition));
    OnPropertyChanged(nameof(AutoMeetingDetectionEnabled));
    OnPropertyChanged(nameof(MeetingDetectionStatus));
    ApplyTheme(_theme);
    ShowPage(DictationsPage, DictationsNav);
    Loaded += (_, _) => StartRuntime(showOnboarding: !_isParkedForBackground);
    Closing += (_, _) =>
    {
        _logService.Info("Main window closing.");
        _meetingAutoStopTimer.Stop();
        _meetingDetectionService.ScanCompleted -= OnMeetingDetectionScanCompleted;
        _globalHotkeyService.Dispose();
        _meetingDetectionService.Dispose();
        _meetingPromptService.Close();
        _meetingTranscriptionClient.Dispose();
        _meetingRecordingCoordinator.Dispose();
        _trayIconService.Dispose();
    };
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
    _trayIconService.Initialize(this);
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
    FitDashboardToWorkArea();
    Opacity = 1;
    ShowInTaskbar = true;
    Show();
    WindowState = WindowState.Normal;
    Activate();
    ShowOnboardingIfNeeded();
}
private void FitDashboardToWorkArea()
{
    var area = SystemParameters.WorkArea;
    MinWidth = Math.Max(760, Math.Min(980, area.Width - 24));
    MinHeight = Math.Max(480, Math.Min(620, area.Height - 24));
    Width = Math.Min(1240, Math.Max(MinWidth, area.Width - 24));
    Height = Math.Min(820, Math.Max(MinHeight, area.Height - 24));
    Left = area.Left + Math.Max(12, (area.Width - Width) / 2);
    Top = area.Top + Math.Max(12, (area.Height - Height) / 2);
}
private async void HoldToDictate_MouseDown(object sender, MouseButtonEventArgs e)
{
    await StartDictationAsync(shouldPasteToActiveApp: false);
}
private void ShowOnboardingIfNeeded()
{
    if (_onboardingCompleted)
    {
        return;
    }
    var window = new Window
    {
        Owner = this,
        Title = "Welcome to Muesli",
        Width = 720,
        Height = 620,
        MinWidth = 680,
        MinHeight = 560,
        WindowStartupLocation = WindowStartupLocation.CenterOwner,
        ResizeMode = ResizeMode.NoResize,
        Background = (System.Windows.Media.Brush)FindResource("BackgroundBaseBrush"),
        Foreground = (System.Windows.Media.Brush)FindResource("TextPrimaryBrush"),
        FontFamily = FontFamily
    };
    var root = new DockPanel { Margin = new Thickness(28) };
    window.Content = root;
    var footer = new DockPanel { Margin = new Thickness(0, 20, 0, 0) };
    DockPanel.SetDock(footer, Dock.Bottom);
    root.Children.Add(footer);
    var finish = new WpfButton
    {
        Content = "Start using Muesli",
        Style = (Style)FindResource("PrimaryButton"),
        MinWidth = 160,
        HorizontalAlignment = System.Windows.HorizontalAlignment.Right
    };
    DockPanel.SetDock(finish, Dock.Right);
    footer.Children.Add(finish);
    var skip = new WpfButton
    {
        Content = "Skip",
        Style = (Style)FindResource("GhostButton"),
        MinWidth = 80,
        Margin = new Thickness(0, 0, 10, 0),
        HorizontalAlignment = System.Windows.HorizontalAlignment.Right
    };
    DockPanel.SetDock(skip, Dock.Right);
    footer.Children.Add(skip);
    var bodyScroll = new ScrollViewer
    {
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
    };
    root.Children.Add(bodyScroll);
    var body = new StackPanel();
    bodyScroll.Content = body;
    body.Children.Add(new TextBlock
    {
        Text = "Set up local dictation",
        Style = (Style)FindResource("PageTitle")
    });
    body.Children.Add(new TextBlock
    {
        Text = "Choose a microphone, pick a shortcut that is free on this Windows laptop, and verify local model readiness.",
        Style = (Style)FindResource("PageSubtitle")
    });
    var micCombo = new System.Windows.Controls.ComboBox
    {
        ItemsSource = MicrophoneDevices,
        SelectedItem = SelectedMicrophone,
        Style = (Style)FindResource("MuesliComboBox")
    };
    body.Children.Add(BuildOnboardingRow("Microphone", "Used for dictation and meeting recording.", micCombo));
    var hotkeyCombo = new System.Windows.Controls.ComboBox
    {
        ItemsSource = HotkeyOptions,
        SelectedItem = SelectedHotkey,
        Style = (Style)FindResource("MuesliComboBox")
    };
    body.Children.Add(BuildOnboardingRow("Shortcut", "Use an alternate shortcut if F8 is already taken.", hotkeyCombo));
    var startupCheck = new System.Windows.Controls.CheckBox
    {
        Content = "Launch at login",
        IsChecked = StartAtLogin,
        Style = (Style)FindResource("MuesliCheckBox")
    };
    body.Children.Add(BuildOnboardingRow("Startup", "Keep Muesli available from the tray after sign-in.", startupCheck));
    var indicatorCheck = new System.Windows.Controls.CheckBox
    {
        Content = "Show floating indicator",
        IsChecked = ShowFloatingIndicator,
        Style = (Style)FindResource("MuesliCheckBox")
    };
    body.Children.Add(BuildOnboardingRow("Floating indicator", "Mirrors the OG app's small dictation pill.", indicatorCheck));
    var readinessText = new WpfTextBox
    {
        Text = SetupReadiness,
        MinHeight = 116,
        Padding = new Thickness(12),
        TextWrapping = TextWrapping.Wrap,
        AcceptsReturn = true,
        IsReadOnly = true,
        Cursor = System.Windows.Input.Cursors.IBeam,
        Style = (Style)FindResource("MuesliTextBox")
    };
    var readinessActions = new StackPanel { Orientation = WpfOrientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
    var checkSetup = new WpfButton { Content = "Check setup", Style = (Style)FindResource("SecondaryButton") };
    var downloadBase = new WpfButton { Content = "Download base model", Style = (Style)FindResource("PrimaryButton"), Margin = new Thickness(10, 0, 0, 0) };
    readinessActions.Children.Add(checkSetup);
    readinessActions.Children.Add(downloadBase);
    var readinessPanel = new StackPanel();
    readinessPanel.Children.Add(readinessText);
    readinessPanel.Children.Add(readinessActions);
    body.Children.Add(BuildOnboardingRow("Local model readiness", "Whisper base is the recommended CPU fallback for broad laptop support.", readinessPanel));
    checkSetup.Click += async (_, _) =>
    {
        await RefreshRuntimeDiagnosticsAsync();
        readinessText.Text = SetupReadiness;
    };
    downloadBase.Click += async (_, _) =>
    {
        try
        {
            downloadBase.IsEnabled = false;
            DictationStatus = "Downloading base model";
            var result = await _dictationCoordinator.DownloadModelAsync("whisper", "base");
            DictationStatus = result.Text;
            await RefreshRuntimeDiagnosticsAsync();
            readinessText.Text = SetupReadiness;
        }
        catch (Exception exception)
        {
            DictationStatus = $"Base model download failed: {exception.Message}";
            _toastNotificationService.Show("Model download failed", exception.Message, ToastState.Error, 5200);
        }
        finally
        {
            downloadBase.IsEnabled = true;
        }
    };
    finish.Click += (_, _) =>
    {
        SelectedMicrophone = micCombo.SelectedItem as string ?? SelectedMicrophone;
        SelectedHotkey = hotkeyCombo.SelectedItem as string ?? SelectedHotkey;
        StartAtLogin = startupCheck.IsChecked == true;
        ShowFloatingIndicator = indicatorCheck.IsChecked == true;
        _onboardingCompleted = true;
        SaveSettings();
        window.Close();
    };
    skip.Click += (_, _) =>
    {
        _onboardingCompleted = true;
        SaveSettings();
        window.Close();
    };
    window.ShowDialog();
}
private Border BuildOnboardingRow(string title, string description, FrameworkElement control)
{
    var card = new Border
    {
        Style = (Style)FindResource("ContentCard"),
        Margin = new Thickness(0, 0, 0, 14)
    };
    var grid = new Grid();
    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(280) });
    card.Child = grid;
    var copy = new StackPanel { Margin = new Thickness(0, 0, 18, 0) };
    copy.Children.Add(new TextBlock
    {
        Text = title,
        FontSize = 15,
        FontWeight = FontWeights.SemiBold,
        Foreground = (System.Windows.Media.Brush)FindResource("TextPrimaryBrush")
    });
    copy.Children.Add(new TextBlock
    {
        Text = description,
        Margin = new Thickness(0, 5, 0, 0),
        FontSize = 12,
        TextWrapping = TextWrapping.Wrap,
        Foreground = (System.Windows.Media.Brush)FindResource("TextSecondaryBrush")
    });
    grid.Children.Add(copy);
    control.VerticalAlignment = VerticalAlignment.Center;
    Grid.SetColumn(control, 1);
    grid.Children.Add(control);
    return card;
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
        DictationStatus = "Press a function key or include Ctrl, Alt, or Shift";
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
            () => Dispatcher.InvokeAsync(HandleHotkeyUpAsync));
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
    var parts = value.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    if (parts.Length == 0)
    {
        return false;
    }
    var keyPart = parts[^1].Replace(" ", "", StringComparison.OrdinalIgnoreCase);
    if (!Enum.TryParse<Key>(keyPart, ignoreCase: true, out var key) || key == Key.None)
    {
        return false;
    }
    var modifiers = new List<string>();
    foreach (var part in parts.Take(parts.Length - 1))
    {
        if (part.Equals("Ctrl", StringComparison.OrdinalIgnoreCase) ||
            part.Equals("Control", StringComparison.OrdinalIgnoreCase))
        {
            modifiers.Add("Ctrl");
        }
        else if (part.Equals("Shift", StringComparison.OrdinalIgnoreCase))
        {
            modifiers.Add("Shift");
        }
        else if (part.Equals("Alt", StringComparison.OrdinalIgnoreCase))
        {
            modifiers.Add("Alt");
        }
        else
        {
            return false;
        }
    }
    normalized = modifiers.Count == 0 ? key.ToString() : $"{string.Join("+", modifiers)}+{key}";
    return key is >= Key.F1 and <= Key.F24 || modifiers.Count > 0;
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
    if (!EnableDoubleTapDictation)
    {
        await StartHotkeyDictationAsync();
        return;
    }
    if (_dictationCoordinator.IsRecording)
    {
        await StopDictationAsync();
        _lastHotkeyTapUtc = DateTime.MinValue;
        return;
    }
    var now = DateTime.UtcNow;
    if (now - _lastHotkeyTapUtc <= TimeSpan.FromMilliseconds(520))
    {
        _lastHotkeyTapUtc = DateTime.MinValue;
        await StartHotkeyDictationAsync();
        return;
    }
    _lastHotkeyTapUtc = now;
    DictationStatus = "Tap again to start hands-free dictation";
    _toastNotificationService.Show("Tap again", "Double-tap shortcut to start", ToastState.Success, 1300);
}
private async Task HandleHotkeyUpAsync()
{
    if (EnableDoubleTapDictation)
    {
        return;
    }
    await StopDictationAsync();
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
    if (!_dictationCoordinator.IsRecording || _dictationCoordinator.IsBusy)
    {
        return;
    }
    TranscriptionResult result;
    try
    {
        DictationStatus = "Transcribing";
        _toastNotificationService.Show("Transcribing", "Processing local audio", ToastState.Transcribing, 0);
        result = await _dictationCoordinator.StopAsync(new TranscriptionOptions(SelectedAsrEngine, SelectedModelProfile));
    }
    catch (Exception exception)
    {
        DictationStatus = $"Dictation failed: {exception.Message}";
        _logService.Error("Dictation transcription failed.", exception);
        _toastNotificationService.Show("Dictation failed", exception.Message, ToastState.Error);
        return;
    }
    var textToUse = "";
    if (!string.IsNullOrWhiteSpace(result.Text))
    {
        textToUse = DictionaryCorrectionService.Apply(result.Text, DictionaryEntries.Select(entry => entry.Record));
        textToUse = await PostProcessIfEnabledAsync(textToUse, "dictation", _dictationCoordinator.PostProcessAsync);
        Dictations.Insert(0, new DictationItem(
            $"dict_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}",
            DateTime.Now,
            DateTime.Now.ToString("hh:mm tt"),
            textToUse,
            SelectedModelProfile,
            result.DurationMs));
        SaveDictations();
        OnPropertyChanged(nameof(DayStreak));
        OnPropertyChanged(nameof(WordsDictated));
        OnPropertyChanged(nameof(WordsDictatedDisplay));
        OnPropertyChanged(nameof(AverageWpm));
        RefreshSearchResults();
    }
    if (textToUse.Length > 0)
    {
        try
        {
            if (_shouldPasteToActiveApp && SelectedPasteBehavior == "active-app")
            {
                await _activeAppPasteService.PasteTextAsync(textToUse, _pasteTargetWindow);
                DictationStatus = "Pasted";
                _toastNotificationService.Show("Pasted", textToUse, ToastState.Success);
            }
            else
            {
                System.Windows.Clipboard.SetText(textToUse);
                DictationStatus = "Copied";
                _toastNotificationService.Show("Copied", "Transcript copied to clipboard", ToastState.Success);
            }
        }
        catch (Exception exception)
        {
            DictationStatus = $"Dictation ready, paste failed: {exception.Message}";
            _logService.Error("Active-app paste failed after successful dictation.", exception);
            _toastNotificationService.Show("Dictation ready", "Paste failed; transcript is in the app", ToastState.Error);
        }
    }
    else
    {
        var diagnostic = FirstDiagnosticLine(result.Diagnostic);
        DictationStatus = string.IsNullOrWhiteSpace(diagnostic) ? "No speech detected" : $"No speech detected. {diagnostic}";
        _logService.Info($"No speech detected. Diagnostic: {result.Diagnostic}");
        _toastNotificationService.Show("No speech detected", diagnostic, ToastState.Error, 3600);
    }
}
private async Task StopActiveRecordingFromIndicatorAsync()
{
    await Dispatcher.InvokeAsync(async () =>
    {
        if (_isMeetingRecording)
        {
            await ToggleMeetingRecordingAsync(null);
            return;
        }
        await StopDictationAsync();
    });
}
private async Task CancelActiveRecordingFromIndicatorAsync()
{
    await Dispatcher.InvokeAsync(async () =>
    {
        if (_isMeetingRecording)
        {
            await ToggleMeetingRecordingAsync(null);
            return;
        }
        if (!_dictationCoordinator.IsRecording || _dictationCoordinator.IsBusy)
        {
            return;
        }
        await _dictationCoordinator.CancelAsync();
        _pasteTargetWindow = IntPtr.Zero;
        _shouldPasteToActiveApp = false;
        DictationStatus = "Dictation cancelled";
        _toastNotificationService.ShowIdle(SelectedHotkey);
    });
}
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
        var result = await _dictationCoordinator.StopAsync(new TranscriptionOptions(SelectedAsrEngine, SelectedModelProfile));
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
private void DeleteDictation_Click(object sender, RoutedEventArgs e)
{
    if (sender is FrameworkElement { DataContext: DictationItem item })
    {
        Dictations.Remove(item);
        SaveDictations();
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
    OnPropertyChanged(nameof(SelectedMeetingTitle));
    OnPropertyChanged(nameof(SelectedMeetingMetadata));
    OnPropertyChanged(nameof(SelectedMeetingNotes));
    OnPropertyChanged(nameof(SelectedMeetingTranscript));
    MeetingsBrowserView.Visibility = Visibility.Collapsed;
    MeetingDetailView.Visibility = Visibility.Visible;
    ShowMeetingDetailTab(showTranscript: false);
}
private void OpenMeetingAudio(MeetingItem item)
{
    var firstPath = item.SourcePath
        .Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
        .FirstOrDefault();
    if (string.IsNullOrWhiteSpace(firstPath) || !System.IO.File.Exists(firstPath))
    {
        DictationStatus = "Meeting audio file not found";
        _toastNotificationService.Show("Audio not found", item.Title, ToastState.Error);
        return;
    }
    Process.Start(new ProcessStartInfo
    {
        FileName = firstPath,
        UseShellExecute = true
    });
}
private static void DeleteFileIfExists(string? path)
{
    if (string.IsNullOrWhiteSpace(path) || !System.IO.File.Exists(path))
    {
        return;
    }
    try
    {
        System.IO.File.Delete(path);
    }
    catch
    {
        // Recording retention is optional; failed cleanup should not block saving the meeting transcript.
    }
}
private void DeleteMeeting(MeetingItem item)
{
    Meetings.Remove(item);
    SaveMeetings();
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
    _selectedMeeting = null;
    MeetingsBrowserView.Visibility = Visibility.Visible;
    MeetingDetailView.Visibility = Visibility.Collapsed;
}
private void ShowMeetingNotesTab_Click(object sender, MouseButtonEventArgs e)
{
    ShowMeetingDetailTab(showTranscript: false);
}
private void ShowMeetingTranscriptTab_Click(object sender, MouseButtonEventArgs e)
{
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
private void CopySelectedMeeting_Click(object sender, RoutedEventArgs e)
{
    if (_selectedMeeting is not null)
    {
        CopyMeetingToClipboard(_selectedMeeting);
    }
}
private void CopySelectedMeetingNotes_Click(object sender, RoutedEventArgs e)
{
    if (_selectedMeeting is null)
    {
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
    System.Windows.Clipboard.SetText(_selectedMeeting.Transcript);
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
    var dialog = new Microsoft.Win32.OpenFileDialog
    {
        Title = "Import meeting audio or video",
        Filter = "Media files|*.wav;*.mp3;*.m4a;*.aac;*.mp4;*.mov;*.mkv;*.webm;*.ogg|All files|*.*"
    };
    if (dialog.ShowDialog(this) != true)
    {
        return;
    }
    try
    {
        DictationStatus = "Transcribing meeting";
        _toastNotificationService.Show("Transcribing meeting", System.IO.Path.GetFileName(dialog.FileName), ToastState.Transcribing, 0);
        var result = await _meetingTranscriptionClient.TranscribeFileAsync(
            System.IO.Path.GetFileNameWithoutExtension(dialog.FileName),
            dialog.FileName,
            new TranscriptionOptions(SelectedAsrEngine, SelectedModelProfile));
        var transcript = DictionaryCorrectionService.Apply(result.Text, DictionaryEntries.Select(entry => entry.Record));
        transcript = await PostProcessIfEnabledAsync(transcript, "meeting import", _meetingTranscriptionClient.PostProcessAsync);
        if (string.IsNullOrWhiteSpace(transcript))
        {
            DictationStatus = "No speech detected in imported meeting";
            _toastNotificationService.Show("No speech detected", System.IO.Path.GetFileName(dialog.FileName), ToastState.Error, 3600);
            return;
        }
        var summary = await CreateMeetingSummaryAsync(transcript, System.IO.Path.GetFileNameWithoutExtension(dialog.FileName));
        var meeting = new MeetingItem(
            $"meet_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}",
            System.IO.Path.GetFileNameWithoutExtension(dialog.FileName),
            DateTime.Now,
            transcript,
            summary,
            dialog.FileName,
            SelectedModelProfile,
            result.DurationMs,
            _selectedMeetingFolderId);
        Meetings.Insert(0, meeting);
        SaveMeetings();
        RefreshMeetingViews();
        RefreshSearchResults();
        DictationStatus = "Meeting transcribed";
        _toastNotificationService.Show("Meeting ready", meeting.Title, ToastState.Success);
        ShowPage(MeetingsPage, MeetingsNav);
    }
    catch (Exception exception)
    {
        DictationStatus = $"Meeting import failed: {exception.Message}";
        _toastNotificationService.Show("Meeting import failed", exception.Message, ToastState.Error, 4200);
    }
}
private async void ToggleMeetingRecording_Click(object sender, RoutedEventArgs e)
{
    await ToggleMeetingRecordingAsync(null);
}
private async Task ToggleMeetingRecordingAsync(string? detectedTitle)
{
    if (_meetingRecordingCoordinator.IsBusy)
    {
        return;
    }
    if (!_isMeetingRecording)
    {
        try
        {
            DictationStatus = "Recording meeting";
            _toastNotificationService.Show("Recording meeting", "Capturing microphone and system audio", ToastState.Recording, 0);
            await _meetingRecordingCoordinator.StartAsync(SelectedMicrophone);
            _currentMeetingTitle = detectedTitle;
            _isMeetingRecording = true;
            StartMeetingAutoStopMonitor();
            OnPropertyChanged(nameof(MeetingRecordingButtonText));
            ShowPage(MeetingsPage, MeetingsNav);
        }
        catch (Exception exception)
        {
            DictationStatus = $"Meeting recording failed: {exception.Message}";
            _toastNotificationService.Show("Meeting recording failed", exception.Message, ToastState.Error);
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
        var result = await _meetingRecordingCoordinator.StopAsync(
            title,
            new TranscriptionOptions(SelectedAsrEngine, SelectedModelProfile));
        _currentMeetingTitle = null;
        var transcript = DictionaryCorrectionService.Apply(result.Transcript, DictionaryEntries.Select(entry => entry.Record));
        transcript = await PostProcessIfEnabledAsync(transcript, "meeting", _meetingTranscriptionClient.PostProcessAsync);
        if (string.IsNullOrWhiteSpace(transcript))
        {
            DictationStatus = "No speech detected in meeting";
            _toastNotificationService.Show("No speech detected", "Meeting audio was captured but no transcript was produced", ToastState.Error, 4200);
            return;
        }
        var summary = await CreateMeetingSummaryAsync(transcript, result.Title);
        var sourceAudioPath = SaveMeetingRecordings
            ? result.SystemAudioPath is null ? result.MicAudioPath : $"{result.MicAudioPath}; {result.SystemAudioPath}"
            : "";
        if (!SaveMeetingRecordings)
        {
            DeleteFileIfExists(result.MicAudioPath);
            DeleteFileIfExists(result.SystemAudioPath);
        }
        var meeting = new MeetingItem(
            $"meet_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}",
            result.Title,
            result.StartedAt,
            transcript,
            summary,
            sourceAudioPath,
            SelectedModelProfile,
            result.DurationMs,
            _selectedMeetingFolderId);
        Meetings.Insert(0, meeting);
        SaveMeetings();
        RefreshMeetingViews();
        RefreshSearchResults();
        DictationStatus = string.IsNullOrWhiteSpace(transcript) ? "Meeting saved with no detected speech" : "Meeting ready";
        _toastNotificationService.Show("Meeting ready", meeting.Title, ToastState.Success);
        ShowPage(MeetingsPage, MeetingsNav);
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
    _meetingMissingScanCount = 0;
    _meetingAutoStopTimer.Stop();
    _meetingAutoStopTimer.Start();
}
private void StopMeetingAutoStopMonitor()
{
    _meetingMissingScanCount = 0;
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
    if (scan.Found)
    {
        _meetingMissingScanCount = 0;
        return;
    }
    _meetingMissingScanCount++;
    if (_meetingMissingScanCount < 2 || _meetingRecordingCoordinator.IsBusy)
    {
        return;
    }
    _logService.Info("Meeting window disappeared; stopping meeting recording automatically.");
    await ToggleMeetingRecordingAsync(null);
}
private void ResetMeetingRecordingUi(string status)
{
    StopMeetingAutoStopMonitor();
    _currentMeetingTitle = null;
    _isMeetingRecording = false;
    OnPropertyChanged(nameof(MeetingRecordingButtonText));
    DictationStatus = status;
    _toastNotificationService.ShowIdle(SelectedHotkey);
}
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
}
private void SaveDictionaryEntry_Click(object sender, RoutedEventArgs e)
{
    SaveDictionary();
    DictationStatus = "Dictionary saved";
}
private async void RefreshRuntimeDiagnostics_Click(object sender, RoutedEventArgs e)
{
    await RefreshRuntimeDiagnosticsAsync();
}
private void OpenModelCache_Click(object sender, RoutedEventArgs e)
{
    try
    {
        _runtimeDiagnosticsService.OpenModelCacheDirectory();
    }
    catch (Exception exception)
    {
        DictationStatus = $"Could not open model cache: {exception.Message}";
        _logService.Error("Could not open model cache.", exception);
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
        DictationStatus = $"Could not open logs: {exception.Message}";
    }
}
private void CheckForUpdates_Click(object sender, RoutedEventArgs e)
{
    DictationStatus = "Opening release page";
    OpenExternalUrl("https://github.com/pHequals7/muesli/releases", "Could not open release page");
}
private void Donate_Click(object sender, RoutedEventArgs e)
{
    OpenExternalUrl("https://buymeacoffee.com/phequals7", "Could not open donation link");
}
private void ViewGitHub_Click(object sender, RoutedEventArgs e)
{
    OpenExternalUrl("https://github.com/pHequals7/muesli", "Could not open GitHub");
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
        _runtimeDiagnosticsService.ClearModelCache();
        DictationStatus = "Model cache cleared";
        await RefreshRuntimeDiagnosticsAsync();
    }
    catch (Exception exception)
    {
        DictationStatus = $"Could not clear model cache: {exception.Message}";
        _logService.Error("Could not clear model cache.", exception);
    }
}
private async void DownloadWhisperModel_Click(object sender, RoutedEventArgs e)
{
    try
    {
        DictationStatus = $"Downloading {SelectedModelProfile}";
        _toastNotificationService.Show("Downloading model", SelectedModelProfile, ToastState.Transcribing, 0);
        var result = await _dictationCoordinator.DownloadModelAsync("whisper", SelectedModelProfile);
        DictationStatus = result.Text;
        _toastNotificationService.Show("Model ready", SelectedModelProfile, ToastState.Success, 3600);
        await RefreshRuntimeDiagnosticsAsync();
    }
    catch (Exception exception)
    {
        DictationStatus = $"Model download failed: {exception.Message}";
        _logService.Error($"Model download failed for {SelectedModelProfile}.", exception);
        _toastNotificationService.Show("Model download failed", exception.Message, ToastState.Error, 5200);
    }
}
public string SetupReadiness
{
    get => _setupReadiness;
    private set => SetField(ref _setupReadiness, value);
}
private void SetParakeetActive_Click(object sender, RoutedEventArgs e)
{
    SelectedAsrEngine = "parakeet-v3";
    DictationStatus = "Parakeet selected";
    _toastNotificationService.Show("Parakeet selected", "Install optional Parakeet runtime dependencies before first use", ToastState.Success, 3600);
}
private async void TestParakeet_Click(object sender, RoutedEventArgs e)
{
    try
    {
        DictationStatus = "Testing Parakeet runtime";
        _toastNotificationService.Show("Testing Parakeet", "Checking NVIDIA/CUDA backend", ToastState.Transcribing, 0);
        var result = await _meetingTranscriptionClient.DownloadModelAsync("parakeet", "parakeet-v3");
        DictationStatus = result.Text;
        _toastNotificationService.Show("Parakeet ready", "NVIDIA backend initialized", ToastState.Success, 4200);
        await RefreshRuntimeDiagnosticsAsync();
    }
    catch (Exception exception)
    {
        DictationStatus = $"Parakeet test failed: {exception.Message}";
        _logService.Error("Parakeet readiness test failed.", exception);
        _toastNotificationService.Show("Parakeet unavailable", exception.Message, ToastState.Error, 6200);
    }
}
private async void DownloadQwenModel_Click(object sender, RoutedEventArgs e)
{
    var model = Environment.GetEnvironmentVariable("MUESLI_POST_PROCESSOR_MODEL") ?? "Qwen/Qwen2.5-3B-Instruct";
    try
    {
        DictationStatus = "Downloading Qwen cleanup model";
        _toastNotificationService.Show("Downloading Qwen", model, ToastState.Transcribing, 0);
        var result = await _meetingTranscriptionClient.DownloadModelAsync("postprocess", model);
        DictationStatus = result.Text;
        _toastNotificationService.Show("Qwen ready", model, ToastState.Success, 3600);
        await RefreshRuntimeDiagnosticsAsync();
    }
    catch (Exception exception)
    {
        DictationStatus = $"Qwen download failed: {exception.Message}";
        _toastNotificationService.Show("Qwen download failed", exception.Message, ToastState.Error, 5200);
    }
}
private async void TestQwenCleanup_Click(object sender, RoutedEventArgs e)
{
    try
    {
        DictationStatus = "Testing Qwen cleanup";
        _toastNotificationService.Show("Testing cleanup", "Running local post-processing", ToastState.Transcribing, 0);
        var sample = "um make a todo list buy milk and then email the team and schedule follow up";
        var result = await _meetingTranscriptionClient.PostProcessAsync(sample, "diagnostic", PostProcessingPrompt);
        DictationStatus = string.IsNullOrWhiteSpace(result.Text) ? "Qwen cleanup returned no text" : "Qwen cleanup ready";
        _toastNotificationService.Show("Cleanup ready", result.Text, ToastState.Success, 5200);
        await RefreshRuntimeDiagnosticsAsync();
    }
    catch (Exception exception)
    {
        DictationStatus = $"Qwen cleanup test failed: {exception.Message}";
        _logService.Error("Qwen post-processing readiness test failed.", exception);
        _toastNotificationService.Show("Cleanup unavailable", "Install/download Qwen before enabling cleanup", ToastState.Error, 6200);
    }
}
private void DeleteDictionaryEntry_Click(object sender, RoutedEventArgs e)
{
    if (sender is FrameworkElement { DataContext: DictionaryEntryItem item })
    {
        DictionaryEntries.Remove(item);
        SaveDictionary();
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
private void MeetingsList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
{
    if (sender is System.Windows.Controls.ListBox { SelectedItem: MeetingItem item })
    {
        OpenMeetingDetail(item);
        ShowPage(MeetingsPage, MeetingsNav);
    }
}
private void ShowDictations_Click(object sender, RoutedEventArgs e) => ShowPage(DictationsPage, DictationsNav);
private void ClearSearch_Click(object sender, RoutedEventArgs e) => SearchQuery = "";
private void ShowMeetings_Click(object sender, RoutedEventArgs e)
{
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
private void ShowSettings_Click(object sender, RoutedEventArgs e) => ShowPage(SettingsPage, SettingsNav);
private void ShowAbout_Click(object sender, RoutedEventArgs e) => ShowPage(AboutPage, AboutNav);
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
private void RefreshSearchResults()
{
    SearchDictationResults.Refresh();
    SearchMeetingResults.Refresh();
    OnPropertyChanged(nameof(SearchDictationCount));
    OnPropertyChanged(nameof(SearchMeetingCount));
    OnPropertyChanged(nameof(SearchResultsSummary));
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
    var area = SystemParameters.WorkArea;
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
    Left = _restoreBounds.Left;
    Top = _restoreBounds.Top;
    Width = _restoreBounds.Width;
    Height = _restoreBounds.Height;
    _isWorkAreaMaximized = false;
}
private void Close_Click(object sender, RoutedEventArgs e)
{
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
private async Task<string> PostProcessIfEnabledAsync(
    string text,
    string context,
    Func<string, string, string, Task<PostProcessingResult>> postProcessor)
{
    if (!PostProcessingEnabled || string.IsNullOrWhiteSpace(text))
    {
        return text;
    }
    try
    {
        DictationStatus = "Cleaning transcript";
        _toastNotificationService.Show("Cleaning transcript", "Applying Qwen post-processing", ToastState.Transcribing, 0);
        var result = await postProcessor(text, context, PostProcessingPrompt);
        if (!string.IsNullOrWhiteSpace(result.Diagnostic) &&
            result.Diagnostic.Contains("fallback", StringComparison.OrdinalIgnoreCase))
        {
            DictationStatus = "Cleaned with fallback";
        }
        return string.IsNullOrWhiteSpace(result.Text) ? text : result.Text.Trim();
    }
    catch (Exception exception)
    {
        DictationStatus = $"Post-processing skipped: {exception.Message}";
        _toastNotificationService.Show("Post-processing skipped", "Raw transcript kept", ToastState.Error, 3200);
        return text;
    }
}
private async Task<string> CreateMeetingSummaryAsync(string transcript, string title)
{
    DictationStatus = SelectedSummaryProvider == "local" ? "Creating local summary" : "Creating AI summary";
    _toastNotificationService.Show("Summarizing meeting", SelectedSummaryProvider, ToastState.Transcribing, 0);
    return await MeetingSummaryService.CreateSummaryAsync(transcript, title, CurrentSettingsSnapshot());
}
private async Task RefreshRuntimeDiagnosticsAsync()
{
    try
    {
        RuntimeDiagnostics = "Checking runtime...";
        SetupReadiness = "Checking setup...";
        var diagnostics = await _runtimeDiagnosticsService.InspectAsync();
        RuntimeDiagnostics = diagnostics.Summary;
        ModelCacheDirectory = diagnostics.ModelCacheDirectory;
        ModelCacheSize = diagnostics.ModelCacheSize;
        SetupReadiness = BuildSetupReadiness(diagnostics);
        _logService.Info($"Runtime diagnostics refreshed. {SetupReadiness.Replace(Environment.NewLine, " | ")}");
    }
    catch (Exception exception)
    {
        RuntimeDiagnostics = $"Diagnostics failed: {exception.Message}";
        SetupReadiness = "Setup check failed. Open logs for details.";
        _logService.Error("Runtime diagnostics failed.", exception);
    }
}
private string BuildSetupReadiness(RuntimeDiagnostics diagnostics)
{
    var lines = new List<string>();
    var dependenciesOk = diagnostics.DependencyStatus.Contains("OK", StringComparison.OrdinalIgnoreCase);
    var qwenOk = diagnostics.PostProcessingDependencyStatus.Contains("OK", StringComparison.OrdinalIgnoreCase);
    var parakeetOk = diagnostics.ParakeetDependencyStatus.Contains("OK", StringComparison.OrdinalIgnoreCase);
    var workerOk = File.Exists(diagnostics.WorkerScript);
    var selectedModelCached = SelectedAsrEngine != "whisper" || _runtimeDiagnosticsService.IsWhisperModelCached(SelectedModelProfile);
    var baseCached = _runtimeDiagnosticsService.IsWhisperModelCached("base");
    lines.Add(dependenciesOk ? "Whisper worker dependencies ready." : "Whisper dependencies missing. Run setup-worker-runtime.ps1.");
    lines.Add(workerOk ? "Transcription worker found." : "Transcription worker missing from this build.");
    lines.Add(selectedModelCached
        ? $"{SelectedModelProfile} model cached."
        : $"{SelectedModelProfile} model is not cached yet. Use Download selected before first offline use.");
    if (!baseCached)
    {
        lines.Add("Recommended: cache Whisper base for reliable CPU fallback.");
    }
    lines.Add(diagnostics.CudaStatus.Contains("CUDA available", StringComparison.OrdinalIgnoreCase)
        ? "CUDA backend available."
        : "CUDA not available; CPU transcription will be used.");
    lines.Add(qwenOk ? "Qwen cleanup dependencies ready." : "Qwen cleanup dependencies missing; install with -WithPostProcessing if cleanup is needed.");
    lines.Add(parakeetOk ? "Parakeet dependencies ready." : "Parakeet dependencies missing; install with -WithParakeet on NVIDIA machines.");
    return string.Join(Environment.NewLine, lines);
}
private MuesliSettings CurrentSettingsSnapshot()
{
    return new MuesliSettings
    {
        Hotkey = SelectedHotkey,
        AsrEngine = SelectedAsrEngine,
        ModelProfile = SelectedModelProfile,
        PasteBehavior = SelectedPasteBehavior,
        OnboardingCompleted = _onboardingCompleted,
        PostProcessingEnabled = PostProcessingEnabled,
        EnableDoubleTapDictation = EnableDoubleTapDictation,
        PostProcessingPrompt = PostProcessingPrompt,
        StartAtLogin = StartAtLogin,
        AutoMeetingDetectionEnabled = AutoMeetingDetectionEnabled,
        MeetingSummaryProvider = SelectedSummaryProvider,
        MeetingSummaryTemplate = SelectedSummaryTemplate,
        MeetingSummaryPromptOverride = CustomMeetingTemplates.FirstOrDefault(template =>
            template.Name.Equals(SelectedSummaryTemplate, StringComparison.OrdinalIgnoreCase))?.Prompt ?? "",
        OpenDashboardOnLaunch = OpenDashboardOnLaunch,
        SaveMeetingRecordings = SaveMeetingRecordings,
        ShowFloatingIndicator = ShowFloatingIndicator,
        IndicatorAnchor = SelectedIndicatorPosition,
        OpenAIApiKey = OpenAIApiKey,
        OpenAIModel = OpenAIModel,
        OpenRouterApiKey = OpenRouterApiKey,
        OpenRouterModel = OpenRouterModel,
        Theme = _theme,
        MicrophoneName = SelectedMicrophone,
        IndicatorLeft = _indicatorLeft,
        IndicatorTop = _indicatorTop
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
    SetColor("TextPrimary", light ? "#E0000000" : "#EBFFFFFF");
    SetColor("TextSecondary", light ? "#8C000000" : "#9EFFFFFF");
    SetColor("TextTertiary", light ? "#54000000" : "#66FFFFFF");
    SetBrush("BorderBrushSoft", light ? "#14000000" : "#12FFFFFF");
    SetBrush("BorderBrushMedium", light ? "#1F000000" : "#1CFFFFFF");
    SetBrush("PrimaryButtonBackgroundBrush", light ? "#D6DFFE" : "#26364F");
    SetBrush("PrimaryButtonBorderBrush", light ? "#9BB7F5" : "#3C5D8E");
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
        () => Dispatcher.InvokeAsync(() => ToggleMeetingRecordingAsync(meeting.Title)),
        () =>
        {
            _ignoredMeetingPrompts[meeting.Key] = DateTime.Now.AddMinutes(30);
            DictationStatus = "Meeting prompt ignored";
        });
}
    private void OnMeetingDetectionScanCompleted(object? sender, MeetingDetectionScan scan)
    {
        Dispatcher.Invoke(() =>
        {
            MeetingDetectionStatus = scan.Found
                ? scan.Summary
                : $"No meeting found. {scan.Summary}";
        });
    }

    private void CheckMeetingDetection_Click(object sender, RoutedEventArgs e)
    {
        AutoMeetingDetectionEnabled = true;
        var scan = _meetingDetectionService.CheckNow();
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
                meeting.FolderId));
        }

        foreach (var entry in _dataStore.LoadDictionary())
        {
            DictionaryEntries.Add(new DictionaryEntryItem(entry));
        }

        foreach (var template in _dataStore.LoadMeetingTemplates())
        {
            CustomMeetingTemplates.Add(new MeetingTemplateItem(template));
            if (!SummaryTemplates.Contains(template.Name))
            {
                SummaryTemplates.Add(template.Name);
            }
        }

        UpdateMeetingFolderCounts();
    }

    private void SaveDictations()
    {
        _dataStore.SaveDictations(Dictations.Select(item => new PersistedDictation(
            item.Id,
            item.Timestamp,
            item.Text,
            item.DurationMs,
            item.ModelProfile)));
    }

    private void SaveMeetings()
    {
        _dataStore.SaveMeetings(Meetings.Select(item => new PersistedMeeting
        {
            Id = item.Id,
            Title = item.Title,
            CreatedAt = item.CreatedAt,
            DurationMs = item.DurationMs,
            Transcript = item.Transcript,
            Summary = item.Summary,
            SourcePath = item.SourcePath,
            ModelProfile = item.ModelProfile,
            FolderId = item.FolderId
        }));
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
}

public sealed record DictationItem(
    string Id,
    DateTime Timestamp,
    string Time,
    string Text,
    string ModelProfile,
    int DurationMs);

public sealed record MeetingItem(
    string Id,
    string Title,
    DateTime CreatedAt,
    string Transcript,
    string Summary,
    string SourcePath,
    string ModelProfile,
    int DurationMs,
    string? FolderId)
{
    public string Metadata => $"{CreatedAt:yyyy-MM-dd HH:mm} • {DurationLabel}";

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
