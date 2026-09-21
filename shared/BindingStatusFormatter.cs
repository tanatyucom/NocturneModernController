using System;
using System.Collections.Generic;
using System.Linq;

namespace NocturneModernController
{
    internal sealed class BindingStatusItem
    {
        internal string DisplayName { get; init; } = string.Empty;
        internal string Context { get; init; } = string.Empty;
        internal string Status { get; init; } = string.Empty;
        internal string DefaultBinding { get; init; } = string.Empty;
        internal string CurrentBinding { get; init; } = string.Empty;
        internal IReadOnlyList<string> ConflictsWith { get; init; } = Array.Empty<string>();
    }

    internal static class BindingStatusFormatter
    {
        internal static string Format(BindingStatusItem item)
        {
            var lines = new List<string>
            {
                item.DisplayName + " [" + item.Context + "]",
                "Status: " + item.Status
            };
            if (!string.IsNullOrWhiteSpace(item.DefaultBinding))
            {
                lines.Add("Default: " + item.DefaultBinding);
            }
            if (!string.IsNullOrWhiteSpace(item.CurrentBinding))
            {
                lines.Add("Current: " + item.CurrentBinding);
            }
            if (item.ConflictsWith.Count > 0)
            {
                lines.Add("Conflicts with: " + string.Join(", ", item.ConflictsWith
                    .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)));
            }
            return string.Join(Environment.NewLine, lines);
        }
    }
}
