using System;
using HarmonyLib;
using Il2Cpp;

namespace NocturneModernController
{
    // Core service: "is the player in field exploration right now".
    //
    // Observes the field update itself, independently of any feature. It is
    // false while the Settings window is open (the game is minimized and the
    // field update stops), on the frame Settings closes, in battle, menus and
    // events, and becomes true again on the first field update after that.
    // GAME binding readiness and request processing, the right stick and
    // ModernControllerApi.IsExplorationActive rely on exactly this behaviour.
    // (The standalone gameplay mods keep their own tracker with the same rule,
    // shared/ExplorationWindow.cs.)
    [HarmonyPatch(typeof(fldPlayer), nameof(fldPlayer.fldPlayerCalc))]
    internal static class ExplorationState
    {
        private static int _lastFieldTick;

        internal static bool IsExplorationActive =>
            ExplorationWindow.IsActive(_lastFieldTick, Environment.TickCount);

        private static void Prefix()
        {
            _lastFieldTick = Environment.TickCount;
        }
    }
}
