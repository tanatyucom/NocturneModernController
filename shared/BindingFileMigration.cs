using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NocturneModernController
{
    internal sealed class BindingFileV1<TBinding>
    {
        [JsonPropertyName("schemaVersion")]
        public int SchemaVersion { get; set; } = 1;

        [JsonPropertyName("bindings")]
        public List<TBinding> Bindings { get; set; } = new();

        [JsonPropertyName("overrides")]
        public List<BindingOverrideEntry> Overrides { get; set; } = new();
    }

    internal sealed class BindingOverrideEntry
    {
        public int Context { get; set; }
        public string ActionId { get; set; } = string.Empty;
        public string State { get; set; } = string.Empty;
    }

    internal sealed class BindingLoadResult<TBinding>
    {
        internal List<TBinding> Bindings { get; } = new();
        internal List<BindingOverrideEntry> Overrides { get; } = new();
        internal List<SavedBindingConflict<TBinding>> SavedConflicts { get; } = new();
        internal bool IsLegacy { get; set; }
    }

    internal sealed class SavedBindingConflict<TBinding>
    {
        internal string SlotKey { get; init; } = string.Empty;
        internal List<TBinding> Bindings { get; init; } = new();
    }

    internal static class BindingFileMigration
    {
        internal static BindingLoadResult<TBinding> Read<TBinding>(
            string path,
            Func<TBinding, bool> isValid,
            Func<TBinding, string> getSlotKey,
            Action<TBinding> markLegacy)
        {
            var result = new BindingLoadResult<TBinding>();
            if (!File.Exists(path))
            {
                return result;
            }

            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
            JsonElement root = document.RootElement;
            JsonElement bindingsElement;
            if (root.ValueKind == JsonValueKind.Array)
            {
                result.IsLegacy = true;
                bindingsElement = root;
            }
            else if (root.ValueKind == JsonValueKind.Object &&
                     root.TryGetProperty("schemaVersion", out JsonElement version) &&
                     version.ValueKind == JsonValueKind.Number &&
                     version.TryGetInt32(out int schemaVersion) &&
                     schemaVersion == 1 &&
                     root.TryGetProperty("bindings", out bindingsElement) &&
                     bindingsElement.ValueKind == JsonValueKind.Array)
            {
                ReadOverrides(root, result.Overrides);
            }
            else
            {
                throw new InvalidDataException("Unsupported bindings.json root or schemaVersion.");
            }

            var parsed = new List<TBinding>();
            foreach (JsonElement element in bindingsElement.EnumerateArray())
            {
                try
                {
                    TBinding? binding = element.Deserialize<TBinding>();
                    if (binding == null)
                    {
                        continue;
                    }
                    if (result.IsLegacy)
                    {
                        markLegacy(binding);
                    }
                    if (isValid(binding))
                    {
                        parsed.Add(binding);
                    }
                }
                catch (Exception)
                {
                }
            }

            foreach (IGrouping<string, TBinding> group in parsed.GroupBy(
                         getSlotKey,
                         StringComparer.Ordinal))
            {
                if (group.Count() == 1)
                {
                    result.Bindings.Add(group.Single());
                }
                else
                {
                    result.SavedConflicts.Add(new SavedBindingConflict<TBinding>
                    {
                        SlotKey = group.Key,
                        Bindings = group.ToList()
                    });
                }
            }
            return result;
        }

        internal static BindingFileV1<TBinding> CreateV1<TBinding>(
            IEnumerable<TBinding> bindings,
            IEnumerable<BindingOverrideEntry> overrides) => new()
        {
            Bindings = bindings.ToList(),
            Overrides = overrides.ToList()
        };

        internal static void CreateLegacyBackupOnce(string path, bool isLegacy)
        {
            if (!isLegacy || !File.Exists(path))
            {
                return;
            }

            string backupPath = path + ".v2.0.3-backup";
            if (!File.Exists(backupPath))
            {
                File.Copy(path, backupPath);
            }
        }

        private static void ReadOverrides(
            JsonElement root,
            List<BindingOverrideEntry> destination)
        {
            if (!root.TryGetProperty("overrides", out JsonElement overrides) ||
                overrides.ValueKind != JsonValueKind.Array)
            {
                return;
            }

            var parsed = new List<BindingOverrideEntry>();
            foreach (JsonElement element in overrides.EnumerateArray())
            {
                try
                {
                    BindingOverrideEntry? entry = element.Deserialize<BindingOverrideEntry>();
                    if (entry != null && IsSingleContext(entry.Context) &&
                        !string.IsNullOrWhiteSpace(entry.ActionId) &&
                        entry.State == "Unassigned")
                    {
                        parsed.Add(entry);
                    }
                }
                catch (JsonException)
                {
                }
            }

            destination.AddRange(parsed
                .GroupBy(
                    entry => entry.Context + ":" + entry.ActionId,
                    StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .OrderBy(entry => entry.Context)
                .ThenBy(entry => entry.ActionId, StringComparer.OrdinalIgnoreCase));
        }

        private static bool IsSingleContext(int context) =>
            context is 1 or 2 or 4 or 8 or 16;
    }
}
