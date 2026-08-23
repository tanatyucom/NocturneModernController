using System;
using HarmonyLib;
using Il2Cpp;
using MelonLoader;

namespace NocturneModernController
{
    internal static class VanillaTurnInvocationPoc
    {
        private static bool _leftHeld;
        private static bool _rightHeld;
        private static bool _upHeld;
        private static bool _downHeld;
        private static bool _loggedLogicalLeft;
        private static bool _loggedLogicalRight;
        private static bool _loggedLogicalUp;
        private static bool _loggedLogicalDown;

        internal static bool LeftHeld => _leftHeld && !_rightHeld;
        internal static bool RightHeld => _rightHeld && !_leftHeld;
        internal static bool UpHeld => _upHeld && !_downHeld;
        internal static bool DownHeld => _downHeld && !_upHeld;

        internal static void SetHeldState(bool left, bool right, bool up, bool down)
        {
            _leftHeld = left;
            _rightHeld = right;
            _upHeld = up;
            _downHeld = down;
        }

        internal static void LogLogicalInjection(bool left, SIActionName action)
        {
            if (left ? _loggedLogicalLeft : _loggedLogicalRight)
            {
                return;
            }

            if (left)
            {
                _loggedLogicalLeft = true;
            }
            else
            {
                _loggedLogicalRight = true;
            }

            MelonLogger.Msg($"[NocturneModernController] Q4 logical {action} injection observed.");
        }

        internal static void LogVerticalLogicalInjection(bool up, SIActionName action)
        {
            if (up ? _loggedLogicalUp : _loggedLogicalDown)
            {
                return;
            }

            if (up)
            {
                _loggedLogicalUp = true;
            }
            else
            {
                _loggedLogicalDown = true;
            }

            MelonLogger.Msg(
                $"[NocturneModernController] Native vertical logical {action} injection observed.");
        }
    }

    [HarmonyPatch(
        typeof(SteamInputAssign),
        nameof(SteamInputAssign.padcheck),
        new Type[] { typeof(int), typeof(SIActionName), typeof(SIPressType) })]
    internal static class PuzzleLogicalTurnPatch
    {
        private static bool Prefix(
            int __0,
            SIActionName __1,
            ref bool __result)
        {
            if (__0 != 0 ||
                !ControllerSettings.Current.RightStickEnabled ||
                !FieldDashPatch.IsExplorationActive ||
                !LegacyShoulderTurnSuppression.IsSuppressedExplorationAction(__1))
            {
                return true;
            }

            __result = false;
            LegacyShoulderTurnSuppression.LogSuppressed(__1);
            return false;
        }

        private static void Postfix(
            int __0,
            SIActionName __1,
            SIPressType __2,
            ref bool __result)
        {
            if (__result || __0 != 0 || __2 != SIPressType.DOWN)
            {
                return;
            }

            if (__1 == SIActionName.FD_Turn_Left ||
                __1 == SIActionName.PZL_MapRot_Left)
            {
                __result = VanillaTurnInvocationPoc.LeftHeld;
                if (__result)
                {
                    VanillaTurnInvocationPoc.LogLogicalInjection(left: true, action: __1);
                }
            }
            else if (__1 == SIActionName.FD_Turn_Right ||
                     __1 == SIActionName.PZL_MapRot_Right)
            {
                __result = VanillaTurnInvocationPoc.RightHeld;
                if (__result)
                {
                    VanillaTurnInvocationPoc.LogLogicalInjection(left: false, action: __1);
                }
            }
        }
    }
}
