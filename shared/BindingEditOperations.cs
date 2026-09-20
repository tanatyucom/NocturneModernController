using System;
using System.Collections.Generic;
using System.Linq;

namespace NocturneModernController
{
    internal static class BindingEditOperations
    {
        internal static void SetUnassigned<TBinding>(
            List<TBinding> bindings,
            List<BindingOverrideEntry> overrides,
            int context,
            string actionId,
            Func<TBinding, int> getContext,
            Func<TBinding, string> getActionId)
        {
            bindings.RemoveAll(binding =>
                getContext(binding) == context &&
                string.Equals(getActionId(binding), actionId, StringComparison.OrdinalIgnoreCase));
            ClearOverride(overrides, context, actionId);
            overrides.Add(new BindingOverrideEntry
            {
                Context = context,
                ActionId = actionId,
                State = "Unassigned"
            });
        }

        internal static void AssignUser<TBinding>(
            List<TBinding> bindings,
            List<BindingOverrideEntry> overrides,
            int context,
            string actionId,
            IReadOnlyList<int> buttons,
            Func<TBinding, int> getContext,
            Func<TBinding, string> getActionId,
            Func<TBinding, IReadOnlyList<int>> getButtons,
            Func<TBinding> createBinding)
        {
            bindings.RemoveAll(binding =>
                getContext(binding) == context &&
                (string.Equals(getActionId(binding), actionId, StringComparison.OrdinalIgnoreCase) ||
                 getButtons(binding).SequenceEqual(buttons)));
            ClearOverride(overrides, context, actionId);
            bindings.Add(createBinding());
        }

        internal static void ResetAction<TBinding>(
            List<TBinding> bindings,
            List<BindingOverrideEntry> overrides,
            int context,
            string actionId,
            Func<TBinding, int> getContext,
            Func<TBinding, string> getActionId)
        {
            bindings.RemoveAll(binding =>
                getContext(binding) == context &&
                string.Equals(getActionId(binding), actionId, StringComparison.OrdinalIgnoreCase));
            ClearOverride(overrides, context, actionId);
        }

        internal static void ResetContext<TBinding>(
            List<TBinding> bindings,
            List<BindingOverrideEntry> overrides,
            int context,
            Func<TBinding, int> getContext)
        {
            bindings.RemoveAll(binding => getContext(binding) == context);
            overrides.RemoveAll(entry => entry.Context == context);
        }

        internal static void ResetAll<TBinding>(
            List<TBinding> bindings,
            List<BindingOverrideEntry> overrides)
        {
            bindings.Clear();
            overrides.Clear();
        }

        internal static void ClearOverride(
            List<BindingOverrideEntry> overrides,
            int context,
            string actionId)
        {
            overrides.RemoveAll(entry =>
                entry.Context == context &&
                string.Equals(entry.ActionId, actionId, StringComparison.OrdinalIgnoreCase));
        }

        internal static bool HasOverride(
            IEnumerable<BindingOverrideEntry> overrides,
            int context,
            string actionId) =>
            overrides.Any(entry =>
                entry.Context == context &&
                string.Equals(entry.ActionId, actionId, StringComparison.OrdinalIgnoreCase) &&
                entry.State == "Unassigned");
    }
}
