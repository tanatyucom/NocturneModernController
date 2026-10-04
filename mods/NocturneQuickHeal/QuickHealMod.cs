using System;
using MelonLoader;

[assembly: MelonInfo(
    typeof(NocturneQuickHeal.QuickHealMod),
    "Nocturne Quick Heal",
    NocturneQuickHeal.QuickHealMod.ModVersion,
    "Gray Ghost")]
[assembly: MelonGame(null, "smt3hd")]

namespace NocturneQuickHeal
{
    // Quick Heal as a standalone mod: press SELECT while exploring to heal the
    // party with learned recovery skills and real MP. With Nocturne Modern
    // Controller installed it also appears in Controller's key config (default
    // RB there, where Controller suppresses the game's RB turn) and Settings
    // (optional integration, see ModernControllerIntegration).
    public sealed class QuickHealMod : MelonMod
    {
        internal const string ModVersion = "0.1.0";

        private static ModernControllerIntegration? _integration;

        public override void OnInitializeMelon()
        {
            string? note = QuickHealSettings.Load();
            if (note != null)
            {
                LoggerInstance.Msg("[NocturneQuickHeal] " + note);
            }
        }

        // After every mod's OnInitializeMelon, so Controller (if installed) has
        // loaded its settings and bindings whatever the mod load order is.
        public override void OnLateInitializeMelon()
        {
            ModernControllerIntegration? integration = ModernControllerIntegration.TryCreate(
                ControllerAssembly.Find(), out string reason);
            if (integration != null)
            {
                try
                {
                    integration.Register(
                        () => QuickHealSettings.Current.Enabled,
                        enabled =>
                        {
                            QuickHealSettings.Current.Enabled = enabled;
                            QuickHealSettings.Save();
                            return true;
                        },
                        ModVersion);
                    _integration = integration;
                }
                catch (Exception exception)
                {
                    reason = "Nocturne Modern Controller integration failed (" +
                        (exception.InnerException ?? exception).GetType().Name + ")";
                }
            }
            LoggerInstance.Msg(
                $"[NocturneQuickHeal] Loaded v{ModVersion}; enabled={QuickHealSettings.Current.Enabled}; " +
                (_integration != null ? "input=Controller key config (" : "input=SELECT, standalone (") + reason + ").");
        }

        public override void OnUpdate()
        {
            bool settingsOpen;
            try
            {
                settingsOpen = _integration?.IsSettingsOpen ?? false;
            }
            catch (Exception)
            {
                return;
            }

            QuickHealRuntime.Sample(
                QuickHealSettings.Current.Enabled,
                ExplorationTracker.IsExplorationActive,
                settingsOpen,
                AutoMapState.IsOpen(),
                () => QuickHealInput.ReadHeld(
                    _integration != null,
                    () => _integration!.IsHeld(),
                    StandaloneInput.IsSelectHeld));
        }
    }
}
