namespace Muesli.Windows.Services;

public sealed record ProductExperienceState(bool SetupIncomplete, string SetupLabel, StartupRegistrationState Startup, IReadOnlyList<ProductHistoryEntry> RecentDictations, IReadOnlyList<ProductHistoryEntry> RecentMeetings, string DetectedNow);
public sealed record ProductHistoryEntry(string Title, DateTimeOffset Timestamp);
public sealed record StartupRegistrationState(bool Enabled, bool BackgroundRegistration, string Label)
{
    public static StartupRegistrationState FromWindows() => new(StartupRegistrationService.IsEnabled(), StartupRegistrationService.IsRegisteredForBackgroundLaunch(), StartupRegistrationService.DescribeState());
}
