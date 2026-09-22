using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using NocturneModernController;

internal static class Program
{
    private static void Main()
    {
        DefaultBindingCandidate[] candidates =
        {
            Candidate(1, "mod.alpha", 5),
            Candidate(1, "mod.beta", 6),
            Candidate(2, "mod.gamma", 5),
            Candidate(1, "mod.conflict-a", 7),
            Candidate(1, "mod.conflict-b", 7),
            Candidate(1, "mod.saved", 8),
            Candidate(1, "mod.unassigned", 9),
            Candidate(1, "mod.orphan-contender", 10)
        };
        var savedActionContexts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            DefaultBindingResolver.ActionContextKey(1, "mod.saved")
        };
        var occupiedSlots = new HashSet<string>(StringComparer.Ordinal)
        {
            DefaultBindingResolver.SlotKey(1, new[] { 8 })
        };
        var overrides = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            DefaultBindingResolver.ActionContextKey(1, "mod.unassigned")
        };

        string forward = Resolve(candidates, savedActionContexts, occupiedSlots, overrides);
        string reverse = Resolve(candidates.Reverse(), savedActionContexts, occupiedSlots, overrides);
        Equal(forward, reverse, "registration order must not change the result");
        Equal(
            "1:5=mod.alpha|1:6=mod.beta|1:10=mod.orphan-contender|2:5=mod.gamma",
            forward,
            "saved, override, conflict, orphan-slot and context rules");

        HashSet<string> clearedOverrides = new HashSet<string>(
            overrides,
            StringComparer.OrdinalIgnoreCase);
        clearedOverrides.Remove(DefaultBindingResolver.ActionContextKey(1, "mod.unassigned"));
        string afterClear = Resolve(
            candidates,
            savedActionContexts,
            occupiedSlots,
            clearedOverrides);
        Contains(afterClear, "1:9=mod.unassigned", "clearing override must restore default");

        var otherContextOverride = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            DefaultBindingResolver.ActionContextKey(2, "mod.alpha")
        };
        string otherContext = Resolve(
            new[] { Candidate(1, "mod.alpha", 5) },
            new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            new HashSet<string>(StringComparer.Ordinal),
            otherContextOverride);
        Equal("1:5=mod.alpha", otherContext, "override must be context-specific");

        VerifyOverridePersistence();
        VerifyBindingEdits();
        VerifyConflictDiagnostics();
        VerifyGameBindingSnapshot();
        VerifyGameActionBindingDisplay();
    }

    private static DefaultBindingCandidate Candidate(int context, string actionId, params int[] buttons) =>
        new() { Context = context, ActionId = actionId, Buttons = buttons };

    private static string Resolve(
        IEnumerable<DefaultBindingCandidate> candidates,
        ISet<string> savedActionContexts,
        ISet<string> occupiedSlots,
        ISet<string> overrides) =>
        string.Join("|", DefaultBindingResolver.Resolve(
            candidates,
            savedActionContexts,
            occupiedSlots,
            overrides).Select(candidate =>
                DefaultBindingResolver.SlotKey(candidate.Context, candidate.Buttons) +
                "=" + candidate.ActionId));

    private static void Equal(string expected, string actual, string message)
    {
        if (!string.Equals(expected, actual, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                message + Environment.NewLine +
                "Expected: " + expected + Environment.NewLine +
                "Actual:   " + actual);
        }
    }

    private static void Contains(string actual, string expectedPart, string message)
    {
        if (!actual.Split('|').Contains(expectedPart, StringComparer.Ordinal))
        {
            throw new InvalidOperationException(message + Environment.NewLine + actual);
        }
    }

    private static void VerifyOverridePersistence()
    {
        string directory = Path.Combine(Path.GetTempPath(), "nmc-binding-resolver-tests");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "bindings.json");
        try
        {
            var document = BindingFileMigration.CreateV1(
                Array.Empty<TestBinding>(),
                new[]
                {
                    new BindingOverrideEntry
                    {
                        Context = 1,
                        ActionId = "orphan.action",
                        State = "Unassigned"
                    },
                    new BindingOverrideEntry
                    {
                        Context = 1,
                        ActionId = "ORPHAN.ACTION",
                        State = "Unassigned"
                    }
                });
            AtomicJsonFile.WriteJsonAtomic(
                path,
                JsonSerializer.Serialize(document));
            BindingLoadResult<TestBinding> loaded = BindingFileMigration.Read<TestBinding>(
                path,
                _ => true,
                _ => string.Empty,
                _ => { });
            Equal("1", loaded.Overrides.Count.ToString(), "duplicate overrides must collapse");
            Equal("orphan.action", loaded.Overrides[0].ActionId, "orphan override must survive round-trip");
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory);
            }
        }
    }

    private static void VerifyBindingEdits()
    {
        foreach (string source in new[] { "Default", "User" })
        {
            var bindings = new List<TestBinding>
            {
                new TestBinding(1, "mod.action", source, 5)
            };
            var overrides = new List<BindingOverrideEntry>();
            BindingEditOperations.SetUnassigned(
                bindings,
                overrides,
                1,
                "mod.action",
                binding => binding.Context,
                binding => binding.ActionId);
            Equal("0", bindings.Count.ToString(), source + " binding must be removed by None");
            Equal("1", overrides.Count.ToString(), source + " None must create override");
        }

        var movedBindings = new List<TestBinding>();
        var movedOverrides = new List<BindingOverrideEntry>
        {
            new BindingOverrideEntry { Context = 1, ActionId = "mod.action", State = "Unassigned" }
        };
        BindingEditOperations.AssignUser(
            movedBindings,
            movedOverrides,
            1,
            "mod.action",
            new[] { 6 },
            binding => binding.Context,
            binding => binding.ActionId,
            binding => binding.Buttons,
            () => new TestBinding(1, "mod.action", "User", 6));
        Equal("0", movedOverrides.Count.ToString(), "assignment must clear override");
        Equal("User", movedBindings[0].Source, "assignment must create User source");

        movedBindings[0].Source = "Legacy";
        BindingEditOperations.AssignUser(
            movedBindings,
            movedOverrides,
            1,
            "mod.action",
            new[] { 6 },
            binding => binding.Context,
            binding => binding.ActionId,
            binding => binding.Buttons,
            () => new TestBinding(1, "mod.action", "User", 6));
        Equal("User", movedBindings[0].Source, "reconfirming Legacy must promote to User");

        BindingEditOperations.SetUnassigned(
            movedBindings,
            movedOverrides,
            1,
            "mod.action",
            binding => binding.Context,
            binding => binding.ActionId);
        BindingEditOperations.ResetAction(
            movedBindings,
            movedOverrides,
            1,
            "mod.action",
            binding => binding.Context,
            binding => binding.ActionId);
        string resetResult = Resolve(
            new[] { Candidate(1, "mod.action", 5) },
            new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            new HashSet<string>(StringComparer.Ordinal),
            new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        Equal("1:5=mod.action", resetResult, "Reset this Action must restore current default");

        var contextBindings = new List<TestBinding>
        {
            new TestBinding(1, "mod.field", "User", 5),
            new TestBinding(2, "mod.battle", "User", 6)
        };
        var contextOverrides = new List<BindingOverrideEntry>
        {
            new BindingOverrideEntry { Context = 1, ActionId = "mod.field", State = "Unassigned" },
            new BindingOverrideEntry { Context = 2, ActionId = "mod.battle", State = "Unassigned" }
        };
        BindingEditOperations.ResetContext(
            contextBindings,
            contextOverrides,
            1,
            binding => binding.Context);
        Equal("2", contextBindings.Single().Context.ToString(), "Reset Context must preserve other contexts");
        Equal("2", contextOverrides.Single().Context.ToString(), "Reset Context must preserve other overrides");

        BindingEditOperations.ResetAll(contextBindings, contextOverrides);
        Equal("0", contextBindings.Count.ToString(), "Reset All must clear MOD bindings");
        Equal("0", contextOverrides.Count.ToString(), "Reset All must clear MOD overrides");
    }

    private static void VerifyConflictDiagnostics()
    {
        DefaultBindingCandidate[] candidates =
        {
            Candidate(1, "mod.quick-heal", 6),
            Candidate(1, "mod.quick-save", 6)
        };
        var saved = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var occupied = new HashSet<string>(StringComparer.Ordinal);
        var overrides = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        DefaultBindingResolver.Resolution forward = DefaultBindingResolver.Analyze(
            candidates, saved, occupied, overrides);
        DefaultBindingResolver.Resolution reverse = DefaultBindingResolver.Analyze(
            candidates.Reverse(), saved, occupied, overrides);
        Equal(
            string.Join(",", forward.Conflicts.Single().ActionIds),
            string.Join(",", reverse.Conflicts.Single().ActionIds),
            "DefaultConflict display data must be registration-order independent");
        Equal("0", forward.Applied.Count.ToString(), "DefaultConflict must not apply a winner");

        string conflictText = BindingStatusFormatter.Format(new BindingStatusItem
        {
            DisplayName = "Quick Heal",
            Context = "Field",
            Status = "Default Conflict",
            DefaultBinding = "RB",
            ConflictsWith = new[] { "Quick Save" }
        });
        TextContains(conflictText, "Status: Default Conflict", "DefaultConflict must be visible");
        TextContains(conflictText, "Conflicts with: Quick Save", "conflict peer must be visible");
        string unassignedText = BindingStatusFormatter.Format(new BindingStatusItem
        {
            DisplayName = "Quick Pass",
            Context = "Field",
            Status = "Unassigned by user"
        });
        TextContains(unassignedText, "Unassigned by user", "user Unassigned must be distinct");
        string orphanText = BindingStatusFormatter.Format(new BindingStatusItem
        {
            DisplayName = "example.old-action",
            Context = "Field",
            Status = "Not currently registered"
        });
        TextContains(orphanText, "Not currently registered", "orphan must be visible");

        string directory = Path.Combine(Path.GetTempPath(), "nmc-binding-conflict-tests");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "bindings.json");
        try
        {
            AtomicJsonFile.WriteJsonAtomic(path, JsonSerializer.Serialize(
                BindingFileMigration.CreateV1(
                    new[]
                    {
                        new TestBinding(1, "mod.first", "User", 6),
                        new TestBinding(1, "mod.second", "Legacy", 6)
                    },
                    Array.Empty<BindingOverrideEntry>())));
            BindingLoadResult<TestBinding> loaded = BindingFileMigration.Read<TestBinding>(
                path,
                _ => true,
                binding => DefaultBindingResolver.SlotKey(binding.Context, binding.Buttons),
                binding => binding.Source = "Legacy");
            Equal("0", loaded.Bindings.Count.ToString(), "saved conflict must stay out of runtime bindings");
            Equal("1", loaded.SavedConflicts.Count.ToString(), "saved conflict diagnostic must be retained");
            Equal("2", loaded.SavedConflicts[0].Bindings.Count.ToString(), "both conflicting entries must be retained");
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
            if (Directory.Exists(directory)) Directory.Delete(directory);
        }

        Equal(
            "0",
            DefaultBindingResolver.Analyze(candidates, saved, occupied, overrides).Applied.Count.ToString(),
            "formatting status must not alter resolver results");
    }

    private static void TextContains(string actual, string expectedPart, string message)
    {
        if (!actual.Contains(expectedPart, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(message + Environment.NewLine + actual);
        }
    }

    private static void VerifyGameBindingSnapshot()
    {
        GameBindingSnapshotResult success = GameBindingSnapshotReader.Capture(
            (controller, key) => controller + ":" + key);
        Equal("True", success.Available.ToString(), "native snapshot must report success");
        Equal("16", success.Bindings.Count.ToString(), "only confirmed physical KeyIDs must be read");
        Equal("True", success.Bindings.All(binding => binding.ReadOnly).ToString(), "GAME bindings must be read-only");
        Equal("A", success.Bindings[0].KeyId, "snapshot must retain physical KeyID");
        var payload = new ActionRegistrySnapshot<string>
        {
            Actions = new List<string> { "mod.action" },
            GameBindingsAvailable = success.Available,
            GameBindings = success.Bindings.ToList()
        };
        ActionRegistrySnapshot<string>? roundTrip =
            JsonSerializer.Deserialize<ActionRegistrySnapshot<string>>(
                JsonSerializer.Serialize(payload));
        Equal("True", (roundTrip?.GameBindingsAvailable ?? false).ToString(), "payload must retain GAME availability");
        Equal("16", (roundTrip?.GameBindings.Count ?? 0).ToString(), "payload must retain GAME bindings");
        string gameText = BindingStatusFormatter.Format(new BindingStatusItem
        {
            DisplayName = "A [GAME]",
            Context = "PAD1",
            Status = "Read-only",
            CurrentBinding = success.Bindings[0].CurrentButton
        });
        TextContains(gameText, "A [GAME]", "GAME entry must be visibly identified");
        TextContains(gameText, "Status: Read-only", "GAME entry must be visibly read-only");
        TextContains(gameText, "Current: 0:0", "GAME current assignment must be visible");

        GameBindingSnapshotResult failure = GameBindingSnapshotReader.Capture(
            (_, _) => throw new InvalidOperationException("native unavailable"));
        Equal("False", failure.Available.ToString(), "native failure must become unavailable");
        Equal("0", failure.Bindings.Count.ToString(), "native failure must not expose a partial snapshot");
    }

    private static void VerifyGameActionBindingDisplay()
    {
        var entries = new List<GameActionBindingRawEntry>
        {
            new() { Index = 5, RawValue = 9, ConfirmedActionName = "キャンセル", ConfirmedPhysicalButton = "B" },
            new() { Index = 4, RawValue = 10, ConfirmedActionName = "決定・アクション", ConfirmedPhysicalButton = "A" },
            new() { Index = 24, RawValue = 3, ConfirmedActionName = null, ConfirmedPhysicalButton = null },
            new() { Index = 7, RawValue = 99, ConfirmedActionName = "コマンドメニュー", ConfirmedPhysicalButton = null }
        };

        IReadOnlyList<GameActionBindingDisplayRow> rows =
            GameActionBindingDisplayFormatter.GetConfirmedRows(entries);
        Equal("2", rows.Count.ToString(), "only fully-confirmed indices must produce a row");
        Equal("決定・アクション", rows[0].ActionName, "rows must be ordered by index");
        Equal("A", rows[0].PhysicalButton, "row must carry the confirmed physical button");
        Equal("キャンセル", rows[1].ActionName, "second confirmed index must follow in order");

        var payload = new ActionRegistrySnapshot<string>
        {
            Actions = new List<string> { "mod.action" },
            GameActionBindingsAvailable = true,
            GameActionBindingsRaw = entries,
            GameActionBindingsAuthoritative = true
        };
        ActionRegistrySnapshot<string>? roundTrip =
            JsonSerializer.Deserialize<ActionRegistrySnapshot<string>>(
                JsonSerializer.Serialize(payload));
        Equal("True", (roundTrip?.GameActionBindingsAuthoritative ?? false).ToString(),
            "authoritative flag must round-trip through JSON");

        var nonAuthoritative = new ActionRegistrySnapshot<string>
        {
            GameActionBindingsAvailable = true,
            GameActionBindingsAuthoritative = false
        };
        Equal("False", nonAuthoritative.GameActionBindingsAuthoritative.ToString(),
            "Available alone must not imply authoritative");
    }

    private sealed class TestBinding
    {
        public int Context { get; }
        public string ActionId { get; }
        public string Source { get; set; }
        public IReadOnlyList<int> Buttons { get; }

        public TestBinding()
        {
            ActionId = string.Empty;
            Source = string.Empty;
            Buttons = Array.Empty<int>();
        }

        public TestBinding(int context, string actionId, string source, params int[] buttons)
        {
            Context = context;
            ActionId = actionId;
            Source = source;
            Buttons = buttons;
        }
    }
}
