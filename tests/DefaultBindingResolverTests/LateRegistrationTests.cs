using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using NocturneModernController;
using ExistingBinding = NocturneModernController.DefaultBindingResolver.ExistingBinding;

// DefaultBindingResolver.PlanNewDefaults: what ModernControllerApi.ResolveBindings
// adds at startup and again when an action is registered after startup.
internal static class LateRegistrationTests
{
    private const int Field = 1;

    internal static void Run()
    {
        LateActionGetsFreeDefault();
        ReRegistrationAddsNothing();
        OccupiedSlotIsNotTaken();
        SavedUserBindingIsKept();
        UnassignedOverrideIsRespected();
        RepeatedPlanningIsStable();
        SurvivesFileRoundTrip();
        OrphanBindingSurvivesUninstallAndReinstall();
    }

    // Force Encounter left Controller: with NocturneForceEncounter removed its
    // saved binding stays in the file but does not block the slot; when the
    // mod is installed again the same ActionId gets that binding back.
    private static void OrphanBindingSurvivesUninstallAndReinstall()
    {
        const string forceEncounter = "nocturne-modern-controller.force-encounter";
        var withoutMod = new State();
        withoutMod.Bindings.Add(new ExistingBinding(Field, forceEncounter, new[] { 3 }));
        withoutMod.Register("core.dash", 7);
        withoutMod.Resolve();
        Check(withoutMod.Bindings.Any(b => b.ActionId == forceEncounter),
            "the unregistered action's saved binding is kept");

        withoutMod.Register("other.late", 3);
        Check(withoutMod.Resolve().Any(c => c.ActionId == "other.late"),
            "an orphan binding does not occupy its slot for registered actions");

        var reinstalled = new State();
        reinstalled.Bindings.Add(new ExistingBinding(Field, forceEncounter, new[] { 3 }));
        reinstalled.Register("core.dash", 7);
        reinstalled.Resolve();
        reinstalled.Register(forceEncounter, 3);
        Check(reinstalled.Resolve().Count == 0 &&
              reinstalled.Bindings.Single(b => b.ActionId == forceEncounter).Buttons.SequenceEqual(new[] { 3 }),
            "reinstalling the mod reuses the saved binding without a duplicate");
    }

    // Simulates the Core state: registered actions, their default candidates and bindings.
    private sealed class State
    {
        internal readonly HashSet<string> Registered = new(StringComparer.OrdinalIgnoreCase);
        internal readonly List<DefaultBindingCandidate> Candidates = new();
        internal readonly List<ExistingBinding> Bindings = new();
        internal readonly List<ExistingBinding> SavedConflicts = new();
        internal readonly List<string> Unassigned = new();

        internal void Register(string actionId, params int[] defaultButtons)
        {
            Registered.Add(actionId);
            Candidates.RemoveAll(candidate => string.Equals(candidate.ActionId, actionId, StringComparison.OrdinalIgnoreCase));
            Candidates.Add(new DefaultBindingCandidate { Context = Field, ActionId = actionId, Buttons = defaultButtons });
        }

        // One ResolveBindings pass: plan, then append what was planned.
        internal IReadOnlyList<DefaultBindingCandidate> Resolve()
        {
            IReadOnlyList<DefaultBindingCandidate> added = DefaultBindingResolver.PlanNewDefaults(
                Registered, Bindings, SavedConflicts, Unassigned, Candidates);
            Bindings.AddRange(added.Select(candidate =>
                new ExistingBinding(candidate.Context, candidate.ActionId, candidate.Buttons.ToArray())));
            return added;
        }

        internal string Describe() => string.Join("|", Bindings
            .OrderBy(binding => binding.ActionId, StringComparer.OrdinalIgnoreCase)
            .Select(binding => $"{binding.Context}:{string.Join(",", binding.Buttons)}={binding.ActionId}"));
    }

    private static State Startup()
    {
        var state = new State();
        state.Register("core.dash", 7);
        state.Register("core.heal", 5);
        state.Resolve();
        return state;
    }

    private static void LateActionGetsFreeDefault()
    {
        State state = Startup();
        state.Register("ext.encounter", 3);
        IReadOnlyList<DefaultBindingCandidate> added = state.Resolve();
        Check(added.Count == 1 && added[0].ActionId == "ext.encounter" && added[0].Buttons.SequenceEqual(new[] { 3 }),
            "a late action gets its free default slot");
        Check(state.Describe() == "1:7=core.dash|1:5=core.heal|1:3=ext.encounter",
            "existing defaults are unchanged: " + state.Describe());
    }

    private static void ReRegistrationAddsNothing()
    {
        State state = Startup();
        state.Register("ext.encounter", 3);
        state.Resolve();
        state.Register("ext.encounter", 3);
        Check(state.Resolve().Count == 0 && state.Bindings.Count(b => b.ActionId == "ext.encounter") == 1,
            "registering the same action again adds no duplicate binding");
    }

    private static void OccupiedSlotIsNotTaken()
    {
        State state = Startup();
        state.Register("ext.encounter", 7);
        Check(state.Resolve().Count == 0, "a late default on an occupied slot is not applied");
        Check(state.Bindings.Single(b => b.Buttons.SequenceEqual(new[] { 7 })).ActionId == "core.dash",
            "the existing action keeps its slot");
    }

    private static void SavedUserBindingIsKept()
    {
        var state = new State();
        state.Bindings.Add(new ExistingBinding(Field, "ext.encounter", new[] { 9 }));
        state.Register("core.dash", 7);
        state.Resolve();
        state.Register("ext.encounter", 3);
        Check(state.Resolve().Count == 0, "a saved user binding blocks the default");
        Check(state.Bindings.Single(b => b.ActionId == "ext.encounter").Buttons.SequenceEqual(new[] { 9 }),
            "the saved user binding is kept");
    }

    private static void UnassignedOverrideIsRespected()
    {
        State state = Startup();
        state.Unassigned.Add(DefaultBindingResolver.ActionContextKey(Field, "ext.encounter"));
        state.Register("ext.encounter", 3);
        Check(state.Resolve().Count == 0 && state.Bindings.All(b => b.ActionId != "ext.encounter"),
            "an Unassigned override keeps the late action unassigned");
    }

    private static void RepeatedPlanningIsStable()
    {
        var state = new State();
        state.Register("core.a", 4);
        state.Register("core.b", 4);   // conflicting defaults: neither applies
        state.Register("core.c", 6);
        state.Resolve();
        string first = state.Describe();
        for (int i = 0; i < 3; i++)
        {
            Check(state.Resolve().Count == 0, "re-running the resolution adds nothing");
        }
        Check(state.Describe() == first && first == "1:6=core.c", "result is stable, conflict still unresolved: " + first);
    }

    private sealed class FileBinding
    {
        public int Context { get; set; }
        public List<int> Buttons { get; set; } = new();
        public string ActionId { get; set; } = string.Empty;
        public string Source { get; set; } = "Default";
    }

    private static void SurvivesFileRoundTrip()
    {
        State state = Startup();
        state.Register("ext.encounter", 3);
        state.Resolve();

        string path = Path.Combine(Path.GetTempPath(), "nmc-late-registration-test.json");
        File.WriteAllText(path, JsonSerializer.Serialize(BindingFileMigration.CreateV1(
            state.Bindings.Select(b => new FileBinding { Context = b.Context, ActionId = b.ActionId, Buttons = b.Buttons.ToList() }),
            Array.Empty<BindingOverrideEntry>())));
        BindingLoadResult<FileBinding> loaded = BindingFileMigration.Read<FileBinding>(
            path,
            binding => binding.Buttons.Count > 0,
            binding => binding.Context + ":" + string.Join(",", binding.Buttons),
            binding => binding.Source = "Legacy");
        File.Delete(path);

        // "Restart": same registrations, bindings come only from the file.
        var restarted = new State();
        restarted.Bindings.AddRange(loaded.Bindings.Select(b => new ExistingBinding(b.Context, b.ActionId, b.Buttons.ToArray())));
        restarted.Register("core.dash", 7);
        restarted.Register("core.heal", 5);
        restarted.Resolve();
        restarted.Register("ext.encounter", 3);
        Check(restarted.Resolve().Count == 0, "after a restart the saved late default is not added again");
        Check(restarted.Describe() == state.Describe(), "bindings are identical after the file round trip: " + restarted.Describe());
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
