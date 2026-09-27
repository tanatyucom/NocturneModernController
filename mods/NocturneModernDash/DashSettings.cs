using System;
using System.IO;
using System.Reflection;
using System.Text.Json;

namespace NocturneModernDash
{
    // Mods\NocturneModernDash.settings.json is the only owner of the enabled
    // state, with or without Controller. On first run the value is taken from
    // Controller's former built-in setting (NocturneModernController.settings.json
    // "DashEnabled") when that file exists, so a player who had turned Dash off
    // keeps it off. Dash Keep is runtime only and is not saved (as before).
    internal sealed class DashSettings
    {
        internal const string FileName = "NocturneModernDash.settings.json";
        private const string ControllerFileName = "NocturneModernController.settings.json";

        public bool Enabled { get; set; } = true;

        internal static DashSettings Current { get; private set; } = new();

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
                    Current = JsonSerializer.Deserialize<DashSettings>(File.ReadAllText(path)) ?? new();
                    return null;
                }

                Current = new DashSettings();
                string? note = null;
                string controllerPath = Path.Combine(directory, ControllerFileName);
                if (File.Exists(controllerPath))
                {
                    using JsonDocument document = JsonDocument.Parse(File.ReadAllText(controllerPath));
                    if (document.RootElement.TryGetProperty("DashEnabled", out JsonElement value) &&
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
                Current = new DashSettings();
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
