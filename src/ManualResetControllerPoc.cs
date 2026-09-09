using System;
using System.Runtime.InteropServices;
using Il2Cpp;
using MelonLoader;

namespace NocturneModernController
{
    // Root-26 diagnostic-only PoC (causal test, not production auto-recovery):
    // on a single explicit key press, calls SteamInputUtil.instance.ResetController()
    // exactly once, then relies entirely on the existing Root26 instrumentation
    // (Root26SteamState CALL logging on SteamPad.SteamControllerReStart() /
    // SteamPad.UpdateConnectedControllers(), and RightStickPollingProbe's native
    // GetPadAnalog STATE-TRANSITION logging) to observe whether Right Stick goes
    // DEAD -> LIVE without any Guide press / Steam Overlay.
    //
    // Trigger key: F9 (VK 0x78). Chosen because it is not bound to any existing
    // NocturneModernController action (dash uses 'P' = 0x50, the disabled
    // FocusCycleProbe Win-Esc probe used VK_LWIN/VK_ESCAPE, ModernControllerApi/
    // BuiltInControllerActions bind physical gamepad inputs only) and is not one
    // of SMT3HD's own assignable function keys per
    // docs/research/RIGHT_STICK_VIEW_AND_DASH_INVESTIGATION.md section 1
    // (F13/F14 are excluded from the in-game assignment screen; F9 is likewise
    // not used by any vanilla default binding referenced in this repo's Evidence).
    //
    // Safety:
    // - Edge-triggered on the down transition only (never fires while held,
    //   never fires every frame) - see IsExplorationActive-independent polling
    //   below.
    // - A minimum cooldown between triggers guards against key-repeat/bounce
    //   even though GetAsyncKeyState edge-detection alone already prevents
    //   continuous firing.
    // - Calls ONLY SteamInputUtil.instance.ResetController(). Never calls
    //   SteamPad.SteamControllerReStart() or SteamPad.UpdateConnectedControllers()
    //   directly - those are only ever observed via the existing Root26SteamState
    //   Harmony Prefix call-count probes, exactly as the game itself would invoke
    //   them (if at all) from inside ResetController().
    // - Verifies SteamInputUtil.instance is non-null before calling; if null,
    //   logs a warning and does nothing (no retry loop, no auto-fallback).
    // - No controller state is written outside of this one explicit,
    //   user-triggered ResetController() call. No Broker/InputHelper/SDL
    //   involvement. No focus/WM_INPUT/Method D instrumentation added.
    internal static class ManualResetControllerPoc
    {
        private const int VirtualKeyManualReset = 0x78; // F9
        private const int MinimumCooldownMilliseconds = 3000;

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int virtualKey);

        private static bool _keyWasDown;
        private static bool _hasTriggeredBefore;
        private static int _lastTriggerTick;

        // Called once per frame from ModMain.OnUpdate(), unconditionally (not
        // gated to FieldDashPatch.IsExplorationActive), so the PoC can be
        // triggered from the same DEAD-state window that RightStickPollingProbe
        // is already observing.
        internal static void Sample()
        {
            bool keyIsDown = (GetAsyncKeyState(VirtualKeyManualReset) & 0x8000) != 0;
            bool risingEdge = keyIsDown && !_keyWasDown;
            _keyWasDown = keyIsDown;

            if (!risingEdge)
            {
                return;
            }

            int now = Environment.TickCount;
            if (_hasTriggeredBefore && unchecked(now - _lastTriggerTick) < MinimumCooldownMilliseconds)
            {
                MelonLogger.Msg(
                    "[NocturneModernController][Root26ManualResetPoc] F9 pressed but ignored " +
                    $"(cooldown, {MinimumCooldownMilliseconds}ms minimum between triggers) at {DateTimeOffset.Now:O}");
                return;
            }
            _hasTriggeredBefore = true;
            _lastTriggerTick = now;

            SteamInputUtil instance;
            try
            {
                instance = SteamInputUtil.instance;
            }
            catch (Exception ex)
            {
                MelonLogger.Warning(
                    "[NocturneModernController][Root26ManualResetPoc] SteamInputUtil.instance access threw " +
                    ex.GetType().FullName + ": " + ex.Message + " - ResetController() NOT called.");
                return;
            }

            if (instance == null)
            {
                MelonLogger.Warning(
                    "[NocturneModernController][Root26ManualResetPoc] SteamInputUtil.instance is null - " +
                    "ResetController() NOT called.");
                return;
            }

            MelonLogger.Msg(
                "[NocturneModernController][Root26ManualResetPoc] MANUAL-RESET-REQUEST " +
                $"(F9, single explicit press, diagnostic causal-test PoC) at {DateTimeOffset.Now:O}");

            try
            {
                instance.ResetController();
                MelonLogger.Msg(
                    "[NocturneModernController][Root26ManualResetPoc] MANUAL-RESET-REQUEST invoked " +
                    $"SteamInputUtil.instance.ResetController() (returned normally) at {DateTimeOffset.Now:O}");
            }
            catch (Exception ex)
            {
                MelonLogger.Warning(
                    "[NocturneModernController][Root26ManualResetPoc] ResetController() threw " +
                    ex.GetType().FullName + ": " + ex.Message);
            }
        }
    }
}
