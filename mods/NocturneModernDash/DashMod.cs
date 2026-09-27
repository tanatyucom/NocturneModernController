using System;
using MelonLoader;

[assembly: MelonInfo(
    typeof(NocturneModernDash.DashMod),
    "Nocturne Modern Dash",
    NocturneModernDash.DashMod.ModVersion,
    "Gray Ghost")]
[assembly: MelonGame(null, "smt3hd")]

namespace NocturneModernDash
{
    // Dash as a standalone mod: hold LT or RT (or P) in the field, dungeons and
    // on the world map to move 1.5x faster; press LT+RT to toggle Dash Keep.
    // With Nocturne Modern Controller installed, Dash and Dash Keep also
    // appear in Controller's key config and Settings (optional integration,
    // see ModernControllerIntegration).
    public sealed class DashMod : MelonMod
    {
        internal const string ModVersion = "0.1.0";

        internal static ModernControllerIntegration? Integration { get; private set; }

        // Read from the field update; a failing read counts as closed so a
        // broken integration cannot leave the speed patched.
        internal static bool IsSettingsOpen
        {
            get
            {
                try
                {
                    return Integration?.IsSettingsOpen ?? false;
                }
                catch (Exception)
                {
                    return false;
                }
            }
        }

        public override void OnInitializeMelon()
        {
            string? note = DashSettings.Load();
            if (note != null)
            {
                LoggerInstance.Msg("[NocturneModernDash] " + note);
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
                        () => DashSettings.Current.Enabled,
                        enabled =>
                        {
                            DashSettings.Current.Enabled = enabled;
                            DashSettings.Save();
                            return true;
                        },
                        ModVersion);
                    Integration = integration;
                }
                catch (Exception exception)
                {
                    reason = "Nocturne Modern Controller integration failed (" +
                        (exception.InnerException ?? exception).GetType().Name + ")";
                }
            }
            LoggerInstance.Msg(
                $"[NocturneModernDash] Loaded v{ModVersion}; enabled={DashSettings.Current.Enabled}; " +
                (Integration != null
                    ? "input=Controller key config + P ("
                    : "input=LT/RT/P, Keep=LT+RT, standalone (") + reason + ").");
        }
    }
}
