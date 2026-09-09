using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Il2Cpp;
using MelonLoader;

namespace NocturneModernController
{
    // Root-26 Phase 1 runtime probe (read-only, managed-side only).
    // See docs/research/RIGHT_STICK_VIEW_AND_DASH_INVESTIGATION.md section 31
    // for the full design rationale and the CONFIRMED static-analysis basis
    // this is built on. Deliberately does NOT hook the native
    // GetAnalogActionData wrapper (func_0x1828A0AF0) yet - that is deferred
    // to a possible Phase 2, only if Phase 1 narrows the result to scenario
    // C (see Root26Phase1ConditionProbe doc comment below).
    //
    // Observes, per the explicit Phase 1 spec:
    //  1. SteamManager.Initialized (condition A in SteamPad.UpdateControl()'s
    //     if/else gate - CONFIRMED via exact RVA match, 0x1825FF2C0 ==
    //     SteamManager.get_Initialized(), RVA 0x25FF2C0).
    //  2. SteamInputUtil.instance != null, as the managed-side reading of
    //     condition C (0x1825FC4B0 == SteamInputUtil.get_Instance(), RVA
    //     0x25FC4B0, exact match). "instance resolved (non-null)" is the
    //     natural managed equivalent of the native call succeeding, but is
    //     still flagged as an approximation: the native call's exact
    //     truthiness semantics (e.g. whether a non-null-but-uninitialized
    //     object could still make the native check fail) have not been
    //     independently re-verified against this managed property.
    //  3. SteamInputUtil.UpdateInput() reach count (Harmony Prefix counter).
    //  4. SteamPad.SteamPadSet(int index) reach count, broken down by index
    //     (Harmony Prefix counter), plus a read-only Controller/ActionSet/
    //     ControllerType/Analog[0..1].Handle snapshot at each call, logged
    //     only on change (item 5 of the spec).
    //  5. SteamInputUtil.SetAnalog(ref ulong ret, int index, int i, float dx,
    //     float dy), observed via Harmony Prefix (never touches ret or any
    //     other argument), tracked per i (0=IG_LSTICK, 1=IG_RSTICK, per the
    //     CONFIRMED SteamPad.EAnalogActionsInGameControls enum) as
    //     NO-CALL / CALLED-ZERO / CALLED-ACTIVE, logged only on state
    //     change. NO-CALL is detected via a timeout (no call for
    //     NoCallTimeoutMs), checked once per frame from Sample() - a Harmony
    //     Prefix alone cannot observe "stopped being called".
    //
    // All Harmony patches here are Prefix-only; none write __result, any
    // ref/out argument, or any controller/input state. No API is invoked
    // here beyond plain getters already used elsewhere in this mod
    // (SteamManager.Initialized, SteamInputUtil.instance, .steam_pad,
    // .Controller, .ActionSets, .Analog - see Root26SteamStateProbe /
    // Root26SteamPadDeepStateProbe). Root26Phase1ConditionProbe.Sample() runs
    // unconditionally from ModMain.OnUpdate(), not gated to
    // FieldDashPatch.IsExplorationActive, so the Guide/Overlay window is
    // still observed. No manual API calls, no ResetController()/
    // SteamControllerReStart() triggers, no F9.
    //
    // Diagnostic framework for the eventual DEAD-session log (see user spec):
    //   A: conditionA=false or conditionC=false -> UpdateInput unreached
    //   B: UpdateInput reached -> SteamPadSet unreached
    //   C: SteamPadSet reached -> i=0 CALLED-ACTIVE, i=1 CALLED-ZERO
    //   D: both i=0/i=1 CALLED-ACTIVE -> problem is further downstream
    //      (game-side conversion/copy), not in this Steam Input path.

    [HarmonyPatch(typeof(SteamInputUtil), nameof(SteamInputUtil.UpdateInput))]
    internal static class Root26Phase1UpdateInputCallProbe
    {
        internal static long CallCount;

        private static void Prefix()
        {
            System.Threading.Interlocked.Increment(ref CallCount);
        }
    }

    [HarmonyPatch(typeof(SteamPad), nameof(SteamPad.SteamPadSet))]
    internal static class Root26Phase1SteamPadSetCallProbe
    {
        internal static long CallCount;
        internal static readonly Dictionary<int, long> CallCountByIndex = new();
        private static readonly Dictionary<int, string> LastSnapshotByIndex = new();
        private static readonly object Sync = new();

        private static void Prefix(SteamPad __instance, int index)
        {
            System.Threading.Interlocked.Increment(ref CallCount);

            string snapshot;
            try
            {
                snapshot = SnapshotControllerState(__instance);
            }
            catch (Exception ex)
            {
                snapshot = "ERROR(" + ex.GetType().Name + ")";
            }

            lock (Sync)
            {
                CallCountByIndex.TryGetValue(index, out long c);
                CallCountByIndex[index] = c + 1;

                if (!LastSnapshotByIndex.TryGetValue(index, out string? last) || last != snapshot)
                {
                    LastSnapshotByIndex[index] = snapshot;
                    MelonLogger.Msg(
                        "[NocturneModernController][Root26Phase1] STATE-CHANGE SteamPadSet(index=" + index +
                        ") snapshot -> (" + snapshot + ") at " + DateTimeOffset.Now.ToString("O"));
                }
            }
        }

        internal static string SnapshotControllerState(SteamPad pad)
        {
            var parts = new List<string>();
            foreach (var key in pad.Controller.Keys)
            {
                SteamPad.InputInfo info = pad.Controller[key];
                string a0 = ResolveAnalogHandle(info, 0);
                string a1 = ResolveAnalogHandle(info, 1);
                parts.Add(
                    $"[handle={key} Current={info.Current} ControllerType={info.ControllerType} " +
                    $"Analog0={a0} Analog1={a1}]");
            }
            return parts.Count == 0 ? "no-controllers" : string.Join(" ", parts);
        }

        internal static string ResolveAnalogHandle(SteamPad.InputInfo info, int i)
        {
            try
            {
                var actionSets = info.ActionSets;
                if (actionSets == null || info.Current < 0 || info.Current >= actionSets.Count)
                {
                    return "N/A(no-current-set)";
                }
                var analogList = actionSets[info.Current].Analog;
                if (analogList == null || i >= analogList.Count)
                {
                    return "N/A(no-analog-" + i + ")";
                }
                SteamPad.AnalogAction action = analogList[i];
                return action.Name + "=" + action.Handle.m_InputAnalogActionHandle;
            }
            catch (Exception ex)
            {
                return "N/A(error:" + ex.GetType().Name + ")";
            }
        }
    }

    [HarmonyPatch(typeof(SteamInputUtil), nameof(SteamInputUtil.SetAnalog))]
    internal static class Root26Phase1SetAnalogProbe
    {
        private enum CallState { NoCall, CalledZero, CalledActive }

        private const int TrackedCount = 2; // i = 0 (IG_LSTICK), 1 (IG_RSTICK)
        private const long NoCallTimeoutMs = 500;

        private static readonly CallState[] State = { CallState.NoCall, CallState.NoCall };
        private static readonly long[] LastCallTickMs = { -1, -1 };
        private static readonly int[] LastIndex = { -1, -1 };
        internal static readonly long[] CallCount = { 0, 0 };

        private static readonly object Sync = new();

        private static void Prefix(ref ulong ret, int index, int i, float dx, float dy)
        {
            if (i < 0 || i >= TrackedCount)
            {
                return;
            }
            long now = Environment.TickCount64;
            CallState newState = (dx != 0f || dy != 0f) ? CallState.CalledActive : CallState.CalledZero;

            lock (Sync)
            {
                CallCount[i]++;
                bool changed = newState != State[i] || index != LastIndex[i];
                LastCallTickMs[i] = now;
                LastIndex[i] = index;
                if (changed)
                {
                    State[i] = newState;
                    LogState(i, index, dx, dy);
                }
            }
        }

        // Called once per frame from Root26Phase1ConditionProbe.Sample().
        // A Harmony Prefix alone cannot observe "stopped being called" - only
        // a periodic poll can detect that transition, hence the timeout.
        internal static void Sample()
        {
            long now = Environment.TickCount64;
            lock (Sync)
            {
                for (int i = 0; i < TrackedCount; i++)
                {
                    if (State[i] == CallState.NoCall)
                    {
                        continue;
                    }
                    if (LastCallTickMs[i] >= 0 && now - LastCallTickMs[i] > NoCallTimeoutMs)
                    {
                        State[i] = CallState.NoCall;
                        MelonLogger.Msg(
                            $"[NocturneModernController][Root26Phase1] STATE-CHANGE SetAnalog i={i} -> NO-CALL " +
                            $"(timeout {NoCallTimeoutMs}ms) at {DateTimeOffset.Now:O}");
                    }
                }
            }
        }

        private static void LogState(int i, int index, float dx, float dy)
        {
            string label = i == 0 ? "IG_LSTICK" : i == 1 ? "IG_RSTICK" : "i=" + i;
            MelonLogger.Msg(
                $"[NocturneModernController][Root26Phase1] STATE-CHANGE SetAnalog i={i}({label}) -> " +
                $"{State[i]} index={index} dx={dx} dy={dy} at {DateTimeOffset.Now:O}");
        }
    }

    // Per-frame condition A / condition C observation + periodic heartbeat.
    // Not a Harmony patch - polled once per frame from ModMain.OnUpdate(),
    // same pattern as the other Root26 Sample() probes. Not gated to
    // FieldDashPatch.IsExplorationActive.
    internal static class Root26Phase1ConditionProbe
    {
        private const string Tag = "[NocturneModernController][Root26Phase1]";
        private const long HeartbeatIntervalMs = 2000;

        private static bool? _lastConditionA;
        private static bool? _lastConditionC;
        private static long _lastHeartbeatMs = -1;
        private static bool _errorLogged;

        internal static void Sample()
        {
            Root26Phase1SetAnalogProbe.Sample();

            bool conditionA;
            bool conditionC;
            try
            {
                conditionA = SteamManager.Initialized;
                conditionC = SteamInputUtil.instance != null;
            }
            catch (Exception ex)
            {
                if (!_errorLogged)
                {
                    _errorLogged = true;
                    MelonLogger.Warning($"{Tag} condition A/C read threw {ex.GetType().FullName}: {ex.Message}");
                }
                return;
            }

            if (_lastConditionA == null || _lastConditionA.Value != conditionA)
            {
                MelonLogger.Msg(
                    $"{Tag} STATE-CHANGE conditionA(SteamManager.Initialized) {Fmt(_lastConditionA)} -> " +
                    $"{conditionA} at {DateTimeOffset.Now:O}");
                _lastConditionA = conditionA;
            }
            if (_lastConditionC == null || _lastConditionC.Value != conditionC)
            {
                MelonLogger.Msg(
                    $"{Tag} STATE-CHANGE conditionC(SteamInputUtil.instance!=null, approximation of native " +
                    $"get_Instance()!=null) {Fmt(_lastConditionC)} -> {conditionC} at {DateTimeOffset.Now:O}");
                _lastConditionC = conditionC;
            }

            long now = Environment.TickCount64;
            if (_lastHeartbeatMs < 0 || now - _lastHeartbeatMs >= HeartbeatIntervalMs)
            {
                _lastHeartbeatMs = now;
                string perIndex;
                lock (Root26Phase1SteamPadSetCallProbe.CallCountByIndex)
                {
                    perIndex = string.Join(
                        ",",
                        Root26Phase1SteamPadSetCallProbe.CallCountByIndex.Select(kv => $"idx{kv.Key}={kv.Value}"));
                }
                MelonLogger.Msg(
                    $"{Tag} HEARTBEAT conditionA={conditionA} conditionC={conditionC} " +
                    $"UpdateInputCalls={Root26Phase1UpdateInputCallProbe.CallCount} " +
                    $"SteamPadSetCalls={Root26Phase1SteamPadSetCallProbe.CallCount}({perIndex}) " +
                    $"SetAnalogCalls(i0={Root26Phase1SetAnalogProbe.CallCount[0]}," +
                    $"i1={Root26Phase1SetAnalogProbe.CallCount[1]}) at {DateTimeOffset.Now:O}");
            }
        }

        private static string Fmt(bool? v) => v == null ? "INITIAL" : v.Value.ToString();
    }
}
