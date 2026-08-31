using System.Runtime.InteropServices;
using System.Windows;
using System.Diagnostics;

namespace Muesli.Windows.Services;

public sealed class ActiveAppPasteService
{
    private const uint InputKeyboard = 1;
    private const uint KeyeventfKeyup = 0x0002;
    private const ushort VkControl = 0x11;
    private const ushort VkV = 0x56;
    private const int SwRestore = 9;

    private readonly IClipboardAdapter _clipboard;
    private readonly IWindowActivationAdapter _windows;
    private readonly IKeyboardInputAdapter _keyboard;
    private readonly IAsyncDelay _delay;

    public ActiveAppPasteService(
        IClipboardAdapter? clipboard = null,
        IWindowActivationAdapter? windows = null,
        IKeyboardInputAdapter? keyboard = null,
        IAsyncDelay? delay = null)
    {
        _clipboard = clipboard ?? new WpfClipboardAdapter();
        _windows = windows ?? new NativeWindowActivationAdapter();
        _keyboard = keyboard ?? new NativeKeyboardInputAdapter();
        _delay = delay ?? new SystemAsyncDelay();
    }

    public IntPtr CaptureForegroundWindow()
    {
        return _windows.ForegroundWindow;
    }

    public PasteTargetInfo DescribeWindow(IntPtr targetWindow)
    {
        if (targetWindow == IntPtr.Zero || !IsWindow(targetWindow))
        {
            return PasteTargetInfo.Unknown;
        }

        _ = GetWindowThreadProcessId(targetWindow, out var processId);
        var processName = "unknown";
        try
        {
            processName = Process.GetProcessById(checked((int)processId)).ProcessName;
        }
        catch
        {
            // The target may close between shortcut press and metadata capture.
        }

        // Window titles can contain document names, URLs, or private content. Dictation diagnostics do not need them.
        return new PasteTargetInfo(processName, processId);
    }

    public async Task<long> CopyTextAsync(string text, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return 0;
        }

        var started = Stopwatch.StartNew();
        await _clipboard.SetTextAsync(text, cancellationToken);
        started.Stop();
        return started.ElapsedMilliseconds;
    }

    public async Task<PasteOperationResult> PasteTextAsync(
        string text,
        IntPtr targetWindow,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return new PasteOperationResult(0, 0, 0, 0, false);
        }

        var totalStarted = Stopwatch.StartNew();
        var clipboardStarted = Stopwatch.StartNew();
        var previousClipboard = await _clipboard.CaptureAsync(cancellationToken);
        await _clipboard.SetTextAsync(text, cancellationToken);
        clipboardStarted.Stop();

        var focusStarted = Stopwatch.StartNew();
        if (!_windows.Exists(targetWindow))
        {
            throw new InvalidOperationException("The app that was active when dictation started is no longer available. The transcript remains on the clipboard.");
        }

        _windows.RequestForeground(targetWindow);
        var targetWasForeground = await WaitForTargetForegroundAsync(targetWindow, cancellationToken);
        focusStarted.Stop();
        if (!targetWasForeground)
        {
            throw new InvalidOperationException("Windows could not restore focus to the original app. The transcript remains on the clipboard.");
        }

        var inputStarted = Stopwatch.StartNew();
        var sent = _keyboard.SendPasteShortcut();
        inputStarted.Stop();
        if (!sent)
        {
            var error = Marshal.GetLastWin32Error();
            throw new InvalidOperationException(
                error == 5
                    ? "Windows blocked paste input. If the target app is running as administrator, run Muesli as administrator too."
                    : $"Windows did not accept the paste input. Win32 error: {error}.");
        }

        _ = RestoreClipboardSafelyAsync(previousClipboard, text);

        totalStarted.Stop();
        return new PasteOperationResult(
            totalStarted.ElapsedMilliseconds,
            clipboardStarted.ElapsedMilliseconds,
            focusStarted.ElapsedMilliseconds,
            inputStarted.ElapsedMilliseconds,
            targetWasForeground);
    }

    public static bool ShouldRestoreClipboard(string expectedMuesliText, string? currentText) =>
        string.Equals(expectedMuesliText, currentText, StringComparison.Ordinal);

    private async Task<bool> WaitForTargetForegroundAsync(IntPtr targetWindow, CancellationToken cancellationToken)
    {
        if (!_windows.Exists(targetWindow))
        {
            return false;
        }

        for (var attempt = 0; attempt < 8; attempt++)
        {
            if (!_windows.Exists(targetWindow))
            {
                return false;
            }
            if (_windows.ForegroundWindow == targetWindow)
            {
                return true;
            }
            await _delay.DelayAsync(TimeSpan.FromMilliseconds(10), cancellationToken);
        }

        return _windows.ForegroundWindow == targetWindow;
    }

    private async Task RestoreClipboardSafelyAsync(ClipboardSnapshot previousClipboard, string placedText)
    {
        try
        {
            await _delay.DelayAsync(TimeSpan.FromMilliseconds(450), CancellationToken.None);
            var currentText = await _clipboard.ReadTextAsync(CancellationToken.None);
            if (ShouldRestoreClipboard(placedText, currentText))
            {
                await _clipboard.RestoreAsync(previousClipboard, CancellationToken.None);
            }
        }
        catch
        {
            // Clipboard restoration is best-effort and must never surface as an unobserved exception.
        }
    }

    internal static bool SendCtrlV()
    {
        var inputs = new[]
        {
            KeyboardInput(VkControl, 0),
            KeyboardInput(VkV, 0),
            KeyboardInput(VkV, KeyeventfKeyup),
            KeyboardInput(VkControl, KeyeventfKeyup)
        };
        return SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>()) == inputs.Length;
    }

    internal static int NativeInputStructureSize => Marshal.SizeOf<Input>();

    internal static bool FocusTargetWindow(IntPtr targetWindow)
    {
        if (targetWindow == IntPtr.Zero || !IsWindow(targetWindow))
        {
            return false;
        }

        if (IsIconic(targetWindow))
        {
            ShowWindow(targetWindow, SwRestore);
        }

        var foregroundWindow = GetForegroundWindow();
        var currentThread = GetCurrentThreadId();
        var foregroundThread = foregroundWindow == IntPtr.Zero ? 0 : GetWindowThreadProcessId(foregroundWindow, out _);
        var targetThread = GetWindowThreadProcessId(targetWindow, out _);

        if (foregroundThread != 0 && foregroundThread != currentThread)
        {
            AttachThreadInput(currentThread, foregroundThread, true);
        }

        if (targetThread != 0 && targetThread != currentThread)
        {
            AttachThreadInput(currentThread, targetThread, true);
        }

        try
        {
            return SetForegroundWindow(targetWindow);
        }
        finally
        {
            if (targetThread != 0 && targetThread != currentThread)
            {
                AttachThreadInput(currentThread, targetThread, false);
            }

            if (foregroundThread != 0 && foregroundThread != currentThread)
            {
                AttachThreadInput(currentThread, foregroundThread, false);
            }
        }
    }

    private static Input KeyboardInput(ushort virtualKey, uint flags)
    {
        return new Input
        {
            Type = InputKeyboard,
            Data = new InputUnion
            {
                Keyboard = new KeyboardInputData
                {
                    VirtualKey = virtualKey,
                    ScanCode = 0,
                    Flags = flags,
                    Time = 0,
                    ExtraInfo = UIntPtr.Zero
                }
            }
        };
    }

    internal static IntPtr GetForegroundWindowNative() => GetForegroundWindow();
    internal static bool IsWindowNative(IntPtr window) => IsWindow(window);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint numberOfInputs, Input[] inputs, int sizeOfInputStructure);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool attach);

    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public uint Type;
        public InputUnion Data;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)]
        public KeyboardInputData Keyboard;

        // INPUT is a native union. MOUSEINPUT is larger than KEYBDINPUT on
        // 64-bit Windows, so it must be represented even though Muesli only
        // sends keyboard input. Otherwise Marshal.SizeOf<Input>() is 32 rather
        // than the required 40 bytes and SendInput fails with ERROR_INVALID_PARAMETER.
        [FieldOffset(0)]
        public MouseInputData Mouse;

        [FieldOffset(0)]
        public HardwareInputData Hardware;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInputData
    {
        public ushort VirtualKey;
        public ushort ScanCode;
        public uint Flags;
        public uint Time;
        public UIntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInputData
    {
        public int X;
        public int Y;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public UIntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HardwareInputData
    {
        public uint Message;
        public ushort ParameterLow;
        public ushort ParameterHigh;
    }
}

public interface IClipboardAdapter
{
    Task<ClipboardSnapshot> CaptureAsync(CancellationToken cancellationToken);
    Task SetTextAsync(string text, CancellationToken cancellationToken);
    Task<string?> ReadTextAsync(CancellationToken cancellationToken);
    Task RestoreAsync(ClipboardSnapshot snapshot, CancellationToken cancellationToken);
}

public interface IWindowActivationAdapter
{
    IntPtr ForegroundWindow { get; }
    bool Exists(IntPtr window);
    bool RequestForeground(IntPtr window);
}

public interface IKeyboardInputAdapter
{
    bool SendPasteShortcut();
}

public interface IAsyncDelay
{
    Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken);
}

public sealed record ClipboardSnapshot(System.Windows.IDataObject? Data);

public sealed class SystemAsyncDelay : IAsyncDelay
{
    public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken) => Task.Delay(delay, cancellationToken);
}

public sealed class NativeWindowActivationAdapter : IWindowActivationAdapter
{
    public IntPtr ForegroundWindow => ActiveAppPasteService.GetForegroundWindowNative();
    public bool Exists(IntPtr window) => ActiveAppPasteService.IsWindowNative(window);
    public bool RequestForeground(IntPtr window) => ActiveAppPasteService.FocusTargetWindow(window);
}

public sealed class NativeKeyboardInputAdapter : IKeyboardInputAdapter
{
    public bool SendPasteShortcut() => ActiveAppPasteService.SendCtrlV();
}

public sealed class WpfClipboardAdapter : IClipboardAdapter
{
    public Task<ClipboardSnapshot> CaptureAsync(CancellationToken cancellationToken) =>
        InvokeWithRetriesAsync(() => new ClipboardSnapshot(System.Windows.Clipboard.GetDataObject()), cancellationToken);

    public Task SetTextAsync(string text, CancellationToken cancellationToken) =>
        InvokeWithRetriesAsync(() => System.Windows.Clipboard.SetText(text), cancellationToken);

    public Task<string?> ReadTextAsync(CancellationToken cancellationToken) =>
        InvokeWithRetriesAsync(
            () => System.Windows.Clipboard.ContainsText() ? System.Windows.Clipboard.GetText() : null,
            cancellationToken);

    public Task RestoreAsync(ClipboardSnapshot snapshot, CancellationToken cancellationToken)
    {
        if (snapshot.Data is null)
        {
            return Task.CompletedTask;
        }
        return InvokeWithRetriesAsync(() => System.Windows.Clipboard.SetDataObject(snapshot.Data, true), cancellationToken);
    }

    private static async Task<T> InvokeWithRetriesAsync<T>(Func<T> action, CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (System.Windows.Application.Current?.Dispatcher is { } dispatcher && !dispatcher.CheckAccess())
                {
                    return await dispatcher.InvokeAsync(action, System.Windows.Threading.DispatcherPriority.Send, cancellationToken);
                }
                return action();
            }
            catch (COMException) when (attempt < 4)
            {
                await Task.Delay(20 * (attempt + 1), cancellationToken);
            }
        }
    }

    private static async Task InvokeWithRetriesAsync(Action action, CancellationToken cancellationToken)
    {
        await InvokeWithRetriesAsync(() =>
        {
            action();
            return true;
        }, cancellationToken);
    }
}

public sealed record PasteOperationResult(
    long TotalMs,
    long ClipboardMs,
    long FocusWaitMs,
    long InputMs,
    bool TargetWasForeground);

public sealed record PasteTargetInfo(string ProcessName, uint ProcessId)
{
    public static PasteTargetInfo Unknown { get; } = new("unknown", 0);
}
