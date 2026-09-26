using System.Collections.Generic;

// Stand-ins for NocturneModernController's public API, used to test the
// reflection-based ModernControllerIntegration without the real Controller.

namespace FakeModernController
{
    public enum ControllerContext { None = 0, Field = 1, Battle = 2 }
    public enum ControllerButton { None, A, B, X, Y }
    public enum ControllerActionBehavior { Press, Hold }

    public sealed class ControllerDefaultBinding
    {
        public ControllerContext Context { get; set; }
        public List<ControllerButton> Buttons { get; set; } = new();
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

    public sealed class FeatureMetadata
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public bool Enabled { get; set; }
        public string Category { get; set; } = string.Empty;
        public int SortOrder { get; set; }
        public string Version { get; set; } = string.Empty;
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
        internal static readonly List<ControllerActionDefinition> Actions = new();
        internal static IModernFeatureProvider? Provider;
        internal static bool Held;
        internal static bool SettingsOpen;
        internal static bool Japanese;

        public static bool IsSettingsOpen => SettingsOpen;
        public static bool UseJapaneseUi => Japanese;
        public static void RegisterAction(ControllerActionDefinition definition) => Actions.Add(definition);
        public static void RegisterFeatureProvider(IModernFeatureProvider provider) => Provider = provider;
        public static bool IsHeld(string actionId, ControllerContext context, int padNumber = 0) =>
            Held && context == ControllerContext.Field &&
            Actions.Exists(action => action.ActionId == actionId);
    }
}

// An older Controller: no IsSettingsOpen / UseJapaneseUi.
namespace FakeOldController
{
    public enum ControllerContext { None = 0, Field = 1 }
    public enum ControllerButton { None, X }
    public enum ControllerActionBehavior { Press }

    public sealed class ControllerDefaultBinding
    {
        public ControllerContext Context { get; set; }
        public List<ControllerButton> Buttons { get; set; } = new();
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

    public sealed class FeatureMetadata
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public bool Enabled { get; set; }
        public string Category { get; set; } = string.Empty;
        public int SortOrder { get; set; }
        public string Version { get; set; } = string.Empty;
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
        internal static int Registered;
        public static void RegisterAction(ControllerActionDefinition definition) => Registered++;
        public static void RegisterFeatureProvider(IModernFeatureProvider provider) => Registered++;
        public static bool IsHeld(string actionId, ControllerContext context, int padNumber = 0) => false;
    }
}

// An incompatible Controller: no IsHeld.
namespace FakeBrokenController
{
    public enum ControllerContext { Field = 1 }
    public enum ControllerButton { X }
    public enum ControllerActionBehavior { Press }
    public sealed class ControllerDefaultBinding { }
    public sealed class ControllerActionDefinition { }
    public sealed class FeatureMetadata { }
    public interface IModernFeatureProvider { }

    public static class ModernControllerApi
    {
        public static void RegisterAction(ControllerActionDefinition definition) { }
        public static void RegisterFeatureProvider(IModernFeatureProvider provider) { }
    }
}
