namespace Muesli.Windows.Tests;

public sealed class SoundFeedbackTests
{
    [Fact]
    public void DisabledSettingNeverPlaysAnyCue()
    {
        foreach (var cue in Enum.GetValues<SoundCue>())
        {
            Assert.False(SoundFeedbackPolicy.ShouldPlay(
                cue, enabled: false, AudioOutputRouteKind.SpeakerLike, DictationSessionKind.Interactive));
        }
    }

    [Fact]
    public void LifecycleCuesPlayOnlyOnConfirmedSpeakers()
    {
        Assert.True(SoundFeedbackPolicy.ShouldPlay(
            SoundCue.DictationStart, true, AudioOutputRouteKind.SpeakerLike, DictationSessionKind.Interactive));
        Assert.True(SoundFeedbackPolicy.ShouldPlay(
            SoundCue.DictationInsert, true, AudioOutputRouteKind.SpeakerLike, DictationSessionKind.Interactive));

        Assert.False(SoundFeedbackPolicy.ShouldPlay(
            SoundCue.DictationStart, true, AudioOutputRouteKind.HeadphoneLike, DictationSessionKind.Interactive));
        Assert.False(SoundFeedbackPolicy.ShouldPlay(
            SoundCue.DictationInsert, true, AudioOutputRouteKind.Unknown, DictationSessionKind.Interactive));
    }

    [Fact]
    public void ModelReadyIgnoresHeadphoneSuppressionAndStillHonorsTheSetting()
    {
        Assert.True(SoundFeedbackPolicy.ShouldPlay(
            SoundCue.ModelReady, true, AudioOutputRouteKind.HeadphoneLike, DictationSessionKind.Interactive));
        Assert.True(SoundFeedbackPolicy.ShouldPlay(
            SoundCue.ModelReady, true, AudioOutputRouteKind.Unknown, DictationSessionKind.SetupTest));
        Assert.False(SoundFeedbackPolicy.ShouldPlay(
            SoundCue.ModelReady, false, AudioOutputRouteKind.SpeakerLike, DictationSessionKind.Interactive));
    }

    [Fact]
    public void SetupAndAuxiliarySessionsSuppressLifecycleCues()
    {
        Assert.False(SoundFeedbackPolicy.ShouldPlay(
            SoundCue.DictationStart, true, AudioOutputRouteKind.SpeakerLike, DictationSessionKind.SetupTest));
        Assert.False(SoundFeedbackPolicy.ShouldPlay(
            SoundCue.DictationInsert, true, AudioOutputRouteKind.SpeakerLike, DictationSessionKind.Auxiliary));
        Assert.True(SoundFeedbackPolicy.ShouldPlay(
            SoundCue.DictationStart, true, AudioOutputRouteKind.SpeakerLike, DictationSessionKind.Interactive));
    }

    [Theory]
    [InlineData(AudioOutputRouteClassifier.FormFactorSpeakers, AudioOutputRouteKind.SpeakerLike)]
    [InlineData(AudioOutputRouteClassifier.FormFactorLineLevel, AudioOutputRouteKind.SpeakerLike)]
    [InlineData(AudioOutputRouteClassifier.FormFactorSpdif, AudioOutputRouteKind.SpeakerLike)]
    [InlineData(AudioOutputRouteClassifier.FormFactorDigitalAudioDisplayDevice, AudioOutputRouteKind.SpeakerLike)]
    [InlineData(AudioOutputRouteClassifier.FormFactorHeadphones, AudioOutputRouteKind.HeadphoneLike)]
    [InlineData(AudioOutputRouteClassifier.FormFactorHeadset, AudioOutputRouteKind.HeadphoneLike)]
    [InlineData(AudioOutputRouteClassifier.FormFactorHandset, AudioOutputRouteKind.HeadphoneLike)]
    [InlineData(AudioOutputRouteClassifier.FormFactorUnknown, AudioOutputRouteKind.Unknown)]
    [InlineData(AudioOutputRouteClassifier.FormFactorRemoteNetworkDevice, AudioOutputRouteKind.Unknown)]
    [InlineData(AudioOutputRouteClassifier.FormFactorMicrophone, AudioOutputRouteKind.Unknown)]
    public void FormFactorMapsToTheOgRouteKinds(int formFactor, AudioOutputRouteKind expected)
    {
        Assert.Equal(expected, AudioOutputRouteClassifier.Classify(new OutputDeviceDescription(formFactor, HasRender: true)));
        Assert.Equal(AudioOutputRouteKind.Unknown, AudioOutputRouteClassifier.Classify(new OutputDeviceDescription(formFactor, HasRender: false)));
    }

    [Fact]
    public void LocatorPicksTheFirstExistingWindowsMediaCandidate()
    {
        using var directory = new TestDirectory();
        File.WriteAllBytes(Path.Combine(directory.Path, "notify.wav"), [0]);
        File.WriteAllBytes(Path.Combine(directory.Path, "Windows Notify.wav"), [1]);

        var path = WindowsSystemSoundLocator.Resolve(SoundCue.DictationInsert, directory.Path);

        Assert.Equal(Path.Combine(directory.Path, "Windows Notify.wav"), path);
        Assert.Null(WindowsSystemSoundLocator.Resolve(SoundCue.DictationStart, directory.Path));
    }

    [Fact]
    public void ServicePlaysInteractiveStartAndSkipsHeadphoneAndDisabledCues()
    {
        var played = new List<SoundCue>();
        var route = AudioOutputRouteKind.SpeakerLike;
        var service = new SoundFeedbackService(new RecordingSoundPlayer(played), () => route);

        service.PlayDictationStart();
        service.PlayDictationInsert(DictationSessionKind.SetupTest);
        route = AudioOutputRouteKind.HeadphoneLike;
        service.PlayDictationInsert();
        service.PlayModelReady();
        service.Enabled = false;
        service.PlayModelReady();

        Assert.Equal([SoundCue.DictationStart, SoundCue.ModelReady], played);
    }

    [Fact]
    public void SoundEnabledDefaultsOnAndRoundTripsWithoutASchemaBump()
    {
        using var directory = new TestDirectory();
        var path = directory.File("settings.json");
        File.WriteAllText(path, "{\"SchemaVersion\":8,\"Hotkey\":\"F8\"}");
        var store = new SettingsStore(path, new InMemorySecretStore());

        var loaded = store.Load();
        Assert.True(loaded.SoundEnabled);
        Assert.Equal(MuesliSettings.CurrentSchemaVersion, loaded.SchemaVersion);

        store.Save(loaded with { SoundEnabled = false });
        Assert.False(store.Load().SoundEnabled);
    }

    [Fact]
    public void AppearanceSettingsExposeTheOgPlaySoundEffectsToggle()
    {
        var root = FindRepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "windows-native", "Muesli.Windows", "Features", "Settings", "SettingsView.xaml"));
        var code = File.ReadAllText(Path.Combine(root, "windows-native", "Muesli.Windows", "MainWindow.xaml.cs"));
        var settings = File.ReadAllText(Path.Combine(root, "windows-native", "Muesli.Windows", "Features", "Runtime", "FeatureRuntime.Settings.cs"));
        var dictations = File.ReadAllText(Path.Combine(root, "windows-native", "Muesli.Windows", "Features", "Runtime", "FeatureRuntime.Dictations.cs"));
        var computerUse = File.ReadAllText(Path.Combine(root, "windows-native", "Muesli.Windows", "Features", "Runtime", "FeatureRuntime.ComputerUse.cs"));

        Assert.Contains("Content=\"Play sound effects\"", xaml);
        Assert.Contains("IsChecked=\"{Binding SoundEnabled}\"", xaml);
        Assert.Contains("public bool SoundEnabled", settings);
        Assert.Contains("SoundFeedback.Enabled", code + Environment.NewLine + settings);
        Assert.Contains("DictationSessionKind.SetupTest", dictations);
        Assert.Contains("DictationSessionKind.Auxiliary", computerUse);
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "windows-native", "Muesli.Windows")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("Repository root was not found.");
    }

    private sealed class RecordingSoundPlayer(List<SoundCue> played) : ISystemSoundPlayer
    {
        public void Play(SoundCue cue) => played.Add(cue);
    }
}
