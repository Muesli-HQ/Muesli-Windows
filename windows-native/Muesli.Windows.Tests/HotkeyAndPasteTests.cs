using System.Windows.Input;

namespace Muesli.Windows.Tests;

public sealed class HotkeyAndPasteTests
{
    [Fact]
    public void NativeInputLayoutMatchesWindowsAbi()
    {
        Assert.Equal(IntPtr.Size == 8 ? 40 : 28, ActiveAppPasteService.NativeInputStructureSize);
    }

    [Theory]
    [InlineData("F8", Key.F8, ModifierKeys.None)]
    [InlineData("control + shift + space", Key.Space, ModifierKeys.Control | ModifierKeys.Shift)]
    [InlineData("Win+Alt+D", Key.D, ModifierKeys.Windows | ModifierKeys.Alt)]
    public void ParsesAndNormalizesHotkeys(string input, Key key, ModifierKeys modifiers)
    {
        var gesture = HotkeyGesture.Parse(input);
        Assert.Equal(key, gesture.Key);
        Assert.Equal(modifiers, gesture.Modifiers);
    }

    [Fact]
    public void ExactModifierMatchingRejectsUnexpectedExtras()
    {
        Assert.True(HotkeyGesture.Parse("F8").Matches(Key.F8, ModifierKeys.None));
        Assert.False(HotkeyGesture.Parse("F8").Matches(Key.F8, ModifierKeys.Control));
        Assert.True(HotkeyGesture.Parse("Ctrl+F8").Matches(Key.F8, ModifierKeys.Control));
        Assert.False(HotkeyGesture.Parse("Ctrl+F8").Matches(Key.F8, ModifierKeys.Control | ModifierKeys.Shift));
        Assert.Throws<InvalidOperationException>(() => HotkeyGesture.Parse("Ctrl+F8+D"));
    }

    [Fact]
    public async Task PasteSuccessRestoresOnlyUnchangedMuesliClipboard()
    {
        var clipboard = new FakeClipboard("before");
        var windows = new FakeWindows { ExistsValue = true, FocusSucceeds = true };
        var delay = new ControlledDelay();
        var service = new ActiveAppPasteService(clipboard, windows, new FakeKeyboard(true), delay);

        var result = await service.PasteTextAsync("transcript", (IntPtr)42);
        Assert.True(result.TargetWasForeground);
        Assert.Equal("transcript", clipboard.Text);
        delay.ReleaseRestore();
        await clipboard.Restored.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal("before", clipboard.Text);
    }

    [Fact]
    public async Task ClipboardRaceDoesNotOverwriteNewUserContent()
    {
        var clipboard = new FakeClipboard("before");
        var delay = new ControlledDelay();
        var service = new ActiveAppPasteService(
            clipboard,
            new FakeWindows { ExistsValue = true, FocusSucceeds = true },
            new FakeKeyboard(true),
            delay);
        await service.PasteTextAsync("transcript", (IntPtr)42);
        clipboard.Text = "new user copy";
        delay.ReleaseRestore();
        await Task.Delay(50);
        Assert.Equal("new user copy", clipboard.Text);
        Assert.False(clipboard.Restored.Task.IsCompleted);
    }

    [Theory]
    [InlineData(false, true, true)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    public async Task PasteFailureLeavesTranscriptOnClipboard(bool exists, bool focus, bool injects)
    {
        var clipboard = new FakeClipboard("before");
        var service = new ActiveAppPasteService(
            clipboard,
            new FakeWindows { ExistsValue = exists, FocusSucceeds = focus },
            new FakeKeyboard(injects),
            new ControlledDelay());

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.PasteTextAsync("transcript", (IntPtr)42));
        Assert.Equal("transcript", clipboard.Text);
    }

    private sealed class FakeClipboard(string? initial) : IClipboardAdapter
    {
        public string? Text { get; set; } = initial;
        public TaskCompletionSource Restored { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<ClipboardSnapshot> CaptureAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new ClipboardSnapshot(new System.Windows.DataObject("Text", Text ?? "")));
        public Task SetTextAsync(string text, CancellationToken cancellationToken) { Text = text; return Task.CompletedTask; }
        public Task<string?> ReadTextAsync(CancellationToken cancellationToken) => Task.FromResult(Text);
        public Task RestoreAsync(ClipboardSnapshot snapshot, CancellationToken cancellationToken)
        {
            Text = snapshot.Data?.GetData("Text") as string;
            Restored.TrySetResult();
            return Task.CompletedTask;
        }
    }

    private sealed class FakeWindows : IWindowActivationAdapter
    {
        public bool ExistsValue { get; init; }
        public bool FocusSucceeds { get; init; }
        public IntPtr ForegroundWindow { get; private set; }
        public bool Exists(IntPtr window) => ExistsValue;
        public bool RequestForeground(IntPtr window)
        {
            if (FocusSucceeds) ForegroundWindow = window;
            return FocusSucceeds;
        }
    }

    private sealed record FakeKeyboard(bool Result) : IKeyboardInputAdapter
    {
        public bool SendPasteShortcut() => Result;
    }

    private sealed class ControlledDelay : IAsyncDelay
    {
        private readonly TaskCompletionSource _restore = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken) =>
            delay >= TimeSpan.FromMilliseconds(400) ? _restore.Task : Task.CompletedTask;
        public void ReleaseRestore() => _restore.TrySetResult();
    }
}
