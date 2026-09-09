using System;
using System.Collections.Generic;
using Il2Cpp;
using Il2CppSteamworks;
using MelonLoader;

namespace NocturneModernController
{
    // Root-26 Chapter 61 follow-up: read-only observation of two additional
    // public Steamworks.NET APIs confirmed bound in this game's IL2CPP
    // interop (see docs/research/RIGHT_STICK_VIEW_AND_DASH_INVESTIGATION.md,
    // static binding survey after chapter 61):
    //   - SteamInput.GetInputTypeForHandle(InputHandle_t) -> ESteamInputType
    //   - SteamInput.GetControllerForGamepadIndex(int nIndex) -> InputHandle_t
    // Both are plain public static Steamworks.NET getters with no documented
    // side effects (same class already used read-only elsewhere in this mod,
    // e.g. Root26Phase2AnalogActionDataProbe's GetAnalogActionData call).
    //
    // Purpose: track (A) whether Steam Input's own device-type
    // classification for each currently-registered controller handle
    // changes across the DEAD/Guide/LIVE boundary, and (B) whether the
    // association between XInput/gamepad slot index (0..3) and Steam Input
    // controller handle changes across that same boundary. Neither
    // observation writes to, activates, or resets any Steam Input state.
    //
    // No native detour/patch/injection/hook of any kind. No manual
    // ResetController()/SteamControllerReStart()/Shutdown()/Init()/
    // UpdateConnectedControllers()/ActivateActionSet()/
    // ActivateActionSetLayer() call. No F9/F10, no SendInput, no Guide
    // spoofing. Sample() runs unconditionally from ModMain.OnUpdate(), not
    // gated to FieldDashPatch.IsExplorationActive, so it keeps observing
    // across the Guide/Overlay window. Handles are read from
    // pad.Controller.Keys (the same live managed state
    // Root26Phase2AnalogActionDataProbe already uses) each frame - no
    // handle value is hardcoded, so this probe adapts automatically to
    // however many controllers exist in a given session.
    internal static class Root26InputTypeAndGamepadIndexProbe
    {
        private const string Tag = "[NocturneModernController][Root26InputTypeGamepadIndex]";
        private const long HeartbeatIntervalMs = 5000;
        private const int GamepadIndexCount = 4; // nIndex = 0..3

        private static readonly Dictionary<ulong, ESteamInputType> LastInputType = new();
        private static readonly HashSet<ulong> KnownHandles = new();
        private static readonly ulong[] LastGamepadIndexHandle = new ulong[GamepadIndexCount];
        private static readonly bool[] GamepadIndexKnown = new bool[GamepadIndexCount];

        private static long _lastHeartbeatMs = -1;
        private static bool _errorLogged;

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

            // --- A: GetInputTypeForHandle, for every handle currently
            //        registered in pad.Controller.Keys (no hardcoded handle
            //        values). ---
            try
            {
                foreach (var handleRaw in pad.Controller.Keys)
                {
                    if (handleRaw == 0)
                    {
                        continue;
                    }
                    SampleInputType(handleRaw);
                }
            }
            catch (Exception ex)
            {
                LogErrorOnce("pad.Controller.Keys iteration (A) threw " + Describe(ex));
            }

            // --- B: GetControllerForGamepadIndex(0..3). ---
            for (int nIndex = 0; nIndex < GamepadIndexCount; nIndex++)
            {
                SampleGamepadIndex(nIndex);
            }

            MaybeLogHeartbeat();
        }

        private static void SampleInputType(ulong handleRaw)
        {
            InputHandle_t handle;
            ESteamInputType type;
            try
            {
                handle = new InputHandle_t(handleRaw);
                type = SteamInput.GetInputTypeForHandle(handle);
            }
            catch (Exception ex)
            {
                LogErrorOnce("SteamInput.GetInputTypeForHandle threw " + Describe(ex));
                return;
            }

            bool known = KnownHandles.Contains(handleRaw);
            if (!known)
            {
                KnownHandles.Add(handleRaw);
                LastInputType[handleRaw] = type;
                MelonLogger.Msg($"{Tag} STATE-CHANGE InputType[handle={handleRaw}] INITIAL -> {type} at {DateTimeOffset.Now:O}");
                return;
            }

            if (LastInputType.TryGetValue(handleRaw, out ESteamInputType last) && last != type)
            {
                MelonLogger.Msg($"{Tag} STATE-CHANGE InputType[handle={handleRaw}] {last} -> {type} at {DateTimeOffset.Now:O}");
                LastInputType[handleRaw] = type;
            }
        }

        private static void SampleGamepadIndex(int nIndex)
        {
            ulong handleRaw;
            try
            {
                InputHandle_t handle = SteamInput.GetControllerForGamepadIndex(nIndex);
                handleRaw = handle.m_InputHandle;
            }
            catch (Exception ex)
            {
                LogErrorOnce("SteamInput.GetControllerForGamepadIndex threw " + Describe(ex));
                return;
            }

            if (!GamepadIndexKnown[nIndex])
            {
                GamepadIndexKnown[nIndex] = true;
                LastGamepadIndexHandle[nIndex] = handleRaw;
                MelonLogger.Msg($"{Tag} STATE-CHANGE GamepadIndex[{nIndex}] INITIAL -> handle={handleRaw} at {DateTimeOffset.Now:O}");
                return;
            }

            if (LastGamepadIndexHandle[nIndex] != handleRaw)
            {
                MelonLogger.Msg($"{Tag} STATE-CHANGE GamepadIndex[{nIndex}] handle={LastGamepadIndexHandle[nIndex]} -> handle={handleRaw} at {DateTimeOffset.Now:O}");
                LastGamepadIndexHandle[nIndex] = handleRaw;
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

            var inputTypeParts = new List<string>();
            foreach (var kv in LastInputType)
            {
                inputTypeParts.Add($"[handle={kv.Key} type={kv.Value}]");
            }

            var gamepadParts = new List<string>();
            for (int i = 0; i < GamepadIndexCount; i++)
            {
                if (!GamepadIndexKnown[i])
                {
                    continue;
                }
                gamepadParts.Add($"[index={i} handle={LastGamepadIndexHandle[i]}]");
            }

            if (inputTypeParts.Count == 0 && gamepadParts.Count == 0)
            {
                return;
            }

            MelonLogger.Msg(
                $"{Tag} HEARTBEAT InputType:{string.Join(" ", inputTypeParts)} " +
                $"GamepadIndex:{string.Join(" ", gamepadParts)} at {DateTimeOffset.Now:O}");
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
