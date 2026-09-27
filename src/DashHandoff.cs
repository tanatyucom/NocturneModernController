namespace NocturneModernController
{
    // TEMPORARY (Phase 3A, removed in Phase 3B together with the built-in
    // Dash): when the external NocturneModernDash mod has registered its
    // feature provider, the built-in FieldDashPatch stops before reading input
    // or writing the movement speed constants, and its feature card is hidden,
    // so only one implementation ever changes the speed.
    internal static class DashHandoff
    {
        private const string ExternalProviderId = "nocturne_modern_dash";

        internal static bool ExternalActive => ModernControllerApi.HasFeatureProvider(ExternalProviderId);
    }
}
