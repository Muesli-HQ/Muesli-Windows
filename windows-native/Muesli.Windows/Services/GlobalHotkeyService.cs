using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Input;

namespace Muesli.Windows.Services;

public sealed class GlobalHotkeyService : IDisposable
{
    private const int WhKeyboardLl = 13;
    private const int WmKeydown = 0x0100;
    private const int WmKeyup = 0x0101;
    private const int WmSyskeydown = 0x0104;
    private const int WmSyskeyup = 0x0105;

    private LowLevelKeyboardProc? _hookProc;
    private IntPtr _hookId = IntPtr.Zero;
    private HotkeyGesture _gesture = HotkeyGesture.Parse("F8");
    private bool _isDown;
    private Action? _onDown;
    private Action? _onUp;

    public void Register(string gesture, Action onDown, Action onUp)
    {
        Dispose();
        _gesture = HotkeyGesture.Parse(gesture);
        _onDown = onDown;
        _onUp = onUp;
        _hookProc = HookCallback;
        _hookId = SetHook(_hookProc);
        if (_hookId == IntPtr.Zero)
        {
            throw new InvalidOperationException($"Could not register global keyboard hook for {_gesture.Label}.");
        }
    }

    public void Dispose()
    {
        if (_hookId == IntPtr.Zero)
        {
            return;
        }

        UnhookWindowsHookEx(_hookId);
        _hookId = IntPtr.Zero;
        _isDown = false;
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var message = wParam.ToInt32();
            var vkCode = Marshal.ReadInt32(lParam);
            var key = KeyInterop.KeyFromVirtualKey(vkCode);
            if (_gesture.Matches(key, ModifierKeysFromState()))
            {
                if ((message == WmKeydown || message == WmSyskeydown) && !_isDown)
                {
                    _isDown = true;
                    _onDown?.Invoke();
                }
                else if ((message == WmKeyup || message == WmSyskeyup) && _isDown)
                {
                    _isDown = false;
                    _onUp?.Invoke();
                }
            }
        }

        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    private static ModifierKeys ModifierKeysFromState()
    {
        var modifiers = ModifierKeys.None;
        if (IsKeyDown(VkControl))
        {
            modifiers |= ModifierKeys.Control;
        }

        if (IsKeyDown(VkShift))
        {
            modifiers |= ModifierKeys.Shift;
        }

        if (IsKeyDown(VkMenu))
        {
            modifiers |= ModifierKeys.Alt;
        }

        if (IsKeyDown(VkLWin) || IsKeyDown(VkRWin))
        {
            modifiers |= ModifierKeys.Windows;
        }

        return modifiers;
    }

    private static bool IsKeyDown(int virtualKey)
    {
        return (GetAsyncKeyState(virtualKey) & 0x8000) != 0;
    }

    private static IntPtr SetHook(LowLevelKeyboardProc proc)
    {
        using var currentProcess = Process.GetCurrentProcess();
        using var currentModule = currentProcess.MainModule;
        return SetWindowsHookEx(
            WhKeyboardLl,
            proc,
            GetModuleHandle(currentModule?.ModuleName),
            0);
    }

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    private const int VkShift = 0x10;
    private const int VkControl = 0x11;
    private const int VkMenu = 0x12;
    private const int VkLWin = 0x5B;
    private const int VkRWin = 0x5C;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    private sealed record HotkeyGesture(Key Key, ModifierKeys Modifiers, string Label)
    {
        public static HotkeyGesture Parse(string value)
        {
            var label = string.IsNullOrWhiteSpace(value) ? "F8" : value.Trim();
            var key = Key.None;
            var modifiers = ModifierKeys.None;

            foreach (var rawPart in label.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var part = rawPart.Replace(" ", "", StringComparison.OrdinalIgnoreCase);
                if (part.Equals("Ctrl", StringComparison.OrdinalIgnoreCase) ||
                    part.Equals("Control", StringComparison.OrdinalIgnoreCase))
                {
                    modifiers |= ModifierKeys.Control;
                }
                else if (part.Equals("Shift", StringComparison.OrdinalIgnoreCase))
                {
                    modifiers |= ModifierKeys.Shift;
                }
                else if (part.Equals("Alt", StringComparison.OrdinalIgnoreCase) ||
                         part.Equals("Option", StringComparison.OrdinalIgnoreCase))
                {
                    modifiers |= ModifierKeys.Alt;
                }
                else if (part.Equals("Win", StringComparison.OrdinalIgnoreCase) ||
                         part.Equals("Windows", StringComparison.OrdinalIgnoreCase))
                {
                    modifiers |= ModifierKeys.Windows;
                }
                else if (!Enum.TryParse(part, ignoreCase: true, out key))
                {
                    throw new InvalidOperationException($"Unsupported hotkey: {label}");
                }
            }

            if (key == Key.None)
            {
                throw new InvalidOperationException($"Unsupported hotkey: {label}");
            }

            return new HotkeyGesture(key, modifiers, label);
        }

        public bool Matches(Key key, ModifierKeys modifiers)
        {
            return key == Key && (modifiers & Modifiers) == Modifiers;
        }
    }
}
