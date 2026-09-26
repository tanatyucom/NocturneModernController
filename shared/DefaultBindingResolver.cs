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

        // Default bindings to add given the current binding state. Only
        // bindings of registered actions count as saved or as occupying a
        // slot; saved conflict bindings mark their action/context as saved.
        // Adding the result to `bindings` and planning again yields nothing,
        // so this can run again whenever an action is registered late.
        internal static IReadOnlyList<DefaultBindingCandidate> PlanNewDefaults(
            ISet<string> registeredActionIds,
            IEnumerable<ExistingBinding> bindings,
            IEnumerable<ExistingBinding> savedConflictBindings,
            IEnumerable<string> unassignedOverrideKeys,
            IEnumerable<DefaultBindingCandidate> candidates)
        {
            var savedActionContexts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var occupiedSlots = new HashSet<string>(StringComparer.Ordinal);
            foreach (ExistingBinding binding in bindings)
            {
                if (!registeredActionIds.Contains(binding.ActionId))
                {
                    continue;
                }

                savedActionContexts.Add(ActionContextKey(binding.Context, binding.ActionId));
                occupiedSlots.Add(SlotKey(binding.Context, binding.Buttons));
            }
            foreach (ExistingBinding binding in savedConflictBindings)
            {
                if (registeredActionIds.Contains(binding.ActionId))
                {
                    savedActionContexts.Add(ActionContextKey(binding.Context, binding.ActionId));
                }
            }

            return Resolve(
                candidates,
                savedActionContexts,
                occupiedSlots,
                new HashSet<string>(unassignedOverrideKeys, StringComparer.OrdinalIgnoreCase));
        }

        // A saved binding as the planner sees it; Buttons must be normalized
        // (distinct, sorted) the same way as DefaultBindingCandidate.Buttons.
        internal readonly record struct ExistingBinding(int Context, string ActionId, IReadOnlyList<int> Buttons);

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
