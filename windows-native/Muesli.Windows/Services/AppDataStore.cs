using System.IO;
namespace Muesli.Windows.Services;

public sealed class AppDataStore
{
    public const int CurrentMeetingSchemaVersion = 5;
    private readonly string _dataDirectory;
    private readonly AtomicJsonFile _json;

    public AppDataStore(string? dataDirectory = null, Action<string>? report = null)
    {
        _dataDirectory = Path.GetFullPath(dataDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "muesli",
            "data"));
        _json = new AtomicJsonFile(report);
    }

    public string? LastWarning { get; private set; }

    public IReadOnlyList<PersistedDictation> LoadDictations()
    {
        return ReadJson("windows-dictations.json", new List<PersistedDictation>());
    }

    public void SaveDictations(IEnumerable<PersistedDictation> dictations)
    {
        WriteJson("windows-dictations.json", dictations.ToList());
    }

    public void SaveDictationsAfterDeletion(IEnumerable<PersistedDictation> dictations)
    {
        WriteJson(
            "windows-dictations.json",
            dictations.ToList(),
            AtomicJsonSaveMode.PrivacySensitive);
    }

    public IReadOnlyList<PersistedMeeting> LoadMeetings()
    {
        return ReadJson("windows-meetings.json", new List<PersistedMeeting>())
            .Select(MigrateMeeting)
            .ToList();
    }

    public void SaveMeetings(IEnumerable<PersistedMeeting> meetings)
    {
        WriteJson("windows-meetings.json", meetings.ToList());
    }

    public void SaveMeetingsAfterDeletion(IEnumerable<PersistedMeeting> meetings)
    {
        WriteJson(
            "windows-meetings.json",
            meetings.ToList(),
            AtomicJsonSaveMode.PrivacySensitive);
    }

    public IReadOnlyList<PersistedMeetingFolder> LoadMeetingFolders()
    {
        return ReadJson("windows-meeting-folders.json", new List<PersistedMeetingFolder>());
    }

    public void SaveMeetingFolders(IEnumerable<PersistedMeetingFolder> folders)
    {
        WriteJson("windows-meeting-folders.json", folders.ToList());
    }

    public IReadOnlyList<DictionaryEntryRecord> LoadDictionary()
    {
        return ReadJson("windows-dictionary.json", new List<DictionaryEntryRecord>());
    }

    public void SaveDictionary(IEnumerable<DictionaryEntryRecord> entries)
    {
        WriteJson("windows-dictionary.json", entries.ToList());
    }

    public IReadOnlyList<PersistedMeetingTemplate> LoadMeetingTemplates()
    {
        return ReadJson("windows-meeting-templates.json", new List<PersistedMeetingTemplate>());
    }

    public void SaveMeetingTemplates(IEnumerable<PersistedMeetingTemplate> templates)
    {
        WriteJson("windows-meeting-templates.json", templates.ToList());
    }

    private T ReadJson<T>(string fileName, T fallback)
    {
        var path = Path.Combine(_dataDirectory, fileName);
        var result = _json.Load(path, fallback);
        LastWarning = result.Warning ?? LastWarning;
        return result.Value;
    }

    private void WriteJson<T>(
        string fileName,
        T value,
        AtomicJsonSaveMode saveMode = AtomicJsonSaveMode.Recoverable)
    {
        var path = Path.Combine(_dataDirectory, fileName);
        _json.Save(path, value, saveMode);
    }

    private static PersistedMeeting MigrateMeeting(PersistedMeeting meeting)
    {
        if (meeting.SchemaVersion > CurrentMeetingSchemaVersion)
        {
            throw new InvalidDataException(
                $"Meeting '{meeting.Id}' uses unsupported schema {meeting.SchemaVersion}.");
        }

        var paths = meeting.SourcePath.Split(
            ';',
            StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var microphonePath = meeting.MicrophoneAudioPath;
        var systemPath = meeting.SystemAudioPath;
        foreach (var path in paths)
        {
            var name = Path.GetFileName(path);
            if (string.IsNullOrWhiteSpace(microphonePath) &&
                name.Contains("microphone", StringComparison.OrdinalIgnoreCase))
            {
                microphonePath = path;
            }
            else if (string.IsNullOrWhiteSpace(systemPath) &&
                     name.Contains("system", StringComparison.OrdinalIgnoreCase))
            {
                systemPath = path;
            }
        }

        if (meeting.SchemaVersion == 0 && string.IsNullOrWhiteSpace(microphonePath) && paths.Length == 1)
        {
            microphonePath = paths[0];
        }

        return meeting with
        {
            SchemaVersion = CurrentMeetingSchemaVersion,
            SessionState = meeting.SchemaVersion == 0
                ? MeetingSessionState.Completed
                : meeting.SessionState,
            MicrophoneAudioPath = microphonePath,
            SystemAudioPath = systemPath,
            SystemCaptureMode = string.IsNullOrWhiteSpace(meeting.SystemCaptureMode)
                ? "legacy-unknown"
                : meeting.SystemCaptureMode,
            LiveTranscriptOwnership = string.IsNullOrWhiteSpace(meeting.LiveTranscriptOwnership) ? "off" : meeting.LiveTranscriptOwnership,
            FinalTranscriptOwnerModelId = string.IsNullOrWhiteSpace(meeting.FinalTranscriptOwnerModelId) ? meeting.ModelProfile : meeting.FinalTranscriptOwnerModelId
        };
    }
}

public sealed record PersistedDictation(
    string Id,
    DateTime Timestamp,
    string Text,
    int DurationMs,
    string ModelProfile);

public sealed record PersistedMeeting
{
    public int SchemaVersion { get; init; }
    public string Id { get; init; } = "";
    public string Title { get; init; } = "";
    public DateTime CreatedAt { get; init; }
    public int DurationMs { get; init; }
    public string Transcript { get; init; } = "";
    public string Summary { get; init; } = "";
    public string SourcePath { get; init; } = "";
    public string ModelProfile { get; init; } = "";
    public string? FolderId { get; init; }
    public int WordCount { get; init; }
    public string TemplateName { get; init; } = "";
    public Dictionary<string, string> SpeakerAliases { get; init; } = new();
    public List<string> HealthWarnings { get; init; } = new();
    public MeetingSessionState SessionState { get; init; } = MeetingSessionState.Completed;
    public string? MicrophoneAudioPath { get; init; }
    public string? SystemAudioPath { get; init; }
    public string SystemCaptureMode { get; init; } = "";
    public bool RecoveredFromInterruption { get; init; }
    public string? LivePreviewModelId { get; init; }
    public string LiveTranscriptOwnership { get; init; } = "off";
    public string FinalTranscriptOwnerModelId { get; init; } = "";
    public string? GapRecoveryModelId { get; init; }

    /// <summary>
    /// Notes the user typed. Generated notes are rewritten by every re-summarization; these are not,
    /// and are never merged into the generated body — losing a user's own writing to a background
    /// regeneration is unrecoverable for them.
    /// </summary>
    public string ManualNotes { get; init; } = "";

    /// <summary>True once the user edits the title, which then survives any later regeneration.</summary>
    public bool TitleIsManual { get; init; }

    /// <summary>
    /// Latest bounded, redacted post-meeting automation outcome. This is diagnostics only and is
    /// deliberately separate from capture-health warnings so an optional hook cannot change the
    /// meeting's durable completion state.
    /// </summary>
    public PostMeetingAutomationResult? AutomationResult { get; init; }
}

public sealed record PersistedMeetingFolder(
    string Id,
    string Name);

public sealed record DictionaryEntryRecord
{
    public string Id { get; init; } = "";
    public string Phrase { get; init; } = "";
    public string Replacement { get; init; } = "";
    public double MatchingThreshold { get; init; } = 0.85;
}

public sealed record PersistedMeetingTemplate
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Prompt { get; init; } = "";
    public string Icon { get; init; } = "square.and.pencil";
}
