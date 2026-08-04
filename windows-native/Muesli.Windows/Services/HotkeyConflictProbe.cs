using System.Runtime.InteropServices;
using System.Windows.Input;

namespace Muesli.Windows.Services;

/// <summary>
/// Separates the decisive candidate-hook result from restoration of the user's active
/// hook.  The candidate result is captured before restoration so a restoration failure
/// cannot rewrite the verification result.
/// </summary>
public sealed record HotkeyCandidateHookResult(bool CandidateRegistered, bool PreviousHookRestored, Exception? RestorationException = null);

public static class HotkeyCandidateHookTest
{
    public static HotkeyCandidateHookResult Run(Func<bool> registerCandidate, Func<bool> restorePrevious)
    {
        var candidateRegistered = false;
        var previousHookRestored = false;
        Exception? restorationException = null;
        try
        {
            candidateRegistered = registerCandidate();
        }
        finally
        {
            try { previousHookRestored = restorePrevious(); }
            catch (Exception exception) { restorationException = exception; }
        }
        return new HotkeyCandidateHookResult(candidateRegistered, previousHookRestored, restorationException);
    }
}

public static class HotkeyConflictProbe
{
    public static bool IsAdvisoryRegistrationAvailable(string gesture)
    {
        var parsed = HotkeyGesture.Parse(gesture);
        var id = unchecked((int)(0x4D550000 | (Environment.TickCount & 0x7fff)));
        var modifiers = (uint)parsed.Modifiers;
        try { return RegisterHotKey(IntPtr.Zero, id, modifiers, (uint)KeyInterop.VirtualKeyFromKey(parsed.Key)); }
        finally { UnregisterHotKey(IntPtr.Zero, id); }
    }

    [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
