using System;
using HarmonyLib;
using Il2Cpp;
using Il2Cpplibsdf_H;
using NocturneModernController;

namespace NocturneForceEncounter
{
    // Force Encounter's own view of the game, so it works without Controller.

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

    // Standalone input: the game's logical X button (the same pad map
    // Controller uses for ControllerButton.X), so any pad the game supports works.
    internal static class StandaloneInput
    {
        internal static bool IsXHeld()
        {
            try
            {
                return dds3PadManager.DDS3_PADCHECK_PRESS(SDF_PADMAP.SDF_PADMAP_RL, 0);
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
