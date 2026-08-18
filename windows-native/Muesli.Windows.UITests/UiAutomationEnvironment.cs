namespace Muesli.Windows.UITests;

/// <summary>
/// CI-skippable gate for out-of-process UI Automation.
/// Set <c>MUESLI_UI_AUTOMATION=1</c> to force-enable, <c>0</c> to force-skip.
/// When unset, tests run only on an interactive desktop and stay skipped in CI.
/// </summary>
internal static class UiAutomationEnvironment
{
    public const string EnableVariable = "MUESLI_UI_AUTOMATION";
    public const string TraitName = "Category";
    public const string TraitValue = "UiAutomation";

    public static bool ShouldRun => SkipReason is null;

    public static string? SkipReason
    {
        get
        {
            var flag = Environment.GetEnvironmentVariable(EnableVariable)?.Trim();
            if (flag is "0" or "false" or "FALSE" or "no" or "NO")
                return $"Skipped because {EnableVariable}={flag}.";

            var forced = flag is "1" or "true" or "TRUE" or "yes" or "YES";
            if (!forced && IsContinuousIntegration)
                return $"Skipped in CI. Set {EnableVariable}=1 on an interactive desktop to run L09 UI Automation.";

            if (!Environment.UserInteractive)
                return "Skipped because the test host is not user-interactive.";

            var session = Environment.GetEnvironmentVariable("SESSIONNAME");
            if (string.Equals(session, "Services", StringComparison.OrdinalIgnoreCase))
                return "Skipped because there is no interactive desktop (session 0).";

            return null;
        }
    }

    private static bool IsContinuousIntegration =>
        IsSet("CI") ||
        IsSet("TF_BUILD") ||
        IsSet("GITHUB_ACTIONS") ||
        IsSet("GITLAB_CI") ||
        IsSet("BUILD_BUILDID");

    private static bool IsSet(string name) =>
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(name));
}
