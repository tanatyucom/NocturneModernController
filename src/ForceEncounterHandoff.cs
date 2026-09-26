namespace NocturneModernController
{
    // TEMPORARY (Phase 1A, removed in Phase 1B together with the built-in
    // Force Encounter): when the external NocturneForceEncounter mod has
    // registered its feature provider, the built-in Force Encounter stops
    // sampling input and hides its feature card, so only one implementation
    // ever requests an encounter.
    internal static class ForceEncounterHandoff
    {
        private const string ExternalProviderId = "nocturne_force_encounter";

        internal static bool ExternalActive => ModernControllerApi.HasFeatureProvider(ExternalProviderId);
    }
}
