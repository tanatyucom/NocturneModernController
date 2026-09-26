using System;
using HarmonyLib;
using Il2Cpp;
using MelonLoader;
using UnityEngine;

namespace NocturneModernController
{
    [HarmonyPatch(typeof(fldCamera), nameof(fldCamera.fldCamMain))]
    internal static class CameraVectorProbe
    {
        private static int _lastLogTick;
        private static Vector4 _lastCameraPos;
        private static Vector4 _lastTargetPos;
        private static Vector4 _lastHsPos;
        private static Vector4 _lastHePos;
        private static float _lastSlope = float.NaN;
        private static float _lastRadius = float.NaN;
        private static float _lastPitch = float.NaN;
        private static float _lastLoggedRadius = float.NaN;
        private static float _lastLoggedPitch = float.NaN;

        private static void Postfix()
        {
            if (!ExplorationState.IsExplorationActive)
            {
                return;
            }

            Vector4 cameraPos = fldCamera.g_cameraPos;
            Vector4 targetPos = fldCamera.g_targetPos;
            Vector4 hsPos = fldCamera.hs_cameraPos;
            Vector4 hePos = fldCamera.he_cameraPos;
            float slope = fldCamera.ooyCamSakaOld;
            Vector3 relative = new Vector3(
                cameraPos.x - targetPos.x,
                cameraPos.y - targetPos.y,
                cameraPos.z - targetPos.z);
            float horizontal = Mathf.Sqrt(
                relative.x * relative.x + relative.z * relative.z);
            float radius = relative.magnitude;
            float pitch = Mathf.Atan2(relative.y, horizontal) * Mathf.Rad2Deg;
            bool changed = (cameraPos - _lastCameraPos).sqrMagnitude > 0.000001f ||
                           (targetPos - _lastTargetPos).sqrMagnitude > 0.000001f ||
                           (hsPos - _lastHsPos).sqrMagnitude > 0.000001f ||
                           (hePos - _lastHePos).sqrMagnitude > 0.000001f ||
                           Math.Abs(slope - _lastSlope) > 0.0001f ||
                           Math.Abs(radius - _lastRadius) > 0.001f ||
                           Math.Abs(pitch - _lastPitch) > 0.001f;
            int now = Environment.TickCount;
            if (changed && unchecked(now - _lastLogTick) >= 100)
            {
                int elapsedMilliseconds = unchecked(now - _lastLogTick);
                float elapsedSeconds = elapsedMilliseconds > 0
                    ? elapsedMilliseconds / 1000.0f
                    : 0.0f;
                float pitchRate = elapsedSeconds > 0.0f && !float.IsNaN(_lastLoggedPitch)
                    ? (pitch - _lastLoggedPitch) / elapsedSeconds
                    : 0.0f;
                float radiusRate = elapsedSeconds > 0.0f && !float.IsNaN(_lastLoggedRadius)
                    ? (radius - _lastLoggedRadius) / elapsedSeconds
                    : 0.0f;
                _lastLogTick = now;
                MelonLogger.Msg(
                    $"[NocturneModernController] CAM-VECTOR " +
                    $"g=({cameraPos.x:F3},{cameraPos.y:F3},{cameraPos.z:F3},{cameraPos.w:F3}) " +
                    $"t=({targetPos.x:F3},{targetPos.y:F3},{targetPos.z:F3},{targetPos.w:F3}) " +
                    $"rel=({relative.x:F3},{relative.y:F3},{relative.z:F3}) " +
                    $"radius={radius:F3} radiusRate={radiusRate:F3}/s " +
                    $"pitch={pitch:F3} pitchRate={pitchRate:F3}deg/s " +
                    $"camLen={fldCamera.CameraCamLeng:F3} " +
                    $"fieldView={fldCamera.CameraFieldView:F3} " +
                    $"viewDist={fldCamera.fViewDistance:F3}/{fldCamera.fViewDist:F3}/" +
                    $"{fldCamera.fViewMaxDistance:F3} " +
                    $"axis=({fldCamera.mAxis.x:F3},{fldCamera.mAxis.y:F3}) " +
                    $"accel=({fldCamera.mAcceleration.x:F3},{fldCamera.mAcceleration.y:F3}) " +
                    $"over={fldCamera.upDownOver} reset={fldCamera.fldCamResetOn} " +
                    $"hs=({hsPos.x:F3},{hsPos.y:F3},{hsPos.z:F3},{hsPos.w:F3}) " +
                    $"he=({hePos.x:F3},{hePos.y:F3},{hePos.z:F3},{hePos.w:F3}) " +
                    $"slope={slope:F4}.");
                _lastLoggedRadius = radius;
                _lastLoggedPitch = pitch;
            }

            _lastCameraPos = cameraPos;
            _lastTargetPos = targetPos;
            _lastHsPos = hsPos;
            _lastHePos = hePos;
            _lastSlope = slope;
            _lastRadius = radius;
            _lastPitch = pitch;
        }
    }
}
