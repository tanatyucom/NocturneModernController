using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Il2Cpp;
using MelonLoader;

namespace NocturneModernController
{
    // Root-26 Chapter 65 follow-up: read-only observation of
    // ISteamInput::GetCurrentActionSet() and
    // ISteamInput::GetAnalogActionOrigins(), neither of which is bound in
    // this game's IL2CPP interop (Il2CppSteamworks.SteamInput - see
    // docs/research/RIGHT_STICK_VIEW_AND_DASH_INVESTIGATION.md chapter 62).
    // Chapter 64/65 confirmed, via read-only PE export-table inspection and
    // direct capstone disassembly of steam_api64.dll (the game's own
    // publicly-redistributable Steamworks SDK stub - NOT steamclient64.dll
    // or GameOverlayRenderer64.dll, neither of which this probe touches),
    // that:
    //   - SteamAPI_SteamInput_v002() takes no arguments and returns a plain
    //     ISteamInput* pointer (a simple, side-effect-free accessor).
    //   - SteamAPI_ISteamInput_GetCurrentActionSet and
    //     SteamAPI_ISteamInput_GetAnalogActionOrigins are trivial two-
    //     instruction vtable-dispatch thunks (mov reg,[rcx]; jmp [reg+off])
    //     with NO register reshuffling, so the flat-API parameter order is
    //     exactly [self, ...original C++ parameters in ABI order].
    //   - Il2CppSteamworks.EInputActionOrigin's managed value__ field has
    //     ECMA-335 element type I4 (System.Int32, 4 bytes), matching
    //     Valve's public C++ enum ABI (also 4-byte int-backed on Windows).
    //
    // This probe calls ONLY these three steam_api64.dll exports:
    //   SteamAPI_SteamInput_v002
    //   SteamAPI_ISteamInput_GetCurrentActionSet
    //   SteamAPI_ISteamInput_GetAnalogActionOrigins
    // All three are documented, public, read-only Steamworks.NET getters
    // (per Valve's ISteamInput interface documentation) with no side
    // effects. No ActivateActionSet/ActivateActionSetLayer/
    // ResetController/SteamControllerReStart/Shutdown/Init/
    // UpdateConnectedControllers call of any kind. No native
    // detour/patch/injection/hook of steamclient64.dll,
    // GameOverlayRenderer64.dll, or steam_api64.dll itself - this is a
    // plain P/Invoke against already-exported, already-loaded functions,
    // the same way the game's own native code (or any legitimate
    // Steamworks-linked application) would call them.
    //
    // Controller handles and the RSTICK analog action handle are never
    // hardcoded - both are read each frame from the same existing managed
    // state Root26Phase2AnalogActionDataProbe already uses
    // (pad.Controller.Keys / InputInfo.ActionSets[Current].Analog[1]).
    //
    // Sample() runs unconditionally from ModMain.OnUpdate(), not gated to
    // FieldDashPatch.IsExplorationActive. If SteamAPI_SteamInput_v002()
    // ever returns IntPtr.Zero, the probe disables itself for the rest of
    // the session after logging one warning (no repeated failed calls).
    internal static class Root26ActionSetAndOriginsProbe
    {
        private const string Tag = "[NocturneModernController][Root26ActionSetOrigins]";
        private const long HeartbeatIntervalMs = 5000;
        private const int SteamInputMaxOrigins = 8; // STEAM_INPUT_MAX_ORIGINS per Valve's public documentation.
        private const int RstickAnalogIndex = 1; // Same convention as Root26Phase2AnalogActionDataProbe (i=1 -> IG_RSTICK).

        [DllImport("steam_api64.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr SteamAPI_SteamInput_v002();

        [DllImport("steam_api64.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern ulong SteamAPI_ISteamInput_GetCurrentActionSet(IntPtr self, ulong inputHandle);

        [DllImport("steam_api64.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int SteamAPI_ISteamInput_GetAnalogActionOrigins(
            IntPtr self, ulong inputHandle, ulong actionSetHandle, ulong analogActionHandle, int[] originsOut);

        private sealed class PerHandleState
        {
            internal ulong? LastActionSet;
            internal ulong? LastAnalogHandle;
            internal string? LastOriginsSummary;
        }

        private static readonly Dictionary<ulong, PerHandleState> PerHandle = new();

        private static bool _selfPointerResolved;
        private static IntPtr _selfPointer = IntPtr.Zero;
        private static bool _disabledLogged;
        private static bool _errorLogged;
        private static long _lastHeartbeatMs = -1;

        // Reused every call - never reallocated per frame, per user's
        // no-per-frame-allocation / no-leak requirement.
        private static readonly int[] OriginsBuffer = new int[SteamInputMaxOrigins];

        internal static void Sample()
        {
            if (!ResolveSelfPointer())
            {
                return;
            }

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
                foreach (var handleRaw in pad.Controller.Keys)
                {
                    if (handleRaw == 0)
                    {
                        continue;
                    }
                    SampleHandle(pad, handleRaw);
                }
            }
            catch (Exception ex)
            {
                LogErrorOnce("pad.Controller.Keys iteration threw " + Describe(ex));
            }

            MaybeLogHeartbeat();
        }

        // Resolves SteamAPI_SteamInput_v002() exactly once per session. If
        // it returns IntPtr.Zero, the probe stays permanently disabled
        // (one warning only) rather than retrying every frame.
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

            MelonLogger.Msg($"{Tag} SteamAPI_SteamInput_v002() resolved self=0x{_selfPointer.ToInt64():X} at {DateTimeOffset.Now:O}");
            return true;
        }

        private static void SampleHandle(SteamPad pad, ulong handleRaw)
        {
            SteamPad.InputInfo info;
            try
            {
                info = pad.Controller[handleRaw];
            }
            catch (Exception ex)
            {
                LogErrorOnce("pad.Controller[handle] access threw " + Describe(ex));
                return;
            }

            var actionSets = info.ActionSets;
            if (actionSets == null || info.Current < 0 || info.Current >= actionSets.Count)
            {
                return;
            }
            var analogList = actionSets[info.Current].Analog;
            if (analogList == null || RstickAnalogIndex >= analogList.Count)
            {
                return;
            }

            ulong rstickAnalogHandleRaw = analogList[RstickAnalogIndex].Handle.m_InputAnalogActionHandle;

            ulong currentActionSet;
            try
            {
                currentActionSet = SteamAPI_ISteamInput_GetCurrentActionSet(_selfPointer, handleRaw);
            }
            catch (Exception ex)
            {
                LogErrorOnce("SteamAPI_ISteamInput_GetCurrentActionSet threw " + Describe(ex));
                return;
            }

            Array.Clear(OriginsBuffer, 0, OriginsBuffer.Length);
            int originCount;
            try
            {
                originCount = SteamAPI_ISteamInput_GetAnalogActionOrigins(
                    _selfPointer, handleRaw, currentActionSet, rstickAnalogHandleRaw, OriginsBuffer);
            }
            catch (Exception ex)
            {
                LogErrorOnce("SteamAPI_ISteamInput_GetAnalogActionOrigins threw " + Describe(ex));
                return;
            }

            string originsSummary;
            if (originCount < 0 || originCount > SteamInputMaxOrigins)
            {
                LogErrorOnce($"SteamAPI_ISteamInput_GetAnalogActionOrigins returned out-of-range count={originCount} for handle={handleRaw} - not reading buffer contents.");
                originsSummary = $"INVALID-COUNT({originCount})";
            }
            else if (originCount == 0)
            {
                originsSummary = "none";
            }
            else
            {
                var parts = new string[originCount];
                for (int i = 0; i < originCount; i++)
                {
                    parts[i] = OriginsBuffer[i].ToString();
                }
                originsSummary = string.Join(",", parts);
            }

            if (!PerHandle.TryGetValue(handleRaw, out PerHandleState? state))
            {
                state = new PerHandleState();
                PerHandle[handleRaw] = state;
            }

            if (state.LastActionSet != currentActionSet)
            {
                MelonLogger.Msg(
                    $"{Tag} STATE-CHANGE CurrentActionSet[handle={handleRaw}] " +
                    $"[{(state.LastActionSet.HasValue ? state.LastActionSet.Value.ToString() : "INITIAL")}] -> [{currentActionSet}] " +
                    $"at {DateTimeOffset.Now:O}");
                state.LastActionSet = currentActionSet;
            }

            if (state.LastAnalogHandle != rstickAnalogHandleRaw)
            {
                MelonLogger.Msg(
                    $"{Tag} STATE-CHANGE RstickAnalogHandle[handle={handleRaw}] " +
                    $"[{(state.LastAnalogHandle.HasValue ? state.LastAnalogHandle.Value.ToString() : "INITIAL")}] -> [{rstickAnalogHandleRaw}] " +
                    $"at {DateTimeOffset.Now:O}");
                state.LastAnalogHandle = rstickAnalogHandleRaw;
            }

            if (state.LastOriginsSummary != originsSummary)
            {
                MelonLogger.Msg(
                    $"{Tag} STATE-CHANGE Origins[handle={handleRaw} actionSet={currentActionSet} " +
                    $"analogHandle={rstickAnalogHandleRaw}] [{state.LastOriginsSummary ?? "INITIAL"}] -> " +
                    $"[count={originCount} origins=({originsSummary})] at {DateTimeOffset.Now:O}");
                state.LastOriginsSummary = originsSummary;
            }
        }

        private static void MaybeLogHeartbeat()
        {
            long now = Environment.TickCount64;
            if (_lastHeartbeatMs >= 0 && now - _lastHeartbeatMs < HeartbeatIntervalMs)
            {
                return;
            }
            _lastHeartbeatMs = now;

            if (PerHandle.Count == 0)
            {
                return;
            }

            var parts = new List<string>();
            foreach (var kv in PerHandle)
            {
                parts.Add(
                    $"[handle={kv.Key} actionSet={kv.Value.LastActionSet} " +
                    $"rstickHandle={kv.Value.LastAnalogHandle} origins=({kv.Value.LastOriginsSummary})]");
            }
            MelonLogger.Msg($"{Tag} HEARTBEAT {string.Join(" ", parts)} at {DateTimeOffset.Now:O}");
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
