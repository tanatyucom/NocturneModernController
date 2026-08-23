using System;
using System.Text;
using Il2Cpp;
using MelonLoader;

namespace NocturneModernController
{
    internal static class QuickHealRuntimeProbe
    {
        private static bool _wasHeld;
        private static bool _healSequenceActive;
        private static int _nextHealTick;
        private static int _sequenceActionCount;

        private const int HealIntervalMilliseconds = 250;
        private const int MaximumActionsPerSequence = 64;

        internal static void Sample()
        {
            if (!FieldDashPatch.IsExplorationActive)
            {
                _wasHeld = false;
                _healSequenceActive = false;
                return;
            }

            bool held;
            try
            {
                held = ModernControllerApi.IsHeld(
                    BuiltInControllerActions.QuickHeal,
                    ControllerContext.Field);
            }
            catch (Exception)
            {
                return;
            }

            if (held && !_wasHeld)
            {
                StartHealSequence();
            }
            _wasHeld = held;

            if (_healSequenceActive && unchecked(Environment.TickCount - _nextHealTick) >= 0)
            {
                RunNextHeal();
            }
        }

        private static void StartHealSequence()
        {
            _healSequenceActive = true;
            _sequenceActionCount = 0;
            _nextHealTick = Environment.TickCount;
            FormationFlagProbe.DumpRoster();
            MelonLogger.Msg("[NocturneModernController] Q7 AUTO-HEAL started.");
        }

        private static void RunNextHeal()
        {
            try
            {
                Il2Cppdds3GlobalWork_H.dds3GlobalWork_t global = dds3GlobalWork.DDS3_GBWK;
                var stocklist = global.stocklist;
                var units = global.unitwork;

                // Revive first. A successful revival is determined only by the
                // target actually leaving HP=0; effect inspection alone is not
                // trusted because ordinary recovery skills may report a value.
                for (int targetStockIndex = 0; targetStockIndex < global.stockcnt; targetStockIndex++)
                {
                    int targetIndex = stocklist[targetStockIndex];
                    if (targetIndex < 0 || targetIndex >= units.Length)
                    {
                        continue;
                    }

                    Il2Cppnewdata_H.datUnitWork_t? target = units[targetIndex];
                    if (target == null || target.Pointer == IntPtr.Zero || target.hp != 0)
                    {
                        continue;
                    }

                    for (int sourcePriority = 0; sourcePriority < 3; sourcePriority++)
                    {
                        for (int sourceStockIndex = 0; sourceStockIndex < global.stockcnt; sourceStockIndex++)
                        {
                            int sourceIndex = stocklist[sourceStockIndex];
                            if (sourceIndex < 0 || sourceIndex >= units.Length)
                            {
                                continue;
                            }

                            Il2Cppnewdata_H.datUnitWork_t? source = units[sourceIndex];
                            if (source == null || source.Pointer == IntPtr.Zero || source.hp == 0 ||
                                GetSourcePriority(sourceIndex, source) != sourcePriority)
                            {
                                continue;
                            }

                            int skillCount = Math.Min(source.skillcnt, source.skill.Length);
                            for (int skillIndex = 0; skillIndex < skillCount; skillIndex++)
                            {
                                ushort skillId = unchecked((ushort)source.skill[skillIndex]);
                                try
                                {
                                    int effect = datCalc.datGetSkillKouka(skillId, 0, source, target);
                                    if (effect <= 0 || cmpMisc.cmpChkSkillCost(skillId, source) == 0)
                                    {
                                        continue;
                                    }

                                    ushort mpBefore = source.mp;
                                    ushort statusBefore = target.badstatus;
                                    int cost = cmpDrawSkill.cmpGetSkillCost(skillId, source);
                                    cmpMisc.cmpRecover(skillId, source, target);
                                    if (target.hp == 0)
                                    {
                                        continue;
                                    }

                                    if (cost > 0 && source.mp >= cost)
                                    {
                                        source.mp = unchecked((ushort)(source.mp - cost));
                                    }

                                    MelonLogger.Msg(
                                        $"[NocturneModernController] Q7 AUTO-REVIVE " +
                                        $"sourceKind={GetSourceKind(sourcePriority)} " +
                                        $"skill={skillId} src={sourceIndex} dst={targetIndex} " +
                                        $"hp=0->{target.hp}/{target.maxhp} " +
                                        $"bad=0x{statusBefore:X4}->0x{target.badstatus:X4} " +
                                        $"cost={cost} mp={mpBefore}->{source.mp}");

                                    ScheduleNextAction();
                                    return;
                                }
                                catch (Exception)
                                {
                                    // Passive and non-camp skills can reject inspection.
                                }
                            }
                        }
                    }
                }

                for (int targetStockIndex = 0; targetStockIndex < global.stockcnt; targetStockIndex++)
                {
                    int targetIndex = stocklist[targetStockIndex];
                    if (targetIndex < 0 || targetIndex >= units.Length)
                    {
                        continue;
                    }
                    Il2Cppnewdata_H.datUnitWork_t? target = units[targetIndex];
                    if (target == null || target.Pointer == IntPtr.Zero ||
                        target.hp == 0 || target.hp >= target.maxhp)
                    {
                        continue;
                    }

                    RecoveryCandidate? candidate = FindBestHpRecoveryCandidate(
                        global,
                        target,
                        target.maxhp - target.hp);
                    if (candidate == null)
                    {
                        continue;
                    }

                    ushort hpBefore = target.hp;
                    ushort mpBefore = candidate.Source.mp;
                    cmpMisc.cmpRecover(candidate.SkillId, candidate.Source, target);
                    if (target.hp != hpBefore && candidate.Cost > 0 &&
                        candidate.Source.mp >= candidate.Cost)
                    {
                        candidate.Source.mp = unchecked(
                            (ushort)(candidate.Source.mp - candidate.Cost));
                    }
                    MelonLogger.Msg(
                        $"[NocturneModernController] Q7 AUTO-RECOVER " +
                        $"sourceKind={GetSourceKind(candidate.SourcePriority)} " +
                        $"skill={candidate.SkillId} src={candidate.SourceIndex} dst={targetIndex} " +
                        $"hp={hpBefore}->{target.hp}/{target.maxhp} " +
                        $"effect={candidate.Effect} projectedCost={candidate.ProjectedCost} " +
                        $"cost={candidate.Cost} mp={mpBefore}->{candidate.Source.mp}");

                    ScheduleNextAction();
                    return;
                }

                // HP recovery is complete (or currently impossible). Continue
                // the same sequence with curable ailments. cmpChkSkillBad returns
                // the ailment mask handled by the skill.
                for (int targetStockIndex = 0; targetStockIndex < global.stockcnt; targetStockIndex++)
                {
                    int targetIndex = stocklist[targetStockIndex];
                    if (targetIndex < 0 || targetIndex >= units.Length)
                    {
                        continue;
                    }

                    Il2Cppnewdata_H.datUnitWork_t? target = units[targetIndex];
                    if (target == null || target.Pointer == IntPtr.Zero ||
                        target.hp == 0 || target.badstatus == 0)
                    {
                        continue;
                    }

                    for (int sourcePriority = 0; sourcePriority < 3; sourcePriority++)
                    {
                        for (int sourceStockIndex = 0; sourceStockIndex < global.stockcnt; sourceStockIndex++)
                        {
                            int sourceIndex = stocklist[sourceStockIndex];
                            if (sourceIndex < 0 || sourceIndex >= units.Length)
                            {
                                continue;
                            }

                            Il2Cppnewdata_H.datUnitWork_t? source = units[sourceIndex];
                            if (source == null || source.Pointer == IntPtr.Zero || source.hp == 0 ||
                                GetSourcePriority(sourceIndex, source) != sourcePriority)
                            {
                                continue;
                            }

                            int skillCount = Math.Min(source.skillcnt, source.skill.Length);
                            for (int skillIndex = 0; skillIndex < skillCount; skillIndex++)
                            {
                                ushort skillId = unchecked((ushort)source.skill[skillIndex]);
                                try
                                {
                                    uint cureMask = cmpMisc.cmpChkSkillBad(skillId);
                                    if (cureMask == 0 ||
                                        (cureMask & target.badstatus) == 0 ||
                                        cmpMisc.cmpChkSkillCost(skillId, source) == 0)
                                    {
                                        continue;
                                    }

                                    ushort statusBefore = target.badstatus;
                                    ushort mpBefore = source.mp;
                                    int cost = cmpDrawSkill.cmpGetSkillCost(skillId, source);
                                    cmpMisc.cmpRecover(skillId, source, target);
                                    if (target.badstatus == statusBefore)
                                    {
                                        continue;
                                    }

                                    if (cost > 0 && source.mp >= cost)
                                    {
                                        source.mp = unchecked((ushort)(source.mp - cost));
                                    }

                                    MelonLogger.Msg(
                                        $"[NocturneModernController] Q7 AUTO-CURE " +
                                        $"sourceKind={GetSourceKind(sourcePriority)} " +
                                        $"skill={skillId} src={sourceIndex} dst={targetIndex} " +
                                        $"bad=0x{statusBefore:X4}->0x{target.badstatus:X4} " +
                                        $"mask=0x{cureMask:X8} cost={cost} mp={mpBefore}->{source.mp}");

                                    ScheduleNextAction();
                                    return;
                                }
                                catch (Exception)
                                {
                                    // Passive and non-camp skills can reject inspection.
                                }
                            }
                        }
                    }
                }

                StopHealSequence(
                    _sequenceActionCount == 0
                        ? "no wounded/ailing target or usable recovery skill"
                        : "all reachable HP/status recovery completed");
            }
            catch (Exception exception)
            {
                _healSequenceActive = false;
                MelonLogger.Warning("[NocturneModernController] Q7 auto-heal failed: " + exception);
            }
        }

        private static int GetSourcePriority(
            int sourceIndex,
            Il2Cppnewdata_H.datUnitWork_t source)
        {
            if (sourceIndex == 0)
            {
                return 2;
            }

            return (source.flag & 0x2u) == 0 ? 0 : 1;
        }

        private static RecoveryCandidate? FindBestHpRecoveryCandidate(
            Il2Cppdds3GlobalWork_H.dds3GlobalWork_t global,
            Il2Cppnewdata_H.datUnitWork_t target,
            int missingHp)
        {
            var stocklist = global.stocklist;
            var units = global.unitwork;
            RecoveryCandidate? best = null;

            for (int sourceStockIndex = 0; sourceStockIndex < global.stockcnt; sourceStockIndex++)
            {
                int sourceIndex = stocklist[sourceStockIndex];
                if (sourceIndex < 0 || sourceIndex >= units.Length)
                {
                    continue;
                }

                Il2Cppnewdata_H.datUnitWork_t? source = units[sourceIndex];
                if (source == null || source.Pointer == IntPtr.Zero || source.hp == 0)
                {
                    continue;
                }

                int sourcePriority = GetSourcePriority(sourceIndex, source);
                int skillCount = Math.Min(source.skillcnt, source.skill.Length);
                for (int skillIndex = 0; skillIndex < skillCount; skillIndex++)
                {
                    ushort skillId = unchecked((ushort)source.skill[skillIndex]);
                    try
                    {
                        int effect = datCalc.datGetSkillKouka(skillId, 0, source, target);
                        if (effect <= 0 || cmpMisc.cmpChkSkillCost(skillId, source) == 0)
                        {
                            continue;
                        }

                        int cost = Math.Max(0, cmpDrawSkill.cmpGetSkillCost(skillId, source));
                        int casts = Math.Max(1, (missingHp + effect - 1) / effect);
                        int projectedCost = checked(cost * casts);
                        int projectedOverheal = checked((effect * casts) - missingHp);
                        RecoveryCandidate current = new RecoveryCandidate(
                            source,
                            sourceIndex,
                            sourcePriority,
                            skillId,
                            effect,
                            cost,
                            projectedCost,
                            projectedOverheal);

                        if (best == null || current.IsBetterThan(best))
                        {
                            best = current;
                        }
                    }
                    catch (Exception)
                    {
                        // Passive and non-camp skills can reject effect inspection.
                    }
                }
            }

            return best;
        }

        private sealed class RecoveryCandidate
        {
            internal RecoveryCandidate(
                Il2Cppnewdata_H.datUnitWork_t source,
                int sourceIndex,
                int sourcePriority,
                ushort skillId,
                int effect,
                int cost,
                int projectedCost,
                int projectedOverheal)
            {
                Source = source;
                SourceIndex = sourceIndex;
                SourcePriority = sourcePriority;
                SkillId = skillId;
                Effect = effect;
                Cost = cost;
                ProjectedCost = projectedCost;
                ProjectedOverheal = projectedOverheal;
            }

            internal Il2Cppnewdata_H.datUnitWork_t Source { get; }
            internal int SourceIndex { get; }
            internal int SourcePriority { get; }
            internal ushort SkillId { get; }
            internal int Effect { get; }
            internal int Cost { get; }
            internal int ProjectedCost { get; }
            internal int ProjectedOverheal { get; }

            internal bool IsBetterThan(RecoveryCandidate other)
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

        private static string GetSourceKind(int sourcePriority)
        {
            return sourcePriority == 0
                ? "reserve"
                : sourcePriority == 1 ? "active" : "protagonist";
        }

        private static void ScheduleNextAction()
        {
            _sequenceActionCount++;
            if (_sequenceActionCount >= MaximumActionsPerSequence)
            {
                StopHealSequence("safety action limit reached");
            }
            else
            {
                _nextHealTick = unchecked(Environment.TickCount + HealIntervalMilliseconds);
            }
        }

        private static void StopHealSequence(string reason)
        {
            _healSequenceActive = false;
            MelonLogger.Msg(
                $"[NocturneModernController] Q7 AUTO-HEAL stopped: {reason}; " +
                $"actions={_sequenceActionCount}.");
        }
    }
}
