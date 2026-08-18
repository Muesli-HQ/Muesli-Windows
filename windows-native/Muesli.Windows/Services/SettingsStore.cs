using System.IO;
using System.Text.Json.Serialization;

namespace Muesli.Windows.Services;

public sealed class SettingsStore
{
    public const string OpenAISecretKey = "openai-api-key";
    public const string OpenRouterSecretKey = "openrouter-api-key";

    private readonly string _settingsPath;
    private readonly AtomicJsonFile _json;
    private readonly ISecretStore _secretStore;
    private bool _saveSuppressedToPreserveUnreadableOrFutureData;

    public SettingsStore(
        string? settingsPath = null,
        ISecretStore? secretStore = null,
        Action<string>? report = null)
    {
        _settingsPath = Path.GetFullPath(settingsPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "muesli",
            "windows-settings.json"));
        _secretStore = secretStore ?? new WindowsCredentialSecretStore();
        _json = new AtomicJsonFile(report);
    }

    public string? LastWarning { get; private set; }

    public MuesliSettings Load()
    {
        _saveSuppressedToPreserveUnreadableOrFutureData = false;
        var result = _json.Load(_settingsPath, new MuesliSettings());
        LastWarning = result.Warning;
        var settings = result.Value;
        if (settings.SchemaVersion > MuesliSettings.CurrentSchemaVersion)
        {
            _saveSuppressedToPreserveUnreadableOrFutureData = true;
            LastWarning =
                $"Settings schema {settings.SchemaVersion} is newer than this app supports. The file was left unchanged and settings changes will not be saved by this version.";
            return new MuesliSettings();
        }
        var settingsMigrated = settings.SchemaVersion < MuesliSettings.CurrentSchemaVersion ||
                               !string.IsNullOrWhiteSpace(settings.LegacyTranscriptionModelIdForMigration) ||
                               !string.IsNullOrWhiteSpace(settings.LegacyAsrEngineForMigration) ||
                               !string.IsNullOrWhiteSpace(settings.LegacyModelProfileForMigration) ||
                               !string.IsNullOrWhiteSpace(settings.LegacyDictationModelProfileForMigration);
        var legacyModelId = ResolveLegacyModelId(settings);
        var migratedModelId = TranscriptionModelCatalog.NormalizeId(legacyModelId);
        settings = settings with
        {
            SchemaVersion = MuesliSettings.CurrentSchemaVersion,
            DictationModelId = NormalizePersistedModelId(
                settingsMigrated && !string.IsNullOrWhiteSpace(legacyModelId) ? migratedModelId : settings.DictationModelId),
            FinalMeetingModelId = NormalizePersistedModelId(
                settingsMigrated && !string.IsNullOrWhiteSpace(legacyModelId) ? migratedModelId : settings.FinalMeetingModelId),
            LiveMeetingModelId = NormalizeStreamingModelId(settings.LiveMeetingModelId),
            LiveTranscriptOwnership = NormalizeOwnership(settings.LiveTranscriptOwnership),
            PostMeetingHookExecutablePath = settings.PostMeetingHookExecutablePath?.Trim() ?? "",
            PostMeetingHookTranscriptPolicy = NormalizeHookTranscriptPolicy(settings.PostMeetingHookTranscriptPolicy),
            PostMeetingHookTimeoutSeconds = Math.Clamp(settings.PostMeetingHookTimeoutSeconds, 1, 600),
            PostMeetingHookMaxAttempts = Math.Clamp(settings.PostMeetingHookMaxAttempts, 1, 3),
            AutoExportMarkdownDirectory = settings.AutoExportMarkdownDirectory?.Trim() ?? "",
            AutoExportMarkdownContent = NormalizeAutoExportContent(settings.AutoExportMarkdownContent),
            ComputerUsePlannerProvider = NormalizeComputerUseProvider(settings.ComputerUsePlannerProvider),
            ComputerUsePlannerModel = settings.ComputerUsePlannerModel?.Trim() ?? "",
            ComputerUsePlannerTimeoutSeconds = Math.Clamp(settings.ComputerUsePlannerTimeoutSeconds, 5, 120),
            ComputerUsePerActionTimeoutSeconds = Math.Clamp(settings.ComputerUsePerActionTimeoutSeconds, 1, 30),
            ComputerUseMaximumActionCount = Math.Clamp(settings.ComputerUseMaximumActionCount, 1, 20),
            ComputerUseAllowedApplications = NormalizeDelimitedAllowlist(settings.ComputerUseAllowedApplications),
            ComputerUseAllowedBrowserDomains = NormalizeDelimitedAllowlist(settings.ComputerUseAllowedBrowserDomains),
            ComputerUseBrowserInterface = NormalizeComputerUseBrowserInterface(settings.ComputerUseBrowserInterface),
            ComputerUseBrowserEndpoint = NormalizeLoopbackEndpoint(settings.ComputerUseBrowserEndpoint),
            LegacyTranscriptionModelIdForMigration = null,
            LegacyAsrEngineForMigration = null,
            LegacyModelProfileForMigration = null,
            LegacyDictationModelProfileForMigration = null
        };
        var plaintextOpenAIKey = settings.PlaintextOpenAIApiKeyForMigration;
        var plaintextOpenRouterKey = settings.PlaintextOpenRouterApiKeyForMigration;
        try
        {
            var migrated = settingsMigrated;
            if (!string.IsNullOrWhiteSpace(settings.PlaintextOpenAIApiKeyForMigration))
            {
                _secretStore.Write(OpenAISecretKey, settings.PlaintextOpenAIApiKeyForMigration);
                migrated = true;
            }
            if (!string.IsNullOrWhiteSpace(settings.PlaintextOpenRouterApiKeyForMigration))
            {
                _secretStore.Write(OpenRouterSecretKey, settings.PlaintextOpenRouterApiKeyForMigration);
                migrated = true;
            }

            settings = settings with
            {
                PlaintextOpenAIApiKeyForMigration = null,
                PlaintextOpenRouterApiKeyForMigration = null,
                ResolvedOpenAIApiKey = _secretStore.Read(OpenAISecretKey) ?? "",
                ResolvedOpenRouterApiKey = _secretStore.Read(OpenRouterSecretKey) ?? ""
            };
            if (migrated)
            {
                SaveSanitized(settings, AtomicJsonSaveMode.PrivacySensitive);
            }
            return settings;
        }
        catch (Exception)
        {
            _saveSuppressedToPreserveUnreadableOrFutureData = true;
            LastWarning = "Secure provider-key migration could not be completed. Existing plaintext keys were left in place so migration can retry after restart, and were not logged.";
            return settings with
            {
                ResolvedOpenAIApiKey = plaintextOpenAIKey ?? "",
                ResolvedOpenRouterApiKey = plaintextOpenRouterKey ?? ""
            };
        }
    }

    public void Save(MuesliSettings settings)
    {
        if (_saveSuppressedToPreserveUnreadableOrFutureData)
        {
            return;
        }
        SaveSanitized(settings, AtomicJsonSaveMode.Recoverable);
    }

    private void SaveSanitized(MuesliSettings settings, AtomicJsonSaveMode saveMode)
    {
        var sanitized = settings with
        {
            PlaintextOpenAIApiKeyForMigration = null,
            PlaintextOpenRouterApiKeyForMigration = null,
            LegacyTranscriptionModelIdForMigration = null,
            LegacyAsrEngineForMigration = null,
            LegacyModelProfileForMigration = null,
            LegacyDictationModelProfileForMigration = null,
            SchemaVersion = MuesliSettings.CurrentSchemaVersion,
            DictationModelId = NormalizePersistedModelId(settings.DictationModelId),
            FinalMeetingModelId = NormalizePersistedModelId(settings.FinalMeetingModelId),
            LiveMeetingModelId = NormalizeStreamingModelId(settings.LiveMeetingModelId),
            LiveTranscriptOwnership = NormalizeOwnership(settings.LiveTranscriptOwnership),
            PostMeetingHookExecutablePath = settings.PostMeetingHookExecutablePath?.Trim() ?? "",
            PostMeetingHookTranscriptPolicy = NormalizeHookTranscriptPolicy(settings.PostMeetingHookTranscriptPolicy),
            PostMeetingHookTimeoutSeconds = Math.Clamp(settings.PostMeetingHookTimeoutSeconds, 1, 600),
            PostMeetingHookMaxAttempts = Math.Clamp(settings.PostMeetingHookMaxAttempts, 1, 3),
            AutoExportMarkdownDirectory = settings.AutoExportMarkdownDirectory?.Trim() ?? "",
            AutoExportMarkdownContent = NormalizeAutoExportContent(settings.AutoExportMarkdownContent),
            ComputerUsePlannerProvider = NormalizeComputerUseProvider(settings.ComputerUsePlannerProvider),
            ComputerUsePlannerModel = settings.ComputerUsePlannerModel?.Trim() ?? "",
            ComputerUsePlannerTimeoutSeconds = Math.Clamp(settings.ComputerUsePlannerTimeoutSeconds, 5, 120),
            ComputerUsePerActionTimeoutSeconds = Math.Clamp(settings.ComputerUsePerActionTimeoutSeconds, 1, 30),
            ComputerUseMaximumActionCount = Math.Clamp(settings.ComputerUseMaximumActionCount, 1, 20),
            ComputerUseAllowedApplications = NormalizeDelimitedAllowlist(settings.ComputerUseAllowedApplications),
            ComputerUseAllowedBrowserDomains = NormalizeDelimitedAllowlist(settings.ComputerUseAllowedBrowserDomains),
            ComputerUseBrowserInterface = NormalizeComputerUseBrowserInterface(settings.ComputerUseBrowserInterface),
            ComputerUseBrowserEndpoint = NormalizeLoopbackEndpoint(settings.ComputerUseBrowserEndpoint),
            ResolvedOpenAIApiKey = "",
            ResolvedOpenRouterApiKey = ""
        };
        _json.Save(_settingsPath, sanitized, saveMode);
    }

    public void SaveSecret(string key, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            _secretStore.Delete(key);
        }
        else
        {
            _secretStore.Write(key, value.Trim());
        }
    }

    public string ReadSecret(string key) => _secretStore.Read(key) ?? "";
    public bool IsSecretConfigured(string key) => _secretStore.IsConfigured(key);

    private static string NormalizePersistedModelId(string? modelId) =>
        TranscriptionModelCatalog.TryGet(modelId, out var model)
            ? model.Id
            : TranscriptionModelCatalog.DefaultModelId;

    private static string? NormalizeStreamingModelId(string? modelId) =>
        StreamingModelCatalog.Get(modelId)?.Id;

    private static string NormalizeOwnership(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "unified-live-final" => "unified-live-final",
        _ => "preview-only"
    };

    private static string NormalizeHookTranscriptPolicy(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "inline" => "inline",
        "auto-export-path" => "auto-export-path",
        _ => "metadata-only"
    };

    private static string NormalizeAutoExportContent(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "transcript" => "transcript",
        "full-meeting" => "full-meeting",
        _ => "notes"
    };

    private static string NormalizeComputerUseProvider(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "openai" => "openai",
        _ => "none"
    };

    private static string NormalizeComputerUseBrowserInterface(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "loopback-devtools" => "loopback-devtools",
        _ => "none"
    };

    private static string NormalizeDelimitedAllowlist(string? value) => string.Join("; ",
        (value ?? "")
        .Split([';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(entry => entry.ToLowerInvariant())
        .Distinct(StringComparer.OrdinalIgnoreCase));

    private static string NormalizeLoopbackEndpoint(string? value)
    {
        var candidate = value?.Trim() ?? "";
        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var endpoint) ||
            endpoint.Scheme != Uri.UriSchemeHttp ||
            !endpoint.IsLoopback)
        {
            return "http://127.0.0.1:9222";
        }
        return endpoint.GetLeftPart(UriPartial.Authority);
    }

    private static string? ResolveLegacyModelId(MuesliSettings settings)
    {
        foreach (var candidate in new[]
                 {
                     settings.LegacyTranscriptionModelIdForMigration,
                     settings.LegacyAsrEngineForMigration
                 })
        {
            if (TranscriptionModelCatalog.TryGet(candidate, out var model))
            {
                return model.Id;
            }
        }

        var profile = settings.LegacyDictationModelProfileForMigration ?? settings.LegacyModelProfileForMigration;
        return profile?.Trim().ToLowerInvariant() switch
        {
            "tiny" or "tiny.en" => "whisper-tiny-en",
            "small" or "small.en" => "whisper-small-en",
            "medium" or "medium.en" or "large-v3" => "whisper-medium-en",
            _ => settings.LegacyTranscriptionModelIdForMigration ?? settings.LegacyAsrEngineForMigration
        };
    }
}

public sealed record MuesliSettings
{
    public const int CurrentSchemaVersion = 8;
    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    public string UserName { get; init; } = "";
    public string Hotkey { get; init; } = "F8";
    public string PasteBehavior { get; init; } = "active-app";
    public string DictationModelId { get; init; } = TranscriptionModelCatalog.DefaultModelId;
    public string FinalMeetingModelId { get; init; } = TranscriptionModelCatalog.DefaultModelId;
    public string? LiveMeetingModelId { get; init; }
    public string LiveTranscriptOwnership { get; init; } = "preview-only";
    public bool ShowLiveWaveformOnHover { get; init; }
    [JsonPropertyName("TranscriptionModelId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public string? LegacyTranscriptionModelIdForMigration { get; init; }
    [JsonPropertyName("AsrEngine")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public string? LegacyAsrEngineForMigration { get; init; }
    [JsonPropertyName("ModelProfile")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public string? LegacyModelProfileForMigration { get; init; }
    [JsonPropertyName("DictationModelProfile")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public string? LegacyDictationModelProfileForMigration { get; init; }
    public bool OnboardingCompleted { get; init; }
    public int LastCompletedFeatureTourVersion { get; init; }
    public bool EnableDoubleTapDictation { get; init; }
    public bool RemoveFillerWords { get; init; } = true;
    public bool EnableLocalCleanup { get; init; }
    public bool StartAtLogin { get; init; }
    public bool AutoMeetingDetectionEnabled { get; init; } = true;
    public string MeetingSummaryProvider { get; init; } = "local";
    public string MeetingSummaryTemplate { get; init; } = "standard";
    public string MeetingSummaryPromptOverride { get; init; } = "";
    public bool OpenDashboardOnLaunch { get; init; } = true;
    public bool SaveMeetingRecordings { get; init; } = true;
    public bool PostMeetingHookEnabled { get; init; }
    public string PostMeetingHookExecutablePath { get; init; } = "";
    public string PostMeetingHookTranscriptPolicy { get; init; } = "metadata-only";
    public int PostMeetingHookTimeoutSeconds { get; init; } = 30;
    public int PostMeetingHookMaxAttempts { get; init; } = 2;
    public bool AutoExportMarkdownEnabled { get; init; }
    public string AutoExportMarkdownDirectory { get; init; } = "";
    public string AutoExportMarkdownContent { get; init; } = "notes";
    public bool ComputerUseEnabled { get; init; }
    public string ComputerUsePlannerProvider { get; init; } = "none";
    public string ComputerUsePlannerModel { get; init; } = "";
    public int ComputerUsePlannerTimeoutSeconds { get; init; } = 30;
    public int ComputerUsePerActionTimeoutSeconds { get; init; } = 10;
    public int ComputerUseMaximumActionCount { get; init; } = 5;
    public string ComputerUseAllowedApplications { get; init; } = "";
    public string ComputerUseAllowedBrowserDomains { get; init; } = "";
    public bool ComputerUseIncludeWindowText { get; init; }
    public bool ComputerUseIncludeScreenshots { get; init; }
    public bool ComputerUseIncludeBrowserPageText { get; init; }
    public string ComputerUseBrowserInterface { get; init; } = "none";
    public string ComputerUseBrowserEndpoint { get; init; } = "http://127.0.0.1:9222";
    public bool ShowFloatingIndicator { get; init; } = true;
    public bool SoundEnabled { get; init; } = true;
    public string IndicatorAnchor { get; init; } = "Top Center";
    [JsonPropertyName("OpenAIApiKey")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public string? PlaintextOpenAIApiKeyForMigration { get; init; }
    [JsonIgnore]
    public string ResolvedOpenAIApiKey { get; init; } = "";
    public string OpenAIModel { get; init; } = "gpt-5.4-mini";
    [JsonPropertyName("OpenRouterApiKey")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public string? PlaintextOpenRouterApiKeyForMigration { get; init; }
    [JsonIgnore]
    public string ResolvedOpenRouterApiKey { get; init; } = "";
    public string OpenRouterModel { get; init; } = "stepfun/step-3.5-flash:free";

    /// <summary>
    /// Ollama runs on the user's own machine, so it needs no credential and, on a loopback
    /// endpoint, no "transcript leaves this machine" disclosure. A non-loopback endpoint is a
    /// network destination and is disclosed as such — see <see cref="SummaryProviderDisclosure"/>.
    /// </summary>
    public string OllamaEndpoint { get; init; } = "http://localhost:11434";
    public string OllamaModel { get; init; } = "llama3.1:8b";
    public string Theme { get; init; } = "dark";
    public string? MicrophoneName { get; init; }
    public double? IndicatorLeft { get; init; }
    public double? IndicatorTop { get; init; }
}
