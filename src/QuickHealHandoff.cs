namespace NocturneModernController
{
    // TEMPORARY (Phase 2A, removed in Phase 2B together with the built-in
    // Quick Heal): when the external NocturneQuickHeal mod has registered its
    // feature provider, the built-in Quick Heal stops sampling input and hides
    // its feature card, so only one implementation ever heals.
    internal static class QuickHealHandoff
    {
        private const string ExternalProviderId = "nocturne_quick_heal";

        internal static bool ExternalActive => ModernControllerApi.HasFeatureProvider(ExternalProviderId);
    }
}
