using System;
using MelonLoader;

[assembly: MelonInfo(
    typeof(NocturneSmartAutoBattle.SmartAutoMod),
    "Nocturne Smart Auto Battle",
    NocturneSmartAutoBattle.SmartAutoMod.ModVersion,
    "Gray Ghost")]
[assembly: MelonGame(null, "smt3hd")]

namespace NocturneSmartAutoBattle
{
    // Smart Auto Battle as a standalone mod: when the game's own Auto is on
    // (its Auto button, Y by default), Skill Priority mode picks each command
    // and single-skill target through the game's normal command path, and the
    // battle can run faster. It adds no button of its own. With Nocturne
    // Modern Controller installed it also appears in Controller's Settings
    // (optional integration, see ModernControllerIntegration).
    public sealed class SmartAutoMod : MelonMod
    {
        internal const string ModVersion = "0.1.0";

        private static ModernControllerIntegration? _integration;

        internal static SmartAutoState State { get; } = new(Log);
        internal static SmartAutoKnowledge Knowledge { get; } = new(SmartAutoKnowledge.ModDirectory, Log);
        internal static PendingSingleTarget Pending { get; } = new();

        internal static void Log(string message) => MelonLogger.Msg("[NocturneSmartAutoBattle] " + message);

        internal static void Warn(string message) => MelonLogger.Warning("[NocturneSmartAutoBattle] " + message);

        public override void OnInitializeMelon()
        {
            string? note = SmartAutoSettings.Load();
            if (note != null)
            {
                Log(note);
            }
            Knowledge.Load();
        }

        // After every mod's OnInitializeMelon, so Controller (if installed) is
        // ready whatever the mod load order is.
        public override void OnLateInitializeMelon()
        {
            ModernControllerIntegration? integration = ModernControllerIntegration.TryCreate(
                ControllerAssembly.Find(), out string reason);
            if (integration != null)
            {
                try
                {
                    integration.Register(SettingsAccess.Instance, ModVersion);
                    _integration = integration;
                }
                catch (Exception exception)
                {
                    reason = "Nocturne Modern Controller integration failed (" +
                        (exception.InnerException ?? exception).GetType().Name + ")";
                }
            }
            SmartAutoSettings settings = SmartAutoSettings.Current;
            Log($"Loaded v{ModVersion}; enabled={settings.Enabled} mode={settings.Mode} " +
                $"speed={SmartAutoSettings.FormatSpeed(settings.Speed)}; input=game Auto button " +
                (_integration != null ? "(" : "(standalone; ") + reason + ").");
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

            SmartAutoSettings settings = SmartAutoSettings.Current;
            State.Sample(
                settings.Enabled,
                settings.Speed,
                ExplorationTracker.IsExplorationActive,
                settingsOpen,
                UnityTimeScale.Instance);
        }

        public override void OnDeinitializeMelon()
        {
            State.Shutdown(UnityTimeScale.Instance);
        }

        private sealed class SettingsAccess : ISmartAutoSettingsAccess
        {
            internal static SettingsAccess Instance { get; } = new();

            public SmartAutoSettings Current => SmartAutoSettings.Current;

            public bool SetEnabled(bool enabled)
            {
                SmartAutoSettings.Current.Enabled = enabled;
                return Save($"enabled={enabled}");
            }

            public bool SetMode(SmartAutoMode mode)
            {
                SmartAutoSettings.Current.Mode = mode;
                return Save($"mode={mode}");
            }

            public bool SetSpeed(float speed)
            {
                SmartAutoSettings.Current.Speed = speed;
                return Save($"speed={SmartAutoSettings.FormatSpeed(speed)}");
            }

            private static bool Save(string change)
            {
                SmartAutoSettings.Save();
                Log("Settings changed from Controller: " + change + ".");
                return true;
            }
        }
    }
}
