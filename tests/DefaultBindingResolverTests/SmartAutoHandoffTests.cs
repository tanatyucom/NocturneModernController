using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NocturneModernController;

// Phase 4D: Controller's built-in Smart Auto hands over to the external
// NocturneSmartAutoBattle mod when that mod is present.
internal static class SmartAutoHandoffTests
{
    internal static void Run()
    {
        Detection();
        BuiltInCardHidden();
        EveryBuiltInEntryPointIsGuarded();
    }

    // 1, 2, 8
    private static void Detection()
    {
        Check(!SmartAutoHandoffRules.IsExternalActive(false, new[] { "NocturneModernController", "NocturneQuickHeal", null }),
            "external absent: built-in Smart Auto keeps running");
        Check(SmartAutoHandoffRules.IsExternalActive(true, Array.Empty<string>()),
            "external provider registered: built-in hands over");
        Check(SmartAutoHandoffRules.IsExternalActive(false, new[] { "NocturneModernController", "nocturnesmartautobattle" }),
            "external assembly loaded (even without Controller integration): built-in hands over");
        Check(!SmartAutoHandoffRules.IsExternalActive(false, new[] { "NocturneSmartAutoBattle.ControllerProvider" }),
            "only the mod assembly itself counts");
    }

    // 7
    private static void BuiltInCardHidden()
    {
        FeatureMetadata[] features =
        {
            new() { Id = "right_stick_camera", Name = "Right Stick Camera" },
            new() { Id = "smart_auto", Name = "Smart Auto Battle" }
        };
        Check(SmartAutoHandoffRules.BuiltInFeatures(features, false).Count() == 2, "no external mod: both cards");
        Check(SmartAutoHandoffRules.BuiltInFeatures(features, true).Select(f => f.Id).SequenceEqual(new[] { "right_stick_camera" }),
            "external mod: built-in Smart Auto card hidden, other cards kept");
    }

    // 2-6: every built-in Harmony patch method and per-frame Sample starts
    // with the handover check, so no path (Auto mirror, speed, command,
    // target, learning, logging) keeps running next to the external mod.
    private static void EveryBuiltInEntryPointIsGuarded()
    {
        string root = RepositoryRoot();
        int patchMethods = 0;
        foreach (string file in new[] { "SmartAutoBattleRuntime.cs", "SmartAutoBattleTelemetry.cs" })
        {
            string source = File.ReadAllText(Path.Combine(root, "src", file));
            foreach (Match match in Regex.Matches(source,
                         @"(?:private|internal) static (?:void|bool) (Prefix|Postfix|Sample)\([^)]*\)\s*\{\s*(?<first>[^\r\n]*)"))
            {
                if (match.Groups[1].Value != "Sample")
                {
                    patchMethods++;
                }
                Check(match.Groups["first"].Value.Contains("SmartAutoHandoff.ExternalActive"),
                    $"{file}: {match.Groups[1].Value} does not start with the handover check: {match.Groups["first"].Value.Trim()}");
            }
        }
        Check(patchMethods == 16, "expected the 16 built-in Smart Auto patch methods, found " + patchMethods);
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
            throw new InvalidOperationException("SmartAutoHandoff: " + message);
        }
    }
}
