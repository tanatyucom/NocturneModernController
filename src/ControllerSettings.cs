using System;
using System.IO;
using System.Reflection;
using System.Text.Json;

namespace NocturneModernController
{
    internal enum RightStickMode
    {
        FullCamera,
        HorizontalTurn
    }

    internal enum AutoBattleMode
    {
        NormalAttackOnly,
        SkillPriority
    }

    internal sealed class ControllerSettings
    {
        internal static ControllerSettings Current { get; } = new ControllerSettings();

        public string UiLanguage { get; set; } = "Auto";
        public RightStickMode RightStickMode { get; set; } = RightStickMode.FullCamera;
        public bool InvertX { get; set; }
        public bool InvertY { get; set; }
        public float SensitivityX { get; set; } = 1.0f;
        public float SensitivityY { get; set; } = 1.0f;
        public float DeadZone { get; set; } = 0.15f;
        public AutoBattleMode AutoBattleMode { get; set; } = AutoBattleMode.NormalAttackOnly;
        public float AutoBattleSpeed { get; set; } = 1.0f;
        public bool RightStickEnabled { get; set; } = true;
        public bool DashEnabled { get; set; } = true;
        public bool QuickHealEnabled { get; set; } = true;
        public bool ForceEncounterEnabled { get; set; } = true;
        public bool SmartAutoEnabled { get; set; } = true;

        public ControllerSettings()
        {
        }

        internal static string SettingsPath => Path.Combine(
            Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? string.Empty,
            "NocturneModernController.settings.json");

        internal static void Load()
        {
            try
            {
                if (!File.Exists(SettingsPath))
                {
                    Save();
                    return;
                }

                ControllerSettings? loaded = JsonSerializer.Deserialize<ControllerSettings>(
                    File.ReadAllText(SettingsPath));
                if (loaded == null)
                {
                    return;
                }

                Current.RightStickMode = loaded.RightStickMode;
                Current.UiLanguage = NormalizeLanguage(loaded.UiLanguage);
                Current.InvertX = loaded.InvertX;
                Current.InvertY = loaded.InvertY;
                Current.SensitivityX = Math.Clamp(loaded.SensitivityX, 0.1f, 3.0f);
                Current.SensitivityY = Math.Clamp(loaded.SensitivityY, 0.1f, 3.0f);
                Current.DeadZone = Math.Clamp(loaded.DeadZone, 0.0f, 0.95f);
                Current.AutoBattleMode = loaded.AutoBattleMode;
                Current.AutoBattleSpeed = loaded.AutoBattleSpeed is 1.5f or 2.0f
                    ? loaded.AutoBattleSpeed
                    : 1.0f;
                Current.RightStickEnabled = loaded.RightStickEnabled;
                Current.DashEnabled = loaded.DashEnabled;
                Current.QuickHealEnabled = loaded.QuickHealEnabled;
                Current.ForceEncounterEnabled = loaded.ForceEncounterEnabled;
                Current.SmartAutoEnabled = loaded.SmartAutoEnabled;
            }
            catch
            {
                // Keep safe defaults when a hand-edited settings file is invalid.
            }
        }

        internal static void Save()
        {
            var options = new JsonSerializerOptions { WriteIndented = true };
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(Current, options));
        }

        internal static bool UseJapanese => Current.UiLanguage.Equals("Japanese", StringComparison.OrdinalIgnoreCase) ||
            (Current.UiLanguage.Equals("Auto", StringComparison.OrdinalIgnoreCase) &&
             System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.Equals("ja", StringComparison.OrdinalIgnoreCase));

        private static string NormalizeLanguage(string? language) => language?.ToLowerInvariant() switch
        {
            "japanese" => "Japanese",
            "english" => "English",
            _ => "Auto"
        };
    }
}
