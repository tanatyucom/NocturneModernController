using System;
using HarmonyLib;
using Il2Cpp;
using MelonLoader;

[assembly: MelonInfo(
    typeof(NocturneForceEncounter.ForceEncounterMod),
    "Nocturne Force Encounter",
    NocturneForceEncounter.ForceEncounterMod.ModVersion,
    "Gray Ghost")]
[assembly: MelonGame(null, "smt3hd")]

namespace NocturneForceEncounter
{
    // Force Encounter as a standalone mod: press X while exploring to request a
    // battle through the game's own encounter check. With Nocturne Modern
    // Controller installed it also appears in Controller's key config and
    // Settings (optional integration, see ModernControllerIntegration).
    public sealed class ForceEncounterMod : MelonMod
    {
        internal const string ModVersion = "0.2.0";

        internal static ForceEncounterLogic Logic { get; } = new();
        private static ModernControllerIntegration? _integration;

        public override void OnInitializeMelon()
        {
            string? note = ForceEncounterSettings.Load();
            if (note != null)
            {
                LoggerInstance.Msg("[NocturneForceEncounter] " + note);
            }
        }

        // After every mod's OnInitializeMelon, so Controller (if installed) has
        // loaded its settings and bindings whatever the mod load order is.
        public override void OnLateInitializeMelon()
        {
            ModernControllerIntegration? integration = ModernControllerIntegration.TryCreate(
                ControllerAssembly.Find(), out string reason);
            if (integration != null)
            {
                try
                {
                    integration.Register(
                        () => ForceEncounterSettings.Current.Enabled,
                        enabled =>
                        {
                            ForceEncounterSettings.Current.Enabled = enabled;
                            ForceEncounterSettings.Save();
                            return true;
                        },
                        ModVersion);
                    _integration = integration;
                }
                catch (Exception exception)
                {
                    reason = "Nocturne Modern Controller integration failed (" +
                        (exception.InnerException ?? exception).GetType().Name + ")";
                }
            }
            LoggerInstance.Msg(
                $"[NocturneForceEncounter] Loaded v{ModVersion}; enabled={ForceEncounterSettings.Current.Enabled}; " +
                (_integration != null ? "input=Controller key config (" : "input=X, standalone (") + reason + ").");
        }

        public override void OnUpdate()
        {
            bool enabled = ForceEncounterSettings.Current.Enabled;
            bool explorationActive = ExplorationTracker.IsExplorationActive;
            bool settingsOpen;
            bool held;
            try
            {
                settingsOpen = _integration?.IsSettingsOpen ?? false;
                held = enabled && explorationActive && !settingsOpen &&
                    ForceEncounterInput.ReadHeld(
                        _integration != null,
                        () => _integration!.IsHeld(),
                        StandaloneInput.IsXHeld);
            }
            catch (Exception)
            {
                return;
            }

            switch (Logic.Sample(enabled, explorationActive, settingsOpen, held, Environment.TickCount))
            {
                case ForceEncounterEvent.Requested:
                    LoggerInstance.Msg("[NocturneForceEncounter] Q8 FORCE-ENCOUNTER requested.");
                    break;
                case ForceEncounterEvent.TimedOut:
                    LoggerInstance.Msg(
                        "[NocturneForceEncounter] Q8 FORCE-ENCOUNTER rejected or unavailable in the current field state.");
                    break;
            }
        }
    }

    [HarmonyPatch(typeof(nbEncount), nameof(nbEncount.nbEncountCalc))]
    internal static class ForceEncounterNativeCheckPatch
    {
        private static void Prefix(ref float __1)
        {
            ForceEncounterMod.Logic.BoostNextNormalCheck(ref __1, ExplorationTracker.IsExplorationActive);
        }

        private static void Postfix(int __result)
        {
            if (ForceEncounterMod.Logic.ObserveNormalCheckResult(__result))
            {
                MelonLogger.Msg(
                    $"[NocturneForceEncounter] Q8 FORCE-ENCOUNTER accepted by native encounter logic: data={__result}.");
            }
        }
    }
}
