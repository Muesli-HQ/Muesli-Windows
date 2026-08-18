using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Muesli.Windows.Services;

namespace Muesli.Windows;

public sealed partial class FeatureRuntime
{
private void ToggleMeetingSort_Click(object sender, RoutedEventArgs e)
{
    _meetingSortNewestFirst = !_meetingSortNewestFirst;
    var sorted = _meetingSortNewestFirst
        ? Meetings.OrderByDescending(item => item.CreatedAt).ToList()
        : Meetings.OrderBy(item => item.CreatedAt).ToList();
    Meetings.Clear();
    foreach (var item in sorted)
    {
        Meetings.Add(item);
    }
    OnPropertyChanged(nameof(MeetingSortLabel));
}
private void OpenButtonContextMenu_Click(object sender, RoutedEventArgs e)
{
    if (sender is System.Windows.Controls.Button button && button.ContextMenu is not null)
    {
        button.ContextMenu.PlacementTarget = button;
        button.ContextMenu.IsOpen = true;
    }
}
private void SetDictationFilter_Click(object sender, RoutedEventArgs e)
{
    if (sender is not System.Windows.Controls.MenuItem { Tag: string filter })
    {
        return;
    }
    _dictationDateFilter = filter;
    FilteredDictations.Refresh();
    OnPropertyChanged(nameof(DictationHeaderLabel));
    OnPropertyChanged(nameof(DictationFilterLabel));
    OnPropertyChanged(nameof(HasDictations));
}
private void SetMeetingFilter_Click(object sender, RoutedEventArgs e)
{
    if (sender is not System.Windows.Controls.MenuItem { Tag: string filter })
    {
        return;
    }
    _meetingDateFilter = filter;
    RefreshMeetingViews();
    OnPropertyChanged(nameof(MeetingFilterLabel));
}
private bool PassesMeetingFolder(object item)
{
    return _selectedMeetingFolderId is null ||
           item is MeetingItem meeting &&
           string.Equals(meeting.FolderId, _selectedMeetingFolderId, StringComparison.Ordinal);
}
private bool PassesSearch(object item)
{
    var query = _searchQuery.Trim();
    if (query.Length == 0)
    {
        return true;
    }
    return item switch
    {
        DictationItem dictation => ContainsSearch(dictation.Text, query) ||
                                  ContainsSearch(dictation.ModelProfile, query),
        MeetingItem meeting => ContainsSearch(meeting.Title, query) ||
                               ContainsSearch(meeting.Summary, query) ||
                               ContainsSearch(meeting.Transcript, query) ||
                               ContainsSearch(meeting.Metadata, query),
        _ => true
    };
}
private static bool ContainsSearch(string value, string query)
{
    return value.Contains(query, StringComparison.OrdinalIgnoreCase);
}
private void RefreshMeetingViews()
{
    UpdateMeetingFolderCounts();
    FilteredMeetings.Refresh();
    OnPropertyChanged(nameof(MeetingCount));
    OnPropertyChanged(nameof(VisibleMeetingCount));
    OnPropertyChanged(nameof(CurrentMeetingFolderName));
    OnPropertyChanged(nameof(HasMeetings));
}
private void UpdateMeetingFolderCounts()
{
    foreach (var folder in MeetingFolders)
    {
        folder.Count = Meetings.Count(meeting => string.Equals(meeting.FolderId, folder.Id, StringComparison.Ordinal));
    }
}
private static bool PassesDateFilter(object item, string filter)
{
    if (filter == "all")
    {
        return true;
    }
    var date = item switch
    {
        DictationItem dictation => dictation.Timestamp,
        MeetingItem meeting => meeting.CreatedAt,
        _ => DateTime.MinValue
    };
    return date >= DateTime.Now - FilterWindow(filter);
}
private static TimeSpan FilterWindow(string filter)
{
    return filter switch
    {
        "last2Days" => TimeSpan.FromDays(2),
        "lastWeek" => TimeSpan.FromDays(7),
        "last2Weeks" => TimeSpan.FromDays(14),
        "lastMonth" => TimeSpan.FromDays(31),
        "last3Months" => TimeSpan.FromDays(93),
        _ => TimeSpan.MaxValue
    };
}
private static string FilterLabel(string filter)
{
    return filter switch
    {
        "last2Days" => "Last 2 days",
        "lastWeek" => "Last week",
        "last2Weeks" => "Last 2 weeks",
        "lastMonth" => "Last month",
        "last3Months" => "Last 3 months",
        _ => "All time"
    };
}
private string NormalizeSummaryTemplateName(string? value)
{
    var normalized = MeetingSummaryService.NormalizeTemplateName(value);
    if (MeetingSummaryService.IsBuiltInTemplate(normalized))
    {
        return normalized;
    }

    var custom = SummaryTemplates.FirstOrDefault(template => template.Equals(normalized, StringComparison.OrdinalIgnoreCase));
    return custom ?? "Standard Meeting Notes";
}
private void DictationsList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
{
    if (sender is System.Windows.Controls.ListBox { SelectedItem: DictationItem item })
    {
        System.Windows.Clipboard.SetText(item.Text);
        DictationStatus = "Copied";
        _toastNotificationService.Show("Copied", item.Text, ToastState.Success);
    }
}
private void MeetingCard_Click(object sender, MouseButtonEventArgs e)
{
    if (sender is FrameworkElement { DataContext: MeetingItem item })
    {
        OpenMeetingDetail(item);
        ShowPage(AppPage.MeetingDetail);
    }
}
private void UpdateSearchPageVisibility()
{
    if (!string.IsNullOrWhiteSpace(SearchQuery))
    {
        if (SearchPage.Visibility != Visibility.Visible)
        {
            ShowPage(AppPage.Search);
        }
        return;
    }
    if (SearchPage.Visibility == Visibility.Visible)
    {
        ShowPage(_appServices.Navigation.State.LastContentPage);
    }
}
private void OpenSearchFromCompactRail_Click(object sender, RoutedEventArgs e)
{
    ShowPage(AppPage.Search);
    Dispatcher.BeginInvoke(() =>
    {
        MainSearchInput.Focus();
        Keyboard.Focus(MainSearchInput);
    });
}
private void RefreshSearchResults()
{
    SearchDictationResults.Refresh();
    SearchMeetingResults.Refresh();
    OnPropertyChanged(nameof(SearchDictationCount));
    OnPropertyChanged(nameof(SearchMeetingCount));
    OnPropertyChanged(nameof(SearchResultsSummary));
    OnPropertyChanged(nameof(HasSearchResults));
}
}
