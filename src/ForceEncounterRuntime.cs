using System;
using HarmonyLib;
using Il2Cpp;
using MelonLoader;

namespace NocturneModernController
{
    internal static class ForceEncounterRuntime
    {
        private const int RequestTimeoutMilliseconds = 2000;
        private const float ForcedTravelLength = 100000.0f;

        private static bool _wasHeld;
        private static bool _requestPending;
        private static int _requestTick;

        internal static void Sample()
        {
            if (!FieldDashPatch.IsExplorationActive)
            {
                _wasHeld = false;
                CancelPendingRequest();
                return;
            }

            bool held;
            try
            {
                held = ModernControllerApi.IsHeld(
                    BuiltInControllerActions.ForceEncounter,
                    ControllerContext.Field);
            }
            catch (Exception)
            {
                return;
            }

            if (held && !_wasHeld)
            {
                _requestPending = true;
                _requestTick = Environment.TickCount;
                MelonLogger.Msg(
                    "[NocturneModernController] Q8 FORCE-ENCOUNTER requested.");
            }
            _wasHeld = held;

            if (_requestPending &&
                unchecked(Environment.TickCount - _requestTick) >= RequestTimeoutMilliseconds)
            {
                _requestPending = false;
                MelonLogger.Msg(
                    "[NocturneModernController] Q8 FORCE-ENCOUNTER rejected or unavailable in the current field state.");
            }
        }

        internal static void BoostNextNormalCheck(ref float length)
        {
            if (_requestPending && FieldDashPatch.IsExplorationActive)
            {
                length = Math.Max(length, ForcedTravelLength);
            }
        }

        internal static void ObserveNormalCheckResult(int result)
        {
            if (!_requestPending || result == 0)
            {
                return;
            }

            _requestPending = false;
            MelonLogger.Msg(
                $"[NocturneModernController] Q8 FORCE-ENCOUNTER accepted by native encounter logic: data={result}.");
        }

        private static void CancelPendingRequest()
        {
            _requestPending = false;
        }
    }

    [HarmonyPatch(typeof(nbEncount), nameof(nbEncount.nbEncountCalc))]
    internal static class ForceEncounterNativeCheckPatch
    {
        private static void Prefix(ref float __1)
        {
            ForceEncounterRuntime.BoostNextNormalCheck(ref __1);
        }

        private static void Postfix(int __result)
        {
            ForceEncounterRuntime.ObserveNormalCheckResult(__result);
        }
    }
}
