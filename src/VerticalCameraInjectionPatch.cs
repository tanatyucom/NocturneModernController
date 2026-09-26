using System;
using HarmonyLib;
using Il2Cpp;

namespace NocturneModernController
{
    [HarmonyPatch(typeof(fldCamera), nameof(fldCamera.fldCamMain))]
    internal static class VerticalCameraInjectionPatch
    {
        private const float UpperLimit = -33.0f;
        private const float LowerLimit = -365.0f;
        private const float UnitsPerSecond = 540.0f;
        private const int NativeResetWindowMilliseconds = 1200;

        private static bool _hasTarget;
        private static float _targetY;
        private static int _nativeResetUntilTick;
        private static int _lastAdjustedFrame = -1;

        internal static void NotifyNativeFrontReset()
        {
            _hasTarget = false;
            _nativeResetUntilTick = unchecked(
                Environment.TickCount + NativeResetWindowMilliseconds);
        }

        [HarmonyPriority(Priority.Last)]
        private static void Postfix()
        {
            if (!ExplorationState.IsExplorationActive)
            {
                _hasTarget = false;
                return;
            }

            if (unchecked(Environment.TickCount - _nativeResetUntilTick) < 0)
            {
                return;
            }

            UnityEngine.Vector4 position = fldCamera.g_cameraPos;
            if (!_hasTarget)
            {
                _targetY = UnityEngine.Mathf.Clamp(position.y, LowerLimit, UpperLimit);
                _hasTarget = true;
            }

            if (VanillaTurnInvocationPoc.UpHeld)
            {
                if (_lastAdjustedFrame != UnityEngine.Time.frameCount)
                {
                    _targetY += UnitsPerSecond * UnityEngine.Time.unscaledDeltaTime;
                }
            }
            else if (VanillaTurnInvocationPoc.DownHeld)
            {
                if (_lastAdjustedFrame != UnityEngine.Time.frameCount)
                {
                    _targetY -= UnitsPerSecond * UnityEngine.Time.unscaledDeltaTime;
                }
            }

            _lastAdjustedFrame = UnityEngine.Time.frameCount;
            _targetY = UnityEngine.Mathf.Clamp(_targetY, LowerLimit, UpperLimit);

            position.y = _targetY;
            fldCamera.g_cameraPos = position;
        }
    }
}
