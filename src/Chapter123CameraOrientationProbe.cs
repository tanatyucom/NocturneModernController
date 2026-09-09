using System;
using HarmonyLib;
using Il2Cpp;
using MelonLoader;
using UnityEngine;

namespace NocturneModernController
{
    // Chapter 123: rebuilt camera-orientation telemetry, replacing the
    // Chapter122 probe. That probe's chosen fields (CameraMoveDir/
    // CameraMoveLR/mMoveLR) turned out NOT to reflect actual camera motion
    // in a real Guide-hold test: CameraMoveDir stayed fixed at 60.0000 for
    // the entire ~27s session, and CameraMoveLR/mMoveLR only ever took 2
    // discrete values each, uncorrelated with the large native X swings
    // that were happening at the same time. That negative result is why
    // this probe does not trust any fldCamera field name at face value.
    //
    // Ground truth used here instead: fldCamera.flgCameraCtrl is a real
    // UnityEngine.Camera (CONFIRMED type, from the DiffableCs decompile of
    // fldCamera.cs - "private static Camera flgCameraCtrl"). Its Transform's
    // `forward` vector is Unity's own, unambiguous world-space look
    // direction for the camera that actually renders the frame - no
    // interpretation of any custom fldCamera field semantics is needed:
    //   yaw   = atan2(forward.x, forward.z)
    //   pitch = atan2(forward.y, sqrt(forward.x^2 + forward.z^2))
    //
    // g_cameraPos/g_targetPos (used by the older, dormant CameraVectorProbe)
    // are logged alongside purely as a diagnostic cross-check: if the
    // direction between them tracks the same yaw/pitch as flgCameraCtrl's
    // transform, that empirically confirms what those two fields are; if
    // not, CameraVectorProbe's old assumption is disproven, without needing
    // manual disassembly of calcCamNormal()/fldCamMain().
    //
    // Also distinguishes native vs. effective Right Stick Y: Y is
    // MOD-overridden by NativeRightStickCameraPatch (AnalogCameraRouteProbe.cs)
    // whenever SdlRightStickInput.HasLiveInput. Two Harmony postfixes on the
    // same GetPadAnalog call, ordered by explicit priority, capture
    // __result before (native) and after (effective) that override -
    // read-only in both cases, __result is never written here.
    //
    // Read-only throughout: no field is written, no Process.Modules
    // enumeration, no reflection, no state-changing API. Logs on
    // state-change only, throttled (Chapter115 lesson).
    internal static class Chapter123CameraOrientationProbe
    {
        private const string Tag = "[NocturneModernController][Ch123]";
        private const long MinLogIntervalMs = 100;
        private const int StickActiveThreshold = 20;

        // Updated by the Harmony postfixes below, consumed by Sample(). All
        // run on the Unity main thread, never concurrently with Sample().
        internal static byte LastNativeX;
        internal static bool HaveNativeX;

        internal static byte LastNativeYRaw;
        internal static bool HaveNativeYRaw;

        internal static byte LastEffectiveY;
        internal static bool HaveEffectiveY;

        internal static int CamMainCallAccumulator;

        private static bool _havePrev;
        private static byte _prevNativeX;
        private static byte _prevEffectiveY;
        private static float _prevYaw;
        private static float _prevPitch;
        private static int _prevCamMainCalls;

        private static long _lastLogTickMs;

        internal static void Sample()
        {
            int callsThisFrame = CamMainCallAccumulator;
            CamMainCallAccumulator = 0;

            if (!FieldDashPatch.IsExplorationActive || !HaveNativeX || !HaveEffectiveY)
            {
                _havePrev = false;
                return;
            }

            Camera cam = fldCamera.flgCameraCtrl;
            if (cam == null)
            {
                _havePrev = false;
                return;
            }

            Transform camTransform = cam.transform;
            if (camTransform == null)
            {
                _havePrev = false;
                return;
            }

            Vector3 forward = camTransform.forward;
            float horizontalLen = Mathf.Sqrt(forward.x * forward.x + forward.z * forward.z);
            float yaw = Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;
            float pitch = Mathf.Atan2(forward.y, horizontalLen) * Mathf.Rad2Deg;

            // Diagnostic cross-check only, not used for the T/V judgement -
            // see class remarks above.
            Vector4 cameraPos = fldCamera.g_cameraPos;
            Vector4 targetPos = fldCamera.g_targetPos;
            float dirX = targetPos.x - cameraPos.x;
            float dirY = targetPos.y - cameraPos.y;
            float dirZ = targetPos.z - cameraPos.z;
            float vecHorizontal = Mathf.Sqrt(dirX * dirX + dirZ * dirZ);
            float yawFromVec = Mathf.Atan2(dirX, dirZ) * Mathf.Rad2Deg;
            float pitchFromVec = Mathf.Atan2(dirY, vecHorizontal) * Mathf.Rad2Deg;

            byte nativeX = LastNativeX;
            byte nativeYRaw = LastNativeYRaw;
            byte effectiveY = LastEffectiveY;

            if (!_havePrev)
            {
                _havePrev = true;
                _prevNativeX = nativeX;
                _prevEffectiveY = effectiveY;
                _prevYaw = yaw;
                _prevPitch = pitch;
                _prevCamMainCalls = callsThisFrame;
                return;
            }

            float yawDelta = Mathf.DeltaAngle(_prevYaw, yaw);
            float pitchDelta = pitch - _prevPitch;

            bool stickActive = Math.Abs(nativeX - 128) > StickActiveThreshold ||
                                Math.Abs(effectiveY - 128) > StickActiveThreshold;
            bool changed = nativeX != _prevNativeX ||
                           effectiveY != _prevEffectiveY ||
                           callsThisFrame != _prevCamMainCalls ||
                           Math.Abs(yawDelta) > 0.01f ||
                           Math.Abs(pitchDelta) > 0.01f;

            long now = Environment.TickCount64;
            if (stickActive && changed && (now - _lastLogTickMs) >= MinLogIntervalMs)
            {
                _lastLogTickMs = now;
                MelonLogger.Msg(
                    $"{Tag} t={DateTimeOffset.Now:O} nativeX={nativeX} " +
                    $"nativeYRaw={nativeYRaw} effectiveY={effectiveY} " +
                    $"yaw={yaw:F4} pitch={pitch:F4} " +
                    $"yawDelta={yawDelta:F4} pitchDelta={pitchDelta:F4} " +
                    $"yawFromVec={yawFromVec:F4} pitchFromVec={pitchFromVec:F4} " +
                    $"camMainCallsThisFrame={callsThisFrame}");
            }

            _prevNativeX = nativeX;
            _prevEffectiveY = effectiveY;
            _prevYaw = yaw;
            _prevPitch = pitch;
            _prevCamMainCalls = callsThisFrame;
        }
    }

    [HarmonyPatch(typeof(dds3PadManager), nameof(dds3PadManager.GetPadAnalog))]
    internal static class Chapter123PadAnalogXWatch
    {
        // Native, non-MOD-overridden horizontal channel confirmed in
        // Chapter117: GetPadAnalog(padno=0, stick_lr=1, xy=0, cip_no=1).
        // Observation only - __result is never assigned.
        private static void Postfix(int __0, int __1, int __2, int __3, byte __result)
        {
            if (!FieldDashPatch.IsExplorationActive)
            {
                return;
            }
            if (__0 == 0 && __1 == 1 && __2 == 0 && __3 == 1)
            {
                Chapter123CameraOrientationProbe.LastNativeX = __result;
                Chapter123CameraOrientationProbe.HaveNativeX = true;
            }
        }
    }

    // Runs before NativeRightStickCameraPatch's Postfix (default Normal
    // priority), so __result here is still the game's own, unmodified
    // return value for the Right Stick Y channel - "native", not
    // MOD-overridden. Observation only.
    [HarmonyPatch(typeof(dds3PadManager), nameof(dds3PadManager.GetPadAnalog))]
    [HarmonyPriority(Priority.First)]
    internal static class Chapter123PadAnalogYNativeWatch
    {
        private static void Postfix(int __0, int __1, int __2, int __3, byte __result)
        {
            if (!FieldDashPatch.IsExplorationActive)
            {
                return;
            }
            if (__0 == 0 && __1 == 1 && __2 == 1 && __3 == 1)
            {
                Chapter123CameraOrientationProbe.LastNativeYRaw = __result;
                Chapter123CameraOrientationProbe.HaveNativeYRaw = true;
            }
        }
    }

    // Runs after NativeRightStickCameraPatch's Postfix, so __result here is
    // whatever the game actually ends up using this frame - "effective"
    // (MOD-overridden when SdlRightStickInput.HasLiveInput, native
    // otherwise). Observation only.
    [HarmonyPatch(typeof(dds3PadManager), nameof(dds3PadManager.GetPadAnalog))]
    [HarmonyPriority(Priority.Last)]
    internal static class Chapter123PadAnalogYEffectiveWatch
    {
        private static void Postfix(int __0, int __1, int __2, int __3, byte __result)
        {
            if (!FieldDashPatch.IsExplorationActive)
            {
                return;
            }
            if (__0 == 0 && __1 == 1 && __2 == 1 && __3 == 1)
            {
                Chapter123CameraOrientationProbe.LastEffectiveY = __result;
                Chapter123CameraOrientationProbe.HaveEffectiveY = true;
            }
        }
    }

    [HarmonyPatch(typeof(fldCamera), nameof(fldCamera.fldCamMain))]
    internal static class Chapter123CamMainCallWatch
    {
        // Counts fldCamMain() invocations only; no field is read or written
        // here (kept as cheap as possible since this runs on every call).
        private static void Postfix()
        {
            if (!FieldDashPatch.IsExplorationActive)
            {
                return;
            }
            Chapter123CameraOrientationProbe.CamMainCallAccumulator++;
        }
    }
}
