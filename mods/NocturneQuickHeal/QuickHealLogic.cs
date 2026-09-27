using System;

namespace NocturneQuickHeal
{
    // Exactly one input source is read per frame: the Controller action
    // (player's key config) when the integration is active, otherwise the
    // game's own RB button. Never both, so one press cannot heal twice.
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

        private bool _wasHeld;
        private bool _active;
        private int _nextHealTick;
        private int _actionCount;

        internal bool IsActive => _active;
        internal int ActionCount => _actionCount;

        // Controller only sampled Quick Heal while the feature was enabled,
        // field exploration was active and Settings was closed; outside that,
        // nothing (including a running sequence) changes. Returns true when
        // the caller should run the next recovery action now; started is true
        // on the frame a press starts a new sequence. A failing input read
        // skips the frame, as before.
        internal bool Sample(
            bool enabled, bool explorationActive, bool settingsOpen, Func<bool> readHeld, int nowTick, out bool started)
        {
            started = false;
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

            if (held && !_wasHeld)
            {
                _active = true;
                _actionCount = 0;
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
