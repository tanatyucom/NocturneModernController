using System;
using System.Collections.Generic;

namespace NocturneModernController
{
    internal sealed class ActionRegistrySnapshot<TAction>
    {
        public int SchemaVersion { get; set; } = 1;
        public List<TAction> Actions { get; set; } = new();
        public bool GameBindingsAvailable { get; set; }
        public List<GameBindingSnapshotEntry> GameBindings { get; set; } = new();
        public bool GameActionBindingsAvailable { get; set; }
        public List<GameActionBindingRawEntry> GameActionBindingsRaw { get; set; } = new();
    }

    internal sealed class GameBindingSnapshotEntry
    {
        public string ControllerId { get; set; } = string.Empty;
        public string KeyId { get; set; } = string.Empty;
        public string CurrentButton { get; set; } = string.Empty;
        public bool ReadOnly { get; set; } = true;
    }

    internal sealed class GameBindingSnapshotResult
    {
        internal bool Available { get; init; }
        internal IReadOnlyList<GameBindingSnapshotEntry> Bindings { get; init; } =
            Array.Empty<GameBindingSnapshotEntry>();
    }

    internal static class GameBindingSnapshotReader
    {
        // Confirmed InputAssign.KeyID values only. Semantic action names are intentionally absent.
        private static readonly (int Value, string Name)[] PhysicalKeys =
        {
            (0, "A"), (1, "B"), (2, "X"), (3, "Y"),
            (4, "R1"), (5, "L1"), (6, "START"),
            (7, "UP"), (8, "DOWN"), (9, "LEFT"), (10, "RIGHT"),
            (23, "R3"), (24, "L3"), (25, "SELECT"),
            (26, "R2"), (27, "L2")
        };

        internal static GameBindingSnapshotResult Capture(
            Func<int, int, string> getAssignCode)
        {
            try
            {
                var bindings = new List<GameBindingSnapshotEntry>();
                foreach ((int value, string name) in PhysicalKeys)
                {
                    bindings.Add(new GameBindingSnapshotEntry
                    {
                        ControllerId = "PAD1",
                        KeyId = name,
                        CurrentButton = getAssignCode(0, value),
                        ReadOnly = true
                    });
                }
                return new GameBindingSnapshotResult
                {
                    Available = true,
                    Bindings = bindings
                };
            }
            catch
            {
                return new GameBindingSnapshotResult();
            }
        }
    }

    // SSoT candidate confirmed 2026-09-21 by real-hardware A/B/A against the
    // native Controller Key Config (SESSION_RESUME_NOTES.md §17):
    // dds3ConfigGamePadSteam.GetConfigGamePad(index) is the current GAME
    // binding read path. This is a distinct index space (0..0x21, 34 slots)
    // from GameBindingSnapshotReader's InputAssign.GetAssignCode-based
    // low-level snapshot above; the two are not interchangeable and both are
    // kept, serving different diagnostic/SSoT roles.
    internal sealed class GameActionBindingRawEntry
    {
        public int Index { get; set; }
        public int RawValue { get; set; }

        // Populated only for indices independently confirmed via A/B testing
        // against the native GUI. Null/empty means "not yet confirmed" --
        // never inferred from enum casts or numeric coincidence.
        public string? ConfirmedActionName { get; set; }
        public string? ConfirmedPhysicalButton { get; set; }
    }

    internal sealed class GameActionBindingSnapshotResult
    {
        internal bool Available { get; init; }
        internal IReadOnlyList<GameActionBindingRawEntry> Entries { get; init; } =
            Array.Empty<GameActionBindingRawEntry>();
    }

    internal static class GameActionBindingSnapshotReader
    {
        // Confirmed 2026-09-21: at OnInitializeMelon time (before the native
        // GAME state is fully constructed), indices 0..30 throw
        // NullReferenceException and only 31..33 succeed (raw=0). Readers of
        // this snapshot must treat Entries.Count < SlotCount as "not ready
        // yet", not as a partial-but-usable result (see ModernControllerApi's
        // readiness retry).
        internal const int SlotCount = 0x22;

        // Confirmed 2026-09-21 by real-hardware A/B/A against the native
        // Controller Key Config screen, each independently verified by
        // changing one action, confirming exactly the expected index(es)
        // changed, then reverting and confirming the diff returned to zero.
        // Full evidence trail: investigations/GAMEBINDING_INDEX_MAP_20260921.md,
        // SESSION_RESUME_NOTES.md §17-21. Do not add further entries without
        // the same independent A/B/A confirmation; do not infer entries from
        // numeric coincidence between indices.
        private static readonly IReadOnlyDictionary<int, string> ConfirmedActionNames =
            new Dictionary<int, string>
            {
                [4] = "決定・アクション",
                [5] = "キャンセル",
                [6] = "UI表示ON/OFF",
                [7] = "コマンドメニュー",
                [12] = "視点変更（左回転）",
                [13] = "視点変更（右回転）",
                [14] = "視点を正面に戻す",
                [16] = "オートマップ表示",
                [17] = "スキルヘルプON/OFF",
                [18] = "オートバトル",
                [19] = "次に回す",
                [20] = "テキストの早送り",
                [23] = "メニュー",
            };

        // Raw -> physical-button values, confirmed per-index only via the
        // same A/B/A evidence as ConfirmedActionNames above. A raw value
        // appearing identical across indices (e.g. 11=Y at indices 7/18/20)
        // is NOT assumed to generalize to indices without independent
        // confirmation -- each entry here was individually verified.
        private static readonly IReadOnlyDictionary<int, IReadOnlyDictionary<int, string>>
            ConfirmedPhysicalButtonsByIndex = new Dictionary<int, IReadOnlyDictionary<int, string>>
            {
                [4] = new Dictionary<int, string> { [10] = "A", [12] = "X" },
                [5] = new Dictionary<int, string> { [9] = "B", [16] = "RT" },
                [6] = new Dictionary<int, string> { [17] = "L3", [26] = "START" },
                [7] = new Dictionary<int, string> { [11] = "Y", [12] = "X" },
                [12] = new Dictionary<int, string> { [13] = "LB", [14] = "LT" },
                [13] = new Dictionary<int, string> { [15] = "RB", [16] = "RT" },
                [14] = new Dictionary<int, string> { [9] = "B", [15] = "RB" },
                [16] = new Dictionary<int, string> { [26] = "START", [13] = "LB" },
                [17] = new Dictionary<int, string> { [25] = "SELECT", [16] = "RT" },
                [18] = new Dictionary<int, string> { [11] = "Y", [12] = "X" },
                [19] = new Dictionary<int, string> { [15] = "RB", [26] = "START" },
                [20] = new Dictionary<int, string> { [11] = "Y", [12] = "X" },
                [23] = new Dictionary<int, string> { [12] = "X", [10] = "A" },
            };

        internal static GameActionBindingSnapshotResult Capture(Func<int, int> getConfigGamePad)
        {
            var entries = new List<GameActionBindingRawEntry>();
            for (int index = 0; index < SlotCount; index++)
            {
                try
                {
                    int raw = getConfigGamePad(index);
                    ConfirmedActionNames.TryGetValue(index, out string? actionName);
                    string? physicalButton = null;
                    if (ConfirmedPhysicalButtonsByIndex.TryGetValue(index, out IReadOnlyDictionary<int, string>? buttonMap))
                    {
                        buttonMap.TryGetValue(raw, out physicalButton);
                    }
                    entries.Add(new GameActionBindingRawEntry
                    {
                        Index = index,
                        RawValue = raw,
                        ConfirmedActionName = actionName,
                        ConfirmedPhysicalButton = physicalButton
                    });
                }
                catch
                {
                    // A single slot failing does not abort the rest of the sweep.
                    // Individual-index failures were root-caused (2026-09-21,
                    // SESSION_RESUME_NOTES.md §18): they occur when captured
                    // before the native GAME state is constructed. Callers
                    // detect this via Entries.Count < SlotCount rather than
                    // per-index logging here.
                }
            }
            return new GameActionBindingSnapshotResult
            {
                Available = entries.Count > 0,
                Entries = entries
            };
        }
    }
}
