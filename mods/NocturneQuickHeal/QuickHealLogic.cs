using System;
using System.Collections.Generic;

namespace NocturneQuickHeal
{
    // Exactly one input source is read per frame: the Controller action
    // (player's key config) when the integration is active, otherwise the
    // game's own SELECT button. Never both, so one press cannot heal twice.
    internal static class QuickHealInput
    {
        internal static bool ReadHeld(bool integrationActive, Func<bool> controllerHeld, Func<bool> standaloneHeld) =>
            integrationActive ? controllerHeld() : standaloneHeld();
    }

    // Sequence state of Quick Heal, moved unchanged from Controller's built-in
    // QuickHealRuntimeProbe. Pure: callers pass in the game/Controller state,
    // so the rules can be unit tested.
    //
    // A press (rising edge) starts one heal sequence. While it is active, one
    // recovery action runs every 250 ms until nothing is left to heal, or 64
    // actions have run.
    internal sealed class QuickHealSequence
    {
        internal const int HealIntervalMilliseconds = 250;
        internal const int MaximumActionsPerSequence = 64;

        private readonly HashSet<int> _ineffectiveHpTargets = new();
        private bool _wasHeld;
        private bool _active;
        private int _nextHealTick;
        private int _actionCount;

        internal bool IsActive => _active;
        internal int ActionCount => _actionCount;

        // A target whose HP did not move after a recovery (its maxhp field can
        // sit above the HP the game will actually restore to) is skipped for
        // the rest of this sequence, so it cannot starve other targets and
        // burn the action limit. Cleared when a new sequence starts.
        internal bool IsHpTargetIneffective(int targetIndex) => _ineffectiveHpTargets.Contains(targetIndex);

        internal void MarkHpTargetIneffective(int targetIndex) => _ineffectiveHpTargets.Add(targetIndex);

        // Controller only sampled Quick Heal while the feature was enabled,
        // field exploration was active and Settings was closed; outside that,
        // nothing (including a running sequence) changes. Returns true when
        // the caller should run the next recovery action now; started is true
        // on the frame a press starts a new sequence. A failing input read
        // skips the frame, as before.
        internal bool Sample(
            bool enabled, bool explorationActive, bool settingsOpen, Func<bool> readHeld, int nowTick, out bool started) =>
            Sample(enabled, explorationActive, settingsOpen, false, readHeld, nowTick, out started, out _);

        // mapOpen (the field auto-map is shown, where the same button also
        // switches floors): a press never STARTS a sequence, but the held
        // state is still recorded, so a button held while the map closes is
        // not seen as a new press - it has to be released and pressed again.
        // A sequence that was already running finishes as usual.
        // suppressedByMap is true on the frame such a press was ignored.
        internal bool Sample(
            bool enabled, bool explorationActive, bool settingsOpen, bool mapOpen, Func<bool> readHeld, int nowTick,
            out bool started, out bool suppressedByMap)
        {
            started = false;
            suppressedByMap = false;
            if (!enabled || !explorationActive || settingsOpen)
            {
                return false;
            }

            bool held;
            try
            {
                held = readHeld();
            }
            catch (Exception)
            {
                return false;
            }

            if (held && !_wasHeld && mapOpen)
            {
                suppressedByMap = true;
            }
            else if (held && !_wasHeld)
            {
                _active = true;
                _actionCount = 0;
                _ineffectiveHpTargets.Clear();
                _nextHealTick = nowTick;
                started = true;
            }
            _wasHeld = held;

            return _active && unchecked(nowTick - _nextHealTick) >= 0;
        }

        // After a recovery action ran. Returns false when the safety limit
        // ended the sequence.
        internal bool ActionPerformed(int nowTick)
        {
            _actionCount++;
            if (_actionCount >= MaximumActionsPerSequence)
            {
                _active = false;
                return false;
            }
            _nextHealTick = unchecked(nowTick + HealIntervalMilliseconds);
            return true;
        }

        internal void Stop()
        {
            _active = false;
        }
    }

    internal static class RecoverySource
    {
        // Reserve demons first, then active party demons, the protagonist
        // (unit 0) last.
        internal static int GetPriority(int sourceIndex, uint flag)
        {
            if (sourceIndex == 0)
            {
                return 2;
            }

            return (flag & 0x2u) == 0 ? 0 : 1;
        }

        internal static string GetKind(int sourcePriority)
        {
            return sourcePriority == 0
                ? "reserve"
                : sourcePriority == 1 ? "active" : "protagonist";
        }
    }

    // Which targets a recovery skill can actually reach. cmpMisc.cmpRecover
    // (ISIL dump, cmpMisc.txt) applies cmpExecRecover to pDst only when
    // cmpGetSkillTargetArea(SkillID) == 1 (single target); for any other
    // area (e.g. メディア) it ignores pDst and heals each active party
    // member (unit flag bit 0x2) instead. A reserve demon can therefore only
    // be healed by a single-target skill - choosing a party-wide one for it
    // changed nothing (2026-10-03 logs: dst=4/6/8/10, メディア, hp unchanged).
    internal static class RecoveryTarget
    {
        internal const int SingleTargetArea = 1;

        internal static bool IsInActiveParty(uint flag) => (flag & 0x2u) != 0;

        internal static bool CanReach(int targetArea, bool targetInParty) =>
            targetArea == SingleTargetArea || targetInParty;
    }

    // Ranking of one HP recovery option for one target.
    internal readonly struct RecoveryRank
    {
        internal RecoveryRank(int sourcePriority, int effect, int cost, int projectedCost, int projectedOverheal)
        {
            SourcePriority = sourcePriority;
            Effect = effect;
            Cost = cost;
            ProjectedCost = projectedCost;
            ProjectedOverheal = projectedOverheal;
        }

        internal int SourcePriority { get; }
        internal int Effect { get; }
        internal int Cost { get; }
        internal int ProjectedCost { get; }
        internal int ProjectedOverheal { get; }

        // effect > 0 and cost >= 0. Casting until the missing HP is covered;
        // overflow throws, and the caller skips that skill.
        internal static RecoveryRank Create(int sourcePriority, int effect, int cost, int missingHp)
        {
            int casts = Math.Max(1, (missingHp + effect - 1) / effect);
            return new RecoveryRank(
                sourcePriority,
                effect,
                cost,
                checked(cost * casts),
                checked((effect * casts) - missingHp));
        }

        internal bool IsBetterThan(RecoveryRank other)
        {
            if (SourcePriority != other.SourcePriority)
            {
                return SourcePriority < other.SourcePriority;
            }
            if (ProjectedCost != other.ProjectedCost)
            {
                return ProjectedCost < other.ProjectedCost;
            }
            if (ProjectedOverheal != other.ProjectedOverheal)
            {
                return ProjectedOverheal < other.ProjectedOverheal;
            }
            if (Cost != other.Cost)
            {
                return Cost < other.Cost;
            }
            return Effect > other.Effect;
        }
    }
}
