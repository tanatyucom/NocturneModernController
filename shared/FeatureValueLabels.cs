using System;
using System.Collections.Generic;

namespace NocturneModernController
{
    // Display name for one of a feature's AllowedValues: the provider's label
    // for that raw value when it supplies a non-blank one, otherwise the
    // Settings window's default label.
    internal static class FeatureValueLabels
    {
        internal static string Resolve(
            string? rawValue,
            IReadOnlyDictionary<string, string>? labels,
            Func<string, string> fallback)
        {
            if (rawValue != null && labels != null &&
                labels.TryGetValue(rawValue, out string? label) &&
                !string.IsNullOrWhiteSpace(label))
            {
                return label;
            }
            return fallback(rawValue ?? string.Empty);
        }
    }
}
