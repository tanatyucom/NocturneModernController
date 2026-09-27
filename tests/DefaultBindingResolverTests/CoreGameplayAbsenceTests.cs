using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using NocturneModernController;

// Controller Core after the gameplay externalization: Force Encounter, Quick
// Heal, Dash / Dash Keep and Smart Auto Battle live only in their standalone
// mods. Controller keeps no gameplay runtime, patch, action, card or Settings
// tab, but still keeps the legacy settings those mods migrate from.
internal static class CoreGameplayAbsenceTests
{
    // Legacy settings.json values the standalone mods read once on first run.
    private static readonly string[] LegacyFields =
    {
        "ForceEncounterEnabled", "QuickHealEnabled", "DashEnabled", "SmartAutoEnabled", "AutoBattleMode", "AutoBattleSpeed"
    };

    internal static void Run()
    {
        OnlyCoreFeatureCard();
        NoGameplayInControllerBuild();
        OnlyCoreAction();
        LegacySettingsSurviveSave();
        SettingsWindowHasNoAutoBattleTab();
    }

    private static void OnlyCoreFeatureCard()
    {
        var provider = BuiltInFeatureProvider.Instance;
        Check(provider.GetFeatures().Select(feature => feature.Id).SequenceEqual(new[] { "right_stick_camera" }),
            "Controller publishes only the right stick card");
        foreach (string gameplay in new[] { "force_encounter", "quick_heal", "dash", "smart_auto" })
        {
            Check(!provider.SetFeatureEnabled(gameplay, false), "no built-in " + gameplay + " feature to toggle");
        }
    }

    // Nothing compiled into Controller runs gameplay: no encounter, healing,
    // movement speed, battle command, Auto button or battle speed code. Only
    // ControllerSettings may name the legacy migration values.
    private static void NoGameplayInControllerBuild()
    {
        string root = RepositoryRoot();
        string[] compiled = CompiledControllerSources(root).ToArray();
        Check(compiled.Length > 10 && compiled.Contains("ModMain.cs") && compiled.Contains("ControllerSettings.cs"),
            "Controller source list resolved");
        string[] forbidden =
        {
            "ForceEncounter", "nbEncount", "fldEnc", "QuickHeal", "cmpRecover", "FormationFlag", "Dash",
            "SmartAuto", "nbCommSelProcess", "nbTarSelProcess", "nbActionProcess", "nbAutoCommProcess",
            "nbPanelProcess", "nbMainProcess", "BTL_AutoBattle", "timeScale", "Handoff", "HasFeatureProvider",
            "smart-auto-knowledge", "\"smart_auto\"", "\"dash\"", "\"quick_heal\"", "\"force_encounter\""
        };
        foreach (string file in compiled.Where(file => file != "ControllerSettings.cs"))
        {
            string source = File.ReadAllText(Path.Combine(root, "src", file));
            foreach (string symbol in forbidden)
            {
                Check(!source.Contains(symbol, StringComparison.Ordinal), $"{file} still contains {symbol}");
            }
        }
        int patches = compiled.Sum(file =>
            Regex.Matches(File.ReadAllText(Path.Combine(root, "src", file)), @"\[HarmonyPatch\(").Count);
        Check(patches == 3, "Controller keeps 3 patch classes (ExplorationState, NativeRightStickCameraPatch, " +
            "PuzzleLogicalTurnPatch), found " + patches);
        foreach (string file in compiled)
        {
            string source = File.ReadAllText(Path.Combine(root, "src", file));
            Check(!source.Contains("CreateClassProcessor", StringComparison.Ordinal) &&
                  !Regex.IsMatch(source, @"\.Patch\(|PatchAll\("),
                $"{file} registers Harmony patches by hand (MelonLoader applies them once)");
        }
    }

    // Controller registers only its own Open Settings action; saved bindings of
    // the external mods' actions are kept by the binding store, not registered.
    private static void OnlyCoreAction()
    {
        string source = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "BuiltInControllerActions.cs"));
        string[] actionIds = Regex.Matches(source, @"const string \w+ = ""([^""]+)""")
            .Select(match => match.Groups[1].Value).ToArray();
        Check(actionIds.SequenceEqual(new[] { "nocturne-modern-controller.open-settings" }),
            "built-in actions: " + string.Join(", ", actionIds));
        Check(Regex.Matches(source, @"RegisterAction\(").Count == 1, "one built-in action registration");
    }

    // Saving Controller settings keeps the values the standalone mods migrate
    // from, and the Settings window's model keeps them too (it rewrites the
    // whole file).
    private static void LegacySettingsSurviveSave()
    {
        ControllerSettings? loaded = JsonSerializer.Deserialize<ControllerSettings>(
            "{\"ForceEncounterEnabled\": false, \"QuickHealEnabled\": false, \"DashEnabled\": false, " +
            "\"SmartAutoEnabled\": false, \"AutoBattleMode\": 1, \"AutoBattleSpeed\": 1.5, \"RightStickEnabled\": true}");
        string saved = JsonSerializer.Serialize(loaded);
        using JsonDocument document = JsonDocument.Parse(saved);
        JsonElement root = document.RootElement;
        Check(!root.GetProperty("ForceEncounterEnabled").GetBoolean() &&
              !root.GetProperty("QuickHealEnabled").GetBoolean() &&
              !root.GetProperty("DashEnabled").GetBoolean() &&
              !root.GetProperty("SmartAutoEnabled").GetBoolean() &&
              root.GetProperty("AutoBattleMode").GetInt32() == 1 &&
              root.GetProperty("AutoBattleSpeed").GetSingle() == 1.5f,
            "legacy gameplay settings are written back unchanged: " + saved);

        string settingsWindow = File.ReadAllText(Path.Combine(RepositoryRoot(), "settings", "Program.cs"));
        string model = settingsWindow.Substring(settingsWindow.IndexOf("internal sealed class SettingsModel", StringComparison.Ordinal));
        model = model.Substring(0, model.IndexOf("\n}", StringComparison.Ordinal) + 2);
        // Outside the model (and the enum its AutoBattleMode needs) nothing uses them: no UI.
        string rest = settingsWindow.Replace(model, string.Empty)
            .Replace("internal enum AutoBattleMode", string.Empty, StringComparison.Ordinal);
        foreach (string field in LegacyFields)
        {
            Check(Regex.IsMatch(model, @"\b" + field + @" \{ get; set; \}"),
                "Settings window keeps " + field + " so its save does not drop it");
            Check(!Regex.IsMatch(rest, @"\b" + field + @"\b"),
                "Settings window only stores " + field + " (no UI for it)");
        }
    }

    // 10
    private static void SettingsWindowHasNoAutoBattleTab()
    {
        string settingsWindow = File.ReadAllText(Path.Combine(RepositoryRoot(), "settings", "Program.cs"));
        foreach (string gone in new[] { "BuildAutoBattlePage", "オートバトル", "_autoBattleMode", "_autoBattleSpeed", "\"smart_auto\"" })
        {
            Check(!settingsWindow.Contains(gone, StringComparison.Ordinal), "Settings window still contains " + gone);
        }
    }

    // src/*.cs minus the files NocturneModernController.csproj removes from compilation.
    private static IEnumerable<string> CompiledControllerSources(string root)
    {
        string project = File.ReadAllText(Path.Combine(root, "NocturneModernController.csproj"));
        var removed = new HashSet<string>(
            Regex.Matches(project, @"<Compile Remove=""src\\([^""\\]+\.cs)""").Select(match => match.Groups[1].Value),
            StringComparer.OrdinalIgnoreCase);
        return Directory.GetFiles(Path.Combine(root, "src"), "*.cs")
            .Select(Path.GetFileName)
            .Where(name => name != null && !removed.Contains(name))
            .Select(name => name!);
    }

    private static string RepositoryRoot()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "NocturneModernController.csproj")))
            {
                return directory.FullName;
            }
        }
        throw new InvalidOperationException("repository root not found");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException("CoreGameplayAbsence: " + message);
        }
    }
}
