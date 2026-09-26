using System.Collections.Generic;
using System.Runtime.CompilerServices;
using NocturneModernController;

namespace NocturneForceEncounter
{
    // Every use of NocturneModernController.dll goes through this class, and
    // it is only touched after ForceEncounterMod has confirmed the Controller
    // assembly is loaded. NoInlining keeps the Controller types out of callers
    // so the mod still loads (and just stays disabled) without Controller.
    internal static class ControllerBridge
    {
        // Same ActionId as Controller's former built-in action, so existing
        // player bindings (bindings.json) keep working without migration.
        internal const string ActionId = "nocturne-modern-controller.force-encounter";

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static void Register()
        {
            bool ja = ModernControllerApi.UseJapaneseUi;
            ModernControllerApi.RegisterAction(new ControllerActionDefinition
            {
                ModId = "NocturneForceEncounter",
                ActionId = ActionId,
                DisplayName = ja ? "強制エンカウント" : "Force Encounter",
                Description = ja ? "通常エンカウント可能な場所で標準の遭遇判定を発生させます。" : "Request a standard encounter where normal encounters are available.",
                Contexts = ControllerContext.Field,
                Behavior = ControllerActionBehavior.Press,
                DefaultBindings = new List<ControllerDefaultBinding>
                {
                    new() { Context = ControllerContext.Field, Buttons = new() { ControllerButton.X } }
                }
            });
            ModernControllerApi.RegisterFeatureProvider(new ForceEncounterFeatureProvider());
        }

        internal static bool IsExplorationActive
        {
            [MethodImpl(MethodImplOptions.NoInlining)]
            get => ModernControllerApi.IsExplorationActive;
        }

        internal static bool IsSettingsOpen
        {
            [MethodImpl(MethodImplOptions.NoInlining)]
            get => ModernControllerApi.IsSettingsOpen;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static bool IsHeld() => ModernControllerApi.IsHeld(ActionId, ControllerContext.Field);

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static IReadOnlyList<ControllerBindingEntry> GetBindings() => ModernControllerApi.GetBindings();
    }

    // Force Encounter's card in the Settings "MOD Features" tab. The feature id
    // stays "force_encounter"; the provider id is this mod's own.
    internal sealed class ForceEncounterFeatureProvider : IModernFeatureProvider
    {
        internal const string Id = "nocturne_force_encounter";
        private const string FeatureId = "force_encounter";

        public string ProviderId => Id;
        public string ProviderName => "Nocturne Force Encounter";
        public string Version => ForceEncounterMod.ModVersion;

        public IReadOnlyList<FeatureMetadata> GetFeatures()
        {
            bool ja = ModernControllerApi.UseJapaneseUi;
            return new[]
            {
                new FeatureMetadata
                {
                    Id = FeatureId,
                    Name = "Force Encounter",
                    Description = ja ? "通常エンカウント可能な場所で戦闘開始を要求します。" : "Request a battle only where normal encounters are available.",
                    Category = "Gameplay Change",
                    Enabled = ForceEncounterSettings.Current.Enabled,
                    SortOrder = 40,
                    Version = ForceEncounterMod.ModVersion
                }
            };
        }

        public bool SetFeatureEnabled(string featureId, bool enabled)
        {
            if (!string.Equals(featureId, FeatureId, System.StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
            ForceEncounterSettings.Current.Enabled = enabled;
            ForceEncounterSettings.Save();
            return true;
        }
    }
}
