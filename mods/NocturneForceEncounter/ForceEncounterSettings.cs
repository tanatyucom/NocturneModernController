using System;
using System.IO;
using System.Reflection;
using System.Text.Json;

namespace NocturneForceEncounter
{
    // Mods\NocturneForceEncounter.settings.json. On first run the enabled
    // state is taken from Controller's former built-in setting
    // (NocturneModernController.settings.json "ForceEncounterEnabled"), so a
    // player who had turned Force Encounter off keeps it off.
    internal sealed class ForceEncounterSettings
    {
        public bool Enabled { get; set; } = true;

        internal static ForceEncounterSettings Current { get; private set; } = new();

        private static string ModDirectory =>
            Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? string.Empty;

        private static string SettingsPath => Path.Combine(ModDirectory, "NocturneForceEncounter.settings.json");

        private static string ControllerSettingsPath =>
            Path.Combine(ModDirectory, "NocturneModernController.settings.json");

        // Returns a note for the log when the value came from Controller's settings.
        internal static string? Load()
        {
            try
            {
                if (File.Exists(SettingsPath))
                {
                    Current = JsonSerializer.Deserialize<ForceEncounterSettings>(File.ReadAllText(SettingsPath)) ?? new();
                    return null;
                }

                Current = new ForceEncounterSettings();
                string? note = null;
                if (File.Exists(ControllerSettingsPath))
                {
                    using JsonDocument document = JsonDocument.Parse(File.ReadAllText(ControllerSettingsPath));
                    if (document.RootElement.TryGetProperty("ForceEncounterEnabled", out JsonElement value) &&
                        (value.ValueKind == JsonValueKind.True || value.ValueKind == JsonValueKind.False))
                    {
                        Current.Enabled = value.GetBoolean();
                        note = $"enabled={Current.Enabled} taken from NocturneModernController.settings.json";
                    }
                }
                Save();
                return note;
            }
            catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
            {
                Current = new ForceEncounterSettings();
                return "settings could not be read (" + exception.GetType().Name + "); using defaults";
            }
        }

        internal static void Save()
        {
            string temporaryPath = SettingsPath + ".tmp";
            File.WriteAllText(temporaryPath,
                JsonSerializer.Serialize(Current, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporaryPath, SettingsPath, overwrite: true);
        }
    }
}
