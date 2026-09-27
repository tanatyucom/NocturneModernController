using System;
using Il2Cpp;
using MelonLoader;

namespace NocturneQuickHeal
{
    // The heal itself, moved unchanged from Controller's built-in
    // QuickHealRuntimeProbe: revive first, then HP (best-ranked learned skill
    // per target), then curable ailments, one action per step, using the
    // game's own recovery (cmpMisc.cmpRecover) and deducting the real MP cost.
    // The former start-of-sequence roster dump (FormationFlagProbe.DumpRoster)
    // was diagnostic logging only and is not carried over.
    internal static class QuickHealRuntime
    {
        private const string LogPrefix = "[NocturneQuickHeal] ";

        internal static QuickHealSequence Sequence { get; } = new();

        internal static void Sample(bool enabled, bool explorationActive, bool settingsOpen, Func<bool> readHeld)
        {
            bool runHeal = Sequence.Sample(
                enabled, explorationActive, settingsOpen, readHeld, Environment.TickCount, out bool started);
            if (started)
            {
                MelonLogger.Msg(LogPrefix + "Q7 AUTO-HEAL started.");
            }
            if (runHeal)
            {
                RunNextHeal();
            }
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
                                RecoverySource.GetPriority(sourceIndex, source.flag) != sourcePriority)
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
                                        LogPrefix + "Q7 AUTO-REVIVE " +
                                        $"sourceKind={RecoverySource.GetKind(sourcePriority)} " +
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
                    if (target.hp != hpBefore && candidate.Rank.Cost > 0 &&
                        candidate.Source.mp >= candidate.Rank.Cost)
                    {
                        candidate.Source.mp = unchecked(
                            (ushort)(candidate.Source.mp - candidate.Rank.Cost));
                    }
                    MelonLogger.Msg(
                        LogPrefix + "Q7 AUTO-RECOVER " +
                        $"sourceKind={RecoverySource.GetKind(candidate.Rank.SourcePriority)} " +
                        $"skill={candidate.SkillId} src={candidate.SourceIndex} dst={targetIndex} " +
                        $"hp={hpBefore}->{target.hp}/{target.maxhp} " +
                        $"effect={candidate.Rank.Effect} projectedCost={candidate.Rank.ProjectedCost} " +
                        $"cost={candidate.Rank.Cost} mp={mpBefore}->{candidate.Source.mp}");

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
                                RecoverySource.GetPriority(sourceIndex, source.flag) != sourcePriority)
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
                                        LogPrefix + "Q7 AUTO-CURE " +
                                        $"sourceKind={RecoverySource.GetKind(sourcePriority)} " +
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
                    Sequence.ActionCount == 0
                        ? "no wounded/ailing target or usable recovery skill"
                        : "all reachable HP/status recovery completed");
            }
            catch (Exception exception)
            {
                Sequence.Stop();
                MelonLogger.Warning(LogPrefix + "Q7 auto-heal failed: " + exception);
            }
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

                int sourcePriority = RecoverySource.GetPriority(sourceIndex, source.flag);
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
                        RecoveryCandidate current = new RecoveryCandidate(
                            source,
                            sourceIndex,
                            skillId,
                            RecoveryRank.Create(sourcePriority, effect, cost, missingHp));

                        if (best == null || current.Rank.IsBetterThan(best.Rank))
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
                ushort skillId,
                RecoveryRank rank)
            {
                Source = source;
                SourceIndex = sourceIndex;
                SkillId = skillId;
                Rank = rank;
            }

            internal Il2Cppnewdata_H.datUnitWork_t Source { get; }
            internal int SourceIndex { get; }
            internal ushort SkillId { get; }
            internal RecoveryRank Rank { get; }
        }

        private static void ScheduleNextAction()
        {
            if (!Sequence.ActionPerformed(Environment.TickCount))
            {
                LogStopped("safety action limit reached");
            }
        }

        private static void StopHealSequence(string reason)
        {
            Sequence.Stop();
            LogStopped(reason);
        }

        private static void LogStopped(string reason)
        {
            MelonLogger.Msg(
                LogPrefix + $"Q7 AUTO-HEAL stopped: {reason}; " +
                $"actions={Sequence.ActionCount}.");
        }
    }
}
