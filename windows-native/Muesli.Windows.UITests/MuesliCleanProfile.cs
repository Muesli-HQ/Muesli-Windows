namespace Muesli.Windows.UITests;

/// <summary>
/// Isolated writable profile for production-startup automation.
/// Production has no custom data-dir environment variable, and launching Muesli.exe
/// (WPF apphost) does not honor process-level APPDATA for
/// <c>Environment.GetFolderPath(ApplicationData)</c> — only
/// <c>GetEnvironmentVariable("APPDATA")</c> changes, which this app does not use.
/// The harness therefore parks any existing %APPDATA%\muesli directory, installs a
/// seed-only directory at that path, and restores the parked directory on dispose.
/// It never uses the developer's parked history/settings as the writable profile.
/// </summary>
internal sealed class MuesliCleanProfile : IDisposable
{
    public const string MarkerFileName = ".l09-ui-harness-profile";
    public const string ParkFolderName = "muesli.l09-harness-park";

    private bool _disposed;
    private readonly bool _createdFreshRoot;

    public MuesliCleanProfile()
    {
        AppDataRoot = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        MuesliRoot = Path.Combine(AppDataRoot, "muesli");
        ParkedProfile = Path.Combine(AppDataRoot, ParkFolderName);
        Install();
        _createdFreshRoot = true;
    }

    public string AppDataRoot { get; }
    public string MuesliRoot { get; }
    public string ParkedProfile { get; }

    public void AssertIsolatedFromDeveloperProfile()
    {
        Assert.True(
            File.Exists(Path.Combine(MuesliRoot, MarkerFileName)),
            "The live %APPDATA%\\muesli directory is not the L09 harness seed profile.");
        Assert.False(
            File.Exists(Path.Combine(MuesliRoot, "windows-dictations.json")),
            "Harness profile must not carry developer dictation history.");
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        if (_createdFreshRoot)
            Restore();
    }

    private void Install()
    {
        RecoverInterruptedSwap();
        if (Directory.Exists(MuesliRoot))
        {
            if (Directory.Exists(ParkedProfile))
            {
                throw new InvalidOperationException(
                    $"Cannot park %APPDATA%\\muesli because '{ParkFolderName}' already exists. Restore it manually before re-running L09 tests.");
            }

            Directory.Move(MuesliRoot, ParkedProfile);
        }

        Directory.CreateDirectory(MuesliRoot);
        SeedCompletedFirstRun();
        File.WriteAllText(
            Path.Combine(MuesliRoot, MarkerFileName),
            "L09 UI automation seed profile. Safe to delete if a test run was interrupted; restore muesli.l09-harness-park to muesli.");
    }

    private void RecoverInterruptedSwap()
    {
        if (!File.Exists(Path.Combine(MuesliRoot, MarkerFileName)))
            return;
        try
        {
            Directory.Delete(MuesliRoot, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        if (Directory.Exists(ParkedProfile) && !Directory.Exists(MuesliRoot))
            Directory.Move(ParkedProfile, MuesliRoot);
    }

    private void Restore()
    {
        if (File.Exists(Path.Combine(MuesliRoot, MarkerFileName)))
        {
            try
            {
                Directory.Delete(MuesliRoot, recursive: true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        if (Directory.Exists(ParkedProfile) && !Directory.Exists(MuesliRoot))
            Directory.Move(ParkedProfile, MuesliRoot);
    }

    private void SeedCompletedFirstRun()
    {
        // Deterministic MainWindow reachability: a truly empty profile opens onboarding.
        // L34 owns onboarding qualification; this skeleton seeds completed first-run gates
        // with non-secret defaults so production startup lands on the dashboard.
        File.WriteAllText(
            Path.Combine(MuesliRoot, "windows-settings.json"),
            """
            {
              "schemaVersion": 8,
              "onboardingCompleted": true,
              "lastCompletedFeatureTourVersion": 1,
              "openDashboardOnLaunch": true,
              "startAtLogin": false,
              "showFloatingIndicator": false,
              "autoMeetingDetectionEnabled": false,
              "theme": "dark"
            }
            """);
        File.WriteAllText(
            Path.Combine(MuesliRoot, "onboarding-progress.json"),
            """
            {
              "schemaVersion": 1,
              "completed": true
            }
            """);
    }
}
