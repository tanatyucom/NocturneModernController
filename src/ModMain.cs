using MelonLoader;

[assembly: MelonInfo(
    typeof(NocturneModernController.ModMain),
    "Nocturne Modern Controller",
    "2.0.3",
    "Gray Ghost")]
[assembly: MelonGame(null, "smt3hd")]

namespace NocturneModernController
{
    public sealed class ModMain : MelonMod
    {
        public override void OnInitializeMelon()
        {
            ControllerSettings.Load();
            ModernControllerApi.RegisterFeatureProvider(BuiltInFeatureProvider.Instance);
            BuiltInControllerActions.Register(SettingsGuiController.IsAvailable);
            ModernControllerApi.ResolveBindings();
            SdlRightStickInput.Initialize(LoggerInstance);
            // [HarmonyPatch] classes are applied by MelonLoader's automatic
            // PatchAll; patching them here as well registered every patch twice.
            LoggerInstance.Msg("[NocturneModernController] Native right-stick dungeon camera loaded; legacy LB/RB field turn suppressed, BATTLE untouched.");
        }

        public override void OnUpdate()
        {
            ModernControllerApi.RetryGameActionBindingsIfNeeded();
            SettingsGuiController.Sample();
            GameBindingRequestProcessor.Sample();
            SdlRightStickInput.Sample();
            bool explorationActive = ExplorationState.IsExplorationActive;
            bool modActionsActive = explorationActive && !SettingsGuiController.IsOpen;
            ExternalInputBridge.UpdateGameContext(modActionsActive);
            ExplorationCursorController.Update(modActionsActive);
        }

        public override void OnDeinitializeMelon()
        {
            SettingsGuiController.Shutdown();
            ExplorationCursorController.Restore();
            SdlRightStickInput.Shutdown();
        }
    }
}
