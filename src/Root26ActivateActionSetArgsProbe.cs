using System;
using Il2Cpp;
using Il2CppSteamworks;
using MelonLoader;

namespace NocturneModernController
{
    // Root-26 Chapter 69 follow-up: read-only observation of the actual
    // arguments the GAME ITSELF passes to
    // Il2CppSteamworks.SteamInput.ActivateActionSet(InputHandle_t,
    // InputActionSetHandle_t) - previously only a call COUNT was tracked
    // (Root26Phase3PostResetWatchProbe.ActivateActionSetCalls), never the
    // arguments. Confirmed via ECMA-335 metadata (this session) that the
    // managed binding's parameter names are exactly "inputHandle" and
    // "actionSetHandle", matching Valve's public
    // ISteamInput::ActivateActionSet(InputHandle_t, InputActionSetHandle_t)
    // signature.
    //
    // This is a Harmony PREFIX ONLY: it observes the arguments the game is
    // about to pass to the real ActivateActionSet call and does not modify
    // them, does not skip/replace the call (no "return false"), and does
    // not touch __result. It never calls ActivateActionSet itself - it only
    // logs what the game's own code already decided to call.
    //
    // Purpose: confirm/deny whether the ActivateActionSet calls already
    // observed at startup (count reaches up to 4 across a session - chapter
    // 69 log) actually target the two known controller handles
    // (19680159496504676 / 91728467815138660) with actionSetHandle=1 (the
    // "Default" candidate whose value was confirmed in chapter 69 to match
    // managed/flat GetActionSetHandle("Default") and the game's own stored
    // ActionInfo.Handle). If so, and GetCurrentActionSet(handle) still
    // reports 0 afterward (as chapters 67/69 observed), that specific
    // explanation for actionSet=0 ("ActivateActionSet was simply never
    // called for this handle/set") is closed off, sharpening the mystery
    // to the flat GetCurrentActionSet API/interface semantics themselves.
    //
    // No ActivateActionSet/ActivateActionSetLayer/ResetController/
    // SteamControllerReStart/Shutdown/Init/UpdateConnectedControllers call
    // of any kind is made BY this probe. No native detour/patch/injection/
    // hook of steamclient64.dll, GameOverlayRenderer64.dll, or
    // steam_api64.dll. GetInputTypeForHandle/GetControllerForGamepadIndex
    // are the same already-used-elsewhere read-only Steamworks.NET getters
    // as Root26InputTypeAndGamepadIndexProbe (chapter 62/63) - called here
    // only to enrich the log with correlated context at the exact moment
    // of the real ActivateActionSet call, not on any polling loop.
    internal static class Root26ActivateActionSetArgsProbe
    {
        private const string Tag = "[NocturneModernController][Root26ActivateActionSetArgs]";
        private const int GamepadIndexCount = 4; // nIndex = 0..3

        private static long _callCount;
        private static bool _errorLogged;

        // Called from Root26Phase3ActivateActionSetCallProbe's Prefix, once
        // per real ActivateActionSet call made by the game itself. This
        // call count is expected to be low (single digits per session, per
        // chapter 69's observation of ActivateActionSetCalls maxing at 4),
        // so per-call logging (rather than count-only) does not risk log
        // flooding.
        internal static void LogCall(InputHandle_t inputHandle, InputActionSetHandle_t actionSetHandle)
        {
            long callCount = System.Threading.Interlocked.Increment(ref _callCount);

            ulong inputHandleRaw = inputHandle.m_InputHandle;
            ulong actionSetHandleRaw = actionSetHandle.m_InputActionSetHandle;

            string inputTypeStr = "n/a";
            try
            {
                ESteamInputType inputType = SteamInput.GetInputTypeForHandle(inputHandle);
                inputTypeStr = inputType.ToString();
            }
            catch (Exception ex)
            {
                LogErrorOnce("SteamInput.GetInputTypeForHandle threw " + Describe(ex));
            }

            string gamepadIndexStr = "n/a";
            try
            {
                for (int nIndex = 0; nIndex < GamepadIndexCount; nIndex++)
                {
                    InputHandle_t forIndex = SteamInput.GetControllerForGamepadIndex(nIndex);
                    if (forIndex.m_InputHandle == inputHandleRaw)
                    {
                        gamepadIndexStr = nIndex.ToString();
                        break;
                    }
                }
                if (gamepadIndexStr == "n/a")
                {
                    gamepadIndexStr = "none-matched";
                }
            }
            catch (Exception ex)
            {
                LogErrorOnce("SteamInput.GetControllerForGamepadIndex threw " + Describe(ex));
            }

            string storedActionSetHandleStr = "n/a";
            try
            {
                SteamInputUtil util = SteamInputUtil.instance;
                SteamPad? pad = util != null ? util.steam_pad : null;
                if (pad != null)
                {
                    SteamPad.InputInfo info = pad.Controller[inputHandleRaw];
                    var actionSets = info.ActionSets;
                    if (actionSets != null && info.Current >= 0 && info.Current < actionSets.Count)
                    {
                        storedActionSetHandleStr = actionSets[info.Current].Handle.m_InputActionSetHandle.ToString();
                    }
                    else
                    {
                        storedActionSetHandleStr = "no-current-set";
                    }
                }
                else
                {
                    storedActionSetHandleStr = "no-pad";
                }
            }
            catch (Exception ex)
            {
                LogErrorOnce("pad.Controller[handle].ActionSets[Current] access threw " + Describe(ex));
            }

            MelonLogger.Msg(
                $"{Tag} CALL count={callCount} inputHandle={inputHandleRaw} " +
                $"actionSetHandle={actionSetHandleRaw} inputType={inputTypeStr} " +
                $"gamepadIndex={gamepadIndexStr} storedActionSetHandle(pre-call)={storedActionSetHandleStr} " +
                $"at {DateTimeOffset.Now:O}");
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
