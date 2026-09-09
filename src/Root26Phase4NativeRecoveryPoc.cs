using System;
using System.Runtime.InteropServices;
using Il2CppSteamworks;
using MelonLoader;

namespace NocturneModernController
{
    // Root-26 Phase 4: minimal-sequence causal PoC (NOT a read-only probe -
    // this deliberately calls a real Steamworks API with side effects, on a
    // single explicit user key press, exactly once per session). See
    // docs/research/RIGHT_STICK_VIEW_AND_DASH_INVESTIGATION.md section 41
    // for the full rationale.
    //
    // Purpose: isolate whether Steamworks.SteamInput.Shutdown()+Init() ALONE
    // (the core operation confirmed inside SteamPad.SteamControllerReStart(),
    // see section 35.3) is sufficient to revive Right Stick analog data
    // delivery, WITHOUT Guide/Steam Overlay and WITHOUT calling
    // SteamInputUtil.ResetController() or SteamPad.
    // UpdateConnectedControllers() at all. Known prior result (user-provided):
    // a valid manual ResetController()-alone test did NOT revive Right Stick,
    // so ResetController() is confirmed NOT sufficient by itself. This PoC
    // tests one narrower candidate (Shutdown+Init alone) in isolation, as
    // the first of a planned one-variable-at-a-time series (if this fails,
    // a separate future PoC would add UpdateConnectedControllers() on top).
    //
    // Trigger key: F10 (VK 0x79). Deliberately NOT F9 - F9 was the trigger
    // for the retired ManualResetControllerPoc and was associated (see
    // section ~18-19 of the Evidence doc) with an unexplained invisible
    // System->Quit confirmation dialog side effect from a third-party mod
    // interaction; F9 is permanently retired for this investigation. F10 is
    // not bound to any NocturneModernController action (dash uses 'P') and
    // is not one of SMT3HD's own assignable function keys.
    //
    // Safety:
    // - Fires at most ONCE per game session (a hard boolean latch, not a
    //   time-based cooldown - avoids the integer-overflow cooldown bug class
    //   that affected the retired F9 PoC's first iteration).
    // - Edge-triggered on the key-down transition only.
    // - Calls ONLY Il2CppSteamworks.SteamInput.Shutdown() then
    //   Il2CppSteamworks.SteamInput.Init(), in that order, with a null/
    //   exception-safe guard. Never calls SteamInputUtil.ResetController(),
    //   SteamPad.SteamControllerReStart(), or SteamPad.
    //   UpdateConnectedControllers() directly - if the game itself calls any
    //   of those afterward, that is observed (not caused) via the existing
    //   Root26 probes exactly as it already does for the Guide/Overlay path.
    // - After firing, notifies Root26Phase3PostResetWatchProbe's existing
    //   observation window (same mechanism used for the Guide/Overlay path)
    //   so the post-trigger interval is captured with the same
    //   instrumentation, enabling a direct side-by-side comparison.
    // - No Broker/InputHelper/SDL involvement, no other Steam Input method
    //   invoked, no write to any handle/buffer/action-data value.
    internal static class Root26Phase4NativeRecoveryPoc
    {
        private const string Tag = "[NocturneModernController][Root26Phase4]";
        private const int VirtualKeyTrigger = 0x79; // F10

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int virtualKey);

        private static bool _keyWasDown;
        private static bool _hasFiredThisSession;

        // Called once per frame from ModMain.OnUpdate(), unconditionally
        // (not gated to FieldDashPatch.IsExplorationActive), matching the
        // pattern used by the retired ManualResetControllerPoc.
        internal static void Sample()
        {
            if (_hasFiredThisSession)
            {
                return;
            }

            bool keyIsDown = (GetAsyncKeyState(VirtualKeyTrigger) & 0x8000) != 0;
            bool risingEdge = keyIsDown && !_keyWasDown;
            _keyWasDown = keyIsDown;

            if (!risingEdge)
            {
                return;
            }

            // Latch immediately, before doing any work, so this can never
            // fire twice even if something below throws.
            _hasFiredThisSession = true;

            MelonLogger.Msg(
                $"{Tag} PHASE4-TRIGGER (F10, single explicit press, one-time-per-session " +
                $"causal PoC) at {DateTimeOffset.Now:O}");

            bool shutdownOk;
            try
            {
                shutdownOk = SteamInput.Shutdown();
                MelonLogger.Msg($"{Tag} PHASE4-CALL SteamInput.Shutdown() returned {shutdownOk} at {DateTimeOffset.Now:O}");
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"{Tag} SteamInput.Shutdown() threw {ex.GetType().FullName}: {ex.Message}");
                return;
            }

            bool initOk;
            try
            {
                initOk = SteamInput.Init();
                MelonLogger.Msg($"{Tag} PHASE4-CALL SteamInput.Init() returned {initOk} at {DateTimeOffset.Now:O}");
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"{Tag} SteamInput.Init() threw {ex.GetType().FullName}: {ex.Message}");
                return;
            }

            MelonLogger.Msg(
                $"{Tag} PHASE4-DONE Shutdown()={shutdownOk} Init()={initOk}. " +
                "ResetController()/SteamControllerReStart()/UpdateConnectedControllers() were NOT " +
                $"called by this PoC. Observing via existing Root26Phase2/NativePoll probes at {DateTimeOffset.Now:O}");

            Root26Phase3PostResetWatchProbe.NotifyResetEvent("Phase4 manual SteamInput.Shutdown()+Init()");
        }
    }
}
