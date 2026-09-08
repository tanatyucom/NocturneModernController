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
            HarmonyInstance.CreateClassProcessor(typeof(Root26ResetControllerCallProbe)).Patch();
            HarmonyInstance.CreateClassProcessor(typeof(Root26SteamControllerReStartCallProbe)).Patch();
            HarmonyInstance.CreateClassProcessor(typeof(Root26Phase1UpdateInputCallProbe)).Patch();
            HarmonyInstance.CreateClassProcessor(typeof(Root26Phase1SteamPadSetCallProbe)).Patch();
            HarmonyInstance.CreateClassProcessor(typeof(Root26Phase1SetAnalogProbe)).Patch();
            HarmonyInstance.CreateClassProcessor(typeof(Root26Phase3UpdateConnectedControllersCallProbe)).Patch();
            HarmonyInstance.CreateClassProcessor(typeof(Root26Phase3GetActionCallProbe)).Patch();
            HarmonyInstance.CreateClassProcessor(typeof(Root26Phase3UpdateControlCallProbe)).Patch();
            HarmonyInstance.CreateClassProcessor(typeof(Root26Phase3ActivateActionSetCallProbe)).Patch();
            HarmonyInstance.CreateClassProcessor(typeof(Root26Phase3GetConnectedControllersCallProbe)).Patch();
            HarmonyInstance.CreateClassProcessor(typeof(Root26KernelMainLoopCallProbe)).Patch();
            try
            {
                HarmonyInstance.CreateClassProcessor(typeof(Root26FramerateModOnFixedUpdateCallProbe)).Patch();
            }
            catch (System.Exception ex)
            {
                LoggerInstance.Warning(
                    "[NocturneModernController][Root26KernelLoop] Could not patch " +
                    "NocturneGraphicsConfigurator.OnFixedUpdate() (third-party Framerate Mod " +
                    "absent/changed?) - " + ex.GetType().FullName + ": " + ex.Message);
            }
            HarmonyInstance.CreateClassProcessor(typeof(Root26Phase5OverlayActivatedProbe)).Patch();
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
            Root26SteamStateProbe.Sample();
            Root26SteamPadDeepStateProbe.Sample();
            Root26SteamPadOffset18Probe.Sample();
            Root26Phase1ConditionProbe.Sample();
            Root26Phase2AnalogActionDataProbe.Sample();
            Root26Phase3PostResetWatchProbe.Sample();
            // Root26Phase4NativeRecoveryPoc.Sample(); // disabled for Chapter 57 field-offset-map test: F10-gated state-mutating PoC, kept off as accidental-press safety while this build's purpose is pure observation.
            Root26KernelLoopFrequencyProbe.Sample();
            // Root26Phase8OverlayToStoreOpenPoc.Sample(); // disabled for Chapter 57 field-offset-map test: auto-fires ActivateGameOverlayToStore() ~5s into exploration, would contaminate the DEAD->Guide->LIVE observation window.
            Root26FieldOffsetMapProbe.Sample();
            Root26SteamInputUtilFlagProbe.Sample();
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
