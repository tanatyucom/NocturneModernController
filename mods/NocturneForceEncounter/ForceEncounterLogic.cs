using System;

namespace NocturneForceEncounter
{
    internal enum ForceEncounterEvent
    {
        None,
        Requested,
        TimedOut
    }

    // Exactly one input source is read per frame: the Controller action
    // (player's key config) when the integration is active, otherwise the
    // game's own X button. Never both, so one press cannot request twice.
    internal static class ForceEncounterInput
    {
        internal static bool ReadHeld(bool integrationActive, Func<bool> controllerHeld, Func<bool> standaloneHeld) =>
            integrationActive ? controllerHeld() : standaloneHeld();
    }

    // Request state of Force Encounter, moved unchanged from Controller's
    // built-in ForceEncounterRuntime. Pure: callers pass in the game/Controller
    // state, so the rules can be unit tested.
    //
    // A press (rising edge) while exploring marks a request as pending. The
    // next native normal-encounter check (nbEncount.nbEncountCalc) is then
    // given a travel length large enough to roll an encounter through the
    // game's own logic. The request ends when that check reports a battle, or
    // after 2 seconds (e.g. no encounters on this map).
    internal sealed class ForceEncounterLogic
    {
        internal const int RequestTimeoutMilliseconds = 2000;
        internal const float ForcedTravelLength = 100000.0f;

        private bool _wasHeld;
        private bool _requestPending;
        private int _requestTick;

        internal bool IsRequestPending => _requestPending;

        // Controller only sampled the request while the feature was enabled,
        // field exploration was active and Settings was closed; outside that,
        // nothing (including the pending request) changes.
        internal ForceEncounterEvent Sample(
            bool enabled, bool explorationActive, bool settingsOpen, bool held, int nowTick)
        {
            if (!enabled || !explorationActive || settingsOpen)
            {
                return ForceEncounterEvent.None;
            }

            ForceEncounterEvent result = ForceEncounterEvent.None;
            if (held && !_wasHeld)
            {
                _requestPending = true;
                _requestTick = nowTick;
                result = ForceEncounterEvent.Requested;
            }
            _wasHeld = held;

            if (_requestPending &&
                unchecked(nowTick - _requestTick) >= RequestTimeoutMilliseconds)
            {
                _requestPending = false;
                result = ForceEncounterEvent.TimedOut;
            }
            return result;
        }

        internal void BoostNextNormalCheck(ref float length, bool explorationActive)
        {
            if (_requestPending && explorationActive)
            {
                length = Math.Max(length, ForcedTravelLength);
            }
        }

        // True when the pending request was accepted by the native check.
        internal bool ObserveNormalCheckResult(int result)
        {
            if (!_requestPending || result == 0)
            {
                return false;
            }
            _requestPending = false;
            return true;
        }
    }
}
