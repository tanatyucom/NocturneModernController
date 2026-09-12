using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using Il2Cpp;

namespace NocturneModernController
{
    [Flags]
    public enum ControllerContext
    {
        None = 0,
        Field = 1,
        Battle = 2,
        Puzzle = 4,
        WorldMap = 8,
        Menu = 16,
        All = Field | Battle | Puzzle | WorldMap | Menu
    }

    public enum ControllerButton
    {
        None,
        A,
        B,
        X,
        Y,
        LB,
        RB,
        LT,
        RT,
        L3,
        R3,
        Start,
        Select,
        DPadUp,
        DPadDown,
        DPadLeft,
        DPadRight
    }

    public enum ControllerActionBehavior
    {
        Press,
        Hold,
        LongPress,
        Toggle,
        DoublePress
    }

    public sealed class ControllerActionDefinition
    {
        public string ModId { get; set; } = string.Empty;
        public string ActionId { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public ControllerContext Contexts { get; set; }
        public ControllerActionBehavior Behavior { get; set; }
        public List<ControllerDefaultBinding> DefaultBindings { get; set; } = new();
    }

    public sealed class ControllerDefaultBinding
    {
        public ControllerContext Context { get; set; }
        public List<ControllerButton> Buttons { get; set; } = new();
    }

    public sealed class ControllerBindingEntry
    {
        public ControllerContext Context { get; set; }
        public List<ControllerButton> Buttons { get; set; } = new();
        public string ActionId { get; set; } = string.Empty;
    }

    public sealed class FeatureMetadata
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public bool Enabled { get; set; }
        public string Category { get; set; } = string.Empty;
        public int SortOrder { get; set; }
        public bool RequiresRestart { get; set; }
        public bool ReadOnly { get; set; }
        public string Version { get; set; } = string.Empty;
        public string Warning { get; set; } = string.Empty;
        public string Notes { get; set; } = string.Empty;
        public string[]? AllowedValues { get; set; }
        public string? Value { get; set; }
    }

    public sealed class FeatureProviderMetadata
    {
        public string ProviderId { get; set; } = string.Empty;
        public string ProviderName { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
        public List<FeatureMetadata> Features { get; set; } = new();
        public string Error { get; set; } = string.Empty;
    }

    public sealed class FeatureToggleRequest
    {
        public string ProviderId { get; set; } = string.Empty;
        public string FeatureId { get; set; } = string.Empty;
        public bool Enabled { get; set; }
        public string? Value { get; set; }
    }

    public interface IModernFeatureProvider
    {
        string ProviderId { get; }
        string ProviderName { get; }
        string Version { get; }
        IReadOnlyList<FeatureMetadata> GetFeatures();
        bool SetFeatureEnabled(string featureId, bool enabled);
    }

    public static class ModernControllerApi
    {
        private static readonly Dictionary<string, ControllerActionDefinition> Actions =
            new(StringComparer.OrdinalIgnoreCase);
        private static readonly List<ControllerBindingEntry> Bindings = new();
        private static readonly Dictionary<string, IModernFeatureProvider> FeatureProviders =
            new(StringComparer.OrdinalIgnoreCase);
        private static bool _bindingsLoaded;

        public static void RegisterFeatureProvider(IModernFeatureProvider provider)
        {
            if (provider == null || string.IsNullOrWhiteSpace(provider.ProviderId) ||
                string.IsNullOrWhiteSpace(provider.ProviderName))
            {
                throw new ArgumentException("ProviderId and ProviderName are required.");
            }

            FeatureProviders[provider.ProviderId] = provider;
            SaveFeatureSnapshot();
        }

        public static bool UnregisterFeatureProvider(string providerId)
        {
            if (string.IsNullOrWhiteSpace(providerId) || !FeatureProviders.Remove(providerId))
            {
                return false;
            }

            SaveFeatureSnapshot();
            return true;
        }

        public static IReadOnlyList<FeatureProviderMetadata> GetFeatureProviders()
        {
            var result = new List<FeatureProviderMetadata>();
            foreach (IModernFeatureProvider provider in FeatureProviders.Values
                         .OrderBy(item => item.ProviderName, StringComparer.OrdinalIgnoreCase))
            {
                var snapshot = new FeatureProviderMetadata
                {
                    ProviderId = provider.ProviderId,
                    ProviderName = provider.ProviderName,
                    Version = provider.Version
                };
                try
                {
                    IReadOnlyList<FeatureMetadata> features =
                        provider.GetFeatures() ?? Array.Empty<FeatureMetadata>();
                    snapshot.Features = features
                        .Where(IsValidFeature)
                        .GroupBy(feature => feature.Id, StringComparer.OrdinalIgnoreCase)
                        .Select(group => CloneFeature(group.First()))
                        .OrderBy(feature => feature.SortOrder)
                        .ThenBy(feature => feature.Name, StringComparer.OrdinalIgnoreCase)
                        .ToList();
                }
                catch (Exception exception)
                {
                    snapshot.Error = exception.GetType().Name;
                }
                result.Add(snapshot);
            }
            LoadExternalFeatureSnapshots(result);
            return result;
        }

        public static bool SetFeatureEnabled(
            string providerId,
            string featureId,
            bool enabled)
        {
            if (!FeatureProviders.TryGetValue(providerId, out IModernFeatureProvider? provider))
            {
                return false;
            }

            try
            {
                FeatureMetadata? feature = provider.GetFeatures()
                    .FirstOrDefault(item => string.Equals(
                        item.Id, featureId, StringComparison.OrdinalIgnoreCase));
                if (feature == null || feature.ReadOnly ||
                    !provider.SetFeatureEnabled(featureId, enabled))
                {
                    return false;
                }

                SaveFeatureSnapshot();
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static void RegisterAction(ControllerActionDefinition definition)
        {
            if (definition == null || string.IsNullOrWhiteSpace(definition.ModId) ||
                string.IsNullOrWhiteSpace(definition.ActionId) ||
                string.IsNullOrWhiteSpace(definition.DisplayName))
            {
                throw new ArgumentException("ModId, ActionId and DisplayName are required.");
            }

            Actions[definition.ActionId] = definition;
            EnsureBindingsLoaded();
            foreach (ControllerDefaultBinding defaultBinding in definition.DefaultBindings)
            {
                bool exists = Bindings.Any(binding =>
                    binding.Context == defaultBinding.Context &&
                    SameChord(binding.Buttons, defaultBinding.Buttons));
                if (!exists)
                {
                    Bindings.Add(new ControllerBindingEntry
                    {
                        Context = defaultBinding.Context,
                        Buttons = NormalizeChord(defaultBinding.Buttons),
                        ActionId = definition.ActionId
                    });
                }
            }
            SaveSnapshots();
        }

        public static IReadOnlyList<ControllerActionDefinition> GetRegisteredActions() =>
            Actions.Values.OrderBy(action => action.ModId).ThenBy(action => action.DisplayName).ToArray();

        public static IReadOnlyList<ControllerBindingEntry> GetBindings() =>
            Bindings.ToArray();

        public static bool IsHeld(string actionId, ControllerContext context, int padNumber = 0)
        {
            EnsureBindingsLoaded();
            foreach (ControllerBindingEntry binding in Bindings)
            {
                if (binding.Context != context ||
                    !string.Equals(binding.ActionId, actionId, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                bool allHeld = binding.Buttons.Count > 0;
                foreach (ControllerButton button in binding.Buttons)
                {
                    Il2Cpplibsdf_H.SDF_PADMAP? map = ToPadMap(button);
                    bool held;
                    try
                    {
                        held = map.HasValue &&
                            dds3PadManager.DDS3_PADCHECK_PRESS(map.Value, padNumber);
                    }
                    catch
                    {
                        held = false;
                    }
                    if (!held)
                    {
                        allHeld = false;
                        break;
                    }
                }
                if (allHeld)
                {
                    return true;
                }
            }
            return false;
        }

        internal static void ReloadBindings()
        {
            _bindingsLoaded = false;
            EnsureBindingsLoaded();
        }

        internal static void SaveSnapshots()
        {
            string directory = ModDirectory;
            Directory.CreateDirectory(directory);
            var options = new JsonSerializerOptions { WriteIndented = true };
            File.WriteAllText(RegistryPath, JsonSerializer.Serialize(Actions.Values, options));
            File.WriteAllText(BindingsPath, JsonSerializer.Serialize(Bindings, options));
            SaveFeatureSnapshot();
        }

        internal static void SaveFeatureSnapshot()
        {
            string directory = ModDirectory;
            Directory.CreateDirectory(directory);
            var options = new JsonSerializerOptions { WriteIndented = true };
            File.WriteAllText(
                FeaturesPath,
                JsonSerializer.Serialize(GetFeatureProviders(), options));
        }

        internal static void ApplyFeatureToggleRequests()
        {
            if (!File.Exists(FeatureRequestsPath))
            {
                return;
            }

            try
            {
                List<FeatureToggleRequest>? requests =
                    JsonSerializer.Deserialize<List<FeatureToggleRequest>>(
                        File.ReadAllText(FeatureRequestsPath));
                if (requests != null)
                {
                    var unhandled = new List<FeatureToggleRequest>();
                    foreach (FeatureToggleRequest request in requests)
                    {
                        if (!SetFeatureEnabled(
                            request.ProviderId,
                            request.FeatureId,
                            request.Enabled))
                        {
                            unhandled.Add(request);
                        }
                    }
                    if (unhandled.Count > 0)
                    {
                        File.WriteAllText(
                            FeatureRequestsPath,
                            JsonSerializer.Serialize(
                                unhandled,
                                new JsonSerializerOptions { WriteIndented = true }));
                    }
                    else
                    {
                        File.Delete(FeatureRequestsPath);
                    }
                }
            }
            catch
            {
            }
            finally
            {
                SaveFeatureSnapshot();
            }
        }

        private static void LoadExternalFeatureSnapshots(
            List<FeatureProviderMetadata> result)
        {
            try
            {
                foreach (string path in Directory.GetFiles(
                             ModDirectory,
                             "NocturneModern*.features.json"))
                {
                    if (string.Equals(path, FeaturesPath, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                    try
                    {
                        List<FeatureProviderMetadata>? providers =
                            JsonSerializer.Deserialize<List<FeatureProviderMetadata>>(
                                File.ReadAllText(path));
                        if (providers == null)
                        {
                            continue;
                        }
                        foreach (FeatureProviderMetadata provider in providers)
                        {
                            if (string.IsNullOrWhiteSpace(provider.ProviderId) ||
                                result.Any(item => string.Equals(
                                    item.ProviderId,
                                    provider.ProviderId,
                                    StringComparison.OrdinalIgnoreCase)))
                            {
                                continue;
                            }
                            provider.Features = (provider.Features ?? new List<FeatureMetadata>())
                                .Where(IsValidFeature)
                                .GroupBy(feature => feature.Id, StringComparer.OrdinalIgnoreCase)
                                .Select(group => CloneFeature(group.First()))
                                .OrderBy(feature => feature.SortOrder)
                                .ThenBy(feature => feature.Name, StringComparer.OrdinalIgnoreCase)
                                .ToList();
                            result.Add(provider);
                        }
                    }
                    catch
                    {
                        // One malformed optional provider must not break the GUI.
                    }
                }
            }
            catch
            {
            }
        }

        private static bool IsValidFeature(FeatureMetadata? feature) =>
            feature != null &&
            !string.IsNullOrWhiteSpace(feature.Id) &&
            !string.IsNullOrWhiteSpace(feature.Name);

        private static FeatureMetadata CloneFeature(FeatureMetadata feature) => new()
        {
            Id = feature.Id,
            Name = feature.Name,
            Description = feature.Description,
            Enabled = feature.Enabled,
            Category = feature.Category,
            SortOrder = feature.SortOrder,
            RequiresRestart = feature.RequiresRestart,
            ReadOnly = feature.ReadOnly,
            Version = feature.Version,
            Warning = feature.Warning,
            Notes = feature.Notes,
            AllowedValues = feature.AllowedValues,
            Value = feature.Value
        };

        private static void EnsureBindingsLoaded()
        {
            if (_bindingsLoaded)
            {
                return;
            }
            _bindingsLoaded = true;
            Bindings.Clear();
            try
            {
                if (File.Exists(BindingsPath))
                {
                    List<ControllerBindingEntry>? loaded =
                        JsonSerializer.Deserialize<List<ControllerBindingEntry>>(
                            File.ReadAllText(BindingsPath));
                    if (loaded != null)
                    {
                        Bindings.AddRange(loaded);
                    }
                }
            }
            catch
            {
            }
        }

        private static List<ControllerButton> NormalizeChord(IEnumerable<ControllerButton> buttons) =>
            buttons.Where(button => button != ControllerButton.None)
                .Distinct()
                .Take(3)
                .OrderBy(button => button)
                .ToList();

        private static bool SameChord(
            IEnumerable<ControllerButton> left,
            IEnumerable<ControllerButton> right) =>
            NormalizeChord(left).SequenceEqual(NormalizeChord(right));

        private static Il2Cpplibsdf_H.SDF_PADMAP? ToPadMap(ControllerButton button) => button switch
        {
            ControllerButton.A => Il2Cpplibsdf_H.SDF_PADMAP.SDF_PADMAP_RD,
            ControllerButton.B => Il2Cpplibsdf_H.SDF_PADMAP.SDF_PADMAP_RR,
            ControllerButton.X => Il2Cpplibsdf_H.SDF_PADMAP.SDF_PADMAP_RL,
            ControllerButton.Y => Il2Cpplibsdf_H.SDF_PADMAP.SDF_PADMAP_RU,
            ControllerButton.LB => Il2Cpplibsdf_H.SDF_PADMAP.SDF_PADMAP_L1,
            ControllerButton.RB => Il2Cpplibsdf_H.SDF_PADMAP.SDF_PADMAP_R1,
            ControllerButton.LT => Il2Cpplibsdf_H.SDF_PADMAP.SDF_PADMAP_L2,
            ControllerButton.RT => Il2Cpplibsdf_H.SDF_PADMAP.SDF_PADMAP_R2,
            ControllerButton.L3 => Il2Cpplibsdf_H.SDF_PADMAP.SDF_PADMAP_L3,
            ControllerButton.R3 => Il2Cpplibsdf_H.SDF_PADMAP.SDF_PADMAP_R3,
            ControllerButton.Start => Il2Cpplibsdf_H.SDF_PADMAP.SDF_PADMAP_START,
            ControllerButton.Select => Il2Cpplibsdf_H.SDF_PADMAP.SDF_PADMAP_SELECT,
            ControllerButton.DPadUp => Il2Cpplibsdf_H.SDF_PADMAP.SDF_PADMAP_U,
            ControllerButton.DPadDown => Il2Cpplibsdf_H.SDF_PADMAP.SDF_PADMAP_D,
            ControllerButton.DPadLeft => Il2Cpplibsdf_H.SDF_PADMAP.SDF_PADMAP_L,
            ControllerButton.DPadRight => Il2Cpplibsdf_H.SDF_PADMAP.SDF_PADMAP_R,
            _ => null
        };

        private static string ModDirectory =>
            Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? string.Empty;
        internal static string RegistryPath => Path.Combine(
            ModDirectory, "NocturneModernController.actions.json");
        internal static string BindingsPath => Path.Combine(
            ModDirectory, "NocturneModernController.bindings.json");
        internal static string FeaturesPath => Path.Combine(
            ModDirectory, "NocturneModernController.features.json");
        internal static string FeatureRequestsPath => Path.Combine(
            ModDirectory, "NocturneModernController.feature-requests.json");
    }
}
