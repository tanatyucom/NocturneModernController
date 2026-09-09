using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using Il2Cpp;
using Il2CppSteamworks;
using MelonLoader;

namespace NocturneModernController
{
    // Root-26 Chapter 68 follow-up: one-shot, read-only comparison of three
    // independent sources of an ActionSet handle for the candidate name
    // "Default" (see docs/research/RIGHT_STICK_VIEW_AND_DASH_INVESTIGATION.md
    // chapter 68 - "Default" is a HYPOTHESIS-level candidate, NOT a
    // call-site-CONFIRMED literal; three independent static techniques to
    // find a direct reference to the GetActionSetHandle icall descriptor
    // string in GameAssembly.dll all failed, so this runtime probe itself
    // doubles as the "Default" hypothesis check):
    //
    //   managedResolved      = Il2CppSteamworks.SteamInput.GetActionSetHandle("Default")
    //   flatResolved         = SteamAPI_ISteamInput_GetActionSetHandle(SteamAPI_SteamInput_v002(), "Default")
    //   storedActionSetHandle = pad.Controller[handle].ActionSets[Current].Handle.m_InputActionSetHandle
    //
    // All three are read-only, side-effect-free getters (chapter 68.4/68.5
    // CONFIRMED the flat export is a trivial 2-instruction vtable-dispatch
    // thunk with the same [self, ...params] ABI as the previously verified
    // exports, and CONFIRMED - via ECMA-335 metadata - that
    // SteamPad.ActionInfo.Handle (InputActionSetHandle_t, backed by a single
    // UInt64 m_InputActionSetHandle field) is the game's own stored
    // ActionSet handle for pad.Controller[handle].ActionSets[Current]).
    //
    // No ActivateActionSet/ActivateActionSetLayer/ResetController/
    // SteamControllerReStart/Shutdown/Init/UpdateConnectedControllers call
    // of any kind. No native detour/patch/injection/hook of
    // steamclient64.dll, GameOverlayRenderer64.dll, or steam_api64.dll
    // itself. Controller handles are read from the same existing managed
    // state other Root-26 probes already use (pad.Controller.Keys) - never
    // hardcoded.
    //
    // This probe logs its result exactly ONCE per handle per session (not
    // every frame, not on a heartbeat) since GetActionSetHandle("Default")
    // and the flat equivalent are expected to be pure, stable name->handle
    // lookups with no meaningful "state" to track over time; the one
    // interesting per-frame-varying value (storedActionSetHandle) is instead
    // logged on STATE-CHANGE plus a ~5s heartbeat, consistent with existing
    // probe conventions.
    internal static class Root26ActionSetHandleCompareProbe
    {
        private const string Tag = "[NocturneModernController][Root26ActionSetHandleCompare]";
        private const string CandidateActionSetName = "Default"; // HYPOTHESIS-level candidate - see chapter 68.
        private const long HeartbeatIntervalMs = 5000;

        [DllImport("steam_api64.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr SteamAPI_SteamInput_v002();

        [DllImport("steam_api64.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern ulong SteamAPI_ISteamInput_GetActionSetHandle(
            IntPtr self, [MarshalAs(UnmanagedType.LPStr)] string pszActionSetName);

        private static bool _selfPointerResolved;
        private static IntPtr _selfPointer = IntPtr.Zero;
        private static bool _disabledLogged;
        private static bool _errorLogged;

        // Logged exactly once per session, since these two values cannot
        // change at runtime (pure name->handle resolution, no per-frame
        // dependency).
        private static bool _oneShotLogged;
        private static ulong _managedResolved;
        private static ulong _flatResolved;

        // Chapter 69 bug fix: this MUST be keyed per-controller-handle,
        // never a single shared field, since a single shared "last value"
        // compared against a loop over N handles falsely detects "change"
        // on every frame once N >= 2 (each handle's value differs from the
        // previous handle's value, not from its own prior frame's value).
        private static readonly Dictionary<ulong, ulong> LastStoredHandle = new();
        private static long _lastHeartbeatMs = -1;

        internal static void Sample()
        {
            if (!ResolveSelfPointer())
            {
                return;
            }

            if (!_oneShotLogged)
            {
                RunOneShotComparison();
            }

            SampleStoredHandle();
        }

        private static bool ResolveSelfPointer()
        {
            if (_selfPointerResolved)
            {
                return _selfPointer != IntPtr.Zero;
            }
            _selfPointerResolved = true;

            try
            {
                _selfPointer = SteamAPI_SteamInput_v002();
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"{Tag} SteamAPI_SteamInput_v002() threw {ex.GetType().FullName}: {ex.Message} - probe disabled for this session.");
                _selfPointer = IntPtr.Zero;
                return false;
            }

            if (_selfPointer == IntPtr.Zero)
            {
                if (!_disabledLogged)
                {
                    _disabledLogged = true;
                    MelonLogger.Warning($"{Tag} SteamAPI_SteamInput_v002() returned IntPtr.Zero - probe disabled for this session.");
                }
                return false;
            }

            return true;
        }

        private static void RunOneShotComparison()
        {
            _oneShotLogged = true;

            try
            {
                _managedResolved = SteamInput.GetActionSetHandle(CandidateActionSetName).m_InputActionSetHandle;
            }
            catch (Exception ex)
            {
                LogErrorOnce("SteamInput.GetActionSetHandle threw " + Describe(ex));
                return;
            }

            try
            {
                _flatResolved = SteamAPI_ISteamInput_GetActionSetHandle(_selfPointer, CandidateActionSetName);
            }
            catch (Exception ex)
            {
                LogErrorOnce("SteamAPI_ISteamInput_GetActionSetHandle threw " + Describe(ex));
                return;
            }

            bool managedEqualsFlat = _managedResolved == _flatResolved;

            MelonLogger.Msg(
                $"{Tag} ONE-SHOT name=\"{CandidateActionSetName}\" managedResolved={_managedResolved} " +
                $"flatResolved={_flatResolved} managed==flat={managedEqualsFlat} at {DateTimeOffset.Now:O}");
        }

        // Reads pad.Controller[handle].ActionSets[Current].Handle for every
        // currently-registered handle, logging on STATE-CHANGE plus a ~5s
        // heartbeat (same convention as other Root-26 probes). Not gated to
        // exploration, so it keeps observing across the Guide/Overlay
        // window.
        private static void SampleStoredHandle()
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

            var sb = new StringBuilder();
            bool any = false;
            bool changed = false;

            try
            {
                foreach (var handleRaw in pad.Controller.Keys)
                {
                    if (handleRaw == 0)
                    {
                        continue;
                    }

                    SteamPad.InputInfo info;
                    try
                    {
                        info = pad.Controller[handleRaw];
                    }
                    catch (Exception ex)
                    {
                        LogErrorOnce("pad.Controller[handle] access threw " + Describe(ex));
                        continue;
                    }

                    var actionSets = info.ActionSets;
                    if (actionSets == null || info.Current < 0 || info.Current >= actionSets.Count)
                    {
                        continue;
                    }

                    ulong storedHandle = actionSets[info.Current].Handle.m_InputActionSetHandle;
                    if (any)
                    {
                        sb.Append(' ');
                    }
                    sb.Append('[').Append("handle=").Append(handleRaw)
                      .Append(" storedActionSetHandle=").Append(storedHandle)
                      .Append(" managed==stored=").Append(_managedResolved == storedHandle)
                      .Append(" flat==stored=").Append(_flatResolved == storedHandle)
                      .Append(']');
                    any = true;

                    // Per-handle comparison (chapter 69 bug fix) - a single
                    // shared "last value" compared across a multi-handle
                    // loop falsely detects "change" every frame once 2+
                    // handles are present, since each handle's value
                    // legitimately differs from the previous handle's
                    // value in the same frame (not from its own prior
                    // frame's value).
                    if (!LastStoredHandle.TryGetValue(handleRaw, out ulong lastForThisHandle) || lastForThisHandle != storedHandle)
                    {
                        changed = true;
                    }
                    LastStoredHandle[handleRaw] = storedHandle;
                }
            }
            catch (Exception ex)
            {
                LogErrorOnce("pad.Controller.Keys iteration threw " + Describe(ex));
                return;
            }

            if (!any)
            {
                return;
            }

            long now = Environment.TickCount64;
            bool heartbeatDue = _lastHeartbeatMs < 0 || now - _lastHeartbeatMs >= HeartbeatIntervalMs;

            if (changed)
            {
                MelonLogger.Msg($"{Tag} STATE-CHANGE StoredActionSetHandle {sb} at {DateTimeOffset.Now:O}");
                _lastHeartbeatMs = now;
            }
            else if (heartbeatDue)
            {
                _lastHeartbeatMs = now;
                MelonLogger.Msg($"{Tag} HEARTBEAT StoredActionSetHandle {sb} at {DateTimeOffset.Now:O}");
            }
        }

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
