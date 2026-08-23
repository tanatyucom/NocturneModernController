using System;
using HarmonyLib;
using Il2Cpp;
using MelonLoader;
using UnityEngine;

namespace NocturneModernController
{
    internal static class SmartAutoBattleRuntime
    {
        private static bool _autoActive;
        private static bool _ownsTimeScale;
        private static float _baselineTimeScale = 1.0f;

        internal static bool IsAutoActive => _autoActive;

        internal static void Sample()
        {
            if (FieldDashPatch.IsExplorationActive || SettingsGuiController.IsOpen)
            {
                SetAutoActive(false, "non-battle context");
                return;
            }

            float multiplier = ControllerSettings.Current.AutoBattleSpeed;
            if (!_autoActive || multiplier <= 1.0f)
            {
                RestoreTimeScale();
                return;
            }

            if (!_ownsTimeScale)
            {
                _baselineTimeScale = Math.Max(0.01f, Time.timeScale);
                _ownsTimeScale = true;
                MelonLogger.Msg(
                    $"[NocturneModernController] Smart Auto speed enabled: {multiplier:0.0}x.");
            }

            float desired = _baselineTimeScale * multiplier;
            if (Math.Abs(Time.timeScale - desired) > 0.001f)
            {
                Time.timeScale = desired;
            }
        }

        internal static void Shutdown()
        {
            _autoActive = false;
            RestoreTimeScale();
        }

        private static void SetAutoActive(bool active, string reason)
        {
            if (_autoActive == active)
            {
                return;
            }

            _autoActive = active;
            MelonLogger.Msg(
                $"[NocturneModernController] Smart Auto {(active ? "ON" : "OFF")}: {reason}.");
            if (!active)
            {
                RestoreTimeScale();
            }
        }

        private static void RestoreTimeScale()
        {
            if (!_ownsTimeScale)
            {
                return;
            }

            Time.timeScale = _baselineTimeScale;
            _ownsTimeScale = false;
            MelonLogger.Msg("[NocturneModernController] Smart Auto speed restored.");
        }

        [HarmonyPatch(
            typeof(SteamInputAssign),
            nameof(SteamInputAssign.padcheck),
            new Type[] { typeof(int), typeof(SIActionName), typeof(SIPressType) })]
        private static class AutoButtonPatch
        {
            private static void Postfix(
                int __0,
                SIActionName __1,
                SIPressType __2,
                bool __result)
            {
                if (__result && __0 == 0 &&
                    __1 == SIActionName.BTL_AutoBattle && __2 == SIPressType.TRIG)
                {
                    SetAutoActive(!_autoActive, "native Auto button");
                }
            }
        }

        [HarmonyPatch(typeof(nbMainProcess), nameof(nbMainProcess.nbMainShutdown))]
        private static class BattleShutdownPatch
        {
            private static void Prefix()
            {
                SetAutoActive(false, "battle shutdown");
            }
        }

        [HarmonyPatch(typeof(nbMainProcess), nameof(nbMainProcess.nbMainProcessClear))]
        private static class BattleClearPatch
        {
            private static void Prefix()
            {
                SetAutoActive(false, "battle cleared");
            }
        }
    }
}
