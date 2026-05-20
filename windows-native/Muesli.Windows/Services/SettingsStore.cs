using System.IO;
using System.Text.Json;

namespace Muesli.Windows.Services;

public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _settingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "muesli",
        "windows-settings.json");

    public MuesliSettings Load()
    {
        if (!File.Exists(_settingsPath))
        {
            return new MuesliSettings();
        }

        try
        {
            var json = File.ReadAllText(_settingsPath);
            return JsonSerializer.Deserialize<MuesliSettings>(json) ?? new MuesliSettings();
        }
        catch
        {
            return new MuesliSettings();
        }
    }

    public void Save(MuesliSettings settings)
    {
        var directory = Path.GetDirectoryName(_settingsPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(_settingsPath, JsonSerializer.Serialize(settings, JsonOptions));
    }
}

public sealed record MuesliSettings
{
    public string UserName { get; init; } = "";
    public string Hotkey { get; init; } = "F8";
    public string AsrEngine { get; init; } = "whisper";
    public string ModelProfile { get; init; } = "base";
    public string PasteBehavior { get; init; } = "active-app";
    public bool OnboardingCompleted { get; init; }
    public bool PostProcessingEnabled { get; init; }
    public bool EnableDoubleTapDictation { get; init; }
    public string PostProcessingPrompt { get; init; } =
        "Clean up speech-to-text transcription. Only make changes when there is a clear error. If the text is already correct, output it exactly as-is.\n\nYou may fix obvious misspellings, remove filler words (um, uh, like), apply deletion commands, and format numbered or bullet lists when dictated.";
    public bool StartAtLogin { get; init; }
    public bool AutoMeetingDetectionEnabled { get; init; } = true;
    public string MeetingSummaryProvider { get; init; } = "local";
    public string MeetingSummaryTemplate { get; init; } = "standard";
    public string MeetingSummaryPromptOverride { get; init; } = "";
    public bool OpenDashboardOnLaunch { get; init; } = true;
    public bool SaveMeetingRecordings { get; init; } = true;
    public bool ShowFloatingIndicator { get; init; } = true;
    public string IndicatorAnchor { get; init; } = "Top Center";
    public string OpenAIApiKey { get; init; } = "";
    public string OpenAIModel { get; init; } = "gpt-5.4-mini";
    public string OpenRouterApiKey { get; init; } = "";
    public string OpenRouterModel { get; init; } = "stepfun/step-3.5-flash:free";
    public string Theme { get; init; } = "dark";
    public string? MicrophoneName { get; init; }
    public double? IndicatorLeft { get; init; }
    public double? IndicatorTop { get; init; }
}
