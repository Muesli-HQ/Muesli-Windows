namespace Muesli.Windows.Tests;

public sealed class AtomicPersistenceTests
{
    [Fact]
    public void LoadsLegacyVersionZeroArray()
    {
        using var directory = new TestDirectory();
        var path = directory.File("history.json");
        File.WriteAllText(path, "[\"one\",\"two\"]");

        var result = new AtomicJsonFile().Load(path, new List<string>());

        Assert.Equal(["one", "two"], result.Value);
        Assert.False(result.HadCorruption);
    }

    [Fact]
    public void AtomicSaveCreatesEnvelopeAndRecoverableBackup()
    {
        using var directory = new TestDirectory();
        var path = directory.File("history.json");
        var store = new AtomicJsonFile();
        store.Save(path, new[] { "first" });
        store.Save(path, new[] { "second" });
        File.WriteAllText(path, "{not json");

        var recovered = store.Load(path, Array.Empty<string>());

        Assert.True(recovered.RecoveredFromBackup);
        Assert.Equal(["first"], recovered.Value);
        Assert.Contains(Directory.EnumerateFiles(directory.Path), file => file.Contains(".corrupt-", StringComparison.Ordinal));
        Assert.Equal(["first"], store.Load(path, Array.Empty<string>()).Value);
    }

    [Fact]
    public void MalformedJsonIsQuarantinedInsteadOfTreatedAsNormalEmptyHistory()
    {
        using var directory = new TestDirectory();
        var path = directory.File("history.json");
        File.WriteAllText(path, "nope");

        var result = new AtomicJsonFile().Load(path, new[] { "fallback" });

        Assert.True(result.HadCorruption);
        Assert.False(result.RecoveredFromBackup);
        Assert.Equal(["fallback"], result.Value);
        Assert.False(File.Exists(path));
        Assert.Single(Directory.EnumerateFiles(directory.Path));
    }

    [Fact]
    public async Task ConcurrentSavesNeverInterleaveJson()
    {
        using var directory = new TestDirectory();
        var path = directory.File("concurrent.json");
        var store = new AtomicJsonFile();

        await Task.WhenAll(Enumerable.Range(0, 24).Select(value => Task.Run(() => store.Save(path, new[] { value }))));

        var loaded = store.Load(path, Array.Empty<int>()).Value;
        Assert.Single(loaded);
        Assert.InRange(loaded[0], 0, 23);
    }

    [Fact]
    public void FailedReplaceLeavesPreviousDestinationReadable()
    {
        using var directory = new TestDirectory();
        var path = directory.File("safe.json");
        new AtomicJsonFile().Save(path, new[] { "safe" });
        var failing = new AtomicJsonFile(beforeReplace: _ => throw new IOException("simulated"));

        Assert.Throws<IOException>(() => failing.Save(path, new[] { "lost" }));
        Assert.Equal(["safe"], new AtomicJsonFile().Load(path, Array.Empty<string>()).Value);
    }

    [Fact]
    public async Task TransientReadLockIsRetriedWithoutQuarantiningValidJson()
    {
        using var directory = new TestDirectory();
        var path = directory.File("locked.json");
        new AtomicJsonFile().Save(path, new[] { "valid" });
        var exclusiveStream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
        var release = Task.Run(async () =>
        {
            await Task.Delay(40);
            exclusiveStream.Dispose();
        });

        var result = new AtomicJsonFile().Load(path, Array.Empty<string>());
        await release;

        Assert.Equal(["valid"], result.Value);
        Assert.False(result.HadCorruption);
        Assert.DoesNotContain(
            Directory.EnumerateFiles(directory.Path),
            candidate => candidate.Contains(".corrupt-", StringComparison.Ordinal));
    }

    [Fact]
    public void PersistentReadLockIsAnIoFailureAndDoesNotMoveValidJson()
    {
        using var directory = new TestDirectory();
        var path = directory.File("locked.json");
        new AtomicJsonFile().Save(path, new[] { "valid" });
        using var exclusiveStream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);

        Assert.ThrowsAny<IOException>(() => new AtomicJsonFile().Load(path, Array.Empty<string>()));

        Assert.True(File.Exists(path));
        Assert.DoesNotContain(
            Directory.EnumerateFiles(directory.Path),
            candidate => candidate.Contains(".corrupt-", StringComparison.Ordinal));
    }

    [Fact]
    public void ExplicitHistoryDeletionPurgesDeletedTranscriptsFromAllArtifacts()
    {
        using var directory = new TestDirectory();
        var store = new AppDataStore(directory.Path);
        var deletedDictation = new PersistedDictation(
            "dictation-delete",
            DateTime.UtcNow,
            "private dictation transcript",
            100,
            "test");
        var retainedDictation = new PersistedDictation(
            "dictation-keep",
            DateTime.UtcNow,
            "retained dictation transcript",
            100,
            "test");
        store.SaveDictations([deletedDictation]);
        store.SaveDictations([deletedDictation, retainedDictation]);
        File.WriteAllText(
            directory.File("windows-dictations.json.corrupt-abandoned.json"),
            deletedDictation.Text);

        var deletedMeeting = new PersistedMeeting
        {
            Id = "meeting-delete",
            Title = "Private meeting",
            CreatedAt = DateTime.UtcNow,
            Transcript = "private meeting transcript"
        };
        var retainedMeeting = new PersistedMeeting
        {
            Id = "meeting-keep",
            Title = "Retained meeting",
            CreatedAt = DateTime.UtcNow,
            Transcript = "retained meeting transcript"
        };
        store.SaveMeetings([deletedMeeting]);
        store.SaveMeetings([deletedMeeting, retainedMeeting]);
        File.WriteAllText(
            directory.File(".windows-meetings.json.abandoned.tmp"),
            deletedMeeting.Transcript);

        store.SaveDictationsAfterDeletion([retainedDictation]);
        store.SaveMeetingsAfterDeletion([retainedMeeting]);

        AssertSecretAbsentFromArtifacts(directory.Path, "windows-dictations.json", deletedDictation.Text);
        AssertSecretAbsentFromArtifacts(directory.Path, "windows-meetings.json", deletedMeeting.Transcript);
        Assert.Equal([retainedDictation], store.LoadDictations());
        var loadedMeeting = Assert.Single(store.LoadMeetings());
        Assert.Equal(retainedMeeting.Id, loadedMeeting.Id);
        Assert.Equal(retainedMeeting.Transcript, loadedMeeting.Transcript);
    }

    private static void AssertSecretAbsentFromArtifacts(string directory, string fileName, string secret)
    {
        var artifacts = Directory.EnumerateFiles(directory)
            .Where(path => Path.GetFileName(path).Contains(fileName, StringComparison.OrdinalIgnoreCase))
            .ToList();
        Assert.Single(artifacts);
        Assert.All(
            artifacts,
            artifact => Assert.DoesNotContain(secret, File.ReadAllText(artifact), StringComparison.Ordinal));
    }
}

internal sealed class TestDirectory : IDisposable
{
    public TestDirectory()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"muesli-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }
    public string File(string name) => System.IO.Path.Combine(Path, name);
    public void Dispose() => Directory.Delete(Path, recursive: true);
}
