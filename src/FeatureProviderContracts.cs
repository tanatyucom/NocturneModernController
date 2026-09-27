using System.Collections.Generic;

namespace NocturneModernController
{
    // Public Feature Provider contract. Kept free of game (Il2Cpp) types so the
    // request routing in FeatureProviderDispatch can be unit tested. External
    // mods bind to these types by name through reflection; their names,
    // namespace and members must not change.

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
        // Optional display names for AllowedValues, keyed by the raw value
        // (not by position, so reordering AllowedValues cannot shift labels).
        // Values without an entry use the Settings window's default label.
        public Dictionary<string, string>? AllowedValueLabels { get; set; }
    }

    public sealed class FeatureProviderMetadata
    {
        public string ProviderId { get; set; } = string.Empty;
        public string ProviderName { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
        public List<FeatureMetadata> Features { get; set; } = new();
        public string Error { get; set; } = string.Empty;
    }

    // Written by the Settings window. A request with a non-empty Value is a
    // value change and never touches Enabled; otherwise it is an on/off change.
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

    // Optional: implement alongside IModernFeatureProvider to receive value
    // changes for features that publish AllowedValues. Providers without it
    // keep working unchanged; value requests for them stay unhandled.
    public interface IModernFeatureValueProvider
    {
        bool SetFeatureValue(string featureId, string value);
    }
}
