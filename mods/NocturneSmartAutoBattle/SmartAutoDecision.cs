using System;
using System.Collections.Generic;

namespace NocturneSmartAutoBattle
{
    // What the decision needs from the running battle. The game implementation
    // (GameBattleView) forwards each call to the matching native function, so
    // the decision below makes exactly the calls, in exactly the order, that
    // Controller's built-in SmartAutoBattleTelemetry.DumpSkillCandidates made.
    internal interface ISmartAutoBattleView
    {
        // False when the acting unit is not initialized yet.
        bool TryGetSource(out SmartAutoSource source);
        int EnemySlotCount { get; }
        // Null for an empty slot.
        SmartAutoEnemy? GetEnemy(int enemyIndex);
        int OrderIndex { get; }
        int OrderLength { get; }
        int OrderAt(int index);
        uint GetAisyo(int skill, int formIndex, int attribute);
        float GetAisyoRitu(int skill, int sourceForm, int destinationForm);
        int GetButuriAttack(int skill, int sourceForm, int destinationForm, int hpN);
        int GetMagicAttack(int skill, int sourceForm, int destinationForm, int hpN);
        int CheckSkillUse(int sourceForm, int skill);
        int CheckSkillCost(int sourceForm, int skill);
        int GetSkillCost(int skill);
        int CheckSingleTargetSkill(int skill);
        int GetNormalSkillAttr(int skill);
        int GetSkillHpN(int skill);
    }

    internal sealed class SmartAutoSource
    {
        public int FormIndex;
        public int UnitId;
        public int Hp;
        public int MaxHp;
        public int Mp;
        public int MaxMp;
        public int[] Skills = Array.Empty<int>();
    }

    internal sealed class SmartAutoEnemy
    {
        public int Id;
        public int Hp;
    }

    // Skill > 0: that skill; -2: Attack; -1: Pass; 0: no recommendation
    // (the game's own Auto choice is kept).
    internal readonly struct SmartAutoRecommendation
    {
        internal const int Attack = -2;
        internal const int Pass = -1;
        internal const int None = 0;

        internal SmartAutoRecommendation(int skill, bool single, int sourceForm, int targetForm)
        {
            Skill = skill;
            Single = single;
            SourceForm = sourceForm;
            TargetForm = targetForm;
        }

        internal int Skill { get; }
        internal bool Single { get; }
        internal int SourceForm { get; }
        internal int TargetForm { get; }

        internal static SmartAutoRecommendation NoRecommendation => new(None, false, -1, -1);
    }

    // Skill Priority decision, moved unchanged from Controller's built-in
    // SmartAutoBattleTelemetry (DumpSkillCandidates and
    // TryForecastRemainingBasicAttacks):
    // 1. Attack when a safe basic attack kills an enemy, or the party's basic
    //    attacks in turn order are forecast to clear the battle.
    // 2. Otherwise the best-scoring offensive skill (known weakness, unknown
    //    affinity to explore, almighty, efficient normal/resist damage, MP).
    // 3. Otherwise Attack when a basic attack is safe, else Pass.
    // Enemies occupy form indices 4.., the party 0..3. Only affinities recorded
    // in the knowledge store count as known.
    internal static class SmartAutoDecision
    {
        internal static SmartAutoRecommendation Evaluate(
            ISmartAutoBattleView view,
            Func<int, int, bool> isKnown,
            Action<string> log)
        {
            try
            {
                if (!view.TryGetSource(out SmartAutoSource source))
                {
                    log("candidates unavailable; source not initialized.");
                    return SmartAutoRecommendation.NoRecommendation;
                }

                int sourceFormIndex = source.FormIndex;
                int skillCount = source.Skills.Length;
                log($"candidate source; form={sourceFormIndex} unit={source.UnitId} " +
                    $"hp={source.Hp}/{source.MaxHp} mp={source.Mp}/{source.MaxMp} skills={skillCount}.");

                int bestSkill = 0;
                int bestScore = int.MinValue;
                uint bestTargetMask = 0;
                int bestWeakCount = 0;
                int bestKillCount = 0;
                bool bestIsSingle = false;
                bool bestIsExploration = false;
                bool basicCanKill = false;
                bool basicIsSafe = false;
                int bestBasicDamage = 0;
                int basicKillCount = 0;
                int livingEnemyCount = 0;

                for (int enemyIndex = 0; enemyIndex < view.EnemySlotCount; enemyIndex++)
                {
                    SmartAutoEnemy? enemy = view.GetEnemy(enemyIndex);
                    if (enemy == null || enemy.Hp == 0)
                    {
                        continue;
                    }
                    livingEnemyCount++;
                    int destinationFormIndex = enemyIndex + 4;
                    uint affinity = view.GetAisyo(0, destinationFormIndex, 0);
                    string affinityName = DecodeAffinity(affinity);
                    bool known = isKnown(enemy.Id, 0);
                    if (known && IsUnsafeAffinity(affinityName))
                    {
                        continue;
                    }
                    int rawDamage = view.GetButuriAttack(0, sourceFormIndex, destinationFormIndex, 0);
                    float ratio = view.GetAisyoRitu(0, sourceFormIndex, destinationFormIndex);
                    int damage = Math.Max(0, (int)Math.Floor(rawDamage * ratio));
                    basicIsSafe = true;
                    bestBasicDamage = Math.Max(bestBasicDamage, damage);
                    if (damage >= enemy.Hp)
                    {
                        basicCanKill = true;
                        basicKillCount++;
                    }
                }

                bool partyBasicCanClear = TryForecastRemainingBasicAttacks(
                    view,
                    isKnown,
                    log,
                    sourceFormIndex,
                    out int forecastActorCount,
                    out int forecastDefeatedCount,
                    out int forecastRemainingHp,
                    out string forecastOrder);

                for (int skillIndex = 0; skillIndex < skillCount; skillIndex++)
                {
                    ushort skillId = unchecked((ushort)source.Skills[skillIndex]);
                    int usable;
                    int costCheck;
                    int cost;
                    int single;
                    int attribute;
                    int hpN;
                    try
                    {
                        usable = view.CheckSkillUse(sourceFormIndex, skillId);
                        costCheck = view.CheckSkillCost(sourceFormIndex, skillId);
                        cost = view.GetSkillCost(skillId);
                        single = view.CheckSingleTargetSkill(skillId);
                        attribute = view.GetNormalSkillAttr(skillId);
                        hpN = view.GetSkillHpN(skillId);
                    }
                    catch (Exception exception)
                    {
                        log($"candidate; skill={skillId} inspect={exception.GetType().Name}.");
                        continue;
                    }

                    bool groupKnownUnsafe = false;
                    int knownWeakCount = 0;
                    int unknownCount = 0;
                    int knownNormalCount = 0;
                    int knownResistCount = 0;
                    uint allTargetsMask = 0;
                    uint preferredSingleTarget = 0;
                    int preferredSingleHp = int.MaxValue;
                    uint preferredUnknownTarget = 0;
                    int preferredUnknownHp = int.MaxValue;
                    uint preferredNormalTarget = 0;
                    int preferredNormalHp = int.MaxValue;
                    uint preferredResistTarget = 0;
                    int preferredResistHp = int.MaxValue;
                    uint preferredAnyTarget = 0;
                    int preferredAnyHp = int.MaxValue;
                    int predictedKillCount = 0;
                    int bestSkillDamage = 0;
                    int totalSkillDamage = 0;
                    int bestComparableBasicDamage = 0;
                    bool skillOutdamagesBasic = false;
                    for (int enemyIndex = 0; enemyIndex < view.EnemySlotCount; enemyIndex++)
                    {
                        SmartAutoEnemy? enemy = view.GetEnemy(enemyIndex);
                        if (enemy == null || enemy.Hp == 0)
                        {
                            continue;
                        }

                        int destinationFormIndex = enemyIndex + 4;
                        uint destinationMask = 1u << destinationFormIndex;
                        allTargetsMask |= destinationMask;
                        if (enemy.Hp < preferredAnyHp)
                        {
                            preferredAnyHp = enemy.Hp;
                            preferredAnyTarget = destinationMask;
                        }
                        try
                        {
                            float ratio = view.GetAisyoRitu(skillId, sourceFormIndex, destinationFormIndex);
                            uint affinity = view.GetAisyo(skillId, destinationFormIndex, attribute);
                            float attackRatio = view.GetAisyoRitu(0, sourceFormIndex, destinationFormIndex);
                            int physicalEstimateHpN = view.GetButuriAttack(
                                skillId, sourceFormIndex, destinationFormIndex, hpN);
                            int magicEstimateHpN = view.GetMagicAttack(
                                skillId, sourceFormIndex, destinationFormIndex, hpN);
                            int basicPhysicalEstimate0 = view.GetButuriAttack(
                                0, sourceFormIndex, destinationFormIndex, 0);
                            string affinityName = DecodeAffinity(affinity);
                            bool affinityKnown = isKnown(enemy.Id, attribute);
                            bool unsafeAffinity = IsUnsafeAffinity(affinityName);
                            if (affinityKnown && unsafeAffinity)
                            {
                                groupKnownUnsafe = true;
                            }
                            if (!affinityKnown)
                            {
                                unknownCount++;
                                if (enemy.Hp < preferredUnknownHp)
                                {
                                    preferredUnknownHp = enemy.Hp;
                                    preferredUnknownTarget = destinationMask;
                                }
                            }
                            else if (affinityName == "Weak")
                            {
                                knownWeakCount++;
                                if (enemy.Hp < preferredSingleHp)
                                {
                                    preferredSingleHp = enemy.Hp;
                                    preferredSingleTarget = destinationMask;
                                }
                            }
                            else if (affinityName == "Normal")
                            {
                                knownNormalCount++;
                                if (enemy.Hp < preferredNormalHp)
                                {
                                    preferredNormalHp = enemy.Hp;
                                    preferredNormalTarget = destinationMask;
                                }
                            }
                            else if (affinityName == "Resist")
                            {
                                knownResistCount++;
                                if (enemy.Hp < preferredResistHp)
                                {
                                    preferredResistHp = enemy.Hp;
                                    preferredResistTarget = destinationMask;
                                }
                            }
                            int rawSkillDamage = attribute == 0
                                ? physicalEstimateHpN
                                : attribute >= 1 && attribute <= 7
                                    ? magicEstimateHpN
                                    : 0;
                            int predictedSkillDamage = Math.Max(
                                0, (int)Math.Floor(rawSkillDamage * ratio));
                            int predictedBasicDamage = Math.Max(
                                0, (int)Math.Floor(basicPhysicalEstimate0 * attackRatio));
                            if (!(affinityKnown && unsafeAffinity))
                            {
                                bestSkillDamage = Math.Max(bestSkillDamage, predictedSkillDamage);
                                totalSkillDamage += predictedSkillDamage;
                                bestComparableBasicDamage = Math.Max(
                                    bestComparableBasicDamage, predictedBasicDamage);
                                if (predictedSkillDamage > predictedBasicDamage)
                                {
                                    skillOutdamagesBasic = true;
                                }
                                if (predictedSkillDamage >= enemy.Hp)
                                {
                                    predictedKillCount++;
                                }
                            }
                        }
                        catch (Exception)
                        {
                            // Ignore a malformed target and continue evaluating
                            // the remaining living enemies.
                        }
                    }

                    bool isSingle = single != 0;
                    if (isSingle && predictedKillCount > 1)
                    {
                        predictedKillCount = 1;
                    }
                    bool offensiveAttribute = attribute >= 0 && attribute < 7;
                    bool almightyAttribute = attribute == 7;
                    bool hasKnownWeakTarget = knownWeakCount > 0 &&
                                              (isSingle ? preferredSingleTarget != 0 : !groupKnownUnsafe);
                    bool canExploreUnknown = unknownCount > 0 &&
                                             (isSingle ? preferredUnknownTarget != 0 : !groupKnownUnsafe);
                    bool canUseAlmighty = almightyAttribute && preferredAnyTarget != 0;
                    bool hasKnownNormalTarget = knownNormalCount > 0 &&
                                                (isSingle ? preferredNormalTarget != 0 : !groupKnownUnsafe);
                    bool hasKnownResistTarget = knownResistCount > 0 &&
                                                (isSingle ? preferredResistTarget != 0 : !groupKnownUnsafe);
                    bool meaningfulSingleAdvantage = skillOutdamagesBasic &&
                        bestSkillDamage >= bestComparableBasicDamage + Math.Max(
                            5, bestComparableBasicDamage / 5);
                    bool meaningfulGroupAdvantage = !isSingle &&
                        totalSkillDamage >= bestComparableBasicDamage * 2;
                    bool efficientNonWeakSkill = meaningfulSingleAdvantage ||
                                                  meaningfulGroupAdvantage;
                    int mpAfterUse = Math.Max(0, source.Mp - cost);
                    bool preservesReserve = source.MaxMp == 0 ||
                                            mpAfterUse * 2 >= source.MaxMp;
                    bool tacticalException = hasKnownWeakTarget ||
                                              canExploreUnknown ||
                                              predictedKillCount >= 3 ||
                                              cost == 0;
                    if (usable == 0 && costCheck == 0 &&
                        (preservesReserve || tacticalException) &&
                        ((offensiveAttribute && (hasKnownWeakTarget || canExploreUnknown ||
                         (efficientNonWeakSkill &&
                          (hasKnownNormalTarget || hasKnownResistTarget)))) ||
                         (canUseAlmighty && efficientNonWeakSkill)))
                    {
                        bool exploration = offensiveAttribute && !hasKnownWeakTarget && canExploreUnknown;
                        int groupBonus = !isSingle && knownWeakCount >= 2 ? 100 : 0;
                        int mpCostPercent = source.MaxMp > 0
                            ? (cost * 1000) / source.MaxMp
                            : cost;
                        int score = (predictedKillCount * 10000000) + (hasKnownWeakTarget
                            ? 1000000 + (knownWeakCount * 1000) + groupBonus - cost
                            : exploration
                                ? 10000 + (isSingle ? 100 : 0) - cost
                                : canUseAlmighty
                                    ? 1000 - cost
                                    : hasKnownNormalTarget
                                        ? 900 - cost
                                        : 800 - cost) - mpCostPercent;
                        if (score > bestScore)
                        {
                            bestScore = score;
                            bestSkill = skillId;
                            bestTargetMask = isSingle
                                ? (canUseAlmighty
                                    ? preferredAnyTarget
                                    : exploration
                                        ? preferredUnknownTarget
                                        : hasKnownNormalTarget
                                            ? preferredNormalTarget
                                            : hasKnownResistTarget
                                                ? preferredResistTarget
                                                : preferredSingleTarget)
                                : allTargetsMask;
                            bestWeakCount = knownWeakCount;
                            bestKillCount = predictedKillCount;
                            bestIsSingle = isSingle;
                            bestIsExploration = exploration;
                        }
                    }
                }

                if (basicIsSafe && (basicCanKill || partyBasicCanClear))
                {
                    log("recommendation; " +
                        $"Attack reason={(basicCanKill ? "basic-kill" : "party-basic-clear")} " +
                        $"damage={bestBasicDamage} " +
                        $"basicKillable={basicKillCount}/{livingEnemyCount} " +
                        $"partyForecast={forecastDefeatedCount}/{livingEnemyCount} " +
                        $"forecastActors={forecastActorCount} remainingHp={forecastRemainingHp} " +
                        $"order=[{forecastOrder}] " +
                        $"bestSkillKills={bestKillCount}.");
                    return new SmartAutoRecommendation(SmartAutoRecommendation.Attack, false, sourceFormIndex, -1);
                }

                if (bestSkill == 0)
                {
                    log(basicIsSafe
                        ? $"recommendation; Attack reason=no-usable-skill damage={bestBasicDamage}."
                        : "recommendation; Pass reason=no-safe-attack.");
                    return new SmartAutoRecommendation(
                        basicIsSafe ? SmartAutoRecommendation.Attack : SmartAutoRecommendation.Pass,
                        false, sourceFormIndex, -1);
                }

                log("recommendation; " +
                    $"skill={bestSkill} targetMask=0x{bestTargetMask:X8} " +
                    $"weakTargets={bestWeakCount} single={bestIsSingle} " +
                    $"exploration={bestIsExploration} predictedKills={bestKillCount} " +
                    $"basicKillable={basicKillCount}/{livingEnemyCount} " +
                    $"score={bestScore}.");
                return new SmartAutoRecommendation(
                    bestSkill,
                    bestIsSingle,
                    sourceFormIndex,
                    bestIsSingle ? BitMaskToFormIndex(bestTargetMask) : -1);
            }
            catch (Exception exception)
            {
                log("candidate evaluation failed: " + exception.GetType().Name + ": " + exception.Message);
                return SmartAutoRecommendation.NoRecommendation;
            }
        }

        private sealed class BasicForecastTarget
        {
            internal int FormIndex;
            internal int DemonId;
            internal int RemainingHp;
        }

        private static bool TryForecastRemainingBasicAttacks(
            ISmartAutoBattleView view,
            Func<int, int, bool> isKnown,
            Action<string> log,
            int currentFormIndex,
            out int actorCount,
            out int defeatedCount,
            out int remainingHp,
            out string orderText)
        {
            actorCount = 0;
            defeatedCount = 0;
            remainingHp = 0;
            orderText = currentFormIndex.ToString();
            try
            {
                var actorForms = new List<int> { currentFormIndex };
                var seenActors = new HashSet<int> { currentFormIndex };
                int orderIndex = view.OrderIndex;
                for (int i = Math.Max(0, orderIndex + 1); i < view.OrderLength; i++)
                {
                    int formIndex = view.OrderAt(i);
                    if (formIndex >= 0 && formIndex < 4 && seenActors.Add(formIndex))
                    {
                        actorForms.Add(formIndex);
                    }
                }

                actorCount = actorForms.Count;
                orderText = string.Join(",", actorForms);
                var targets = new List<BasicForecastTarget>();
                for (int enemyIndex = 0; enemyIndex < view.EnemySlotCount; enemyIndex++)
                {
                    SmartAutoEnemy? enemy = view.GetEnemy(enemyIndex);
                    if (enemy == null || enemy.Hp == 0)
                    {
                        continue;
                    }

                    targets.Add(new BasicForecastTarget
                    {
                        FormIndex = enemyIndex + 4,
                        DemonId = enemy.Id,
                        RemainingHp = enemy.Hp
                    });
                }

                int initialTargetCount = targets.Count;
                foreach (int actorFormIndex in actorForms)
                {
                    BasicForecastTarget? selected = null;
                    int selectedDamage = 0;
                    bool selectedCanKill = false;
                    foreach (BasicForecastTarget target in targets)
                    {
                        uint affinity = view.GetAisyo(0, target.FormIndex, 0);
                        string affinityName = DecodeAffinity(affinity);
                        bool known = isKnown(target.DemonId, 0);
                        if (known && IsUnsafeAffinity(affinityName))
                        {
                            continue;
                        }

                        int rawDamage = view.GetButuriAttack(0, actorFormIndex, target.FormIndex, 0);
                        float ratio = view.GetAisyoRitu(0, actorFormIndex, target.FormIndex);
                        int damage = Math.Max(0, (int)Math.Floor(rawDamage * ratio));
                        bool canKill = damage >= target.RemainingHp;
                        if (selected == null ||
                            (canKill && !selectedCanKill) ||
                            (canKill == selectedCanKill && target.RemainingHp < selected.RemainingHp))
                        {
                            selected = target;
                            selectedDamage = damage;
                            selectedCanKill = canKill;
                        }
                    }

                    if (selected == null)
                    {
                        continue;
                    }

                    selected.RemainingHp = Math.Max(0, selected.RemainingHp - selectedDamage);
                    if (selected.RemainingHp == 0)
                    {
                        targets.Remove(selected);
                    }
                }

                defeatedCount = initialTargetCount - targets.Count;
                foreach (BasicForecastTarget target in targets)
                {
                    remainingHp += target.RemainingHp;
                }
                return initialTargetCount > 0 && targets.Count == 0;
            }
            catch (Exception exception)
            {
                log($"party forecast unavailable; {exception.GetType().Name}.");
                return false;
            }
        }

        internal static string DecodeAffinity(uint affinity)
        {
            if ((affinity & 0x80000000u) != 0) return "Weak";
            if ((affinity & 0x00040000u) != 0) return "Drain";
            if ((affinity & 0x00020000u) != 0) return "Reflect";
            if ((affinity & 0x00010000u) != 0 || affinity == 0) return "Null";
            if (affinity < 100u) return "Resist";
            return "Normal";
        }

        private static bool IsUnsafeAffinity(string affinityName) =>
            affinityName == "Null" || affinityName == "Reflect" || affinityName == "Drain";

        internal static int BitMaskToFormIndex(uint mask)
        {
            for (int index = 0; index < 32; index++)
            {
                if ((mask & (1u << index)) != 0)
                {
                    return index;
                }
            }
            return -1;
        }
    }
}
