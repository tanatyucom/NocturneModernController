using System;
using System.Collections.Generic;
using System.Linq;

namespace NocturneModernController
{
    internal sealed class DefaultBindingCandidate
    {
        internal int Context { get; init; }
        internal IReadOnlyList<int> Buttons { get; init; } = Array.Empty<int>();
        internal string ActionId { get; init; } = string.Empty;
    }

    internal static class DefaultBindingResolver
    {
        internal sealed class Resolution
        {
            internal IReadOnlyList<DefaultBindingCandidate> Applied { get; init; } =
                Array.Empty<DefaultBindingCandidate>();
            internal IReadOnlyList<DefaultBindingConflict> Conflicts { get; init; } =
                Array.Empty<DefaultBindingConflict>();
        }

        internal sealed class DefaultBindingConflict
        {
            internal int Context { get; init; }
            internal IReadOnlyList<int> Buttons { get; init; } = Array.Empty<int>();
            internal IReadOnlyList<string> ActionIds { get; init; } = Array.Empty<string>();
        }

        internal static IReadOnlyList<DefaultBindingCandidate> Resolve(
            IEnumerable<DefaultBindingCandidate> candidates,
            ISet<string> savedActionContexts,
            ISet<string> occupiedSlots,
            ISet<string> unassignedOverrides) =>
            Analyze(candidates, savedActionContexts, occupiedSlots, unassignedOverrides).Applied;

        internal static Resolution Analyze(
            IEnumerable<DefaultBindingCandidate> candidates,
            ISet<string> savedActionContexts,
            ISet<string> occupiedSlots,
            ISet<string> unassignedOverrides)
        {
            IGrouping<string, DefaultBindingCandidate>[] groups = candidates
                .Where(candidate =>
                    !savedActionContexts.Contains(ActionContextKey(
                        candidate.Context,
                        candidate.ActionId)) &&
                    !unassignedOverrides.Contains(ActionContextKey(
                        candidate.Context,
                        candidate.ActionId)))
                .GroupBy(
                    candidate => SlotKey(candidate.Context, candidate.Buttons),
                    StringComparer.Ordinal)
                .ToArray();
            DefaultBindingCandidate[] applied = groups
                .Where(group => group.Count() == 1 &&
                    !occupiedSlots.Contains(group.Key))
                .Select(group => group.Single())
                .OrderBy(candidate => candidate.Context)
                .ThenBy(candidate => ButtonOrderKey(candidate.Buttons), StringComparer.Ordinal)
                .ThenBy(candidate => candidate.ActionId, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            DefaultBindingConflict[] conflicts = groups
                .Where(group => group.Count() > 1)
                .Select(group => new DefaultBindingConflict
                {
                    Context = group.First().Context,
                    Buttons = group.First().Buttons,
                    ActionIds = group.Select(candidate => candidate.ActionId)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .OrderBy(actionId => actionId, StringComparer.OrdinalIgnoreCase)
                        .ToArray()
                })
                .OrderBy(conflict => conflict.Context)
                .ThenBy(conflict => ButtonOrderKey(conflict.Buttons), StringComparer.Ordinal)
                .ToArray();
            return new Resolution { Applied = applied, Conflicts = conflicts };
        }

        internal static string ActionContextKey(int context, string actionId) =>
            context + ":" + actionId;

        internal static string SlotKey(int context, IEnumerable<int> buttons) =>
            context + ":" + string.Join(",", buttons);

        private static string ButtonOrderKey(IEnumerable<int> buttons) =>
            string.Join(",", buttons.Select(button => button.ToString("D3")));
    }
}
