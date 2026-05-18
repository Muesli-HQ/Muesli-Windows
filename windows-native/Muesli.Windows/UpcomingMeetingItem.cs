using System;
using System.Collections.Generic;
using System.ComponentModel;

namespace Muesli.Windows;

public sealed class UpcomingMeetingItem : INotifyPropertyChanged
{
    private string _title = "";
    private DateTime _startTime;
    private DateTime _endTime;
    private string _meetingUrl = "";
    private string _platform = "";

    public string Id { get; set; } = "";

    public string Title
    {
        get => _title;
        set => SetField(ref _title, value);
    }

    public DateTime StartTime
    {
        get => _startTime;
        set => SetField(ref _startTime, value);
    }

    public DateTime EndTime
    {
        get => _endTime;
        set => SetField(ref _endTime, value);
    }

    public string MeetingUrl
    {
        get => _meetingUrl;
        set => SetField(ref _meetingUrl, value);
    }

    public string Platform
    {
        get => _platform;
        set => SetField(ref _platform, value);
    }

    public string DayLabel
    {
        get
        {
            var today = DateTime.Today;
            if (StartTime.Date == today)
                return "Today";
            if (StartTime.Date == today.AddDays(1))
                return "Tomorrow";
            return StartTime.ToString("MMM");
        }
    }

    public string DayNumber => StartTime.ToString("d");

    public string DayOfWeek => StartTime.ToString("ddd");

    public bool IsToday => StartTime.Date == DateTime.Today;

    public string TimeRange => $"{StartTime:HH:mm} – {EndTime:HH:mm}";

    public bool HasUrl => !string.IsNullOrWhiteSpace(MeetingUrl);

    public event PropertyChangedEventHandler? PropertyChanged;

    private bool SetField<T>(ref T field, T value)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(""));
        return true;
    }
}
