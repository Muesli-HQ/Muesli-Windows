using System.Windows;
using Muesli.Windows.Services;

namespace Muesli.Windows;

public sealed partial class FeatureRuntime
{
private void AddDictionaryEntry_Click(object sender, RoutedEventArgs e)
{
    var phrase = DictionaryPhraseBox.Text.Trim();
    var replacement = DictionaryReplacementBox.Text.Trim();
    if (string.IsNullOrWhiteSpace(phrase) || string.IsNullOrWhiteSpace(replacement))
    {
        DictationStatus = "Dictionary entry needs both fields";
        return;
    }
    DictionaryEntries.Insert(0, new DictionaryEntryItem(
        $"dictentry_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}",
        phrase,
        replacement,
        DictionaryThresholdSlider.Value));
    DictionaryPhraseBox.Text = "";
    DictionaryReplacementBox.Text = "";
    SaveDictionary();
    OnPropertyChanged(nameof(HasDictionaryEntries));
}
private void SaveDictionaryEntry_Click(object sender, RoutedEventArgs e)
{
    SaveDictionary();
    DictationStatus = "Dictionary saved";
    OnPropertyChanged(nameof(HasDictionaryEntries));
}
private void DeleteDictionaryEntry_Click(object sender, RoutedEventArgs e)
{
    if (sender is FrameworkElement { DataContext: DictionaryEntryItem item })
    {
        DictionaryEntries.Remove(item);
        SaveDictionary();
        OnPropertyChanged(nameof(HasDictionaryEntries));
    }
}
}
