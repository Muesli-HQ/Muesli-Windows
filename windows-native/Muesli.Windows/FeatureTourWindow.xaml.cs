using System.Windows;
using System.Windows.Input;
namespace Muesli.Windows;
public partial class FeatureTourWindow : Window
{
    public const int CurrentVersion = 1;
    private readonly Action? _completed;
    private readonly bool _visualVerification;
    private int _step;
    private static readonly (string Title, string Body)[] Steps =
    [
        ("Dictation and real history", "Hold your shortcut to dictate and release it to transcribe. Only successful real dictations appear in history and drive your statistics."),
        ("Separate model roles", "Dictation and final meeting/import transcription use independent offline role selections. Live preview is optional; selecting a model never starts a download."),
        ("Meetings: detected now", "Detected now is based on current Windows meeting evidence. Upcoming meetings stay unavailable until Muesli has a real calendar source; it never fabricates calendar entries."),
        ("Tray and startup", "The tray exposes real recent metadata, setup resume, and detected-now state. Launch at login is read from Windows and can be repaired only when you choose."),
        ("Privacy, diagnostics, accessibility", "Audio and models are local by default. Optional cloud summaries disclose their destination. About provides privacy, logs and safe diagnostics; keyboard focus and Escape work throughout Muesli.")
    ];
    public FeatureTourWindow(Action? completed = null, bool visualVerification = false) { _completed = completed; _visualVerification = visualVerification; InitializeComponent(); }
    private void Render() { var item = Steps[_step]; PreviewModeText.Visibility = _visualVerification ? Visibility.Visible : Visibility.Collapsed; PreviewModeText.Text = _visualVerification ? "VISUAL VERIFICATION MODE · in-memory presentation only" : ""; ProgressText.Text = $"FEATURE TOUR · {_step + 1} OF {Steps.Length}"; TitleText.Text = item.Title; BodyText.Text = item.Body; BackButton.IsEnabled = _step > 0; NextButton.Content = _step == Steps.Length - 1 ? "Done" : "Next"; }
    private void Window_Loaded(object sender, RoutedEventArgs e) { Render(); NextButton.Focus(); }
    private void Back_Click(object sender, RoutedEventArgs e) { _step = Math.Max(0, _step - 1); Render(); }
    private void Next_Click(object sender, RoutedEventArgs e) { if (_step == Steps.Length - 1) { _completed?.Invoke(); Close(); return; } _step++; Render(); }
    private void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e) { if (e.Key == Key.Escape) Close(); }
}
