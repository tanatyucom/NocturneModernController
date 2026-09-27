using System;
using System.IO;
using System.Linq;
using System.Reflection;
using FakeModernController;
using NocturneModernDash;

// Standalone NocturneModernDash: gate and Dash / Dash Keep state moved from
// Controller's built-in FieldDashPatch, input routing, the optional
// reflection-bound Controller integration, and the settings file.
internal static class DashTests
{
    private static readonly Assembly Self = typeof(DashTests).Assembly;

    internal static void Run()
    {
        Gate();
        HoldAndRelease();
        KeepToggle();
        InputRouting();
        DetectionFailureFallsBack();
        OlderControllerStillIntegrates();
        IntegrationRegistersActionsAndProvider();
        SettingsPersistenceAndMigration();
    }

    private static void Gate()
    {
        Check(DashGate.Allows(true, false), "enabled, Settings closed: Dash may act");
        Check(!DashGate.Allows(false, false), "disabled: vanilla speed");
        Check(!DashGate.Allows(true, true), "Settings open: vanilla speed");
    }

    private static void HoldAndRelease()
    {
        var state = new DashState();
        Check(!state.Update(false, false, false, out bool toggled) && !toggled, "no input: vanilla speed");
        Check(state.Update(true, false, false, out _), "LT/RT held: dash");
        Check(!state.Update(false, false, false, out _), "released without Keep: back to vanilla");
        Check(state.Update(false, false, true, out _), "P held: dash");
        Check(!state.Update(false, false, false, out _), "P released: back to vanilla");
    }

    private static void KeepToggle()
    {
        var state = new DashState();
        // LT+RT also counts as a held Dash (both are bound), as before.
        Check(state.Update(true, true, false, out bool toggled) && toggled && state.IsKeepOn,
            "Keep press turns Keep on");
        Check(state.Update(true, true, false, out toggled) && !toggled && state.IsKeepOn,
            "holding the combo over further frames does not toggle again");
        Check(state.Update(false, false, false, out toggled) && !toggled,
            "Keep on: dash continues with nothing held");
        Check(state.Update(false, false, false, out _), "Keep on: still dashing on later frames");
        Check(state.Update(true, true, false, out toggled) && toggled && !state.IsKeepOn,
            "second Keep press turns Keep off (still dashing while LT/RT are held)");
        Check(!state.Update(false, false, false, out _), "Keep off and released: vanilla speed");

        // The gate is checked before Update, so while Dash is disabled or
        // Settings is open the latch is not sampled and survives unchanged.
        var latched = new DashState();
        latched.Update(true, true, false, out _);
        latched.Update(false, false, false, out _);
        Check(latched.IsKeepOn && latched.Update(false, false, false, out _),
            "Keep stays on across updates that are not sampled");
    }

    private static void InputRouting()
    {
        int controllerReads = 0, standaloneReads = 0;
        bool held = DashInput.ReadHeld(true, () => { controllerReads++; return true; }, () => { standaloneReads++; return true; });
        Check(held && controllerReads == 1 && standaloneReads == 0,
            "integration active: only the Controller action is read (no standalone LT/RT route)");

        held = DashInput.ReadHeld(false, () => { controllerReads++; return true; }, () => { standaloneReads++; return false; });
        Check(!held && controllerReads == 1 && standaloneReads == 1,
            "integration inactive: only the standalone LT/RT buttons are read");
    }

    private static void DetectionFailureFallsBack()
    {
        Check(ModernControllerIntegration.TryCreate(null, out string reason) == null && reason.Contains("not installed"),
            "no Controller assembly: standalone");
        Check(ModernControllerIntegration.TryCreate(typeof(object).Assembly, out reason) == null && reason.Contains("not compatible"),
            "assembly without the Controller API: standalone");
        Check(ModernControllerIntegration.TryCreate(Self, out reason, "FakeBrokenController") == null && reason.Contains("IsHeld"),
            "Controller without IsHeld: standalone, reason names the missing member");
    }

    private static void OlderControllerStillIntegrates()
    {
        FakeOldController.ModernControllerApi.Registered = 0;
        ModernControllerIntegration? integration = ModernControllerIntegration.TryCreate(Self, out _, "FakeOldController");
        Check(integration != null && !integration.IsSettingsOpen &&
              !integration.IsHeld(ModernControllerIntegration.DashActionId),
            "Controller without IsSettingsOpen/UseJapaneseUi still integrates; Settings counts as closed");
        integration!.Register(() => true, _ => true, "t");
        Check(FakeOldController.ModernControllerApi.Registered == 3,
            "older Controller receives both actions and the provider");
    }

    private static void IntegrationRegistersActionsAndProvider()
    {
        ModernControllerApi.Actions.Clear();
        ModernControllerApi.Japanese = true;
        ModernControllerIntegration? integration = ModernControllerIntegration.TryCreate(Self, out string reason, "FakeModernController");
        Check(integration != null && reason.Contains("active"), "compatible Controller: integration active");

        bool enabled = true;
        integration!.Register(() => enabled, value => { enabled = value; return true; }, "0.1.0");

        Check(ModernControllerApi.Actions.Count == 2 &&
              ModernControllerApi.Actions.All(action => action.ModId == "NocturneModernDash" &&
                                                        action.Contexts == ControllerContext.Field),
            "two actions registered, owned by NocturneModernDash, Field only");
        ControllerActionDefinition dash = ModernControllerApi.Actions.Single(a => a.ActionId == "nocturne-modern-controller.dash");
        Check(dash.Behavior == ControllerActionBehavior.Hold && dash.DisplayName == "ダッシュ" &&
              dash.DefaultBindings.Count == 2 &&
              dash.DefaultBindings.All(b => b.Context == ControllerContext.Field) &&
              dash.DefaultBindings[0].Buttons.SequenceEqual(new[] { ControllerButton.LT }) &&
              dash.DefaultBindings[1].Buttons.SequenceEqual(new[] { ControllerButton.RT }),
            "Dash keeps its ActionId, Hold, and the Field LT / Field RT defaults");
        ControllerActionDefinition keep = ModernControllerApi.Actions.Single(a => a.ActionId == "nocturne-modern-controller.dash-keep");
        Check(keep.Behavior == ControllerActionBehavior.Press && keep.DisplayName == "ダッシュ固定切替" &&
              keep.DefaultBindings.Single().Buttons.SequenceEqual(new[] { ControllerButton.LT, ControllerButton.RT }),
            "Dash Keep keeps its ActionId, Press, and the Field LT+RT default");

        IModernFeatureProvider provider = ModernControllerApi.Provider!;
        Check(provider.ProviderId == "nocturne_modern_dash" && provider.ProviderName == "Nocturne Modern Dash" &&
              provider.Version == "0.1.0", "provider identity");
        FeatureMetadata feature = provider.GetFeatures().Single();
        Check(feature.Id == "dash" && feature.Name == "Dash" && feature.Enabled &&
              feature.SortOrder == 20 && feature.Category == "QoL", "one Dash card, mirroring the built-in one");
        Check(provider.SetFeatureEnabled("dash", false) && !enabled && !provider.GetFeatures().Single().Enabled,
            "Settings toggle reaches the mod's own setting");
        Check(!provider.SetFeatureEnabled("dash-keep", true) && !enabled, "other feature ids are refused");

        ModernControllerApi.Held = true;
        Check(integration.IsHeld(ModernControllerIntegration.DashActionId) &&
              integration.IsHeld(ModernControllerIntegration.DashKeepActionId),
            "both Controller actions are read through the integration");
        ModernControllerApi.SettingsOpen = true;
        Check(integration.IsSettingsOpen, "Controller Settings state is read through the integration");
        ModernControllerApi.Held = false;
        ModernControllerApi.SettingsOpen = false;
        ModernControllerApi.Japanese = false;
    }

    private static void SettingsPersistenceAndMigration()
    {
        string directory = Path.Combine(Path.GetTempPath(), "nmc-dash-settings-test");
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, true);
        }
        Directory.CreateDirectory(directory);
        string ownPath = Path.Combine(directory, DashSettings.FileName);
        string controllerPath = Path.Combine(directory, "NocturneModernController.settings.json");
        try
        {
            Check(DashSettings.Load(directory) == null && DashSettings.Current.Enabled && File.Exists(ownPath),
                "no files: enabled by default and the settings file is created");
            Check(!File.ReadAllText(ownPath).Contains("Keep"), "Dash Keep is not persisted");

            File.Delete(ownPath);
            File.WriteAllText(controllerPath, "{ \"DashEnabled\": false, \"QuickHealEnabled\": true }");
            string? note = DashSettings.Load(directory);
            Check(note != null && !DashSettings.Current.Enabled && File.Exists(ownPath),
                "first run takes the disabled state from Controller's DashEnabled");
            Check(File.ReadAllText(controllerPath).Contains("\"DashEnabled\": false"),
                "Controller's settings file is not written back");

            DashSettings.Current.Enabled = true;
            DashSettings.Save(directory);
            Check(DashSettings.Load(directory) == null && DashSettings.Current.Enabled,
                "own settings file wins afterwards (Controller still says false) and survives a reload");

            File.WriteAllText(ownPath, "{ not json");
            Check(DashSettings.Load(directory) != null && DashSettings.Current.Enabled,
                "unreadable settings fall back to defaults");
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException("Dash: " + message);
        }
    }
}
