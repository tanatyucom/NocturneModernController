using System;
using System.Linq;
using MelonLoader;

namespace NocturneModernController
{
    // TEMPORARY (Phase 4D, removed with the built-in Smart Auto in Phase 4E):
    // while the external NocturneSmartAutoBattle mod is present, every
    // built-in Smart Auto patch and update returns before doing anything (no
    // Auto mirror, no speed, no command or target override, no learning, no
    // logging) and the built-in card is hidden, so only the external mod runs
    // Smart Auto.
    internal static class SmartAutoHandoff
    {
        private static bool? _assemblyLoaded;
        private static bool _logged;

        internal static bool ExternalActive
        {
            get
            {
                // Every mod assembly is loaded before any mod initializes, so
                // one look at the loaded assemblies is enough.
                _assemblyLoaded ??= SmartAutoHandoffRules.IsExternalActive(
                    false,
                    AppDomain.CurrentDomain.GetAssemblies().Select(assembly => assembly.GetName().Name));
                bool active = SmartAutoHandoffRules.IsExternalActive(
                    ModernControllerApi.HasFeatureProvider(SmartAutoHandoffRules.ExternalProviderId),
                    _assemblyLoaded.Value ? new[] { SmartAutoHandoffRules.ExternalAssemblyName } : Array.Empty<string>());
                if (active && !_logged)
                {
                    _logged = true;
                    MelonLogger.Msg(
                        "[NocturneModernController] Smart Auto handed over to NocturneSmartAutoBattle; " +
                        "built-in Smart Auto disabled.");
                }
                return active;
            }
        }
    }
}
