using System;
using System.Collections.Generic;
using System.Linq;

namespace NocturneModernController
{
    // TEMPORARY (Phase 4D, removed with the built-in Smart Auto in Phase 4E).
    // Rules for handing Smart Auto over to the external NocturneSmartAutoBattle
    // mod; kept free of game types so they can be unit tested.
    internal static class SmartAutoHandoffRules
    {
        internal const string ExternalProviderId = "nocturne_smart_auto_battle";
        internal const string ExternalAssemblyName = "NocturneSmartAutoBattle";
        internal const string BuiltInFeatureId = "smart_auto";

        // The external mod counts as present once it registered its feature
        // provider, or as soon as its assembly is loaded: it runs standalone
        // even when its Controller integration fails, and both must never
        // change battle commands or speed at the same time.
        internal static bool IsExternalActive(bool providerRegistered, IEnumerable<string?> loadedAssemblyNames) =>
            providerRegistered || loadedAssemblyNames.Any(name =>
                string.Equals(name, ExternalAssemblyName, StringComparison.OrdinalIgnoreCase));

        // Controller's own Smart Auto card is hidden while the external mod
        // owns Smart Auto; its Settings cards replace it.
        internal static IEnumerable<FeatureMetadata> BuiltInFeatures(
            IEnumerable<FeatureMetadata> features, bool externalActive) =>
            features.Where(feature => !externalActive ||
                !string.Equals(feature.Id, BuiltInFeatureId, StringComparison.OrdinalIgnoreCase));
    }
}
