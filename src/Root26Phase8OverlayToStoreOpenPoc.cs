using System;
using Il2Cpp;
using Il2CppSteamworks;
using MelonLoader;

namespace NocturneModernController
{
    // Root-26 Phase 8: minimal causal PoC (NOT read-only - calls a real
    // Steamworks API with a genuine, visible side effect: it opens Steam's
    // own Store-page Overlay for this game). See docs/research/
    // RIGHT_STICK_VIEW_AND_DASH_INVESTIGATION.md section 49.
    //
    // Purpose: test whether a REAL Steam Overlay activation triggered from
    // inside the game (via the one Overlay-opening API this game's
    // Steamworks.NET binding actually exposes - section 48.1,
    // SteamFriends.ActivateGameOverlayToStore - no general-purpose
    // ActivateGameOverlay(string) is bound) produces a genuine
    // GameOverlayActivated_t callback from Steam, exactly like a physical
    // Guide-button-triggered Overlay does (sections 44-45). This is
    // fundamentally different from Phase 4 (section 42): it does NOT call
    // SteamInputUtil.ResetController()/SteamPad.SteamControllerReStart()/
    // Steamworks.SteamInput.Shutdown()/Init()/SteamPad.
    // UpdateConnectedControllers() directly - it only asks Steam to open
    // its own Overlay through the official SDK entry point, then relies
    // entirely on the game's own existing OnGameOverlayActivated callback
    // (section 44) to react exactly as it already does for a physical
    // Guide/Overlay session. Closing the Overlay is left to the user
    // (manual) in this Phase - no synthetic Esc/SendInput is used here.
    //
    // Safety:
    // - Fires AT MOST ONCE per game session (hard boolean latch, set
    //   before the call so it can never re-fire even if something below
    //   throws).
    // - Not bound to any new keyboard hotkey - triggers automatically a
    //   fixed delay after FieldDashPatch.IsExplorationActive first becomes
    //   true, reusing the existing exploration-state signal instead of
    //   adding new input handling.
    // - The AppId is read from Il2Cpp.SteamManager.APPID (the game's own
    //   already-initialized Steamworks AppId) - never hardcoded.
    // - Calls ONLY SteamFriends.ActivateGameOverlayToStore(appId,
    //   EOverlayToStoreFlag.k_EOverlayToStoreFlag_None) - no cart flags,
    //   no other Steamworks API.
    // - No manual ResetController()/SteamControllerReStart()/Shutdown()/
    //   Init()/UpdateConnectedControllers() call. No F9/F10. No SendInput/
    //   synthetic key events of any kind.
    // - All subsequent observation (Overlay ON/OFF callback, RSTICK
    //   revival) happens via the existing Root26Phase5 / Root26Phase2 /
    //   RightStickPollingProbe instrumentation - nothing new is added for
    //   that here.
    internal static class Root26Phase8OverlayToStoreOpenPoc
    {
        private const string Tag = "[NocturneModernController][Root26Phase8]";
        private const long DelayMsAfterExplorationStart = 5000;

        private static bool _hasFiredThisSession;
        private static bool _explorationEverActive;
        private static long _explorationStartTickMs = -1;

        // Called once per frame from ModMain.OnUpdate(), unconditionally.
        internal static void Sample()
        {
            if (_hasFiredThisSession)
            {
                return;
            }

            bool explorationActive;
            try
            {
                explorationActive = FieldDashPatch.IsExplorationActive;
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"{Tag} FieldDashPatch.IsExplorationActive access threw {ex.GetType().FullName}: {ex.Message}");
                return;
            }

            if (!explorationActive)
            {
                return;
            }

            long now = Environment.TickCount64;
            if (!_explorationEverActive)
            {
                _explorationEverActive = true;
                _explorationStartTickMs = now;
                MelonLogger.Msg(
                    $"{Tag} exploration first became active at {DateTimeOffset.Now:O}; " +
                    $"will fire ActivateGameOverlayToStore PoC in {DelayMsAfterExplorationStart}ms " +
                    "(single-fire-per-session latch, no new hotkey)");
                return;
            }

            if (now - _explorationStartTickMs < DelayMsAfterExplorationStart)
            {
                return;
            }

            // Latch immediately, before doing any work, so this can never
            // fire twice even if something below throws.
            _hasFiredThisSession = true;

            MelonLogger.Msg($"{Tag} PHASE8-TRIGGER at {DateTimeOffset.Now:O}");

            AppId_t appId;
            try
            {
                appId = SteamManager.APPID;
                MelonLogger.Msg($"{Tag} PHASE8-APPID appId={appId} at {DateTimeOffset.Now:O}");
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"{Tag} SteamManager.APPID threw {ex.GetType().FullName}: {ex.Message} - PoC aborted.");
                return;
            }

            try
            {
                SteamFriends.ActivateGameOverlayToStore(appId, EOverlayToStoreFlag.k_EOverlayToStoreFlag_None);
                MelonLogger.Msg(
                    $"{Tag} PHASE8-CALL SteamFriends.ActivateGameOverlayToStore(appId={appId}, " +
                    $"None) returned normally at {DateTimeOffset.Now:O}. Overlay close is manual - " +
                    "no synthetic Esc/SendInput is used in this Phase.");
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"{Tag} ActivateGameOverlayToStore threw {ex.GetType().FullName}: {ex.Message}");
            }
        }
    }
}
