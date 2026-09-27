using System;
using System.IO;
using System.Reflection;
using System.Text.Json;

namespace NocturneQuickHeal
{
    // Mods\NocturneQuickHeal.settings.json is the only owner of the enabled
    // state, with or without Controller. On first run the value is taken from
    // Controller's former built-in setting (NocturneModernController.settings.json
    // "QuickHealEnabled") when that file exists, so a player who had turned
    // Quick Heal off keeps it off.
    internal sealed class QuickHealSettings
    {
        internal const string FileName = "NocturneQuickHeal.settings.json";
        private const string ControllerFileName = "NocturneModernController.settings.json";

        public bool Enabled { get; set; } = true;

        internal static QuickHealSettings Current { get; private set; } = new();

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
                    Current = JsonSerializer.Deserialize<QuickHealSettings>(File.ReadAllText(path)) ?? new();
                    return null;
                }

                Current = new QuickHealSettings();
                string? note = null;
                string controllerPath = Path.Combine(directory, ControllerFileName);
                if (File.Exists(controllerPath))
                {
                    using JsonDocument document = JsonDocument.Parse(File.ReadAllText(controllerPath));
                    if (document.RootElement.TryGetProperty("QuickHealEnabled", out JsonElement value) &&
                        (value.ValueKind == JsonValueKind.True || value.ValueKind == JsonValueKind.False))
                    {
                        Current.Enabled = value.GetBoolean();
                        note = $"enabled={Current.Enabled} taken from {ControllerFileName}";
                    }
                }
                Save(directory);
                return note;
            }
            catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
            {
                Current = new QuickHealSettings();
                return "settings could not be read (" + exception.GetType().Name + "); using defaults";
            }
        }

        internal static void Save() => Save(ModDirectory);

        internal static void Save(string directory)
        {
            string path = Path.Combine(directory, FileName);
            string temporaryPath = path + ".tmp";
            File.WriteAllText(temporaryPath,
                JsonSerializer.Serialize(Current, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporaryPath, path, overwrite: true);
        }
    }
}
