using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using NocturneModernController;

// Phase 4E: Smart Auto Battle lives only in NocturneSmartAutoBattle. Controller
// keeps no Smart Auto runtime, patch, card or Settings tab, but still keeps the
// legacy settings the mod migrates from.
internal static class CoreSmartAutoRemovalTests
{
    internal static void Run()
    {
        NoBuiltInSmartAutoCard();
        NoSmartAutoRuntimeInControllerBuild();
        LegacySettingsSurviveSave();
        SettingsWindowHasNoAutoBattleTab();
    }

    // 1
    private static void NoBuiltInSmartAutoCard()
    {
        var provider = BuiltInFeatureProvider.Instance;
        Check(provider.GetFeatures().Select(feature => feature.Id).SequenceEqual(new[] { "right_stick_camera" }),
            "Controller publishes only the right stick card");
        Check(!provider.SetFeatureEnabled("smart_auto", false), "no built-in smart_auto feature to toggle");
    }

    // 2, 3, 8: nothing compiled into Controller touches battle commands, the
    // Auto button or the battle speed; the game's Auto runs unchanged.
    private static void NoSmartAutoRuntimeInControllerBuild()
    {
        string root = RepositoryRoot();
        string[] compiled = CompiledControllerSources(root).ToArray();
        Check(compiled.Length > 10 && compiled.Contains("ModMain.cs") && compiled.Contains("ControllerSettings.cs"),
            "Controller source list resolved");
        string[] forbidden =
        {
            "SmartAutoBattleRuntime", "SmartAutoBattleTelemetry", "SmartAutoKnowledgeStore", "SmartAutoHandoff",
            "nbCommSelProcess", "nbTarSelProcess", "nbActionProcess", "nbAutoCommProcess", "nbPanelProcess",
            "nbMainProcess", "BTL_AutoBattle", "timeScale", "smart-auto-knowledge", "\"smart_auto\""
        };
        foreach (string file in compiled)
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
    }

    // 4: saving Controller settings keeps the values NocturneSmartAutoBattle
    // migrates from.
    private static void LegacySettingsSurviveSave()
    {
        ControllerSettings? loaded = JsonSerializer.Deserialize<ControllerSettings>(
            "{\"AutoBattleMode\": 1, \"AutoBattleSpeed\": 1.5, \"SmartAutoEnabled\": false, \"RightStickEnabled\": true}");
        string saved = JsonSerializer.Serialize(loaded);
        using JsonDocument document = JsonDocument.Parse(saved);
        JsonElement root = document.RootElement;
        Check(root.GetProperty("AutoBattleMode").GetInt32() == 1 &&
              root.GetProperty("AutoBattleSpeed").GetSingle() == 1.5f &&
              !root.GetProperty("SmartAutoEnabled").GetBoolean(),
            "legacy Smart Auto settings are written back unchanged: " + saved);

        string settingsWindow = File.ReadAllText(Path.Combine(RepositoryRoot(), "settings", "Program.cs"));
        string model = settingsWindow.Substring(settingsWindow.IndexOf("internal sealed class SettingsModel", StringComparison.Ordinal));
        model = model.Substring(0, model.IndexOf("\n}", StringComparison.Ordinal) + 2);
        foreach (string property in new[] { "AutoBattleMode AutoBattleMode", "float AutoBattleSpeed", "bool SmartAutoEnabled" })
        {
            Check(model.Contains(property, StringComparison.Ordinal),
                "Settings window keeps " + property + " so its save does not drop it");
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
            throw new InvalidOperationException("CoreSmartAutoRemoval: " + message);
        }
    }
}
