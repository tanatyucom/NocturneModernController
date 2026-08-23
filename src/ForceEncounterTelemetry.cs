using System;
using HarmonyLib;
using Il2Cpp;
using MelonLoader;

namespace NocturneModernController
{
    internal static class ForceEncounterTelemetry
    {
        private static int _lastCalcResult = int.MinValue;
        private static int _lastFieldCheck = int.MinValue;
        private static int _lastLogTick;

        internal static void LogCalc(int id, float length, int result)
        {
            int now = Environment.TickCount;
            if (result == _lastCalcResult && unchecked(now - _lastLogTick) < 1000)
            {
                return;
            }

            _lastCalcResult = result;
            _lastLogTick = now;
            MelonLogger.Msg(
                $"[NocturneModernController] Q8 ENCOUNTER-CALC id={id} " +
                $"length={length:F3} result={result} count={nbEncount.nbGetEncountCnt()}.");
        }

        internal static void LogFieldCheck(int result)
        {
            if (result == _lastFieldCheck)
            {
                return;
            }

            _lastFieldCheck = result;
            MelonLogger.Msg(
                $"[NocturneModernController] Q8 FIELD-ENCOUNTER-CHECK result={result} " +
                $"encOn={fldEnc.gencOn} mode={fldEnc.gencMode} " +
                $"random={fldEnc.gencIsRandomEncount} noEnc={fldGlobal.fldGb.NoEncountCnt}.");
        }
    }

    [HarmonyPatch(typeof(nbEncount), nameof(nbEncount.nbEncountCalc))]
    internal static class EncounterCalcTelemetryPatch
    {
        private static void Postfix(int __0, float __1, int __result)
        {
            if (FieldDashPatch.IsExplorationActive)
            {
                ForceEncounterTelemetry.LogCalc(__0, __1, __result);
            }
        }
    }

    [HarmonyPatch(typeof(fldTest), nameof(fldTest.fldRunEncountChk))]
    internal static class FieldEncounterCheckTelemetryPatch
    {
        private static void Postfix(int __result)
        {
            if (FieldDashPatch.IsExplorationActive)
            {
                ForceEncounterTelemetry.LogFieldCheck(__result);
            }
        }
    }

    [HarmonyPatch(typeof(fldEnc), nameof(fldEnc.encStart))]
    internal static class EncounterStartTelemetryPatch
    {
        private static void Prefix(bool __0)
        {
            MelonLogger.Msg(
                $"[NocturneModernController] Q8 ENCOUNTER-START random={__0} " +
                $"encOn={fldEnc.gencOn} mode={fldEnc.gencMode} " +
                $"pack={fldProcess.fldBattleData.encountpack} " +
                $"table={fldProcess.fldBattleData.encounttbl}.");
        }
    }
}
