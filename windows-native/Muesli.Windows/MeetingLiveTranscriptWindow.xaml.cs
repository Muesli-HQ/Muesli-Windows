using System.Windows;
using System.Windows.Input;
using Muesli.Windows.Services;

namespace Muesli.Windows;

public partial class MeetingLiveTranscriptWindow : Window
{
    private readonly bool _showWaveformOnHover;
    private string _copyText = "";

    public MeetingLiveTranscriptWindow(bool showWaveformOnHover)
    {
        InitializeComponent();
        _showWaveformOnHover = showWaveformOnHover;
        Loaded += (_, _) => PositionOnActiveMonitor();
    }

    private void PositionOnActiveMonitor()
    {
        var area = WindowPlacementService.GetWorkAreaForCursor(this);
        Left = Math.Max(area.Left + 8, area.Right - ActualWidth - 20);
        Top = Math.Max(area.Top + 8, area.Bottom - ActualHeight - 20);
    }

    public void Update(Muesli.Windows.Services.LiveTranscriptSnapshot snapshot)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => Update(snapshot));
            return;
        }
        CommittedText.Text = snapshot.CommittedText;
        OthersPartialText.Text = snapshot.PartialSystem;
        YouPartialText.Text = snapshot.PartialMicrophone;
        OthersPartialBorder.Visibility = string.IsNullOrWhiteSpace(snapshot.PartialSystem) ? Visibility.Collapsed : Visibility.Visible;
        YouPartialBorder.Visibility = string.IsNullOrWhiteSpace(snapshot.PartialMicrophone) ? Visibility.Collapsed : Visibility.Visible;
        MicLevel.Value = snapshot.MicrophoneLevel;
        SystemLevel.Value = snapshot.SystemLevel;
        StatusText.Text = snapshot.DroppedPackets > 0
            ? $"Live · recovering {snapshot.DroppedPackets} dropped packet(s) from retained audio"
            : snapshot.QueuedPackets > 0 ? $"Live · {snapshot.QueuedPackets} queued" : "Live · speech-boundary commits";
        _copyText = string.Join(Environment.NewLine, new[]
        {
            snapshot.CommittedText,
            string.IsNullOrWhiteSpace(snapshot.PartialSystem) ? "" : $"Others (partial): {snapshot.PartialSystem}",
            string.IsNullOrWhiteSpace(snapshot.PartialMicrophone) ? "" : $"You (partial): {snapshot.PartialMicrophone}"
        }.Where(text => !string.IsNullOrWhiteSpace(text)));
        TranscriptScroll.ScrollToEnd();
    }

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(_copyText)) System.Windows.Clipboard.SetText(_copyText);
    }

    private void Dismiss_Click(object sender, RoutedEventArgs e) => Hide();
    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); }
    private void Window_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e) { if (_showWaveformOnHover) WaveformPanel.Visibility = Visibility.Visible; }
    private void Window_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e) { if (_showWaveformOnHover) WaveformPanel.Visibility = Visibility.Collapsed; }
}
