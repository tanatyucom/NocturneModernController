using System;
using HarmonyLib;
using Il2Cpp;
using Il2Cpplibsdf_H;
using NocturneModernController;

namespace NocturneQuickHeal
{
    // Quick Heal's own view of the game, so it works without Controller.

    // Field exploration: same rule as Controller's ExplorationState (the field
    // update fldPlayer.fldPlayerCalc ran within the last 100 ms; the rule is
    // compiled in from shared/ExplorationWindow.cs, not referenced).
    [HarmonyPatch(typeof(fldPlayer), nameof(fldPlayer.fldPlayerCalc))]
    internal static class ExplorationTracker
    {
        private static int _lastFieldTick;

        internal static bool IsExplorationActive =>
            ExplorationWindow.IsActive(_lastFieldTick, Environment.TickCount);

        private static void Prefix()
        {
            _lastFieldTick = Environment.TickCount;
        }
    }

    // Standalone input: the game's logical SELECT button (the same pad map
    // Controller uses for ControllerButton.Select), so any pad the game
    // supports works. Not RB: without Controller, RB is also the game's own
    // field turn (Controller suppresses that, this mod does not), and SELECT
    // has no field/dungeon action in the game's default key config.
    internal static class StandaloneInput
    {
        internal static bool IsSelectHeld()
        {
            try
            {
                return dds3PadManager.DDS3_PADCHECK_PRESS(SDF_PADMAP.SDF_PADMAP_SELECT, 0);
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
