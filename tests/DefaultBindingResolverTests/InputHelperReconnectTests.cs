using System;

// InputHelper: which SDL gamepad ADDED / REMOVED events close or reopen the
// opened handle (helper/GamepadReconnectState.cs).
internal static class InputHelperReconnectTests
{
    internal static void Run()
    {
        var state = new GamepadReconnectState();
        Check(state.AllowsPeriodicRetry && !state.ShouldReopenOnAdded(),
            "startup keeps the existing periodic retry; ADDED alone does not reopen");

        state.Opened(7);
        Check(!state.AllowsPeriodicRetry, "an opened handle stops the retry");

        Check(!state.ShouldCloseOnRemoved(9) && state.HasOpen && state.OpenedId == 7,
            "Test A: another controller removed - the opened handle stays");

        Check(!state.ShouldReopenOnAdded(),
            "Test D: ADDED while a handle is open - no second open");

        Check(state.ShouldCloseOnRemoved(7), "Test B: the opened controller removed - close it");
        state.ClosedAfterRemoval();
        Check(!state.HasOpen && state.WaitingForAdded && !state.AllowsPeriodicRetry,
            "Test B/E: closed (the loop then writes active=0, x=0, y=0) and waits for ADDED, no polling");

        Check(state.ShouldReopenOnAdded(), "Test C: no handle + ADDED - reopen attempt");
        Check(state.ShouldReopenOnAdded() && state.WaitingForAdded,
            "a failed reopen keeps waiting for the next ADDED");

        state.Opened(12);
        Check(state.HasOpen && state.OpenedId == 12 && !state.WaitingForAdded,
            "Test F: reopen success tracks the new SDL instance id");
        Check(!state.ShouldCloseOnRemoved(7), "the old instance id no longer matches");
        Check(state.ShouldCloseOnRemoved(12), "repeated disconnects: the new id is tracked");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException("InputHelper reconnect: " + message);
        }
    }
}
