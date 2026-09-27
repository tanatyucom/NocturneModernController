using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using FakeModernController;
using NocturneSmartAutoBattle;

// Standalone NocturneSmartAutoBattle: settings and migration, the Enabled /
// Mode gates, the Skill Priority decision moved from Controller's built-in
// SmartAutoBattleTelemetry, pending targets, battle speed, knowledge, and the
// optional reflection-bound Controller integration with Mode / Speed values.
internal static class SmartAutoBattleTests
{
    private static readonly Assembly Self = typeof(SmartAutoBattleTests).Assembly;

    internal static void Run()
    {
        NewInstallDefaults();
        LegacyMigration();
        ExternalSettingsAuthoritative();
        UnreadableSettingsFallBack();
        DisabledIsFullNoOp();
        NormalAttackOnlyKeepsNativeAuto();
        DecisionAttack();
        DecisionSkill();
        DecisionSingleTarget();
        DecisionPass();
        DecisionWithoutSource();
        DecisionCallOrderMatchesBuiltIn();
        PendingTarget();
        SpeedOwnershipAndRestore();
        KnowledgeCopiedOnceFromController();
        IntegrationPublishesToggleAndSelectors();
        ModeValues();
        SpeedValues();
        ValueChangesDoNotAlterEnabled();
        OlderControllerGetsToggleOnly();
        ControllerUnavailableIsStandalone();
    }

    // 1
    private static void NewInstallDefaults()
    {
        InTemp(directory =>
        {
            Check(SmartAutoSettings.Load(directory) == null, "fresh install needs no migration note");
            SmartAutoSettings current = SmartAutoSettings.Current;
            Check(current.Enabled && current.Mode == SmartAutoMode.SkillPriority && current.Speed == 1.0f,
                "fresh install: Enabled=true, Mode=SkillPriority, Speed=1.0");
            string saved = File.ReadAllText(Path.Combine(directory, SmartAutoSettings.FileName));
            Check(saved.Contains("\"SkillPriority\"") && saved.Contains("\"Speed\": 1"),
                "defaults are written, Mode by name: " + saved);
        });
    }

    // 2
    private static void LegacyMigration()
    {
        InTemp(directory =>
        {
            string controller = Path.Combine(directory, "NocturneModernController.settings.json");
            string legacy = "{\"SmartAutoEnabled\": false, \"AutoBattleMode\": 1, \"AutoBattleSpeed\": 2}";
            File.WriteAllText(controller, legacy);
            string? note = SmartAutoSettings.Load(directory);
            SmartAutoSettings current = SmartAutoSettings.Current;
            Check(!current.Enabled && current.Mode == SmartAutoMode.SkillPriority && current.Speed == 2.0f,
                "legacy SmartAutoEnabled / AutoBattleMode / AutoBattleSpeed are taken over");
            Check(note != null && note.Contains("NocturneModernController.settings.json"), "migration is logged");
            Check(File.ReadAllText(controller) == legacy, "Controller settings are never written");
        });
        InTemp(directory =>
        {
            File.WriteAllText(Path.Combine(directory, "NocturneModernController.settings.json"),
                "{\"SmartAutoEnabled\": true, \"AutoBattleMode\": 0, \"AutoBattleSpeed\": 1.5}");
            SmartAutoSettings.Load(directory);
            Check(SmartAutoSettings.Current.Mode == SmartAutoMode.NormalAttackOnly &&
                  SmartAutoSettings.Current.Speed == 1.5f, "existing Normal Attack Only user keeps it");
        });
        InTemp(directory =>
        {
            File.WriteAllText(Path.Combine(directory, "NocturneModernController.settings.json"),
                "{\"QuickHealEnabled\": true, \"AutoBattleSpeed\": 1.7}");
            SmartAutoSettings.Load(directory);
            SmartAutoSettings current = SmartAutoSettings.Current;
            Check(current.Enabled && current.Mode == SmartAutoMode.NormalAttackOnly && current.Speed == 1.0f,
                "Controller file without Smart Auto keys: Controller's own defaults, invalid speed -> 1.0");
        });
    }

    // 3
    private static void ExternalSettingsAuthoritative()
    {
        InTemp(directory =>
        {
            string controller = Path.Combine(directory, "NocturneModernController.settings.json");
            File.WriteAllText(controller, "{\"SmartAutoEnabled\": true, \"AutoBattleMode\": 0, \"AutoBattleSpeed\": 1}");
            SmartAutoSettings.Load(directory);
            SmartAutoSettings.Current.Mode = SmartAutoMode.SkillPriority;
            SmartAutoSettings.Current.Speed = 2.0f;
            SmartAutoSettings.Save(directory);
            File.WriteAllText(controller, "{\"SmartAutoEnabled\": false, \"AutoBattleMode\": 0, \"AutoBattleSpeed\": 1.5}");
            Check(SmartAutoSettings.Load(directory) == null, "no second migration");
            SmartAutoSettings current = SmartAutoSettings.Current;
            Check(current.Enabled && current.Mode == SmartAutoMode.SkillPriority && current.Speed == 2.0f,
                "after migration the external file wins");

            File.WriteAllText(Path.Combine(directory, SmartAutoSettings.FileName),
                "{\"Enabled\": true, \"Mode\": 0, \"Speed\": 3}");
            SmartAutoSettings.Load(directory);
            Check(SmartAutoSettings.Current.Mode == SmartAutoMode.NormalAttackOnly &&
                  SmartAutoSettings.Current.Speed == 1.0f,
                "numeric Mode is accepted; unsupported Speed becomes 1.0");
        });
    }

    private static void UnreadableSettingsFallBack()
    {
        InTemp(directory =>
        {
            File.WriteAllText(Path.Combine(directory, SmartAutoSettings.FileName), "{ not json");
            string? note = SmartAutoSettings.Load(directory);
            Check(note != null && SmartAutoSettings.Current.Enabled &&
                  SmartAutoSettings.Current.Mode == SmartAutoMode.SkillPriority,
                "unreadable settings fall back to defaults");
        });
    }

    // 4
    private static void DisabledIsFullNoOp()
    {
        foreach (SmartAutoMode mode in Enum.GetValues<SmartAutoMode>())
        {
            Check(!SmartAutoPolicy.OverridesCommands(false, mode), "disabled: no command override in " + mode);
        }
        Check(!SmartAutoPolicy.AppliesTargets(false), "disabled: no target override");
        Check(!SmartAutoPolicy.Learns(false), "disabled: no knowledge learning");

        var time = new FakeTimeScale(1.0f);
        var state = new SmartAutoState(_ => { });
        state.OnAutoButton(time);
        for (int frame = 0; frame < 3; frame++)
        {
            state.Sample(false, 2.0f, false, false, time);
        }
        Check(time.Value == 1.0f && time.Writes == 0 && !state.OwnsTimeScale, "disabled: no speed change");

        state.Sample(true, 2.0f, false, false, time);
        Check(time.Value == 2.0f, "enabled again: speed applies");
        state.Sample(false, 2.0f, false, false, time);
        Check(time.Value == 1.0f && !state.OwnsTimeScale, "disabling mid-Auto restores the speed");
    }

    // 5
    private static void NormalAttackOnlyKeepsNativeAuto()
    {
        Check(!SmartAutoPolicy.OverridesCommands(true, SmartAutoMode.NormalAttackOnly),
            "Normal Attack Only never overrides the game's Auto command");
        Check(SmartAutoPolicy.OverridesCommands(true, SmartAutoMode.SkillPriority), "Skill Priority overrides");
        Check(SmartAutoPolicy.AppliesTargets(true) && SmartAutoPolicy.Learns(true),
            "enabled: targets and learning as in the built-in version");
    }

    // 6
    private static void DecisionAttack()
    {
        var view = new FakeBattleView { Source = Source(0, mp: 50, maxMp: 50) };
        view.AddEnemy(0, id: 10, hp: 20, basicDamage: 25);
        SmartAutoRecommendation result = SmartAutoDecision.Evaluate(view, (_, _) => false, _ => { });
        Check(result.Skill == SmartAutoRecommendation.Attack && !result.Single && result.SourceForm == 0,
            "a safe basic attack that kills -> Attack");

        var party = new FakeBattleView { Source = Source(0, mp: 50, maxMp: 50), Order = new[] { 0, 5, 1, 2 } };
        party.AddEnemy(0, id: 10, hp: 30, basicDamage: 20);
        party.BasicDamageByActor[1] = 20;
        SmartAutoRecommendation forecast = SmartAutoDecision.Evaluate(party, (_, _) => false, _ => { });
        Check(forecast.Skill == SmartAutoRecommendation.Attack, "party basic attacks in turn order clear -> Attack");
    }

    // 7
    private static void DecisionSkill()
    {
        var view = new FakeBattleView { Source = Source(0, mp: 100, maxMp: 100, 101, 102) };
        view.AddEnemy(0, id: 10, hp: 500, basicDamage: 10);
        view.AddEnemy(1, id: 11, hp: 400, basicDamage: 10);
        view.AddSkill(101, attribute: 1, cost: 10, single: false, damage: 60, weakTo: new[] { 10, 11 });
        view.AddSkill(102, attribute: 2, cost: 5, single: false, damage: 60, weakTo: Array.Empty<int>());
        var known = new HashSet<(int, int)> { (10, 1), (11, 1), (10, 2), (11, 2) };
        SmartAutoRecommendation result = SmartAutoDecision.Evaluate(view, (id, attr) => known.Contains((id, attr)), _ => { });
        Check(result.Skill == 101 && !result.Single && result.TargetForm == -1,
            "known group weakness beats a known-normal skill that is not efficient: " + result.Skill);
    }

    private static void DecisionSingleTarget()
    {
        var view = new FakeBattleView { Source = Source(1, mp: 100, maxMp: 100, 201) };
        view.AddEnemy(0, id: 20, hp: 300, basicDamage: 10);
        view.AddEnemy(2, id: 21, hp: 120, basicDamage: 10);
        view.AddSkill(201, attribute: 3, cost: 8, single: true, damage: 50, weakTo: Array.Empty<int>());
        SmartAutoRecommendation result = SmartAutoDecision.Evaluate(view, (_, _) => false, _ => { });
        Check(result.Skill == 201 && result.Single && result.TargetForm == 6 && result.SourceForm == 1,
            "unknown affinity: single skill explores the lowest-HP enemy (form 6)");
    }

    // 8
    private static void DecisionPass()
    {
        var view = new FakeBattleView { Source = Source(0, mp: 0, maxMp: 50) };
        view.AddEnemy(0, id: 30, hp: 100, basicDamage: 10, basicAffinity: 0x00010000u);
        SmartAutoRecommendation result = SmartAutoDecision.Evaluate(view, (id, attr) => id == 30 && attr == 0, _ => { });
        Check(result.Skill == SmartAutoRecommendation.Pass, "known physical Null and no skill -> Pass");

        SmartAutoRecommendation unknown = SmartAutoDecision.Evaluate(view, (_, _) => false, _ => { });
        Check(unknown.Skill == SmartAutoRecommendation.Attack, "an unknown affinity is not treated as unsafe");
    }

    private static void DecisionWithoutSource()
    {
        var view = new FakeBattleView();
        var log = new List<string>();
        SmartAutoRecommendation result = SmartAutoDecision.Evaluate(view, (_, _) => false, log.Add);
        Check(result.Skill == SmartAutoRecommendation.None && log.Any(line => line.Contains("source not initialized")),
            "uninitialized actor: no recommendation, native Auto");

        var failing = new FakeBattleView { Source = Source(0, mp: 10, maxMp: 10), ThrowOnAisyo = true };
        failing.AddEnemy(0, id: 1, hp: 10, basicDamage: 10);
        Check(SmartAutoDecision.Evaluate(failing, (_, _) => false, _ => { }).Skill == SmartAutoRecommendation.None,
            "a failing native call ends the evaluation with no recommendation");
    }

    // The native calls, in order, that the built-in DumpSkillCandidates made.
    private static void DecisionCallOrderMatchesBuiltIn()
    {
        var view = new FakeBattleView { Source = Source(0, mp: 40, maxMp: 40, 300) };
        view.AddEnemy(0, id: 40, hp: 999, basicDamage: 5);
        view.AddSkill(300, attribute: 1, cost: 4, single: true, damage: 30, weakTo: Array.Empty<int>());
        SmartAutoDecision.Evaluate(view, (_, _) => false, _ => { });
        string[] expected =
        {
            "Aisyo(0,4,0)", "Buturi(0,0,4,0)", "Ritu(0,0,4)",
            "Aisyo(0,4,0)", "Buturi(0,0,4,0)", "Ritu(0,0,4)",
            "Use(0,300)", "Cost(0,300)", "SkillCost(300)", "Single(300)", "Attr(300)", "HpN(300)",
            "Ritu(300,0,4)", "Aisyo(300,4,1)", "Ritu(0,0,4)", "Buturi(300,0,4,7)", "Magic(300,0,4,7)", "Buturi(0,0,4,0)"
        };
        Check(view.Calls.SequenceEqual(expected), "native call order changed: " + string.Join(" ", view.Calls));
    }

    private static void PendingTarget()
    {
        var pending = new PendingSingleTarget();
        pending.Set(201, 6);
        Check(pending.Form == 6 && pending.Mask == 0x40, "pending mask is 1 << form");
        Check(!pending.TryTake(2, 201, out _, out _), "non-skill command does not take the target");
        Check(!pending.TryTake(1, 999, out _, out _), "another skill does not take the target");
        Check(pending.TryTake(1, 201, out uint mask, out int form) && mask == 0x40 && form == 6, "pending skill takes it");
        Check(pending.Form == -1 && !pending.TryTake(1, 201, out _, out _), "applied once");

        pending.Set(5, -1);
        Check(pending.Mask == 0 && !pending.TryTake(1, 5, out _, out _), "no target form -> nothing to apply");

        Check(PendingSingleTarget.FindCursorIndex(new[] { 4, 6, 5 }, 3, 6) == 1, "form-number list");
        Check(PendingSingleTarget.FindCursorIndex(new[] { 0, 2, 1 }, 3, 6) == 1, "slot-number list (+4)");
        Check(PendingSingleTarget.FindCursorIndex(new[] { 4, 5 }, 3, 7) == -1, "missing enemy -> native target");
        Check(PendingSingleTarget.FindCursorIndex(new[] { 4, 6 }, 1, 6) == -1, "only the first count entries count");
    }

    private static void SpeedOwnershipAndRestore()
    {
        var log = new List<string>();
        var time = new FakeTimeScale(1.25f);
        var state = new SmartAutoState(log.Add);
        state.Sample(true, 2.0f, false, false, time);
        Check(time.Writes == 0, "Auto off: no speed change");

        state.OnAutoButton(time);
        state.Sample(true, 2.0f, false, false, time);
        Check(Math.Abs(time.Value - 2.5f) < 0.0001f && state.OwnsTimeScale, "baseline x speed while Auto runs");
        int writes = time.Writes;
        state.Sample(true, 2.0f, false, false, time);
        Check(time.Writes == writes, "no rewrite while already at the target speed");

        state.OnAutoButton(time);
        Check(time.Value == 1.25f && !state.AutoActive, "Auto button off restores the baseline");

        foreach (string reason in new[] { "exploration", "settings", "speed1", "shutdown", "battle" })
        {
            var t = new FakeTimeScale(1.0f);
            var s = new SmartAutoState(_ => { });
            s.OnAutoButton(t);
            s.Sample(true, 1.5f, false, false, t);
            Check(t.Value == 1.5f, "speed applied before " + reason);
            switch (reason)
            {
                case "exploration": s.Sample(true, 1.5f, true, false, t); break;
                case "settings": s.Sample(true, 1.5f, false, true, t); break;
                case "speed1": s.Sample(true, 1.0f, false, false, t); break;
                case "shutdown": s.Shutdown(t); break;
                case "battle": s.SetAutoActive(false, "battle shutdown", t); break;
            }
            Check(t.Value == 1.0f && !s.OwnsTimeScale, "restored on " + reason);
        }
        Check(log.Contains("Smart Auto speed enabled: 2.0x.") && log.Contains("Smart Auto speed restored."),
            "speed changes are logged");
    }

    private static void KnowledgeCopiedOnceFromController()
    {
        InTemp(directory =>
        {
            string legacy = Path.Combine(directory, "NocturneModernController.smart-auto-knowledge.json");
            File.WriteAllText(legacy, "{\"97\": [0, 1, 3], \"137\": [3]}");
            var knowledge = new SmartAutoKnowledge(directory, _ => { });
            knowledge.Load();
            Check(knowledge.DemonCount == 2 && knowledge.IsKnown(97, 1) && !knowledge.IsKnown(97, 2),
                "Controller knowledge is carried over");
            Check(File.Exists(Path.Combine(directory, SmartAutoKnowledge.FileName)), "copied to the mod's own file");

            knowledge.Learn(97, 2);
            knowledge.LearnAll(500);
            File.WriteAllText(legacy, "{}");
            var reloaded = new SmartAutoKnowledge(directory, _ => { });
            reloaded.Load();
            Check(reloaded.IsKnown(97, 2) && reloaded.IsKnown(500, 6) && reloaded.IsKnown(137, 3),
                "own file is authoritative after the copy");
            Check(File.ReadAllText(legacy) == "{}", "Controller knowledge file is not written");
            knowledge.Learn(0, 1);
            knowledge.Learn(5, 7);
            Check(!knowledge.IsKnown(0, 1) && !knowledge.IsKnown(5, 7), "invalid ids / attributes are ignored");
        });
    }

    // 14 + 15
    private static void IntegrationPublishesToggleAndSelectors()
    {
        ModernControllerIntegration integration = Integrate(out var settings);
        Check(integration.SupportsValues, "Controller with value support gets selectors");
        object provider = ModernControllerApi.Provider!;
        Check(provider is IModernFeatureProvider && provider is IModernFeatureValueProvider,
            "one provider is both a feature provider and a value provider");

        FeatureMetadata[] features = ((IModernFeatureProvider)provider).GetFeatures().ToArray();
        Check(features.Select(f => f.Id).SequenceEqual(new[] { "smart_auto", "smart_auto_mode", "smart_auto_speed" }),
            "three cards: toggle, Mode, Speed");
        Check(features[0].AllowedValues == null && features[0].Enabled, "Smart Auto card is the on/off toggle");
        Check(features[1].AllowedValues!.SequenceEqual(new[] { "NormalAttackOnly", "SkillPriority" }) &&
              features[1].Value == "SkillPriority", "Mode selector values");
        Check(features[1].AllowedValueLabels!["NormalAttackOnly"] == "Normal Attack Only" &&
              features[1].AllowedValueLabels!["SkillPriority"] == "Skill Priority", "Mode labels (English)");
        Check(features[2].AllowedValues!.SequenceEqual(new[] { "1.0", "1.5", "2.0" }) && features[2].Value == "1.0",
            "Speed selector values");
        Check(features[2].AllowedValueLabels!["1.5"] == "x1.5" && features[2].AllowedValueLabels!["2.0"] == "x2.0",
            "Speed labels");
        Check(((IModernFeatureProvider)provider).ProviderId == "nocturne_smart_auto_battle", "provider id");

        ModernControllerApi.Japanese = true;
        FeatureMetadata[] japanese = ((IModernFeatureProvider)provider).GetFeatures().ToArray();
        ModernControllerApi.Japanese = false;
        Check(japanese[1].AllowedValueLabels!["SkillPriority"] == "スキル優先" &&
              japanese[1].AllowedValueLabels!["NormalAttackOnly"] == "通常攻撃のみ", "Mode labels (Japanese)");
    }

    // 9 + 10
    private static void ModeValues()
    {
        Integrate(out var settings);
        var values = (IModernFeatureValueProvider)ModernControllerApi.Provider!;
        Check(values.SetFeatureValue("smart_auto_mode", "NormalAttackOnly") &&
              settings.Current.Mode == SmartAutoMode.NormalAttackOnly, "Mode -> Normal Attack Only");
        Check(values.SetFeatureValue("smart_auto_mode", "SkillPriority") &&
              settings.Current.Mode == SmartAutoMode.SkillPriority, "Mode -> Skill Priority");
        foreach (string invalid in new[] { "skillpriority", "1", "0", "", "Always" })
        {
            Check(!values.SetFeatureValue("smart_auto_mode", invalid) &&
                  settings.Current.Mode == SmartAutoMode.SkillPriority, "invalid Mode rejected: '" + invalid + "'");
        }
        Check(settings.Saves == 2, "only accepted values are saved");
    }

    // 11 + 12
    private static void SpeedValues()
    {
        Integrate(out var settings);
        var values = (IModernFeatureValueProvider)ModernControllerApi.Provider!;
        foreach ((string raw, float speed) in new[] { ("1.5", 1.5f), ("2.0", 2.0f), ("1.0", 1.0f) })
        {
            Check(values.SetFeatureValue("smart_auto_speed", raw) && settings.Current.Speed == speed, "Speed " + raw);
        }
        foreach (string invalid in new[] { "1", "2", "1.50", "3.0", "x2.0", "", "1,5" })
        {
            Check(!values.SetFeatureValue("smart_auto_speed", invalid) && settings.Current.Speed == 1.0f,
                "invalid Speed rejected: '" + invalid + "'");
        }
        Check(!values.SetFeatureValue("smart_auto", "SkillPriority") && !values.SetFeatureValue("other", "1.0"),
            "values only for the Mode / Speed features");
    }

    // 13
    private static void ValueChangesDoNotAlterEnabled()
    {
        Integrate(out var settings);
        var provider = (IModernFeatureProvider)ModernControllerApi.Provider!;
        var values = (IModernFeatureValueProvider)provider;
        values.SetFeatureValue("smart_auto_mode", "NormalAttackOnly");
        values.SetFeatureValue("smart_auto_speed", "2.0");
        Check(settings.Current.Enabled, "value changes keep Enabled");

        // An older Controller routed value requests to SetFeatureEnabled(id, false).
        Check(!provider.SetFeatureEnabled("smart_auto_mode", false) &&
              !provider.SetFeatureEnabled("smart_auto_speed", false) && settings.Current.Enabled,
            "Mode / Speed cards are not on/off features");
        Check(provider.SetFeatureEnabled("smart_auto", false) && !settings.Current.Enabled &&
              settings.Current.Mode == SmartAutoMode.NormalAttackOnly && settings.Current.Speed == 2.0f,
            "turning Smart Auto off keeps Mode and Speed");
    }

    private static void OlderControllerGetsToggleOnly()
    {
        FakeOldController.ModernControllerApi.Registered = 0;
        ModernControllerIntegration? integration =
            ModernControllerIntegration.TryCreate(Self, out string reason, "FakeOldController");
        Check(integration != null && !integration.SupportsValues && reason.Contains("selectors unavailable"),
            "older Controller: toggle only: " + reason);
        integration!.Register(new FakeSettingsAccess(), "test");
        Check(FakeOldController.ModernControllerApi.Registered == 1, "provider registered, no actions");
    }

    // 16
    private static void ControllerUnavailableIsStandalone()
    {
        // Smart Auto needs no action API, so a Controller without IsHeld still integrates.
        Check(ModernControllerIntegration.TryCreate(Self, out string reason, "FakeBrokenController") != null,
            "a Controller without IsHeld is enough for Smart Auto");
        Check(ModernControllerIntegration.TryCreate(null, out reason) == null &&
              reason.Contains("not installed"), "no Controller: standalone");
        Check(ModernControllerIntegration.TryCreate(Self, out reason, "FakeMissingController") == null &&
              reason.Contains("not compatible"), "Controller without the provider API: standalone");
    }

    private static ModernControllerIntegration Integrate(out FakeSettingsAccess settings)
    {
        ModernControllerApi.Provider = null;
        ModernControllerApi.Actions.Clear();
        ModernControllerIntegration? integration =
            ModernControllerIntegration.TryCreate(Self, out string reason, "FakeModernController");
        Check(integration != null, "fake Controller integrates: " + reason);
        settings = new FakeSettingsAccess();
        integration!.Register(settings, "test");
        Check(ModernControllerApi.Actions.Count == 0, "Smart Auto registers no Controller action");
        return integration;
    }

    private static SmartAutoSource Source(int form, int mp, int maxMp, params int[] skills) => new()
    {
        FormIndex = form, UnitId = 1, Hp = 100, MaxHp = 100, Mp = mp, MaxMp = maxMp, Skills = skills
    };

    private static void InTemp(Action<string> body)
    {
        string directory = Path.Combine(Path.GetTempPath(), "nsab-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            body(directory);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    private sealed class FakeSettingsAccess : ISmartAutoSettingsAccess
    {
        internal int Saves;
        public SmartAutoSettings Current { get; } = new();
        public bool SetEnabled(bool enabled) { Current.Enabled = enabled; Saves++; return true; }
        public bool SetMode(SmartAutoMode mode) { Current.Mode = mode; Saves++; return true; }
        public bool SetSpeed(float speed) { Current.Speed = speed; Saves++; return true; }
    }

    private sealed class FakeTimeScale : ITimeScale
    {
        private float _value;
        internal int Writes;
        internal FakeTimeScale(float value) => _value = value;
        public float Value { get => _value; set { _value = value; Writes++; } }
    }

    // A small battle: enemies by slot, per-skill damage, known weaknesses.
    private sealed class FakeBattleView : ISmartAutoBattleView
    {
        private const uint Normal = 150;
        private const uint Weak = 0x80000000u | 150;
        private readonly SmartAutoEnemy?[] _enemies = new SmartAutoEnemy?[4];
        private readonly Dictionary<int, int> _basicDamage = new();
        private readonly Dictionary<int, uint> _basicAffinity = new();
        private readonly Dictionary<int, (int Attribute, int Cost, bool Single, int Damage, HashSet<int> WeakTo)> _skills = new();
        internal readonly Dictionary<int, int> BasicDamageByActor = new();
        internal readonly List<string> Calls = new();
        internal SmartAutoSource? Source;
        internal int[] Order = Array.Empty<int>();
        internal bool ThrowOnAisyo;

        internal void AddEnemy(int slot, int id, int hp, int basicDamage, uint basicAffinity = Normal)
        {
            _enemies[slot] = new SmartAutoEnemy { Id = id, Hp = hp };
            _basicDamage[slot + 4] = basicDamage;
            _basicAffinity[slot + 4] = basicAffinity;
        }

        internal void AddSkill(int id, int attribute, int cost, bool single, int damage, int[] weakTo) =>
            _skills[id] = (attribute, cost, single, damage, new HashSet<int>(weakTo));

        public bool TryGetSource(out SmartAutoSource source)
        {
            source = Source ?? new SmartAutoSource();
            return Source != null;
        }

        public int EnemySlotCount => _enemies.Length;
        public SmartAutoEnemy? GetEnemy(int enemyIndex) => _enemies[enemyIndex];
        public int OrderIndex => 0;
        public int OrderLength => Order.Length;
        public int OrderAt(int index) => Order[index];

        public uint GetAisyo(int skill, int formIndex, int attribute)
        {
            Calls.Add($"Aisyo({skill},{formIndex},{attribute})");
            if (ThrowOnAisyo) throw new InvalidOperationException("native failure");
            if (skill == 0) return _basicAffinity[formIndex];
            int demon = _enemies[formIndex - 4]!.Id;
            return _skills[skill].WeakTo.Contains(demon) ? Weak : Normal;
        }

        public float GetAisyoRitu(int skill, int sourceForm, int destinationForm)
        {
            Calls.Add($"Ritu({skill},{sourceForm},{destinationForm})");
            return 1.0f;
        }

        public int GetButuriAttack(int skill, int sourceForm, int destinationForm, int hpN)
        {
            Calls.Add($"Buturi({skill},{sourceForm},{destinationForm},{hpN})");
            if (skill != 0) return _skills[skill].Damage;
            return sourceForm == 0 || !BasicDamageByActor.TryGetValue(sourceForm, out int damage)
                ? _basicDamage[destinationForm]
                : damage;
        }

        public int GetMagicAttack(int skill, int sourceForm, int destinationForm, int hpN)
        {
            Calls.Add($"Magic({skill},{sourceForm},{destinationForm},{hpN})");
            return _skills[skill].Damage;
        }

        public int CheckSkillUse(int sourceForm, int skill) { Calls.Add($"Use({sourceForm},{skill})"); return 0; }
        public int CheckSkillCost(int sourceForm, int skill) { Calls.Add($"Cost({sourceForm},{skill})"); return 0; }
        public int GetSkillCost(int skill) { Calls.Add($"SkillCost({skill})"); return _skills[skill].Cost; }
        public int CheckSingleTargetSkill(int skill) { Calls.Add($"Single({skill})"); return _skills[skill].Single ? 1 : 0; }
        public int GetNormalSkillAttr(int skill) { Calls.Add($"Attr({skill})"); return _skills[skill].Attribute; }
        public int GetSkillHpN(int skill) { Calls.Add($"HpN({skill})"); return 7; }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException("SmartAuto: " + message);
        }
    }
}
