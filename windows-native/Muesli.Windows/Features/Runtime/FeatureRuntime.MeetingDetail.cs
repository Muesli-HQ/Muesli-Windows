using System.Windows;
using Muesli.Windows.Services;

namespace Muesli.Windows;

public sealed partial class FeatureRuntime
{
    public string SelectedMeetingTitle
    {
        get => _selectedMeeting?.Title ?? "";
        set
        {
            if (_selectedMeeting is null || !MeetingTitleService.IsAcceptableManualTitle(value)) return;
            if (string.Equals(_selectedMeeting.Title, value.Trim(), StringComparison.Ordinal)) return;
            UpdateSelectedMeeting(meeting => meeting with { Title = value.Trim(), TitleIsManual = true });
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedMeetingTitleOwnership));
        }
    }

    public string SelectedMeetingTitleOwnership => _selectedMeeting?.TitleIsManual == true
        ? "Your title · kept when notes are regenerated"
        : "Generated title · replaced when notes are regenerated";

    public string SelectedMeetingManualNotes
    {
        get => _selectedMeeting?.ManualNotes ?? "";
        set
        {
            if (_selectedMeeting is null) return;
            var trimmed = value?.Trim() ?? "";
            if (string.Equals(_selectedMeeting.ManualNotes, trimmed, StringComparison.Ordinal)) return;
            UpdateSelectedMeeting(meeting => meeting with { ManualNotes = trimmed });
            OnPropertyChanged();
        }
    }

    public string SelectedMeetingMetadata => _selectedMeeting?.Metadata ?? "";

    /// <summary>Applies an edit to the selected meeting and persists it, keeping list and detail in step.</summary>
    private void UpdateSelectedMeeting(Func<MeetingItem, MeetingItem> edit)
    {
        if (_selectedMeeting is null) return;
        var index = Meetings.IndexOf(_selectedMeeting);
        var updated = edit(_selectedMeeting);
        if (index >= 0) Meetings[index] = updated;
        _selectedMeeting = updated;
        SaveMeetings();
        RefreshSearchResults();
    }

    public string SelectedMeetingNotes => string.IsNullOrWhiteSpace(_selectedMeeting?.Summary)
        ? ""
        : ApplySpeakerAliasesToNotes(_selectedMeeting.Summary, _activeSpeakerAliases);

    public string SelectedMeetingTemplate
    {
        get => _selectedMeetingTemplate;
        set
        {
            var normalized = NormalizeSummaryTemplateName(value);
            if (SetField(ref _selectedMeetingTemplate, normalized))
            {
                OnPropertyChanged(nameof(SelectedMeetingNotesActionLabel));
            }
        }
    }

    public string SelectedMeetingNotesActionLabel => string.IsNullOrWhiteSpace(_selectedMeeting?.Summary) ? "Generate Notes" : "Regenerate Notes";

    /// <summary>True while a notes request is running, which drives the progress and Cancel affordances.</summary>
    public bool IsSummarizing
    {
        get => _isSummarizing;
        private set
        {
            if (!SetField(ref _isSummarizing, value)) return;
            OnPropertyChanged(nameof(IsNotSummarizing));
            OnPropertyChanged(nameof(CanRetrySummary));
        }
    }

    public bool IsNotSummarizing => !_isSummarizing;
    public bool CanRetrySummary => _summaryRetryAvailable && !_isSummarizing;

    private void CancelSummary_Click(object sender, RoutedEventArgs e)
    {
        _summaryCancellation?.Cancel();
        DictationStatus = "Cancelling notes generation…";
    }

    /// <summary>True while a media import is running, which drives the progress and Cancel affordances.</summary>
    public bool IsImportingMeeting
    {
        get => _isImportingMeeting;
        private set
        {
            if (!SetField(ref _isImportingMeeting, value)) return;
            OnPropertyChanged(nameof(IsNotImportingMeeting));
        }
    }

    public bool IsNotImportingMeeting => !_isImportingMeeting;

    /// <summary>Overall import completion, 0-100, across decode, transcription, cleanup and notes.</summary>
    public double ImportProgressPercent
    {
        get => _importProgressPercent;
        private set => SetField(ref _importProgressPercent, value);
    }

    public string ImportProgressLabel
    {
        get => _importProgressLabel;
        private set => SetField(ref _importProgressLabel, value);
    }

    /// <summary>
    /// True for stages that report no fraction. Parakeet decodes a whole file in one native call,
    /// so the bar must say "working" rather than invent a percentage that never moves.
    /// </summary>
    public bool ImportProgressIsIndeterminate
    {
        get => _importProgressIsIndeterminate;
        private set => SetField(ref _importProgressIsIndeterminate, value);
    }

    private void CancelImport_Click(object sender, RoutedEventArgs e)
    {
        _importCancellation?.Cancel();
        DictationStatus = "Cancelling import…";
        ApplyImportProgress(MeetingImportProgressMapper.Cancelling(ImportProgressPercent));
    }

    private void ReportImportProgress(MeetingImportStage stage, double? fraction) =>
        ApplyImportProgress(MeetingImportProgressMapper.Map(stage, fraction));

    private void ApplyImportProgress(MeetingImportProgress progress)
    {
        ImportProgressLabel = progress.Label;
        ImportProgressPercent = progress.Percent;
        ImportProgressIsIndeterminate = progress.IsIndeterminate;
    }

    private void RetrySummary_Click(object sender, RoutedEventArgs e) => _ = RetrySummaryAsync();

    private async Task RetrySummaryAsync()
    {
        _summaryRetryAvailable = false;
        OnPropertyChanged(nameof(CanRetrySummary));
        await GenerateSelectedMeetingNotesAsync();
    }
}
