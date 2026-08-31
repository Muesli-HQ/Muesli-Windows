namespace Muesli.Windows.Services;

public sealed class DictationHotkeyStateMachine
{
    public DictationHotkeyState State { get; private set; } = DictationHotkeyState.Idle;

    public bool IsHandsFree => State == DictationHotkeyState.HandsFree;

    public DictationHotkeyAction KeyDown(bool doubleTapEnabled)
    {
        if (!doubleTapEnabled)
        {
            if (State != DictationHotkeyState.Idle)
            {
                return DictationHotkeyAction.None;
            }

            State = DictationHotkeyState.Holding;
            return DictationHotkeyAction.StartRecording;
        }

        return State switch
        {
            DictationHotkeyState.Idle => Transition(DictationHotkeyState.Holding, DictationHotkeyAction.StartRecording),
            DictationHotkeyState.AwaitingSecondTap => Transition(DictationHotkeyState.HandsFree, DictationHotkeyAction.EnterHandsFree),
            DictationHotkeyState.HandsFree => Transition(DictationHotkeyState.Idle, DictationHotkeyAction.StopRecording),
            _ => DictationHotkeyAction.None
        };
    }

    public DictationHotkeyAction KeyUp(bool doubleTapEnabled)
    {
        if (State != DictationHotkeyState.Holding)
        {
            return DictationHotkeyAction.None;
        }

        return doubleTapEnabled
            ? Transition(DictationHotkeyState.AwaitingSecondTap, DictationHotkeyAction.StartDoubleTapTimer)
            : Transition(DictationHotkeyState.Idle, DictationHotkeyAction.StopRecording);
    }

    public DictationHotkeyAction DoubleTapWindowElapsed()
    {
        return State == DictationHotkeyState.AwaitingSecondTap
            ? Transition(DictationHotkeyState.Idle, DictationHotkeyAction.StopRecording)
            : DictationHotkeyAction.None;
    }

    public void Reset() => State = DictationHotkeyState.Idle;

    private DictationHotkeyAction Transition(DictationHotkeyState state, DictationHotkeyAction action)
    {
        State = state;
        return action;
    }
}

public enum DictationHotkeyState
{
    Idle,
    Holding,
    AwaitingSecondTap,
    HandsFree
}

public enum DictationHotkeyAction
{
    None,
    StartRecording,
    StopRecording,
    StartDoubleTapTimer,
    EnterHandsFree
}
