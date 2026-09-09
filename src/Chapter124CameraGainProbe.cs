using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using HarmonyLib;
using Il2Cpp;
using MelonLoader;
using UnityEngine;

namespace NocturneModernController
{
    // Chapter 124: unified real-machine correlation probe combining
    // everything the static (Ghidra) dataflow trace found relevant to the
    // Guide-hold ~3.4x yaw speed-up (RIGHT_STICK_VIEW_AND_DASH_
    // INVESTIGATION.md, Chapter124):
    //   - native X/Y input (Chapter117/123)
    //   - fldCamera.MouseDraggCheck() result. Static analysis found the
    //     real analog-driven camera rotation function (native VA
    //     0x18201ECE0) checks a function at native VA 0x18201CC50 to
    //     choose between a fixed-step (CameraMoveLR/UD) rotation formula
    //     and an mAcceleration-based formula; that function's own body
    //     (four calls to the same native helper, same argument sequence
    //     5/4/6/7, OR'd together) matches fldCamera.MouseDraggCheck()'s
    //     already-known structure exactly, so this probe hooks
    //     MouseDraggCheck directly (a real, named, Harmony-hookable Il2Cpp
    //     method - no raw address call needed).
    //   - fldCamera.mAxis / fldCamera.mAcceleration (confirmed-name
    //     fields, already read successfully by the existing dormant
    //     NativeMouseDirectionPatch in NativeMouseVerticalCameraPoc.cs)
    //   - fldCamera.CameraMoveLR / CameraMoveUD (Chapter123's near-constant
    //     step values)
    //   - true camera yaw/pitch, computed geometrically from
    //     fldCamera.flgCameraCtrl's real UnityEngine.Transform.forward
    //     (Chapter123's proven method - CameraMoveDir was proven NOT to be
    //     the real angle)
    //
    // Purpose: classify which stage the ~3.4x yaw change happens at:
    //   R1: MouseDraggCheck() result changes with Guide state
    //   R2: mAxis changes with Guide state
    //   R3: mAcceleration changes (but not mAxis) with Guide state
    //   R4: all three stay constant while yaw still changes (points further
    //       downstream than this probe currently observes)
    //
    // Supersedes Chapter123CameraOrientationProbe (disabled in ModMain) -
    // same yaw/pitch method, extended with mAxis/mAcceleration/
    // MouseDraggCheck.
    //
    // Read-only throughout: no field is written, no GetPadAnalog __result
    // is written, no MouseDraggCheck __result is written, no
    // Process.Modules enumeration, no reflection, no state-changing API.
    // Logs on state-change only, throttled (Chapter115 lesson).
    internal static class Chapter124CameraGainProbe
    {
        private const string Tag = "[NocturneModernController][Ch124]";
        private const long MinLogIntervalMs = 100;
        private const int StickActiveThreshold = 20;

        // Chapter124 static (Ghidra) trace: calcCamNormal() only overwrites
        // fldCamera.mAxis/mAcceleration when
        // *(long*)(*(long*)(inputModeStorage+0xb8)+0x48)+0x24 != 0 (the
        // SAME field fldCamMain()'s own switch() reads to pick the
        // normal-analog-gamepad path, case 0). When that condition holds,
        // both mAxis and mAcceleration are overwritten with the same
        // 8-byte value read from *(long*)(externalSourceStorage+0xb8)+0x0
        // via func_0x000182882000(), a different, not-yet-identified
        // class's static field. All addresses below are PE-preferred-base
        // (Ghidra image base 0x180000000) + RVA; resolved to the real
        // runtime address by adding the actual loaded GameAssembly.dll
        // base (found once via Process.Modules, cached - never re-scanned
        // per frame, per the Chapter115 lesson).
        // RVA = Ghidra VA - image base (0x180000000):
        //   0x182E4E678 - 0x180000000 = 0x02E4E678
        //   0x182E5ACF8 - 0x180000000 = 0x02E5ACF8
        // (Previously miscalculated as 0xE4E678/0xE5ACF8 - missing the
        // leading 0x02000000 - caught before deploy; never ran on the
        // real machine.)
        private const long InputModeStorageRva = 0x02E4E678L;
        private const int InputModeDataPtrOffset = 0xb8;
        private const int InputModeRefFieldOffset = 0x48;
        private const int InputModeValueOffset = 0x24;

        private const long ExternalSourceStorageRva = 0x02E5ACF8L;
        private const int ExternalSourceDataPtrOffset = 0xb8;

        private static bool _gameAssemblyBaseResolved;
        private static IntPtr _gameAssemblyBase = IntPtr.Zero;
        private static bool _rawReadErrorLogged;

        // Tracks which stage the raw-memory read chain reached, and logs
        // only on a STAGE TRANSITION (not "only once ever" - a design bug
        // in the first diagnostic build meant a pre-Guide failure latched
        // the one-shot flag and silently hid a later success). Cheap
        // string-equality check, no per-frame cost beyond that.
        private static string _lastRawStage = string.Empty;

        internal static byte LastNativeX;
        internal static bool HaveNativeX;

        internal static byte LastNativeYRaw;
        internal static bool HaveNativeYRaw;

        internal static byte LastEffectiveY;
        internal static bool HaveEffectiveY;

        internal static bool LastMouseDraggCheck;
        internal static bool HaveMouseDraggCheck;

        internal static int CamMainCallAccumulator;

        private static bool _havePrev;
        private static byte _prevNativeX;
        private static byte _prevEffectiveY;
        private static bool _prevMouseDragg;
        private static int _prevInputMode;
        private static float _prevYaw;
        private static float _prevPitch;
        private static int _prevCamMainCalls;

        private static long _lastLogTickMs;

        // Read-only: two Marshal.ReadIntPtr/ReadInt32/ReadInt64 chains,
        // resolved against a cached module base. Never writes any memory.
        // Fails safe (returns false, caller skips those fields for this
        // sample) on any null-pointer or exception - never throws upward.
        private static bool TryReadRawState(
            out int inputMode, out long sourceRaw, out float sourceX, out float sourceY)
        {
            inputMode = 0;
            sourceRaw = 0;
            sourceX = 0f;
            sourceY = 0f;

            IntPtr gameAssemblyBase = ResolveGameAssemblyBase();
            if (gameAssemblyBase == IntPtr.Zero)
            {
                LogStageIfChanged("module-not-found",
                    "GameAssembly.dll not found via Process.Modules (resolved once, cached as failed).");
                return false;
            }

            try
            {
                IntPtr inputModeStorage = IntPtr.Add(gameAssemblyBase, (int)InputModeStorageRva);
                IntPtr inputModeData = Marshal.ReadIntPtr(inputModeStorage, InputModeDataPtrOffset);
                if (inputModeData == IntPtr.Zero)
                {
                    LogStageIfChanged("inputModeData-null",
                        $"base=0x{gameAssemblyBase.ToInt64():X} inputModeStorage=0x{inputModeStorage.ToInt64():X} (storage+0xb8)=NULL.");
                    return false;
                }
                IntPtr inputModeRefObj = Marshal.ReadIntPtr(inputModeData, InputModeRefFieldOffset);
                if (inputModeRefObj == IntPtr.Zero)
                {
                    LogStageIfChanged("inputModeRefObj-null",
                        $"base=0x{gameAssemblyBase.ToInt64():X} inputModeData=0x{inputModeData.ToInt64():X} (inputModeData+0x48)=NULL.");
                    return false;
                }
                inputMode = Marshal.ReadInt32(inputModeRefObj, InputModeValueOffset);

                IntPtr sourceStorage = IntPtr.Add(gameAssemblyBase, (int)ExternalSourceStorageRva);
                IntPtr sourceData = Marshal.ReadIntPtr(sourceStorage, ExternalSourceDataPtrOffset);
                if (sourceData == IntPtr.Zero)
                {
                    LogStageIfChanged("sourceData-null",
                        $"base=0x{gameAssemblyBase.ToInt64():X} sourceStorage=0x{sourceStorage.ToInt64():X} (sourceStorage+0xb8)=NULL. inputMode(ok)={inputMode}");
                    return false;
                }
                sourceRaw = Marshal.ReadInt64(sourceData, 0);
                sourceX = BitConverter.Int32BitsToSingle((int)(sourceRaw & 0xFFFFFFFFL));
                sourceY = BitConverter.Int32BitsToSingle((int)(sourceRaw >> 32));

                LogStageIfChanged("ok",
                    $"base=0x{gameAssemblyBase.ToInt64():X} inputModeRefObj=0x{inputModeRefObj.ToInt64():X} " +
                    $"inputMode={inputMode} sourceData=0x{sourceData.ToInt64():X} sourceRaw=0x{sourceRaw:X16}");
                return true;
            }
            catch (Exception ex)
            {
                if (!_rawReadErrorLogged)
                {
                    _rawReadErrorLogged = true;
                    MelonLogger.Warning($"{Tag} raw memory read threw {ex.GetType().FullName}: {ex.Message} - raw fields disabled for this session.");
                }
                return false;
            }
        }

        // Logs only when the reached stage differs from the last logged
        // stage (covers module-not-found / each null-pointer stage / ok),
        // so a later success after an earlier failure - e.g. once Guide is
        // actually pressed - is never hidden by a one-shot flag.
        private static void LogStageIfChanged(string stage, string details)
        {
            if (stage == _lastRawStage)
            {
                return;
            }
            _lastRawStage = stage;
            MelonLogger.Msg($"{Tag} DIAGNOSTIC raw-read stage={stage} {details}");
        }

        // Attempts Process.Modules resolution exactly once per session,
        // success or failure (Chapter115 lesson: never re-enumerate
        // modules on a per-frame/per-Sample basis). If GameAssembly.dll is
        // not found on the one attempt, _gameAssemblyBase stays Zero for
        // the remainder of the session and TryReadRawState() fails safe
        // (raw fields simply stay unavailable) rather than retrying.
        private static IntPtr ResolveGameAssemblyBase()
        {
            if (_gameAssemblyBaseResolved)
            {
                return _gameAssemblyBase;
            }
            _gameAssemblyBaseResolved = true;
            using Process current = Process.GetCurrentProcess();
            foreach (ProcessModule module in current.Modules)
            {
                try
                {
                    if (string.Equals(module.ModuleName, "GameAssembly.dll", StringComparison.OrdinalIgnoreCase))
                    {
                        _gameAssemblyBase = module.BaseAddress;
                        return _gameAssemblyBase;
                    }
                }
                finally
                {
                    module.Dispose();
                }
            }
            return IntPtr.Zero;
        }

        internal static void Sample()
        {
            int callsThisFrame = CamMainCallAccumulator;
            CamMainCallAccumulator = 0;

            if (!FieldDashPatch.IsExplorationActive || !HaveNativeX || !HaveEffectiveY || !HaveMouseDraggCheck)
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

            byte nativeX = LastNativeX;
            byte nativeYRaw = LastNativeYRaw;
            byte effectiveY = LastEffectiveY;
            bool mouseDragg = LastMouseDraggCheck;

            float axisX = fldCamera.mAxis.x;
            float axisY = fldCamera.mAxis.y;
            float accelX = fldCamera.mAcceleration.x;
            float accelY = fldCamera.mAcceleration.y;
            float cameraMoveLr = fldCamera.CameraMoveLR;
            float cameraMoveUd = fldCamera.CameraMoveUD;

            bool haveRaw = TryReadRawState(out int inputMode, out long sourceRaw, out float sourceX, out float sourceY);

            if (!_havePrev)
            {
                _havePrev = true;
                _prevNativeX = nativeX;
                _prevEffectiveY = effectiveY;
                _prevMouseDragg = mouseDragg;
                _prevInputMode = haveRaw ? inputMode : 0;
                _prevYaw = yaw;
                _prevPitch = pitch;
                _prevCamMainCalls = callsThisFrame;
                return;
            }

            float yawDelta = Mathf.DeltaAngle(_prevYaw, yaw);
            float pitchDelta = pitch - _prevPitch;

            bool inputModeChanged = haveRaw && inputMode != _prevInputMode;

            bool stickActive = Math.Abs(nativeX - 128) > StickActiveThreshold ||
                                Math.Abs(effectiveY - 128) > StickActiveThreshold;
            bool changed = nativeX != _prevNativeX ||
                           effectiveY != _prevEffectiveY ||
                           mouseDragg != _prevMouseDragg ||
                           inputModeChanged ||
                           callsThisFrame != _prevCamMainCalls ||
                           Math.Abs(yawDelta) > 0.01f ||
                           Math.Abs(pitchDelta) > 0.01f;

            long now = Environment.TickCount64;
            if ((stickActive || mouseDragg != _prevMouseDragg || inputModeChanged) && changed &&
                (now - _lastLogTickMs) >= MinLogIntervalMs)
            {
                _lastLogTickMs = now;
                string rawFields = haveRaw
                    ? $"inputMode={inputMode} sourceRaw=0x{sourceRaw:X16} sourceXY=({sourceX:F4},{sourceY:F4})"
                    : "inputMode=N/A sourceRaw=N/A sourceXY=N/A";
                MelonLogger.Msg(
                    $"{Tag} t={DateTimeOffset.Now:O} nativeX={nativeX} nativeYRaw={nativeYRaw} " +
                    $"effectiveY={effectiveY} mouseDragg={mouseDragg} {rawFields} " +
                    $"axis=({axisX:F4},{axisY:F4}) accel=({accelX:F4},{accelY:F4}) " +
                    $"cameraMoveLR={cameraMoveLr:F4} cameraMoveUD={cameraMoveUd:F4} " +
                    $"yaw={yaw:F4} pitch={pitch:F4} yawDelta={yawDelta:F4} pitchDelta={pitchDelta:F4} " +
                    $"camMainCallsThisFrame={callsThisFrame}");
            }

            _prevNativeX = nativeX;
            _prevEffectiveY = effectiveY;
            _prevMouseDragg = mouseDragg;
            _prevInputMode = haveRaw ? inputMode : _prevInputMode;
            _prevYaw = yaw;
            _prevPitch = pitch;
            _prevCamMainCalls = callsThisFrame;
        }
    }

    [HarmonyPatch(typeof(dds3PadManager), nameof(dds3PadManager.GetPadAnalog))]
    internal static class Chapter124PadAnalogXWatch
    {
        private static void Postfix(int __0, int __1, int __2, int __3, byte __result)
        {
            if (!FieldDashPatch.IsExplorationActive)
            {
                return;
            }
            if (__0 == 0 && __1 == 1 && __2 == 0 && __3 == 1)
            {
                Chapter124CameraGainProbe.LastNativeX = __result;
                Chapter124CameraGainProbe.HaveNativeX = true;
            }
        }
    }

    [HarmonyPatch(typeof(dds3PadManager), nameof(dds3PadManager.GetPadAnalog))]
    [HarmonyPriority(Priority.First)]
    internal static class Chapter124PadAnalogYNativeWatch
    {
        private static void Postfix(int __0, int __1, int __2, int __3, byte __result)
        {
            if (!FieldDashPatch.IsExplorationActive)
            {
                return;
            }
            if (__0 == 0 && __1 == 1 && __2 == 1 && __3 == 1)
            {
                Chapter124CameraGainProbe.LastNativeYRaw = __result;
                Chapter124CameraGainProbe.HaveNativeYRaw = true;
            }
        }
    }

    [HarmonyPatch(typeof(dds3PadManager), nameof(dds3PadManager.GetPadAnalog))]
    [HarmonyPriority(Priority.Last)]
    internal static class Chapter124PadAnalogYEffectiveWatch
    {
        private static void Postfix(int __0, int __1, int __2, int __3, byte __result)
        {
            if (!FieldDashPatch.IsExplorationActive)
            {
                return;
            }
            if (__0 == 0 && __1 == 1 && __2 == 1 && __3 == 1)
            {
                Chapter124CameraGainProbe.LastEffectiveY = __result;
                Chapter124CameraGainProbe.HaveEffectiveY = true;
            }
        }
    }

    [HarmonyPatch(typeof(fldCamera), nameof(fldCamera.fldCamMain))]
    internal static class Chapter124CamMainCallWatch
    {
        private static void Postfix()
        {
            if (!FieldDashPatch.IsExplorationActive)
            {
                return;
            }
            Chapter124CameraGainProbe.CamMainCallAccumulator++;
        }
    }

    // fldCamera.MouseDraggCheck() - statically identified (Chapter124) as
    // the function the real analog-camera-rotation path checks to switch
    // between a fixed-step and an mAcceleration-based rotation formula.
    // This is a second, independent postfix from the existing
    // NativeMouseDragCheckPatch - Harmony already runs multiple postfixes
    // on one method elsewhere in this codebase (GetPadAnalog). Read-only:
    // never writes __result.
    [HarmonyPatch(typeof(fldCamera), "MouseDraggCheck")]
    internal static class Chapter124MouseDraggCheckWatch
    {
        private static void Postfix(bool __result)
        {
            Chapter124CameraGainProbe.LastMouseDraggCheck = __result;
            Chapter124CameraGainProbe.HaveMouseDraggCheck = true;
        }
    }
}
