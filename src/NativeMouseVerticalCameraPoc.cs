using HarmonyLib;
using Il2Cpp;
using MelonLoader;

namespace NocturneModernController
{
    /// <summary>
    /// Passive telemetry for the game's native mouse-drag camera path.
    /// It intentionally does not alter results while diagnosing the Steam
    /// recording transition that unexpectedly enabled free vertical camera.
    /// </summary>
    internal static class NativeMouseVerticalCameraPoc
    {
        internal static bool ExplorationActive => ExplorationState.IsExplorationActive;
    }

    [HarmonyPatch(typeof(fldCamera), "MouseDraggCheck")]
    internal static class NativeMouseDragCheckPatch
    {
        private static bool _hasLast;
        private static bool _lastResult;

        private static void Postfix(ref bool __result)
        {
            if (!NativeMouseVerticalCameraPoc.ExplorationActive)
            {
                _hasLast = false;
                return;
            }

            if (!_hasLast || __result != _lastResult)
            {
                MelonLogger.Msg(
                    $"[NocturneModernController] NATIVE-MOUSE drag={__result} " +
                    $"stickUp={VanillaTurnInvocationPoc.UpHeld} " +
                    $"stickDown={VanillaTurnInvocationPoc.DownHeld}.");
                _lastResult = __result;
                _hasLast = true;
            }
        }
    }

    [HarmonyPatch(typeof(fldCamera), "MouseDirection")]
    internal static class NativeMouseDirectionPatch
    {
        private static int _lastLogTick;

        private static void Postfix(ref int __0, ref int __1)
        {
            if (!NativeMouseVerticalCameraPoc.ExplorationActive)
            {
                return;
            }

            int now = System.Environment.TickCount;
            if ((__0 != 0 || __1 != 0) &&
                unchecked(now - _lastLogTick) >= 50)
            {
                _lastLogTick = now;
                MelonLogger.Msg(
                    $"[NocturneModernController] NATIVE-MOUSE direction=({__0},{__1}) " +
                    $"axis=({fldCamera.mAxis.x:F3},{fldCamera.mAxis.y:F3}) " +
                    $"accel=({fldCamera.mAcceleration.x:F3},{fldCamera.mAcceleration.y:F3}) " +
                    $"moveUD={fldCamera.mMoveUD} camUD={fldCamera.CameraMoveUD:F3} " +
                    $"over={fldCamera.upDownOver}.");
            }
        }
    }
}
