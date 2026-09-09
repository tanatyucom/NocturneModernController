using System;
using HarmonyLib;
using Il2Cpp;
using MelonLoader;

namespace NocturneModernController
{
    // Root-26 diagnostic-only PoC: read-only observation of the managed
    // Steam Input integration layer (SteamInputUtil / SteamPad), found via
    // static metadata inspection of the deployed
    // MelonLoader/Il2CppAssemblies/Assembly-CSharp.dll - see
    // docs/research/RIGHT_STICK_VIEW_AND_DASH_INVESTIGATION.md section 15
    // for the full static analysis this is based on.
    //
    // Purpose: correlate SteamInputUtil.ResetController() /
    // SteamPad.SteamControllerReStart() call timing, and
    // SteamInputUtil.PadConnectDiff / PadConnectMax / PadConnectDiffCount /
    // CurrentInputID / LastInputIndex / LastInputIndex_OLD value changes,
    // against the existing RightStickPollingProbe STATE-TRANSITION
    // timestamps (same DateTimeOffset.Now:O format, same MelonLogger
    // stream) to see what changes around the DEAD -> LIVE boundary.
    //
    // SteamPad.UpdateConnectedControllers() was removed from call logging
    // (Root-26 session C, 2026-09-07): it CONFIRMED fires continuously every
    // frame (~35ms, twice per tick) regardless of connection state, so its
    // call count carries no DEAD->LIVE boundary signal and only inflates the
    // log. See docs/research/RIGHT_STICK_VIEW_AND_DASH_INVESTIGATION.md
    // section 14.3/14.4.
    //
    // Purely observational: no controller state is written here, and
    // ResetController / SteamControllerReStart are never called by this mod
    // - only their call timing is logged via Harmony Prefix (which never
    // suppresses or alters the original call). Root26SteamStateProbe.Sample()
    // runs unconditionally from ModMain.OnUpdate(), NOT gated to
    // FieldDashPatch.IsExplorationActive, because the events of interest
    // (Guide press, Steam Overlay) can happen between exploration sessions,
    // i.e. exactly when RightStickPollingProbe itself is not sampling. No
    // focus/Method D/WM_INPUT instrumentation is added here.

    [HarmonyPatch(typeof(SteamInputUtil), nameof(SteamInputUtil.ResetController))]
    internal static class Root26ResetControllerCallProbe
    {
        private static int _callCount;

        private static void Prefix()
        {
            _callCount++;
            MelonLogger.Msg(
                "[NocturneModernController][Root26SteamState] CALL SteamInputUtil.ResetController() " +
                $"count={_callCount} at {DateTimeOffset.Now:O}");
            Root26CallerDiagnostics.LogCallerContext("SteamInputUtil.ResetController()", _callCount);
            Root26Phase3PostResetWatchProbe.NotifyResetEvent("ResetController()");
        }
    }

    [HarmonyPatch(typeof(SteamPad), nameof(SteamPad.SteamControllerReStart))]
    internal static class Root26SteamControllerReStartCallProbe
    {
        private static int _callCount;

        private static void Prefix()
        {
            _callCount++;
            MelonLogger.Msg(
                "[NocturneModernController][Root26SteamState] CALL SteamPad.SteamControllerReStart() " +
                $"count={_callCount} at {DateTimeOffset.Now:O}");
            Root26CallerDiagnostics.LogCallerContext("SteamPad.SteamControllerReStart()", _callCount);
            Root26Phase3PostResetWatchProbe.NotifyResetEvent("SteamControllerReStart()");
        }
    }

    // Per-frame, change-only observation of SteamInputUtil singleton fields.
    // Not a Harmony patch - polled once per frame from ModMain.OnUpdate(),
    // same pattern as RightStickPollingProbe.Sample() / FocusCycleProbe.Sample().
    internal static class Root26SteamStateProbe
    {
        private static bool _everObservedInstance;
        private static bool _instanceAccessErrorLogged;
        private static bool _fieldAccessErrorLogged;

        private static bool _padConnectDiffKnown;
        private static int _padConnectDiff;
        private static bool _padConnectMaxKnown;
        private static int _padConnectMax;
        private static bool _padConnectDiffCountKnown;
        private static int _padConnectDiffCount;
        private static bool _currentInputIdKnown;
        private static ulong _currentInputId;
        private static bool _lastInputIndexKnown;
        private static int _lastInputIndex;
        private static bool _lastInputIndexOldKnown;
        private static int _lastInputIndexOld;

        internal static void Sample()
        {
            SteamInputUtil instance;
            try
            {
                instance = SteamInputUtil.instance;
            }
            catch (Exception ex)
            {
                LogOnce(
                    ref _instanceAccessErrorLogged,
                    "[NocturneModernController][Root26SteamState] SteamInputUtil.instance access threw " +
                    ex.GetType().FullName + ": " + ex.Message);
                return;
            }

            if (instance == null)
            {
                if (_everObservedInstance)
                {
                    MelonLogger.Msg(
                        "[NocturneModernController][Root26SteamState] STATE-CHANGE SteamInputUtil.instance " +
                        $"non-null -> null at {DateTimeOffset.Now:O}");
                    _everObservedInstance = false;
                }
                return;
            }

            if (!_everObservedInstance)
            {
                _everObservedInstance = true;
                MelonLogger.Msg(
                    "[NocturneModernController][Root26SteamState] STATE-CHANGE SteamInputUtil.instance " +
                    $"null -> non-null at {DateTimeOffset.Now:O}");
            }

            try
            {
                ObserveInt("PadConnectDiff", instance.PadConnectDiff, ref _padConnectDiffKnown, ref _padConnectDiff);
                ObserveInt("PadConnectMax", instance.PadConnectMax, ref _padConnectMaxKnown, ref _padConnectMax);
                ObserveInt(
                    "PadConnectDiffCount",
                    instance.PadConnectDiffCount,
                    ref _padConnectDiffCountKnown,
                    ref _padConnectDiffCount);
                ObserveInt("LastInputIndex", instance.LastInputIndex, ref _lastInputIndexKnown, ref _lastInputIndex);
                ObserveInt(
                    "LastInputIndex_OLD",
                    instance.LastInputIndex_OLD,
                    ref _lastInputIndexOldKnown,
                    ref _lastInputIndexOld);
                ObserveULong("CurrentInputID", instance.CurrentInputID, ref _currentInputIdKnown, ref _currentInputId);
            }
            catch (Exception ex)
            {
                LogOnce(
                    ref _fieldAccessErrorLogged,
                    "[NocturneModernController][Root26SteamState] SteamInputUtil field read threw " +
                    ex.GetType().FullName + ": " + ex.Message);
            }
        }

        private static void LogOnce(ref bool alreadyLogged, string message)
        {
            if (alreadyLogged)
            {
                return;
            }
            alreadyLogged = true;
            MelonLogger.Warning(message);
        }

        private static void ObserveInt(string name, int value, ref bool known, ref int last)
        {
            if (!known)
            {
                known = true;
                last = value;
                MelonLogger.Msg(
                    $"[NocturneModernController][Root26SteamState] STATE-CHANGE {name} INITIAL -> {value} " +
                    $"at {DateTimeOffset.Now:O}");
                return;
            }
            if (value == last)
            {
                return;
            }
            MelonLogger.Msg(
                $"[NocturneModernController][Root26SteamState] STATE-CHANGE {name} {last} -> {value} " +
                $"at {DateTimeOffset.Now:O}");
            last = value;
        }

        private static void ObserveULong(string name, ulong value, ref bool known, ref ulong last)
        {
            if (!known)
            {
                known = true;
                last = value;
                MelonLogger.Msg(
                    $"[NocturneModernController][Root26SteamState] STATE-CHANGE {name} INITIAL -> {value} " +
                    $"at {DateTimeOffset.Now:O}");
                return;
            }
            if (value == last)
            {
                return;
            }
            MelonLogger.Msg(
                $"[NocturneModernController][Root26SteamState] STATE-CHANGE {name} {last} -> {value} " +
                $"at {DateTimeOffset.Now:O}");
            last = value;
        }
    }
}
