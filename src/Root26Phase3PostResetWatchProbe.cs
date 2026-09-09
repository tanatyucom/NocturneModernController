using System;
using System.Collections.Generic;
using HarmonyLib;
using Il2Cpp;
using Il2CppSteamworks;
using MelonLoader;

namespace NocturneModernController
{
    // Root-26 Phase 3 (minimal): read-only observation of what GameAssembly
    // -side calls happen in the ~2s window after ResetController()/
    // SteamControllerReStart() fire, to check whether the ~815ms delay
    // before RSTICK values go real (see section 34) is explained by
    // additional GameAssembly-side re-initialization/re-enumeration/
    // re-registration work, or whether nothing new happens on the game
    // side during that window (supporting the section 38.6 HYPOTHESIS that
    // the delay is Steam-client-side async work outside GameAssembly.dll).
    //
    // Newly tracked call counters (Harmony Prefix, count-only + periodic
    // snapshot - NOT logged on every call, since some of these are known/
    // suspected to fire every frame and per-call logging would flood):
    //   - SteamPad.UpdateConnectedControllers()
    //   - SteamPad.get_action(int index)          (captures index)
    //   - SteamPad.UpdateControl()
    //   - Il2CppSteamworks.SteamInput.ActivateActionSet(...)
    //   - Il2CppSteamworks.SteamInput.GetConnectedControllers(...)
    // Reuses existing counters for SteamPadSet/UpdateInput/ResetController/
    // SteamControllerReStart (Root26Phase1* / Root26*CallProbe) rather than
    // duplicating them.
    //
    // Watch window: starts (or extends) whenever ResetController() or
    // SteamControllerReStart() fires (hooked from those existing probes'
    // Prefix), and stays open for WatchWindowMs (2500ms, comfortably
    // covering the observed ~815ms-1.3s delay with margin). While open,
    // Sample() (called every frame from ModMain.OnUpdate()) logs a
    // SNAPSHOT every SnapshotIntervalMs (~100ms) of cumulative call counts
    // for every tracked method, so the resulting log can be read as a
    // delta-per-100ms timeline distinguishing "fired once right after
    // Reset" from "keeps firing every frame throughout the window".
    //
    // Read-only: all Harmony patches are Prefix-only, never touch
    // __result or any argument. No manual ResetController()/
    // SteamControllerReStart() call, no F9, no native patch/detour.
    internal static class Root26Phase3PostResetWatchProbe
    {
        private const string Tag = "[NocturneModernController][Root26Phase3]";
        private const long WatchWindowMs = 2500;
        private const long SnapshotIntervalMs = 100;

        // Cumulative call counters, incremented from each Harmony Prefix.
        internal static long UpdateConnectedControllersCalls;
        internal static long GetActionCalls;
        internal static readonly Dictionary<int, long> GetActionCallsByIndex = new();
        internal static long UpdateControlCalls;
        internal static long ActivateActionSetCalls;
        internal static long GetConnectedControllersCalls;

        private static long _watchUntilMs = -1;
        private static long _lastSnapshotMs = -1;
        private static bool _windowActive;

        // Baselines captured for the reused Phase1 counters at window
        // start, so the log can show deltas-since-window-start for those
        // too (not just the newly-added counters here).
        private static long _baseUpdateInputCalls;
        private static long _baseSteamPadSetCalls;

        internal static void NotifyResetEvent(string source)
        {
            long now = Environment.TickCount64;
            bool wasActive = _windowActive;
            if (!wasActive)
            {
                _baseUpdateInputCalls = Root26Phase1UpdateInputCallProbe.CallCount;
                _baseSteamPadSetCalls = Root26Phase1SteamPadSetCallProbe.CallCount;
                _lastSnapshotMs = -1;
                MelonLogger.Msg($"{Tag} WATCH-WINDOW-START triggered by {source} at {DateTimeOffset.Now:O}");
            }
            else
            {
                MelonLogger.Msg($"{Tag} WATCH-WINDOW-EXTEND triggered by {source} at {DateTimeOffset.Now:O}");
            }
            _watchUntilMs = now + WatchWindowMs;
            _windowActive = true;
        }

        internal static void Sample()
        {
            if (!_windowActive)
            {
                return;
            }
            long now = Environment.TickCount64;
            if (now >= _watchUntilMs)
            {
                _windowActive = false;
                LogSnapshot("WINDOW-END");
                return;
            }
            if (_lastSnapshotMs < 0 || now - _lastSnapshotMs >= SnapshotIntervalMs)
            {
                _lastSnapshotMs = now;
                LogSnapshot("SNAPSHOT");
            }
        }

        private static void LogSnapshot(string kind)
        {
            string getActionByIndex;
            lock (GetActionCallsByIndex)
            {
                var parts = new List<string>();
                foreach (var kv in GetActionCallsByIndex)
                {
                    parts.Add($"idx{kv.Key}={kv.Value}");
                }
                getActionByIndex = string.Join(",", parts);
            }

            long deltaUpdateInput = Root26Phase1UpdateInputCallProbe.CallCount - _baseUpdateInputCalls;
            long deltaSteamPadSet = Root26Phase1SteamPadSetCallProbe.CallCount - _baseSteamPadSetCalls;

            MelonLogger.Msg(
                $"{Tag} {kind} UpdateConnectedControllers={UpdateConnectedControllersCalls} " +
                $"get_action={GetActionCalls}({getActionByIndex}) UpdateControl={UpdateControlCalls} " +
                $"ActivateActionSet={ActivateActionSetCalls} " +
                $"GetConnectedControllers={GetConnectedControllersCalls} " +
                $"UpdateInput(sinceWindowStart)={deltaUpdateInput} " +
                $"SteamPadSet(sinceWindowStart)={deltaSteamPadSet} at {DateTimeOffset.Now:O}");
        }
    }

    [HarmonyPatch(typeof(SteamPad), nameof(SteamPad.UpdateConnectedControllers))]
    internal static class Root26Phase3UpdateConnectedControllersCallProbe
    {
        private static void Prefix()
        {
            System.Threading.Interlocked.Increment(ref Root26Phase3PostResetWatchProbe.UpdateConnectedControllersCalls);
        }
    }

    [HarmonyPatch(typeof(SteamPad), "get_action")]
    internal static class Root26Phase3GetActionCallProbe
    {
        private static void Prefix(int index)
        {
            System.Threading.Interlocked.Increment(ref Root26Phase3PostResetWatchProbe.GetActionCalls);
            lock (Root26Phase3PostResetWatchProbe.GetActionCallsByIndex)
            {
                Root26Phase3PostResetWatchProbe.GetActionCallsByIndex.TryGetValue(index, out long c);
                Root26Phase3PostResetWatchProbe.GetActionCallsByIndex[index] = c + 1;
            }
        }
    }

    [HarmonyPatch(typeof(SteamPad), nameof(SteamPad.UpdateControl))]
    internal static class Root26Phase3UpdateControlCallProbe
    {
        private static void Prefix()
        {
            System.Threading.Interlocked.Increment(ref Root26Phase3PostResetWatchProbe.UpdateControlCalls);
        }
    }

    [HarmonyPatch(typeof(SteamInput), nameof(SteamInput.ActivateActionSet))]
    internal static class Root26Phase3ActivateActionSetCallProbe
    {
        // Chapter 69 follow-up: parameter names (inputHandle,
        // actionSetHandle) confirmed via ECMA-335 metadata to match the
        // managed binding's actual signature - this is a read-only
        // observation Prefix, arguments are never modified and the real
        // call is never skipped/replaced.
        private static void Prefix(InputHandle_t inputHandle, InputActionSetHandle_t actionSetHandle)
        {
            System.Threading.Interlocked.Increment(ref Root26Phase3PostResetWatchProbe.ActivateActionSetCalls);
            Root26ActivateActionSetArgsProbe.LogCall(inputHandle, actionSetHandle);
        }
    }

    [HarmonyPatch(typeof(SteamInput), nameof(SteamInput.GetConnectedControllers))]
    internal static class Root26Phase3GetConnectedControllersCallProbe
    {
        private static void Prefix()
        {
            System.Threading.Interlocked.Increment(ref Root26Phase3PostResetWatchProbe.GetConnectedControllersCalls);
        }
    }
}
