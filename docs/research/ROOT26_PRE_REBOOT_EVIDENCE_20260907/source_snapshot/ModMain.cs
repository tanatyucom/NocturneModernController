using MelonLoader;

[assembly: MelonInfo(
    typeof(NocturneModernController.ModMain),
    "Nocturne Modern Controller",
    "1.0.0",
    "Gray Ghost")]
[assembly: MelonGame(null, "smt3hd")]

namespace NocturneModernController
{
    public sealed class ModMain : MelonMod
    {
        public override void OnInitializeMelon()
        {
            ControllerSettings.Load();
            SmartAutoKnowledgeStore.Load();
            ModernControllerApi.RegisterFeatureProvider(BuiltInFeatureProvider.Instance);
            BuiltInControllerActions.Register(SettingsGuiController.IsAvailable);
            SdlRightStickInput.Initialize(LoggerInstance);
            HarmonyInstance.CreateClassProcessor(typeof(FieldDashPatch)).Patch();
            HarmonyInstance.CreateClassProcessor(typeof(PuzzleLogicalTurnPatch)).Patch();
            HarmonyInstance.CreateClassProcessor(typeof(FormationFlagProbe)).Patch();
            HarmonyInstance.CreateClassProcessor(typeof(NativeRightStickCameraPatch)).Patch();
            HarmonyInstance.CreateClassProcessor(typeof(ForceEncounterNativeCheckPatch)).Patch();
            HarmonyInstance.CreateClassProcessor(typeof(RightStickPollingProbe)).Patch();
            LoggerInstance.Msg("[NocturneModernController] Dash loaded: hold LT/RT/P; press LT+RT to toggle dash keep.");
            LoggerInstance.Msg("[NocturneModernController] Native right-stick dungeon camera loaded; legacy LB/RB field turn suppressed, BATTLE untouched.");
        }

        public override void OnUpdate()
        {
            SettingsGuiController.Sample();
            SdlRightStickInput.Sample();
            if (ControllerSettings.Current.SmartAutoEnabled)
            {
                SmartAutoBattleTelemetry.Sample();
                SmartAutoBattleRuntime.Sample();
            }
            bool explorationActive = FieldDashPatch.IsExplorationActive;
            FocusCycleProbe.Sample();
            RightStickPollingProbe.Sample();
            bool modActionsActive = explorationActive && !SettingsGuiController.IsOpen;
            ExternalInputBridge.UpdateGameContext(modActionsActive);
            ExplorationCursorController.Update(modActionsActive);
            if (modActionsActive)
            {
                if (ControllerSettings.Current.QuickHealEnabled)
                {
                    QuickHealRuntimeProbe.Sample();
                }
                if (ControllerSettings.Current.ForceEncounterEnabled)
                {
                    ForceEncounterRuntime.Sample();
                }
            }
        }

        public override void OnDeinitializeMelon()
        {
            RightStickPollingProbe.Shutdown();
            FocusCycleProbe.Shutdown();
            SettingsGuiController.Shutdown();
            SmartAutoBattleRuntime.Shutdown();
            ExplorationCursorController.Restore();
            SdlRightStickInput.Shutdown();
        }
    }
}
