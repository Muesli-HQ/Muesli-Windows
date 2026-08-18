using NAudio.CoreAudioApi;

namespace Muesli.Windows.Tests;

public sealed class Phase2DictationTests
{
    [Fact]
    public void HoldToTalkStartsOnPressAndStopsOnRelease()
    {
        var state = new DictationHotkeyStateMachine();

        Assert.Equal(DictationHotkeyAction.StartRecording, state.KeyDown(doubleTapEnabled: false));
        Assert.Equal(DictationHotkeyAction.None, state.KeyDown(doubleTapEnabled: false));
        Assert.Equal(DictationHotkeyAction.StopRecording, state.KeyUp(doubleTapEnabled: false));
        Assert.Equal(DictationHotkeyState.Idle, state.State);
    }

    [Fact]
    public void DoubleTapLocksAndThirdTapStopsHandsFreeRecording()
    {
        var state = new DictationHotkeyStateMachine();

        Assert.Equal(DictationHotkeyAction.StartRecording, state.KeyDown(doubleTapEnabled: true));
        Assert.Equal(DictationHotkeyAction.StartDoubleTapTimer, state.KeyUp(doubleTapEnabled: true));
        Assert.Equal(DictationHotkeyAction.EnterHandsFree, state.KeyDown(doubleTapEnabled: true));
        Assert.Equal(DictationHotkeyAction.None, state.KeyUp(doubleTapEnabled: true));
        Assert.True(state.IsHandsFree);
        Assert.Equal(DictationHotkeyAction.StopRecording, state.KeyDown(doubleTapEnabled: true));
        Assert.Equal(DictationHotkeyState.Idle, state.State);
    }

    [Fact]
    public void SingleTapWindowExpiryStopsAndResetCancelsWithoutLateStop()
    {
        var state = new DictationHotkeyStateMachine();
        state.KeyDown(doubleTapEnabled: true);
        state.KeyUp(doubleTapEnabled: true);
        Assert.Equal(DictationHotkeyAction.StopRecording, state.DoubleTapWindowElapsed());

        state.KeyDown(doubleTapEnabled: true);
        state.Reset();
        Assert.Equal(DictationHotkeyAction.None, state.DoubleTapWindowElapsed());
    }

    [Theory]
    [InlineData("Um, please, you know, send this.", "Please, send this.")]
    [InlineData("like, this is ready", "This is ready")]
    [InlineData("I like this result.", "I like this result.")]
    [InlineData("kind of sort of finished", "Finished")]
    public void FillerRemovalIsDeterministicAndDoesNotRemoveSemanticLike(string input, string expected)
    {
        Assert.Equal(expected, FillerWordFilter.Apply(input));
    }

    [Fact]
    public void DictionaryUsesLongestPunctuationAwarePhraseAndPreservesOuterPunctuation()
    {
        var result = DictionaryCorrectionService.Apply(
            "Ask new york, then muesly; leave Mueslium intact.",
            [
                new DictionaryEntryRecord { Phrase = "new york", Replacement = "New York City", MatchingThreshold = 0.90 },
                new DictionaryEntryRecord { Phrase = "york", Replacement = "York", MatchingThreshold = 0.90 },
                new DictionaryEntryRecord { Phrase = "muesli", Replacement = "Muesli", MatchingThreshold = 0.90 }
            ]);

        Assert.Equal("Ask New York City, then Muesli; leave Mueslium intact.", result);
    }

    [Fact]
    public void DictionaryRequiresEveryPhraseTokenAndExactShortWords()
    {
        var entries = new[]
        {
            new DictionaryEntryRecord { Phrase = "Ada Lovelace", Replacement = "Ada Lovelace", MatchingThreshold = 0.90 },
            new DictionaryEntryRecord { Phrase = "cat", Replacement = "CAT", MatchingThreshold = 0.85 }
        };

        Assert.Equal("Ada totally wrong and bat", DictionaryCorrectionService.Apply("Ada totally wrong and bat", entries));
        Assert.Equal("Ada Lovelace and CAT", DictionaryCorrectionService.Apply("Ada Lovelace and cat", entries));
    }

    [Fact]
    public void FillerSettingMigratesAndRoundTripsWithoutChangingModelRoles()
    {
        using var directory = new TestDirectory();
        var path = directory.File("settings.json");
        File.WriteAllText(path, "{\"SchemaVersion\":2,\"DictationModelId\":\"whisper-tiny-en\",\"FinalMeetingModelId\":\"parakeet-v3\"}");
        var store = new SettingsStore(path, new InMemorySecretStore());

        var migrated = store.Load();
        Assert.True(migrated.RemoveFillerWords);
        Assert.Equal("whisper-tiny-en", migrated.DictationModelId);
        Assert.Equal("parakeet-v3", migrated.FinalMeetingModelId);

        store.Save(migrated with { RemoveFillerWords = false });
        Assert.False(store.Load().RemoveFillerWords);
    }

    [Theory]
    [InlineData(44, 1000, false)]
    [InlineData(1024, 0, false)]
    [InlineData(1024, 1000, true)]
    [InlineData(BenchmarkCaptureRetentionPolicy.MaximumBytes + 1, 1000, false)]
    [InlineData(1024, BenchmarkCaptureRetentionPolicy.MaximumDurationMs + 1, false)]
    public void BenchmarkRetentionIsBounded(long bytes, int durationMs, bool expected)
    {
        Assert.Equal(expected, BenchmarkCaptureRetentionPolicy.ShouldRetain(bytes, durationMs));
    }

    [Fact]
    public void InterruptedPhase2TemporaryAudioIsRecoveredWithoutTouchingLegacyOrRetainedAudio()
    {
        using var directory = new TestDirectory();
        var segment = directory.File($"{DictationTemporaryAudioPolicy.SegmentPrefix}one.wav");
        var merged = directory.File($"{DictationTemporaryAudioPolicy.MergedPrefix}two.wav");
        var retained = directory.File("last-dictation.wav");
        var legacy = directory.File("native-legacy.wav");
        File.WriteAllBytes(segment, [1, 2, 3]);
        File.WriteAllBytes(merged, [1, 2, 3]);
        File.WriteAllBytes(retained, [1, 2, 3]);
        File.WriteAllBytes(legacy, [1, 2, 3]);

        var result = DictationTemporaryAudioPolicy.CleanupInterruptedFiles(directory.Path);

        Assert.Equal(2, result.DeletedCount);
        Assert.Empty(result.FailedPaths);
        Assert.False(File.Exists(segment));
        Assert.False(File.Exists(merged));
        Assert.True(File.Exists(retained));
        Assert.True(File.Exists(legacy));
    }

    [Fact]
    public void RoutePolicyTracksDefaultDisconnectAndBluetoothStyleReappearance()
    {
        Assert.True(AudioRouteRecoveryPolicy.ShouldRotate(
            AudioCaptureService.SystemDefaultMicrophone,
            "old-default",
            false,
            new AudioEndpointChange(AudioEndpointChangeKind.DefaultChanged, "new-default")));
        Assert.True(AudioRouteRecoveryPolicy.ShouldRotate(
            "Headset (Bluetooth)",
            "bluetooth-handsfree",
            false,
            new AudioEndpointChange(AudioEndpointChangeKind.StateChanged, "bluetooth-handsfree", DeviceState.Unplugged)));
        Assert.True(AudioRouteRecoveryPolicy.ShouldRotate(
            "Headset (Bluetooth)",
            "fallback-default",
            true,
            new AudioEndpointChange(AudioEndpointChangeKind.Added, "bluetooth-handsfree")));
        Assert.False(AudioRouteRecoveryPolicy.ShouldRotate(
            "Studio Mic",
            "studio",
            false,
            new AudioEndpointChange(AudioEndpointChangeKind.DefaultChanged, "other")));
    }

    [Fact]
    public void NoSpeechPreflightOnlyRejectsTinyOrEffectivelySilentCapture()
    {
        using var tiny = Audio(80, 2048, 0.2, 0.5f);
        using var silent = Audio(1000, 2048, 0.00001, 0.0001f);
        using var voiced = Audio(1000, 2048, 0.01, 0.1f);
        Assert.True(DictationAudioQualityPolicy.IsNoSpeech(tiny));
        Assert.True(DictationAudioQualityPolicy.IsNoSpeech(silent));
        Assert.False(DictationAudioQualityPolicy.IsNoSpeech(voiced));
    }

    private static CapturedAudio Audio(int durationMs, long bytes, double rms, float peak) =>
        new("source.wav", "transcription.wav", [], bytes, durationMs, rms, peak, "mic", "id");
}
