using System;
using System.Linq;
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
    // Force Encounter as a gameplay mod for the Nocturne Modern Controller
    // ecosystem: Controller provides input bindings, exploration state and the
    // Settings integration; this mod owns the feature and its native hook.
    public sealed class ForceEncounterMod : MelonMod
    {
        internal const string ModVersion = "0.1.0";
        private const string ControllerAssembly = "NocturneModernController";

        internal static ForceEncounterLogic Logic { get; } = new();
        internal static bool Available { get; private set; }

        public override void OnInitializeMelon()
        {
            string? note = ForceEncounterSettings.Load();
            if (note != null)
            {
                LoggerInstance.Msg("[NocturneForceEncounter] " + note);
            }
        }

        // After every mod's OnInitializeMelon, so Controller has loaded its
        // settings and bindings whatever the mod load order is.
        public override void OnLateInitializeMelon()
        {
            if (!AppDomain.CurrentDomain.GetAssemblies().Any(assembly =>
                    string.Equals(assembly.GetName().Name, ControllerAssembly, StringComparison.OrdinalIgnoreCase)))
            {
                LoggerInstance.Warning(
                    "[NocturneForceEncounter] Nocturne Modern Controller is not installed; Force Encounter is disabled.");
                return;
            }

            try
            {
                ControllerBridge.Register();
                Available = true;
                string binding = string.Join(", ", ControllerBridge.GetBindings()
                    .Where(entry => entry.ActionId == ControllerBridge.ActionId)
                    .Select(entry => $"{entry.Context} [{string.Join("+", entry.Buttons)}] ({entry.Source})"));
                LoggerInstance.Msg(
                    $"[NocturneForceEncounter] Loaded; enabled={ForceEncounterSettings.Current.Enabled} " +
                    $"binding={(binding.Length == 0 ? "none" : binding)}.");
            }
            catch (Exception exception)
            {
                LoggerInstance.Warning(
                    "[NocturneForceEncounter] Controller integration failed (" + exception.GetType().Name +
                    ": " + exception.Message + "); Force Encounter is disabled.");
            }
        }

        public override void OnUpdate()
        {
            if (!Available)
            {
                return;
            }

            bool held;
            bool explorationActive;
            bool settingsOpen;
            try
            {
                explorationActive = ControllerBridge.IsExplorationActive;
                settingsOpen = ControllerBridge.IsSettingsOpen;
                held = ForceEncounterSettings.Current.Enabled && explorationActive && !settingsOpen &&
                    ControllerBridge.IsHeld();
            }
            catch (Exception)
            {
                return;
            }

            switch (Logic.Sample(
                        ForceEncounterSettings.Current.Enabled,
                        explorationActive,
                        settingsOpen,
                        held,
                        Environment.TickCount))
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
            if (ForceEncounterMod.Available && ForceEncounterMod.Logic.IsRequestPending)
            {
                ForceEncounterMod.Logic.BoostNextNormalCheck(ref __1, ControllerBridge.IsExplorationActive);
            }
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
