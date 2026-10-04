// Which SDL device events the helper acts on. Pure, so the rules can be
// unit tested without SDL.
//
// Startup is unchanged: until the first device removal the helper keeps its
// existing periodic TARGET WAIT retry. Once the opened gamepad is removed
// (SDL_EVENT_GAMEPAD_REMOVED for its own SDL_JoystickID - other controllers
// are ignored), the handle is closed and the helper waits for
// SDL_EVENT_GAMEPAD_ADDED instead of polling; each ADDED runs the existing
// TryOpenValidatedGamepad selection until one succeeds.
internal sealed class GamepadReconnectState
{
    internal uint OpenedId { get; private set; }
    internal bool HasOpen { get; private set; }
    internal bool WaitingForAdded { get; private set; }

    internal bool AllowsPeriodicRetry => !HasOpen && !WaitingForAdded;

    internal void Opened(uint instanceId)
    {
        OpenedId = instanceId;
        HasOpen = true;
        WaitingForAdded = false;
    }

    internal bool ShouldCloseOnRemoved(uint removedId) => HasOpen && removedId == OpenedId;

    internal void ClosedAfterRemoval()
    {
        OpenedId = 0;
        HasOpen = false;
        WaitingForAdded = true;
    }

    internal bool ShouldReopenOnAdded() => !HasOpen && WaitingForAdded;
}
