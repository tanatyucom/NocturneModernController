using System;
using System.IO;
using System.Linq;
using System.Reflection;
using FakeModernController;
using NocturneForceEncounter;

// Standalone NocturneForceEncounter: input routing, the optional
// reflection-bound Controller integration, and the settings file.
internal static class ForceEncounterStandaloneTests
{
    private static readonly Assembly Self = typeof(ForceEncounterStandaloneTests).Assembly;

    internal static void Run()
    {
        StandaloneRequests();
        InputRouting();
        DetectionFailureFallsBack();
        OlderControllerStillIntegrates();
        IntegrationRegistersActionAndProvider();
        SettingsPersistence();
    }

    // Without Controller: the game's X button drives the same request rules.
    private static void StandaloneRequests()
    {
        var logic = new ForceEncounterLogic();
        bool Held(bool x) => ForceEncounterInput.ReadHeld(false, () => throw new InvalidOperationException(), () => x);
        Check(logic.Sample(true, true, false, Held(true), 0) == ForceEncounterEvent.Requested, "standalone X requests");

        var outside = new ForceEncounterLogic();
        Check(outside.Sample(true, false, false, Held(true), 0) == ForceEncounterEvent.None, "standalone: no request outside exploration");
        var disabled = new ForceEncounterLogic();
        Check(disabled.Sample(false, true, false, Held(true), 0) == ForceEncounterEvent.None, "standalone: no request while disabled");
    }

    private static void InputRouting()
    {
        int controllerReads = 0, standaloneReads = 0;
        bool held = ForceEncounterInput.ReadHeld(true, () => { controllerReads++; return true; }, () => { standaloneReads++; return true; });
        Check(held && controllerReads == 1 && standaloneReads == 0,
            "integration active: only the Controller action is read (no second route)");

        held = ForceEncounterInput.ReadHeld(false, () => { controllerReads++; return true; }, () => { standaloneReads++; return false; });
        Check(!held && controllerReads == 1 && standaloneReads == 1,
            "integration inactive: only the standalone X button is read");
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
        ModernControllerIntegration? integration = ModernControllerIntegration.TryCreate(Self, out _, "FakeOldController");
        Check(integration != null && !integration.IsSettingsOpen && !integration.IsHeld(),
            "Controller without IsSettingsOpen/UseJapaneseUi still integrates; Settings counts as closed");
        integration!.Register(() => true, _ => true, "t");
        Check(FakeOldController.ModernControllerApi.Registered == 2, "older Controller receives the action and the provider");
    }

    private static void IntegrationRegistersActionAndProvider()
    {
        ModernControllerApi.Actions.Clear();
        ModernControllerApi.Japanese = true;
        ModernControllerIntegration? integration = ModernControllerIntegration.TryCreate(Self, out string reason, "FakeModernController");
        Check(integration != null && reason.Contains("active"), "compatible Controller: integration active");

        bool enabled = true;
        integration!.Register(() => enabled, value => { enabled = value; return true; }, "0.2.0");

        ControllerActionDefinition action = ModernControllerApi.Actions.Single();
        Check(action.ActionId == "nocturne-modern-controller.force-encounter" && action.ModId == "NocturneForceEncounter" &&
              action.Contexts == ControllerContext.Field && action.Behavior == ControllerActionBehavior.Press &&
              action.DisplayName == "強制エンカウント",
            "action registered with the former ActionId, Field, Press and the Japanese name");
        Check(action.DefaultBindings.Single().Context == ControllerContext.Field &&
              action.DefaultBindings.Single().Buttons.SequenceEqual(new[] { ControllerButton.X }),
            "default binding is Field X");

        IModernFeatureProvider provider = ModernControllerApi.Provider!;
        Check(provider.ProviderId == "nocturne_force_encounter" && provider.Version == "0.2.0", "provider identity");
        FeatureMetadata feature = provider.GetFeatures().Single();
        Check(feature.Id == "force_encounter" && feature.Enabled && feature.SortOrder == 40 &&
              feature.Category == "Gameplay Change", "feature card mirrors the built-in one");
        Check(provider.SetFeatureEnabled("force_encounter", false) && !enabled && !provider.GetFeatures().Single().Enabled,
            "Settings toggle reaches the mod's own setting");
        Check(!provider.SetFeatureEnabled("other", true) && !enabled, "unknown feature id is refused");

        ModernControllerApi.Held = true;
        Check(integration.IsHeld(), "Controller action input is read through the integration");
        ModernControllerApi.SettingsOpen = true;
        Check(integration.IsSettingsOpen, "Controller Settings state is read through the integration");
        ModernControllerApi.Held = false;
        ModernControllerApi.SettingsOpen = false;
    }

    private static void SettingsPersistence()
    {
        string directory = Path.Combine(Path.GetTempPath(), "nmc-force-encounter-settings-test");
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, true);
        }
        Directory.CreateDirectory(directory);
        try
        {
            Check(ForceEncounterSettings.Load(directory) == null && ForceEncounterSettings.Current.Enabled &&
                  File.Exists(Path.Combine(directory, ForceEncounterSettings.FileName)),
                "no files: enabled by default and the settings file is created");

            File.Delete(Path.Combine(directory, ForceEncounterSettings.FileName));
            File.WriteAllText(Path.Combine(directory, "NocturneModernController.settings.json"),
                "{ \"ForceEncounterEnabled\": false, \"DashEnabled\": true }");
            string? note = ForceEncounterSettings.Load(directory);
            Check(note != null && !ForceEncounterSettings.Current.Enabled,
                "first run takes the disabled state from Controller's settings");

            ForceEncounterSettings.Current.Enabled = true;
            ForceEncounterSettings.Save(directory);
            Check(ForceEncounterSettings.Load(directory) == null && ForceEncounterSettings.Current.Enabled,
                "own settings file wins afterwards and survives a reload");
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
            throw new InvalidOperationException(message);
        }
    }
}
