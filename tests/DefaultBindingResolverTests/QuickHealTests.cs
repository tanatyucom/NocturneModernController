using System;
using System.IO;
using System.Linq;
using System.Reflection;
using FakeModernController;
using NocturneQuickHeal;

// Standalone NocturneQuickHeal: sequence rules moved from Controller's
// built-in QuickHealRuntimeProbe, recovery ranking, input routing, the
// optional reflection-bound Controller integration, and the settings file.
internal static class QuickHealTests
{
    private static readonly Assembly Self = typeof(QuickHealTests).Assembly;

    internal static void Run()
    {
        Gates();
        OnePressOneSequence();
        SequenceTimingAndLimit();
        InputRouting();
        DetectionFailureFallsBack();
        OlderControllerStillIntegrates();
        IntegrationRegistersActionAndProvider();
        SettingsPersistenceAndMigration();
        RecoverySourcePriority();
        RecoveryRanking();
    }

    private static bool Sample(QuickHealSequence sequence, bool enabled, bool exploring, bool settingsOpen,
        bool held, int now, out bool started) =>
        sequence.Sample(enabled, exploring, settingsOpen, () => held, now, out started);

    private static void Gates()
    {
        var sequence = new QuickHealSequence();
        Check(!Sample(sequence, false, true, false, true, 0, out bool started) && !started && !sequence.IsActive,
            "disabled: no heal");
        Check(!Sample(sequence, true, false, false, true, 0, out started) && !started && !sequence.IsActive,
            "not exploring: no heal");
        Check(!Sample(sequence, true, true, true, true, 0, out started) && !started && !sequence.IsActive,
            "Settings open: no heal");
        Check(!Sample(sequence, true, true, false, false, 0, out started) && !started && !sequence.IsActive,
            "no input: no heal");
        Check(!sequence.Sample(true, true, false, () => throw new InvalidOperationException(), 0, out started) &&
              !started && !sequence.IsActive,
            "failing input read skips the frame");
    }

    private static void OnePressOneSequence()
    {
        var sequence = new QuickHealSequence();
        Check(Sample(sequence, true, true, false, true, 100, out bool started) && started && sequence.IsActive,
            "press starts a sequence and heals on the same frame");
        sequence.ActionPerformed(100);
        Check(!Sample(sequence, true, true, false, true, 120, out started) && !started,
            "holding the button does not start another sequence");
        sequence.Stop();
        Check(!Sample(sequence, true, true, false, true, 400, out started) && !started && !sequence.IsActive,
            "a finished sequence is not restarted while the button is still held");
        Sample(sequence, true, true, false, false, 500, out _);
        Check(Sample(sequence, true, true, false, true, 600, out started) && started && sequence.ActionCount == 0,
            "release and press starts a new sequence with a fresh action count");

        // Outside the gates nothing changes (Controller did not sample Quick
        // Heal then): a running sequence resumes afterwards, and the held
        // button is not seen as released.
        var paused = new QuickHealSequence();
        Sample(paused, true, true, false, true, 0, out _);
        paused.ActionPerformed(0);
        Check(!Sample(paused, true, false, false, false, 1000, out _) && paused.IsActive,
            "leaving exploration pauses the sequence without resetting it");
        Check(Sample(paused, true, true, false, true, 1000, out started) && !started,
            "back in exploration the same sequence continues; still-held button does not restart");
    }

    private static void SequenceTimingAndLimit()
    {
        var sequence = new QuickHealSequence();
        Sample(sequence, true, true, false, true, 0, out _);
        Check(sequence.ActionPerformed(0), "first action keeps the sequence running");
        Check(!Sample(sequence, true, true, false, true, QuickHealSequence.HealIntervalMilliseconds - 1, out _),
            "next action waits for the 250 ms interval");
        Check(Sample(sequence, true, true, false, true, QuickHealSequence.HealIntervalMilliseconds, out _),
            "next action runs after 250 ms");

        var wrap = new QuickHealSequence();
        Sample(wrap, true, true, false, true, int.MaxValue - 10, out _);
        wrap.ActionPerformed(int.MaxValue - 10);
        Check(Sample(wrap, true, true, false, true, unchecked(int.MaxValue - 10 + QuickHealSequence.HealIntervalMilliseconds), out _),
            "interval survives TickCount wrap-around");

        var limited = new QuickHealSequence();
        Sample(limited, true, true, false, true, 0, out _);
        for (int i = 1; i < QuickHealSequence.MaximumActionsPerSequence; i++)
        {
            Check(limited.ActionPerformed(0), "below the safety limit the sequence continues");
        }
        Check(!limited.ActionPerformed(0) && !limited.IsActive &&
              limited.ActionCount == QuickHealSequence.MaximumActionsPerSequence,
            "the 64th action ends the sequence");
    }

    private static void InputRouting()
    {
        int controllerReads = 0, standaloneReads = 0;
        bool held = QuickHealInput.ReadHeld(true, () => { controllerReads++; return true; }, () => { standaloneReads++; return true; });
        Check(held && controllerReads == 1 && standaloneReads == 0,
            "integration active: only the Controller action is read (no standalone SELECT route)");

        held = QuickHealInput.ReadHeld(false, () => { controllerReads++; return true; }, () => { standaloneReads++; return false; });
        Check(!held && controllerReads == 1 && standaloneReads == 1,
            "integration inactive: only the standalone SELECT button is read");
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
        integration!.Register(() => enabled, value => { enabled = value; return true; }, "0.1.0");

        ControllerActionDefinition action = ModernControllerApi.Actions.Single();
        Check(action.ActionId == "nocturne-modern-controller.quick-heal" && action.ModId == "NocturneQuickHeal" &&
              action.Contexts == ControllerContext.Field && action.Behavior == ControllerActionBehavior.Press &&
              action.DisplayName == "クイックヒール",
            "action registered with the former ActionId, Field, Press and the Japanese name");
        Check(action.DefaultBindings.Single().Context == ControllerContext.Field &&
              action.DefaultBindings.Single().Buttons.SequenceEqual(new[] { ControllerButton.RB }),
            "default binding is Field RB");

        IModernFeatureProvider provider = ModernControllerApi.Provider!;
        Check(provider.ProviderId == "nocturne_quick_heal" && provider.ProviderName == "Nocturne Quick Heal" &&
              provider.Version == "0.1.0", "provider identity");
        FeatureMetadata feature = provider.GetFeatures().Single();
        Check(feature.Id == "quick_heal" && feature.Name == "Quick Heal" && feature.Enabled &&
              feature.SortOrder == 30 && feature.Category == "QoL", "feature card mirrors the built-in one");
        Check(provider.SetFeatureEnabled("quick_heal", false) && !enabled && !provider.GetFeatures().Single().Enabled,
            "Settings toggle reaches the mod's own setting");
        Check(!provider.SetFeatureEnabled("force_encounter", true) && !enabled, "other feature ids are refused");

        ModernControllerApi.Held = true;
        Check(integration.IsHeld(), "Controller action input is read through the integration");
        ModernControllerApi.SettingsOpen = true;
        Check(integration.IsSettingsOpen, "Controller Settings state is read through the integration");
        ModernControllerApi.Held = false;
        ModernControllerApi.SettingsOpen = false;
        ModernControllerApi.Japanese = false;
    }

    private static void SettingsPersistenceAndMigration()
    {
        string directory = Path.Combine(Path.GetTempPath(), "nmc-quick-heal-settings-test");
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, true);
        }
        Directory.CreateDirectory(directory);
        string ownPath = Path.Combine(directory, QuickHealSettings.FileName);
        string controllerPath = Path.Combine(directory, "NocturneModernController.settings.json");
        try
        {
            Check(QuickHealSettings.Load(directory) == null && QuickHealSettings.Current.Enabled && File.Exists(ownPath),
                "no files: enabled by default and the settings file is created");

            File.Delete(ownPath);
            File.WriteAllText(controllerPath, "{ \"QuickHealEnabled\": false, \"ForceEncounterEnabled\": true }");
            string? note = QuickHealSettings.Load(directory);
            Check(note != null && !QuickHealSettings.Current.Enabled && File.Exists(ownPath),
                "first run takes the disabled state from Controller's QuickHealEnabled");
            Check(File.ReadAllText(controllerPath).Contains("\"QuickHealEnabled\": false"),
                "Controller's settings file is not written back");

            QuickHealSettings.Current.Enabled = true;
            QuickHealSettings.Save(directory);
            Check(QuickHealSettings.Load(directory) == null && QuickHealSettings.Current.Enabled,
                "own settings file wins afterwards (Controller still says false) and survives a reload");

            File.WriteAllText(ownPath, "{ not json");
            Check(QuickHealSettings.Load(directory) != null && QuickHealSettings.Current.Enabled,
                "unreadable settings fall back to defaults");
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    private static void RecoverySourcePriority()
    {
        Check(RecoverySource.GetPriority(0, 0x2u) == 2 && RecoverySource.GetPriority(0, 0) == 2,
            "unit 0 (protagonist) is always used last");
        Check(RecoverySource.GetPriority(5, 0) == 0 && RecoverySource.GetKind(0) == "reserve",
            "demon without flag 0x2 is a reserve source, used first");
        Check(RecoverySource.GetPriority(5, 0x2u) == 1 && RecoverySource.GetPriority(5, 0x3u) == 1 &&
              RecoverySource.GetKind(1) == "active",
            "demon with flag 0x2 is an active party source");
        Check(RecoverySource.GetKind(2) == "protagonist", "kind names");
    }

    private static void RecoveryRanking()
    {
        // 100 HP missing: Dia (effect 60, cost 3) needs 2 casts = 6 MP, 20 overheal;
        // Diarama (effect 200, cost 7) needs 1 cast = 7 MP.
        RecoveryRank dia = RecoveryRank.Create(0, 60, 3, 100);
        RecoveryRank diarama = RecoveryRank.Create(0, 200, 7, 100);
        Check(dia.ProjectedCost == 6 && dia.ProjectedOverheal == 20 && diarama.ProjectedCost == 7 &&
              diarama.ProjectedOverheal == 100, "projected cost/overheal cover the missing HP");
        Check(dia.IsBetterThan(diarama) && !diarama.IsBetterThan(dia), "lower projected MP wins");

        RecoveryRank activeCheap = RecoveryRank.Create(1, 60, 0, 100);
        Check(dia.IsBetterThan(activeCheap), "source priority beats cost (reserve before active)");

        RecoveryRank a = RecoveryRank.Create(0, 50, 3, 100);   // 2 casts, 6 MP, 0 overheal
        Check(a.IsBetterThan(dia), "equal projected MP: less overheal wins");

        RecoveryRank b = new RecoveryRank(0, 50, 2, 6, 0);
        Check(b.IsBetterThan(a), "equal projected MP and overheal: cheaper per cast wins");
        RecoveryRank c = new RecoveryRank(0, 70, 2, 6, 0);
        Check(c.IsBetterThan(b) && !b.IsBetterThan(c), "all else equal: larger effect wins");
        Check(!b.IsBetterThan(b), "an identical option is not better (first found is kept)");

        Check(RecoveryRank.Create(0, 500, 4, 1).ProjectedCost == 4, "at least one cast");
        bool overflowed = false;
        try
        {
            RecoveryRank.Create(0, 1, int.MaxValue, 10);
        }
        catch (OverflowException)
        {
            overflowed = true;
        }
        Check(overflowed, "projected cost overflow throws (the skill is skipped)");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException("QuickHeal: " + message);
        }
    }
}
