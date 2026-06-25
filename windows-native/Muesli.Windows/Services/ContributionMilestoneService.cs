namespace Muesli.Windows.Services;

/// <summary>
/// Tracks usage milestones and prompts users to support the project.
/// Ported from macOS MuesliNativeApp/ContributionMilestonePrompt.swift.
/// </summary>
public enum ContributionMilestoneKind
{
    DictationWords,
    Meetings
}

public sealed record ContributionMilestonePrompt(
    ContributionMilestoneKind Kind,
    int Count,
    bool ShowGitHubStar,
    bool ShowBuyMeCoffee)
{
    public string Id => $"{Kind}:{Count}";

    public string Title => Kind switch
    {
        ContributionMilestoneKind.DictationWords => $"You crossed {FormatCount(Count)} words!",
        ContributionMilestoneKind.Meetings => $"You captured {FormatCount(Count)} meetings!",
        _ => "Muesli milestone"
    };

    public string Message => Kind switch
    {
        ContributionMilestoneKind.DictationWords =>
            "That is a serious pile of words. If Muesli has been saving your fingers and your flow, a GitHub star or a coffee helps keep it moving.",
        ContributionMilestoneKind.Meetings =>
            "That is a lot of conversations turned into something useful. If Muesli has been keeping your meetings in order, a GitHub star or a coffee helps keep it moving.",
        _ => ""
    };

    public static string GitHubStarUrl => "https://github.com/Muesli-HQ/Muesli-Windows";
    public static string BuyMeCoffeeUrl => "https://buymeacoffee.com/phequals7";

    private static string FormatCount(int value)
    {
        return value.ToString("N0");
    }
}

public static class ContributionMilestonePolicy
{
    public const int DictationWordInterval = 1_000;
    public const int MeetingInterval = 25;

    public static int NextMilestone(int totalWords)
    {
        return NextMilestoneInternal(totalWords, DictationWordInterval);
    }

    public static int NextMeetingMilestone(int totalMeetings)
    {
        return NextMilestoneInternal(totalMeetings, MeetingInterval);
    }

    public static int NextMilestone(int total, ContributionMilestoneKind kind)
    {
        return kind switch
        {
            ContributionMilestoneKind.DictationWords => NextMilestone(total),
            ContributionMilestoneKind.Meetings => NextMeetingMilestone(total),
            _ => NextMilestone(total)
        };
    }

    private static int NextMilestoneInternal(int total, int interval)
    {
        var clamped = Math.Max(total, 0);
        return ((clamped / interval) + 1) * interval;
    }

    public static int? ResolvedNextMilestone(
        int? storedNextMilestone,
        int total,
        ContributionMilestoneKind kind,
        bool githubStarClicked,
        bool buyMeCoffeeClicked)
    {
        if (githubStarClicked && buyMeCoffeeClicked)
        {
            return null;
        }

        if (storedNextMilestone is null)
        {
            return NextMilestone(total, kind);
        }

        var interval = kind switch
        {
            ContributionMilestoneKind.DictationWords => DictationWordInterval,
            ContributionMilestoneKind.Meetings => MeetingInterval,
            _ => DictationWordInterval
        };

        if (total < storedNextMilestone.Value + interval)
        {
            return storedNextMilestone;
        }

        return NextMilestone(total, kind);
    }

    public static ContributionMilestonePrompt? CheckPrompt(
        ContributionMilestoneKind kind,
        int total,
        int? nextMilestone,
        bool githubStarClicked,
        bool buyMeCoffeeClicked,
        bool dismissedThisLaunch)
    {
        if (dismissedThisLaunch || nextMilestone is null || total < nextMilestone.Value)
        {
            return null;
        }

        if (githubStarClicked && buyMeCoffeeClicked)
        {
            return null;
        }

        return new ContributionMilestonePrompt(
            kind,
            nextMilestone.Value,
            ShowGitHubStar: !githubStarClicked,
            ShowBuyMeCoffee: !buyMeCoffeeClicked);
    }
}
