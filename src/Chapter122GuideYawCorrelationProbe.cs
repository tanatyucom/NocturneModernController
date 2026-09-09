using System;
using HarmonyLib;
using Il2Cpp;
using MelonLoader;

namespace NocturneModernController
{
    // Chapter 122: minimal, read-only correlation probe between the native
    // right-stick X input and the camera's horizontal (yaw) output, to
    // distinguish the source of the Guide-hold camera-speed-up reproduced
    // and quantified in Chapter116-122 (video analysis: two stable
    // horizontal-only angular-velocity plateaus around 43.7-47.6 deg/s vs.
    // ~10.3 deg/s, ratio ~4.2-4.6x, full right-stick deflection confirmed by
    // the user in both regimes, vertical/pitch essentially zero throughout).
    //
    // Inputs/fields used (all name+offset CONFIRMED, not guessed):
    //   - dds3PadManager.GetPadAnalog(0, 1, 0, 1): native, non-MOD-overridden
    //     Right Stick X channel (Chapter117; see also
    //     docs/research/RIGHT_STICK_VIEW_AND_DASH_INVESTIGATION.md 4.1/25).
    //     Center = 128 (0x80), full deflection = 0 or 255.
    //   - fldCamera.mMoveLR       (int,   field offset 0x14)
    //   - fldCamera.CameraMoveLR  (float, field offset 0x34)
    //   - fldCamera.CameraMoveDir (float, field offset 0x3C)
    //     Names and offsets confirmed via the DiffableCs decompile of the
    //     deployed GameAssembly.dll referenced in Chapter121.6(c). These are
    //     read-only observations; no field is written.
    //
    // Distinguishes:
    //   T1 upstream input change  - nativeX itself differs with Guide state.
    //   T2 camera gain change     - nativeX unchanged, CameraMoveLR/yaw-per-
    //                               update differs.
    //   T3 update-count change    - nativeX and per-call yaw both unchanged,
    //                               but fldCamera.fldCamMain() runs more
    //                               times within the same rendered frame.
    //
    // Read-only throughout: GetPadAnalog's __result is never written, no
    // fldCamera field is written, no Process.Modules enumeration, no
    // reflection, no state-changing API of any kind. Logs on state-change
    // only, throttled, following the Chapter115 lesson about per-frame cost.
    //
    // This probe does not hook or detect the Guide button itself; Guide
    // ON/OFF timing is identified by the user during the real-machine test
    // and/or from a companion recording, per Chapter122 scope.
    internal static class Chapter122GuideYawCorrelationProbe
    {
        private const string Tag = "[NocturneModernController][Ch122]";
        private const long MinLogIntervalMs = 100;

        // Stick is considered "active" (worth logging) when it deviates from
        // the confirmed center value of 128 by more than this amount.
        private const int StickActiveThreshold = 20;

        // Updated by Chapter122PadAnalogXWatch.Postfix (Harmony), consumed by
        // Sample(). Plain fields are safe here: both the Harmony postfix and
        // Sample() run on the Unity main thread, never concurrently.
        internal static byte LastNativeX;
        internal static bool HaveNativeX;

        // Updated by Chapter122CamMainCallWatch.Postfix (Harmony), drained by
        // Sample() every call so it always reflects "calls since the last
        // Sample()", i.e. calls within the frame that just completed.
        internal static int CamMainCallAccumulator;

        private static bool _havePrev;
        private static byte _prevNativeX;
        private static float _prevCameraMoveDir;
        private static int _prevCamMainCalls;

        private static long _lastLogTickMs;

        internal static void Sample()
        {
            int callsThisFrame = CamMainCallAccumulator;
            CamMainCallAccumulator = 0;

            if (!FieldDashPatch.IsExplorationActive || !HaveNativeX)
            {
                _havePrev = false;
                return;
            }

            byte nativeX = LastNativeX;
            float cameraMoveLr = fldCamera.CameraMoveLR;
            float cameraMoveDir = fldCamera.CameraMoveDir;
            int mMoveLr = fldCamera.mMoveLR;

            if (!_havePrev)
            {
                _havePrev = true;
                _prevNativeX = nativeX;
                _prevCameraMoveDir = cameraMoveDir;
                _prevCamMainCalls = callsThisFrame;
                return;
            }

            float yawDelta = cameraMoveDir - _prevCameraMoveDir;
            bool stickActive = Math.Abs(nativeX - 128) > StickActiveThreshold;
            bool changed = nativeX != _prevNativeX ||
                           callsThisFrame != _prevCamMainCalls ||
                           Math.Abs(yawDelta) > 0.01f;

            long now = Environment.TickCount64;
            if (stickActive && changed && (now - _lastLogTickMs) >= MinLogIntervalMs)
            {
                _lastLogTickMs = now;
                MelonLogger.Msg(
                    $"{Tag} t={DateTimeOffset.Now:O} nativeX={nativeX} " +
                    $"mMoveLR={mMoveLr} cameraMoveLR={cameraMoveLr:F4} " +
                    $"cameraMoveDir={cameraMoveDir:F4} yawDelta={yawDelta:F4} " +
                    $"camMainCallsThisFrame={callsThisFrame}");
            }

            _prevNativeX = nativeX;
            _prevCameraMoveDir = cameraMoveDir;
            _prevCamMainCalls = callsThisFrame;
        }
    }

    [HarmonyPatch(typeof(dds3PadManager), nameof(dds3PadManager.GetPadAnalog))]
    internal static class Chapter122PadAnalogXWatch
    {
        // Exact native, non-MOD-overridden horizontal channel confirmed in
        // Chapter117: GetPadAnalog(padno=0, stick_lr=1, xy=0, cip_no=1) =
        // Right Stick X. Observation only - __result is never assigned.
        private static void Postfix(int __0, int __1, int __2, int __3, byte __result)
        {
            if (!FieldDashPatch.IsExplorationActive)
            {
                return;
            }

            if (__0 == 0 && __1 == 1 && __2 == 0 && __3 == 1)
            {
                Chapter122GuideYawCorrelationProbe.LastNativeX = __result;
                Chapter122GuideYawCorrelationProbe.HaveNativeX = true;
            }
        }
    }

    [HarmonyPatch(typeof(fldCamera), nameof(fldCamera.fldCamMain))]
    internal static class Chapter122CamMainCallWatch
    {
        // Counts fldCamMain() invocations only; no field is read or written
        // here (kept as cheap as possible since this runs on every call).
        private static void Postfix()
        {
            if (!FieldDashPatch.IsExplorationActive)
            {
                return;
            }

            Chapter122GuideYawCorrelationProbe.CamMainCallAccumulator++;
        }
    }
}
