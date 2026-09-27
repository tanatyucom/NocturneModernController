using System;
using System.Collections.Generic;
using System.Linq;

namespace NocturneModernController
{
    // Calls into feature providers and routes Settings requests. A provider
    // that throws is treated as "not handled"; it never breaks the caller.
    internal static class FeatureProviderDispatch
    {
        internal static bool IsValueRequest(FeatureToggleRequest request) =>
            !string.IsNullOrEmpty(request.Value);

        internal static bool SetEnabled(IModernFeatureProvider? provider, string featureId, bool enabled)
        {
            if (provider == null)
            {
                return false;
            }

            try
            {
                FeatureMetadata? feature = FindFeature(provider, featureId);
                return feature != null && !feature.ReadOnly &&
                    provider.SetFeatureEnabled(featureId, enabled);
            }
            catch
            {
                return false;
            }
        }

        // False when the provider does not implement IModernFeatureValueProvider,
        // the feature is unknown or read-only, or the value is not one of the
        // feature's AllowedValues (when it publishes any).
        internal static bool SetValue(IModernFeatureProvider? provider, string featureId, string? value)
        {
            if (provider is not IModernFeatureValueProvider valueProvider ||
                string.IsNullOrEmpty(value))
            {
                return false;
            }

            try
            {
                FeatureMetadata? feature = FindFeature(provider, featureId);
                if (feature == null || feature.ReadOnly)
                {
                    return false;
                }
                if (feature.AllowedValues != null && feature.AllowedValues.Length > 0 &&
                    !feature.AllowedValues.Contains(value, StringComparer.Ordinal))
                {
                    return false;
                }
                return valueProvider.SetFeatureValue(featureId, value);
            }
            catch
            {
                return false;
            }
        }

        // Applies each request through the matching path and returns the ones
        // that were not handled, in their original order. A value request only
        // ever goes through applyValue.
        internal static List<FeatureToggleRequest> ApplyRequests(
            IEnumerable<FeatureToggleRequest?> requests,
            Func<FeatureToggleRequest, bool> applyEnabled,
            Func<FeatureToggleRequest, bool> applyValue)
        {
            var unhandled = new List<FeatureToggleRequest>();
            foreach (FeatureToggleRequest? request in requests)
            {
                if (request == null)
                {
                    continue;
                }

                bool handled;
                try
                {
                    handled = IsValueRequest(request) ? applyValue(request) : applyEnabled(request);
                }
                catch
                {
                    handled = false;
                }
                if (!handled)
                {
                    unhandled.Add(request);
                }
            }
            return unhandled;
        }

        private static FeatureMetadata? FindFeature(IModernFeatureProvider provider, string featureId) =>
            provider.GetFeatures()?.FirstOrDefault(item => item != null && string.Equals(
                item.Id, featureId, StringComparison.OrdinalIgnoreCase));
    }
}
