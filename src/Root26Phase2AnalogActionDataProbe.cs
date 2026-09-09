using System;
using System.Collections.Generic;
using Il2Cpp;
using Il2CppSteamworks;
using MelonLoader;

namespace NocturneModernController
{
    // Root-26 Phase 2 runtime probe (read-only). See docs/research/
    // RIGHT_STICK_VIEW_AND_DASH_INVESTIGATION.md section 36 for design
    // rationale.
    //
    // Purpose: observe, via the same public Steam Input API the game's own
    // GetAnalogActionData equivalent uses (SteamPad.SteamPadSet -> the
    // anonymous native wrapper func_0x1828A0AF0, statically CONFIRMED in
    // this investigation to BE Il2CppSteamworks.SteamInput.
    // GetAnalogActionData(InputHandle_t, InputAnalogActionHandle_t) - exact
    // RVA match, 0x28A0AF0) - the SAME controller handle / analog action
    // handle values already read elsewhere in this mod
    // (Root26SteamPadDeepStateProbe / Root26Phase1SteamPadSetCallProbe),
    // read-only, once per frame. IMPORTANT SCOPE NOTE: this confirms the
    // API ENTRY POINT is the same public Steamworks.NET method the game
    // itself calls through - it does NOT establish that our call happens
    // under identical call conditions, timing, or native-side
    // pre-processing as the game's own internal call site (e.g. whatever
    // the func_0x1828A0AF0 wrapper's own lazy-init/profiler-marker
    // boilerplate does before invoking the underlying native function -
    // see section 26/35 for that wrapper's structure). Treat this as an
    // independent read-only poll of the same underlying Steam Input state,
    // not a byte-for-byte reproduction of the game's internal call path.
    // This requires NO native hooking or detouring of any kind -
    // GetAnalogActionData is a plain public static Steamworks.NET wrapper
    // method, already referenced elsewhere in this project via
    // Assembly-CSharp-firstpass. It does not register, activate, or mutate
    // any Steam Input state - it only reads the current action-data
    // snapshot Steam already maintains internally.
    //
    // No native detour, no patch, no manual ResetController()/
    // SteamControllerReStart() call, no write to any handle/buffer/input
    // value. Sample() runs unconditionally from ModMain.OnUpdate(), not
    // gated to FieldDashPatch.IsExplorationActive.
    internal static class Root26Phase2AnalogActionDataProbe
    {
        private const string Tag = "[NocturneModernController][Root26Phase2]";
        private const int TrackedCount = 2; // i = 0 (IG_LSTICK), 1 (IG_RSTICK)

        private enum State { Inactive, ActiveZero, ActiveNonZero }

        private sealed class PerControllerState
        {
            internal readonly State[] LastState = { State.Inactive, State.Inactive };
            internal readonly bool[] StateKnown = new bool[TrackedCount];
            internal readonly EInputSourceMode[] LastMode = new EInputSourceMode[TrackedCount];
            internal readonly bool[] ModeKnown = new bool[TrackedCount];
            internal readonly ulong[] LastAnalogHandle = new ulong[TrackedCount];
            internal readonly bool[] HandleKnown = new bool[TrackedCount];
        }

        private static readonly Dictionary<ulong, PerControllerState> PerController = new();
        private static bool _errorLogged;

        private const long HeartbeatIntervalMs = 3000;
        private static long _lastHeartbeatMs = -1;

        internal static void Sample()
        {
            SteamInputUtil util;
            SteamPad pad;
            try
            {
                util = SteamInputUtil.instance;
                if (util == null)
                {
                    return;
                }
                pad = util.steam_pad;
                if (pad == null)
                {
                    return;
                }
            }
            catch (Exception ex)
            {
                LogErrorOnce("SteamInputUtil.instance/steam_pad access threw " + Describe(ex));
                return;
            }

            try
            {
                foreach (var key in pad.Controller.Keys)
                {
                    SteamPad.InputInfo info = pad.Controller[key];
                    SampleController(key, info);
                }
            }
            catch (Exception ex)
            {
                LogErrorOnce("pad.Controller iteration threw " + Describe(ex));
            }

            MaybeLogHeartbeat();
        }

        // Low-frequency heartbeat so a long steady period (e.g. "still
        // INACTIVE, nothing changed for 10 seconds") is visible in the log
        // without requiring a state transition - mirrors
        // Root26Phase1ConditionProbe's heartbeat design.
        private static void MaybeLogHeartbeat()
        {
            long now = Environment.TickCount64;
            if (_lastHeartbeatMs >= 0 && now - _lastHeartbeatMs < HeartbeatIntervalMs)
            {
                return;
            }
            _lastHeartbeatMs = now;

            if (PerController.Count == 0)
            {
                return;
            }

            var parts = new List<string>();
            foreach (var kv in PerController)
            {
                ulong handle = kv.Key;
                PerControllerState state = kv.Value;
                for (int i = 0; i < TrackedCount; i++)
                {
                    if (!state.StateKnown[i])
                    {
                        continue;
                    }
                    string label = i == 0 ? "IG_LSTICK" : i == 1 ? "IG_RSTICK" : "i=" + i;
                    parts.Add(
                        $"[handle={handle} i={i}({label}) analogActionHandle={state.LastAnalogHandle[i]} " +
                        $"state={StateLabel(state.LastState[i])} eMode={state.LastMode[i]}]");
                }
            }
            if (parts.Count == 0)
            {
                return;
            }
            MelonLogger.Msg($"{Tag} HEARTBEAT {string.Join(" ", parts)} at {DateTimeOffset.Now:O}");
        }

        private static void SampleController(ulong controllerHandleRaw, SteamPad.InputInfo info)
        {
            var actionSets = info.ActionSets;
            if (actionSets == null || info.Current < 0 || info.Current >= actionSets.Count)
            {
                return;
            }
            var analogList = actionSets[info.Current].Analog;
            if (analogList == null)
            {
                return;
            }

            if (!PerController.TryGetValue(controllerHandleRaw, out PerControllerState? state))
            {
                state = new PerControllerState();
                PerController[controllerHandleRaw] = state;
            }

            var controllerHandle = new InputHandle_t(controllerHandleRaw);

            for (int i = 0; i < TrackedCount && i < analogList.Count; i++)
            {
                SteamPad.AnalogAction action = analogList[i];
                ulong analogHandleRaw = action.Handle.m_InputAnalogActionHandle;

                InputAnalogActionData_t data;
                try
                {
                    data = SteamInput.GetAnalogActionData(controllerHandle, action.Handle);
                }
                catch (Exception ex)
                {
                    LogErrorOnce("SteamInput.GetAnalogActionData threw " + Describe(ex));
                    continue;
                }

                bool bActive = data.bActive != 0;
                State newState = !bActive
                    ? State.Inactive
                    : (data.x != 0f || data.y != 0f)
                        ? State.ActiveNonZero
                        : State.ActiveZero;

                bool handleChanged = !state.HandleKnown[i] || state.LastAnalogHandle[i] != analogHandleRaw;
                bool stateChanged = !state.StateKnown[i] || state.LastState[i] != newState;
                bool modeChanged = !state.ModeKnown[i] || state.LastMode[i] != data.eMode;

                if (handleChanged || stateChanged || modeChanged)
                {
                    string label = i == 0 ? "IG_LSTICK" : i == 1 ? "IG_RSTICK" : "i=" + i;
                    MelonLogger.Msg(
                        $"{Tag} STATE-CHANGE GetAnalogActionData controllerHandle={controllerHandleRaw} " +
                        $"i={i}({label}) analogActionHandle={analogHandleRaw} " +
                        $"state={StateLabel(newState)} bActive={data.bActive} eMode={data.eMode} " +
                        $"x={data.x} y={data.y} at {DateTimeOffset.Now:O}");
                }

                state.LastAnalogHandle[i] = analogHandleRaw;
                state.HandleKnown[i] = true;
                state.LastState[i] = newState;
                state.StateKnown[i] = true;
                state.LastMode[i] = data.eMode;
                state.ModeKnown[i] = true;
            }
        }

        private static string StateLabel(State s) => s switch
        {
            State.Inactive => "INACTIVE",
            State.ActiveZero => "ACTIVE-ZERO",
            State.ActiveNonZero => "ACTIVE-NONZERO",
            _ => s.ToString(),
        };

        private static string Describe(Exception ex) => ex.GetType().FullName + ": " + ex.Message;

        private static void LogErrorOnce(string message)
        {
            if (_errorLogged)
            {
                return;
            }
            _errorLogged = true;
            MelonLogger.Warning($"{Tag} {message}");
        }
    }
}
