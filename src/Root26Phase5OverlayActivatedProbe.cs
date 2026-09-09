using System;
using HarmonyLib;
using Il2Cpp;
using Il2CppSteamworks;
using MelonLoader;

namespace NocturneModernController
{
    // Root-26 Phase 5: read-only observation of the Steam Overlay
    // activation/deactivation callback, dds3DefaultMain.
    // OnGameOverlayActivated(GameOverlayActivated_t). See docs/research/
    // RIGHT_STICK_VIEW_AND_DASH_INVESTIGATION.md section 44.
    //
    // Static analysis (Ghidra decompile of the exact 431-byte native body,
    // RVA 0x22D2DE0) found that this handler CALLS SteamInputUtil.
    // ResetController() DIRECTLY, unconditionally, whenever the Overlay is
    // reported as deactivated (pCallback.m_bActive == 0) - this is the
    // "extra state transition present on the Guide/Overlay path but not on
    // a bare manual SteamInput.Shutdown()/Init() call" that section 43
    // was looking for. It also calls dds3DefaultMain.PauseResume(bool)
    // (RVA 0x22D2F90, the SAME helper OnApplicationFocus(bool) calls at
    // its own tail) - once with 0 when the overlay becomes active, and
    // conditionally with 1 shortly after it becomes inactive.
    //
    // This probe just observes the Harmony Prefix argument (m_bActive) and
    // logs on every call (this event is rare - only fires when Steam
    // Overlay opens/closes - so per-call logging does not flood, unlike
    // the per-frame probes elsewhere in this mod). Never touches the
    // argument or any state. No manual Steam API calls, no F9/F10, no
    // ResetController()/SteamControllerReStart()/Shutdown()/Init() called
    // by this probe itself - only observes what the game's own callback
    // handler does.
    [HarmonyPatch(typeof(dds3DefaultMain), nameof(dds3DefaultMain.OnGameOverlayActivated))]
    internal static class Root26Phase5OverlayActivatedProbe
    {
        private const string Tag = "[NocturneModernController][Root26Phase5]";
        private static long _callCount;

        private static void Prefix(GameOverlayActivated_t pCallback)
        {
            long count = System.Threading.Interlocked.Increment(ref _callCount);
            MelonLogger.Msg(
                $"{Tag} OVERLAY-ACTIVATED-CALLBACK count={count} m_bActive={pCallback.m_bActive} " +
                $"at {DateTimeOffset.Now:O}");
        }
    }
}
