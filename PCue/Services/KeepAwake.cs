using System.Runtime.InteropServices;

namespace PCue.Services;

/// <summary>Keeps the PC and displays awake while a cue plays or a schedule is pending.</summary>
public static class KeepAwake
{
    private const uint EsContinuous = 0x80000000;
    private const uint EsSystemRequired = 0x00000001;
    private const uint EsDisplayRequired = 0x00000002;

    private static bool _active;

    [DllImport("kernel32.dll")]
    private static extern uint SetThreadExecutionState(uint flags);

    /// <summary>Must be called from the UI thread (the flag is per-thread).</summary>
    public static void Set(bool active)
    {
        if (_active == active)
            return;

        _active = active;
        SetThreadExecutionState(active
            ? EsContinuous | EsSystemRequired | EsDisplayRequired
            : EsContinuous);
    }
}
