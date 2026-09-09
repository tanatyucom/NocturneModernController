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
            // Chapter 118: re-enabling this pre-existing (2026-08-22 era), read-only
            // native mouse-drag camera telemetry (never writes __result/__0/__1, only
            // logs on state-change) to test whether fldCamera.MouseDirection() fires
            // with nonzero deltas specifically while the physical Guide button is held,
            // as a candidate explanation for the user-reported "camera speed increases
            // only while Guide is held" phenomenon (Chapter 116/117).
            HarmonyInstance.CreateClassProcessor(typeof(NativeMouseDragCheckPatch)).Patch();
            HarmonyInstance.CreateClassProcessor(typeof(NativeMouseDirectionPatch)).Patch();
            // Chapter 122: disabled Chapter123 follow-up - the real-machine
            // test showed CameraMoveDir/CameraMoveLR/mMoveLR do not reflect
            // actual camera motion (CameraMoveDir stayed fixed at 60.0000
            // for the whole session; CameraMoveLR/mMoveLR only ever took 2
            // discrete values, uncorrelated with the native X swings
            // happening at the same time). Superseded by
            // Chapter123CameraOrientationProbe, which derives yaw/pitch
            // from fldCamera.flgCameraCtrl's real UnityEngine.Transform
            // instead of trusting fldCamera field names.
            // HarmonyInstance.CreateClassProcessor(typeof(Chapter122PadAnalogXWatch)).Patch();
            // HarmonyInstance.CreateClassProcessor(typeof(Chapter122CamMainCallWatch)).Patch();
            // Chapter 123: superseded by Chapter124CameraGainProbe below,
            // which reuses the same proven flgCameraCtrl.transform-based
            // yaw/pitch method and adds mAxis/mAcceleration/
            // MouseDraggCheck() observation identified by Chapter124's
            // static (Ghidra) dataflow trace.
            // HarmonyInstance.CreateClassProcessor(typeof(Chapter123PadAnalogXWatch)).Patch();
            // HarmonyInstance.CreateClassProcessor(typeof(Chapter123PadAnalogYNativeWatch)).Patch();
            // HarmonyInstance.CreateClassProcessor(typeof(Chapter123PadAnalogYEffectiveWatch)).Patch();
            // HarmonyInstance.CreateClassProcessor(typeof(Chapter123CamMainCallWatch)).Patch();
            HarmonyInstance.CreateClassProcessor(typeof(Chapter124PadAnalogXWatch)).Patch();
            HarmonyInstance.CreateClassProcessor(typeof(Chapter124PadAnalogYNativeWatch)).Patch();
            HarmonyInstance.CreateClassProcessor(typeof(Chapter124PadAnalogYEffectiveWatch)).Patch();
            HarmonyInstance.CreateClassProcessor(typeof(Chapter124CamMainCallWatch)).Patch();
            HarmonyInstance.CreateClassProcessor(typeof(Chapter124MouseDraggCheckWatch)).Patch();
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
            Root26InputTypeAndGamepadIndexProbe.Sample();
            Root26ActionSetAndOriginsProbe.Sample();
            Root26ActionSetHandleCompareProbe.Sample();
            Root26AnalogDataFieldOffsetProbe.Sample();
            Root26AnalogActionDataCompareProbe.Sample();
            Root26ManagedSelfTraceProbe.Sample();
            Root26AnalogDataSelfSwapProbe.Sample();
            Root26StoredHandleCompareProbe.Sample();
            Root26EntryTimelineProbe.Sample();
            // Chapter 124b (RSTICK DEAD/LIVE re-focus): re-enabled per
            // Chapter115.9's design instruction - Process.Modules is now
            // snapshotted exactly once per session, and the vtableSlot0
            // module/RVA lookup is only recomputed when the target address
            // itself changes (see Root26LastInputIndexVtableTargetProbe.cs).
            Root26LastInputIndexVtableTargetProbe.Sample();
            // Chapter122GuideYawCorrelationProbe.Sample(); // disabled Chapter123 follow-up - see OnInitializeMelon comment above.
            // Chapter123CameraOrientationProbe.Sample(); // disabled Chapter124 follow-up - see OnInitializeMelon comment above.
            Chapter124CameraGainProbe.Sample();
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
