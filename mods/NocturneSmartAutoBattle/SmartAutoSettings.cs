using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NocturneSmartAutoBattle
{
    // Same names and numeric values as Controller's former AutoBattleMode.
    internal enum SmartAutoMode
    {
        NormalAttackOnly = 0,
        SkillPriority = 1
    }

    // Mods\NocturneSmartAutoBattle.settings.json owns Enabled / Mode / Speed,
    // with or without Controller. Without that file, the values come once from
    // Controller's former built-in settings (NocturneModernController.settings.json
    // "SmartAutoEnabled", "AutoBattleMode", "AutoBattleSpeed"); a fresh install
    // without either file starts in Skill Priority, where the mod actually
    // changes what Auto does. Controller's file is never written.
    internal sealed class SmartAutoSettings
    {
        internal const string FileName = "NocturneSmartAutoBattle.settings.json";
        private const string ControllerFileName = "NocturneModernController.settings.json";

        internal static readonly float[] AllowedSpeeds = { 1.0f, 1.5f, 2.0f };

        public bool Enabled { get; set; } = true;
        public SmartAutoMode Mode { get; set; } = SmartAutoMode.SkillPriority;
        public float Speed { get; set; } = 1.0f;

        internal static SmartAutoSettings Current { get; private set; } = new();

        private static readonly JsonSerializerOptions Options = new()
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() }
        };

        private static string ModDirectory =>
            Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? string.Empty;

        // Returns a note for the log, or null.
        internal static string? Load() => Load(ModDirectory);

        internal static string? Load(string directory)
        {
            string path = Path.Combine(directory, FileName);
            try
            {
                if (File.Exists(path))
                {
                    Current = Normalize(JsonSerializer.Deserialize<SmartAutoSettings>(File.ReadAllText(path), Options));
                    return null;
                }

                Current = new SmartAutoSettings();
                string? note = null;
                string controllerPath = Path.Combine(directory, ControllerFileName);
                if (File.Exists(controllerPath))
                {
                    using JsonDocument document = JsonDocument.Parse(File.ReadAllText(controllerPath));
                    JsonElement root = document.RootElement;
                    if (root.TryGetProperty("SmartAutoEnabled", out JsonElement enabled) &&
                        (enabled.ValueKind == JsonValueKind.True || enabled.ValueKind == JsonValueKind.False))
                    {
                        Current.Enabled = enabled.GetBoolean();
                    }
                    if (root.TryGetProperty("AutoBattleMode", out JsonElement mode) &&
                        TryReadMode(mode, out SmartAutoMode legacyMode))
                    {
                        Current.Mode = legacyMode;
                    }
                    else
                    {
                        // Controller's own default when it had no mode stored.
                        Current.Mode = SmartAutoMode.NormalAttackOnly;
                    }
                    if (root.TryGetProperty("AutoBattleSpeed", out JsonElement speed) &&
                        speed.ValueKind == JsonValueKind.Number)
                    {
                        Current.Speed = NormalizeSpeed(speed.GetSingle());
                    }
                    note = $"enabled={Current.Enabled} mode={Current.Mode} speed={FormatSpeed(Current.Speed)} " +
                           $"taken from {ControllerFileName}";
                }
                Save(directory);
                return note;
            }
            catch (Exception exception) when (exception is IOException or JsonException or
                                              UnauthorizedAccessException or InvalidOperationException)
            {
                Current = new SmartAutoSettings();
                return "settings could not be read (" + exception.GetType().Name + "); using defaults";
            }
        }

        internal static void Save() => Save(ModDirectory);

        internal static void Save(string directory)
        {
            string path = Path.Combine(directory, FileName);
            string temporaryPath = path + ".tmp";
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(Current, Options));
            File.Move(temporaryPath, path, overwrite: true);
        }

        // Settings values as the Settings window selectors use them.
        internal static bool TryParseMode(string? value, out SmartAutoMode mode) =>
            Enum.TryParse(value, ignoreCase: false, out mode) && Enum.IsDefined(mode) &&
            !int.TryParse(value, out _);

        internal static bool TryParseSpeed(string? value, out float speed)
        {
            speed = 1.0f;
            if (value == null)
            {
                return false;
            }
            foreach (float allowed in AllowedSpeeds)
            {
                if (value == FormatSpeed(allowed))
                {
                    speed = allowed;
                    return true;
                }
            }
            return false;
        }

        internal static string FormatSpeed(float speed) => speed.ToString("0.0", CultureInfo.InvariantCulture);

        // Controller accepted only 1.5 and 2.0 besides 1.0; anything else is 1.0.
        internal static float NormalizeSpeed(float speed) => speed is 1.5f or 2.0f ? speed : 1.0f;

        private static SmartAutoSettings Normalize(SmartAutoSettings? settings)
        {
            settings ??= new SmartAutoSettings();
            if (!Enum.IsDefined(settings.Mode))
            {
                settings.Mode = SmartAutoMode.SkillPriority;
            }
            settings.Speed = NormalizeSpeed(settings.Speed);
            return settings;
        }

        private static bool TryReadMode(JsonElement element, out SmartAutoMode mode)
        {
            mode = SmartAutoMode.NormalAttackOnly;
            if (element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out int number) &&
                Enum.IsDefined((SmartAutoMode)number))
            {
                mode = (SmartAutoMode)number;
                return true;
            }
            return element.ValueKind == JsonValueKind.String && TryParseMode(element.GetString(), out mode);
        }
    }
}
