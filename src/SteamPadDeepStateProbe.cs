using System;
using System.Collections.Generic;
using System.Linq;
using Il2Cpp;
using Il2CppSteamworks;
using MelonLoader;

namespace NocturneModernController
{
    // Root-26 diagnostic-only PoC: read-only observation of deeper SteamPad /
    // Steam Input controller-slot and action-set state, added after the
    // manual-ResetController() causal PoC (17.2 in
    // docs/research/RIGHT_STICK_VIEW_AND_DASH_INVESTIGATION.md) REJECTED
    // "ResetController() alone is sufficient for revival". Purpose here is
    // purely comparative: capture a success session (Guide/Overlay -> LIVE)
    // and a failure session (F9 manual reset -> stays DEAD) and diff which of
    // these values actually change.
    //
    // Everything here is read-only: no controller state is written, no
    // Steamworks/SteamPad method is called that mutates anything (only
    // getters: Controller, InputHandles*, GetCurrentControlDevice(),
    // GetControlDevice(0), GetControllerType(0), and read-only traversal of
    // InputInfo.ActionSets[Current].Analog looking for "IG_RSTICK").
    // Root26SteamPadDeepStateProbe.Sample() runs unconditionally from
    // ModMain.OnUpdate(), NOT gated to FieldDashPatch.IsExplorationActive, so
    // it keeps observing while exploration is inactive (the Guide/Overlay
    // window, and the gap around a manual F9 press). No focus/WM_INPUT/
    // Method D instrumentation is added here. Every risky sub-read is wrapped
    // in its own try/catch with a logged-once failure flag, so one unexpected
    // interop failure degrades gracefully instead of disabling the whole
    // probe or crashing the game.
    internal static class Root26SteamPadDeepStateProbe
    {
        private const string Tag = "[NocturneModernController][Root26SteamPadState]";

        private static bool _steamPadAccessErrorLogged;
        private static bool _controllerIterationErrorLogged;
        private static bool _inputHandlesErrorLogged;
        private static bool _controlDeviceErrorLogged;

        private static int? _lastControllerCount;
        private static string? _lastInputHandlesSummary;
        private static string? _lastInputHandlesNewSummary;
        private static string? _lastInputHandlesIdSummary;
        private static EControlType? _lastCurrentControlDevice;
        private static EControlType? _lastControlDevice0;
        private static ESteamInputType? _lastControllerType0;
        private static readonly Dictionary<ulong, string> LastPerControllerSummary = new();

        internal static void Sample()
        {
            SteamInputUtil util;
            try
            {
                util = SteamInputUtil.instance;
            }
            catch (Exception ex)
            {
                LogOnce(ref _steamPadAccessErrorLogged, "SteamInputUtil.instance access threw " + Describe(ex));
                return;
            }
            if (util == null)
            {
                return;
            }

            SteamPad pad;
            try
            {
                pad = util.steam_pad;
            }
            catch (Exception ex)
            {
                LogOnce(ref _steamPadAccessErrorLogged, "SteamInputUtil.instance.steam_pad access threw " + Describe(ex));
                return;
            }
            if (pad == null)
            {
                return;
            }

            SampleControllerCount(pad);
            SampleControllerDictionary(pad);
            SampleInputHandleArrays(pad);
            SampleControlDeviceAndType(pad);
        }

        private static void SampleControllerCount(SteamPad pad)
        {
            try
            {
                int count = pad.Controller.Count;
                if (_lastControllerCount == null)
                {
                    _lastControllerCount = count;
                    Log($"STATE-CHANGE Controller.Count INITIAL -> {count}");
                    return;
                }
                if (count != _lastControllerCount.Value)
                {
                    Log($"STATE-CHANGE Controller.Count {_lastControllerCount.Value} -> {count}");
                    _lastControllerCount = count;
                }
            }
            catch (Exception ex)
            {
                LogOnce(ref _controllerIterationErrorLogged, "pad.Controller.Count threw " + Describe(ex));
            }
        }

        private static void SampleControllerDictionary(SteamPad pad)
        {
            try
            {
                var seenKeys = new HashSet<ulong>();
                foreach (var key in pad.Controller.Keys)
                {
                    seenKeys.Add(key);
                    SteamPad.InputInfo info = pad.Controller[key];
                    string ig_rstick = ResolveIgRstickHandle(info);
                    string summary = $"Current={info.Current} ControllerType={info.ControllerType} IG_RSTICK_Handle={ig_rstick}";

                    if (!LastPerControllerSummary.TryGetValue(key, out string? last))
                    {
                        LastPerControllerSummary[key] = summary;
                        Log($"STATE-CHANGE Controller[{key}] INITIAL -> ({summary})");
                        continue;
                    }
                    if (last != summary)
                    {
                        Log($"STATE-CHANGE Controller[{key}] ({last}) -> ({summary})");
                        LastPerControllerSummary[key] = summary;
                    }
                }

                // Entries that disappeared from the dictionary since last sample.
                foreach (var goneKey in LastPerControllerSummary.Keys.Where(k => !seenKeys.Contains(k)).ToList())
                {
                    Log($"STATE-CHANGE Controller[{goneKey}] ({LastPerControllerSummary[goneKey]}) -> (removed)");
                    LastPerControllerSummary.Remove(goneKey);
                }
            }
            catch (Exception ex)
            {
                LogOnce(ref _controllerIterationErrorLogged, "pad.Controller iteration threw " + Describe(ex));
            }
        }

        private static string ResolveIgRstickHandle(SteamPad.InputInfo info)
        {
            try
            {
                var actionSets = info.ActionSets;
                if (actionSets == null || info.Current < 0 || info.Current >= actionSets.Count)
                {
                    return "N/A(no-current-set)";
                }
                SteamPad.ActionInfo actionInfo = actionSets[info.Current];
                var analogList = actionInfo.Analog;
                if (analogList == null)
                {
                    return "N/A(no-analog-list)";
                }
                for (int i = 0; i < analogList.Count; i++)
                {
                    SteamPad.AnalogAction action = analogList[i];
                    if (action.Name == "IG_RSTICK")
                    {
                        return action.Handle.m_InputAnalogActionHandle.ToString();
                    }
                }
                return "N/A(not-found)";
            }
            catch (Exception ex)
            {
                return "N/A(error:" + ex.GetType().Name + ")";
            }
        }

        private static void SampleInputHandleArrays(SteamPad pad)
        {
            try
            {
                string handles = JoinHandles(pad.InputHandles);
                if (_lastInputHandlesSummary != handles)
                {
                    Log($"STATE-CHANGE InputHandles [{_lastInputHandlesSummary ?? "INITIAL"}] -> [{handles}]");
                    _lastInputHandlesSummary = handles;
                }

                string handlesNew = JoinHandles(pad.InputHandles_new);
                if (_lastInputHandlesNewSummary != handlesNew)
                {
                    Log($"STATE-CHANGE InputHandles_new [{_lastInputHandlesNewSummary ?? "INITIAL"}] -> [{handlesNew}]");
                    _lastInputHandlesNewSummary = handlesNew;
                }

                var idArray = pad.InputHandles_id;
                string ids = idArray == null
                    ? "null"
                    : string.Join(",", Enumerable.Range(0, idArray.Length).Select(i => idArray[i].ToString()));
                if (_lastInputHandlesIdSummary != ids)
                {
                    Log($"STATE-CHANGE InputHandles_id [{_lastInputHandlesIdSummary ?? "INITIAL"}] -> [{ids}]");
                    _lastInputHandlesIdSummary = ids;
                }
            }
            catch (Exception ex)
            {
                LogOnce(ref _inputHandlesErrorLogged, "InputHandles* array read threw " + Describe(ex));
            }
        }

        private static string JoinHandles(Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<InputHandle_t> arr)
        {
            if (arr == null)
            {
                return "null";
            }
            return string.Join(",", Enumerable.Range(0, arr.Length).Select(i => arr[i].m_InputHandle.ToString()));
        }

        private static void SampleControlDeviceAndType(SteamPad pad)
        {
            try
            {
                EControlType current = pad.GetCurrentControlDevice();
                if (_lastCurrentControlDevice == null || _lastCurrentControlDevice.Value != current)
                {
                    Log($"STATE-CHANGE GetCurrentControlDevice() {Fmt(_lastCurrentControlDevice)} -> {current}");
                    _lastCurrentControlDevice = current;
                }

                EControlType device0 = pad.GetControlDevice(0);
                if (_lastControlDevice0 == null || _lastControlDevice0.Value != device0)
                {
                    Log($"STATE-CHANGE GetControlDevice(0) {Fmt(_lastControlDevice0)} -> {device0}");
                    _lastControlDevice0 = device0;
                }

                ESteamInputType type0 = pad.GetControllerType(0);
                if (_lastControllerType0 == null || _lastControllerType0.Value != type0)
                {
                    Log($"STATE-CHANGE GetControllerType(0) {Fmt(_lastControllerType0)} -> {type0}");
                    _lastControllerType0 = type0;
                }
            }
            catch (Exception ex)
            {
                LogOnce(ref _controlDeviceErrorLogged, "GetCurrentControlDevice/GetControlDevice/GetControllerType threw " + Describe(ex));
            }
        }

        private static string Fmt<T>(T? value) where T : struct => value == null ? "INITIAL" : value.Value.ToString() ?? "?";

        private static string Describe(Exception ex) => ex.GetType().FullName + ": " + ex.Message;

        private static void Log(string message) =>
            MelonLogger.Msg($"{Tag} {message} at {DateTimeOffset.Now:O}");

        private static void LogOnce(ref bool alreadyLogged, string message)
        {
            if (alreadyLogged)
            {
                return;
            }
            alreadyLogged = true;
            MelonLogger.Warning($"{Tag} {message}");
        }
    }
}
