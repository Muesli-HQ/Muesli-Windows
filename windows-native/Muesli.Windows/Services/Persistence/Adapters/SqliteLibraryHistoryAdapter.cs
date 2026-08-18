using System.IO;

namespace Muesli.Windows.Services.Persistence;

/// <summary>
/// SQLite-backed <see cref="ILibraryHistoryAdapter"/>. Used by L27 tests. Agent E wires it into
/// FeatureRuntime after <see cref="PersistenceCutover.EnsureMigrated"/>; this slice does not.
/// Dictionary entries stay on <c>windows-dictionary.json</c>.
/// </summary>
public sealed class SqliteLibraryHistoryAdapter : ILibraryHistoryAdapter, IDisposable
{
    private readonly MuesliPersistenceStore _store;
    private readonly AppDataStore _dictionary;
    private readonly string _jsonDirectory;
    private readonly bool _ownsStore;
    private bool _disposed;

    public SqliteLibraryHistoryAdapter(
        MuesliPersistenceStore store,
        string jsonDirectory,
        bool ownsStore = false)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentException.ThrowIfNullOrWhiteSpace(jsonDirectory);
        _store = store;
        _jsonDirectory = Path.GetFullPath(jsonDirectory);
        _ownsStore = ownsStore;
        _dictionary = new AppDataStore(_jsonDirectory);
    }

    /// <summary>Opens <c>muesli.db</c> beside the JSON history directory.</summary>
    public static SqliteLibraryHistoryAdapter Open(string jsonDirectory)
    {
        var store = MuesliPersistenceStore.Open(PersistencePaths.DatabasePathFor(jsonDirectory));
        return new SqliteLibraryHistoryAdapter(store, jsonDirectory, ownsStore: true);
    }

    public MuesliPersistenceStore Store => _store;

    public string? LastWarning => _dictionary.LastWarning;

    public IReadOnlyList<PersistedDictation> LoadDictations() =>
        _store.Dictations.List(UnboundedHistoryQuery.Dictations)
            .Select(LibraryHistoryMapper.ToPersisted)
            .ToList();

    public void SaveDictations(IEnumerable<PersistedDictation> dictations, bool afterExplicitDeletion = false)
    {
        var incoming = dictations.ToList();
        if (afterExplicitDeletion)
        {
            var keep = incoming.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
            foreach (var existing in _store.Dictations.List(UnboundedHistoryQuery.Dictations))
            {
                if (!keep.Contains(existing.Id))
                {
                    _store.Dictations.Delete(existing.Id);
                }
            }

            PurgeLeftoverJson(JsonHistorySnapshotReader.DictationsFileName, incoming);
        }

        foreach (var dictation in incoming)
        {
            _store.Dictations.Upsert(LibraryHistoryMapper.ToRecord(dictation));
        }
    }

    public IReadOnlyList<PersistedMeeting> LoadMeetings()
    {
        var heads = _store.Meetings.List(UnboundedHistoryQuery.Meetings);
        var meetings = new List<PersistedMeeting>(heads.Count);
        foreach (var head in heads)
        {
            var detail = _store.Meetings.FindDetail(head.Id);
            if (detail is not null)
            {
                meetings.Add(LibraryHistoryMapper.ToPersisted(detail));
            }
        }

        return meetings;
    }

    public void SaveMeetings(IEnumerable<PersistedMeeting> meetings, bool afterExplicitDeletion = false)
    {
        var incoming = meetings.ToList();
        if (afterExplicitDeletion)
        {
            var keep = incoming.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
            foreach (var existing in _store.Meetings.List(UnboundedHistoryQuery.Meetings))
            {
                if (!keep.Contains(existing.Id))
                {
                    _store.Meetings.Delete(existing.Id);
                }
            }

            PurgeLeftoverJson(JsonHistorySnapshotReader.MeetingsFileName, incoming);
        }

        foreach (var meeting in incoming)
        {
            var existing = _store.Meetings.FindDetail(meeting.Id);
            if (existing is null)
            {
                _store.Meetings.Save(LibraryHistoryMapper.ToNewDetail(meeting));
                continue;
            }

            // Per-field writes. Save(MeetingDetail) from UI-shaped rows would delete edited
            // transcripts, follow-ups, and other children the list never loaded.
            _store.Meetings.Upsert(LibraryHistoryMapper.MergeHead(existing, meeting));
            if (!string.IsNullOrEmpty(meeting.Summary))
            {
                _store.Meetings.SetNote(meeting.Id, MeetingNoteKind.Generated, meeting.Summary);
            }

            if (!string.IsNullOrEmpty(meeting.ManualNotes))
            {
                _store.Meetings.SetNote(meeting.Id, MeetingNoteKind.Manual, meeting.ManualNotes);
            }

            if (meeting.SpeakerAliases.Count > 0)
            {
                _store.Meetings.ReplaceSpeakerAliases(meeting.Id, meeting.SpeakerAliases);
            }

            if (!string.IsNullOrEmpty(meeting.Transcript))
            {
                var displayed = LibraryHistoryMapper.DisplayedTranscript(existing);
                if (!string.Equals(meeting.Transcript, displayed, StringComparison.Ordinal) &&
                    !string.Equals(meeting.Transcript, existing.Transcript(MeetingTranscriptKind.Raw), StringComparison.Ordinal))
                {
                    _store.Meetings.SetTranscript(meeting.Id, MeetingTranscriptKind.Edited, meeting.Transcript);
                }
            }
        }
    }

    public IReadOnlyList<PersistedMeetingFolder> LoadMeetingFolders() =>
        _store.Folders.List().Select(LibraryHistoryMapper.ToPersisted).ToList();

    public void SaveMeetingFolders(IEnumerable<PersistedMeetingFolder> folders)
    {
        var incoming = folders.ToList();
        var now = DateTimeOffset.UtcNow;
        for (var index = 0; index < incoming.Count; index++)
        {
            var folder = incoming[index];
            var existing = _store.Folders.Find(folder.Id);
            _store.Folders.Upsert(new FolderRecord
            {
                Id = folder.Id,
                Name = folder.Name ?? "",
                ParentId = existing?.ParentId,
                SortOrder = index,
                Metadata = existing?.Metadata ?? "",
                CreatedAtUtc = existing?.CreatedAtUtc ?? now,
                UpdatedAtUtc = now
            });
        }
    }

    public IReadOnlyList<DictionaryEntryRecord> LoadDictionary() => _dictionary.LoadDictionary();

    public void SaveDictionary(IEnumerable<DictionaryEntryRecord> entries) =>
        _dictionary.SaveDictionary(entries);

    public IReadOnlyList<PersistedMeetingTemplate> LoadMeetingTemplates() =>
        _store.Templates.List().Select(LibraryHistoryMapper.ToPersisted).ToList();

    public void SaveMeetingTemplates(IEnumerable<PersistedMeetingTemplate> templates)
    {
        var incoming = templates.ToList();
        var keep = incoming.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var existing in _store.Templates.List())
        {
            if (!keep.Contains(existing.Id))
            {
                _store.Templates.Delete(existing.Id);
            }
        }

        for (var index = 0; index < incoming.Count; index++)
        {
            var template = incoming[index];
            _store.Templates.Upsert(
                LibraryHistoryMapper.ToRecord(template, index, _store.Templates.Find(template.Id)));
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_ownsStore)
        {
            _store.Dispose();
        }
    }

    private void PurgeLeftoverJson<T>(string fileName, T value)
    {
        var path = Path.Combine(_jsonDirectory, fileName);
        if (!File.Exists(path))
        {
            return;
        }

        new AtomicJsonFile().Save(path, value, AtomicJsonSaveMode.PrivacySensitive);
    }
}
