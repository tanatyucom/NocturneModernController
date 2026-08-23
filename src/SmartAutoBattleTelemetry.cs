using HarmonyLib;
using Il2Cpp;
using Il2Cppnewbattle_H;
using MelonLoader;
using System;
using System.Collections.Generic;
using System.Text;

namespace NocturneModernController
{
    internal static class SmartAutoBattleTelemetry
    {
        private static int _autoSession;
        private static int _pollUntilTick;
        private static string _lastSelection = string.Empty;
        private static int _recommendedSkill;
        private static bool _recommendedSingle;
        private static int _recommendedSourceForm = -1;
        private static int _recommendedTargetForm = -1;
        private static int _pendingSingleTargetForm = -1;
        private static uint _pendingSingleTargetMask;
        private static int _pendingSingleSkill;
        private static bool _manualSelectionInProgress;

        internal static void Sample()
        {
            if (unchecked(System.Environment.TickCount - _pollUntilTick) >= 0)
            {
                return;
            }

            string current = DescribeCurrentSelection();
            if (current == _lastSelection)
            {
                return;
            }

            _lastSelection = current;
            MelonLogger.Msg(
                $"[NocturneModernController] SMART-AUTO selection changed; " +
                $"session={_autoSession} [{current}].");
        }

        private static string DescribeSelection(nbCommSelProcessData_t selection)
        {
            try
            {
                nbActionProcessData_t action = selection.act;
                if (action == null || action.Pointer == System.IntPtr.Zero)
                {
                    return $"nowtype={selection.nowtype} nowstat={selection.nowstat} act=null";
                }

                return $"nowtype={selection.nowtype} nowstat={selection.nowstat} " +
                       $"select={action.select} autoskill={action.autoskill} " +
                       $"aiselect={action.aiselect} target={action.target}";
            }
            catch (System.Exception exception)
            {
                return "unavailable: " + exception.GetType().Name;
            }
        }

        [HarmonyPatch(typeof(nbAutoCommProcess), nameof(nbAutoCommProcess.nbInitAutoCommProcess))]
        private static class AutoProcessInitPatch
        {
            private static void Prefix()
            {
                _autoSession++;
                MelonLogger.Msg(
                    $"[NocturneModernController] SMART-AUTO native process initialized; " +
                    $"session={_autoSession}.");
            }
        }

        [HarmonyPatch(typeof(nbCommSelProcess), nameof(nbCommSelProcess.nbSetAutoCommSel))]
        private static class AutoCommandSelectionPatch
        {
            private static bool Prefix(out string __state)
            {
                __state = DescribeCurrentSelection();
                if (_manualSelectionInProgress)
                {
                    return false;
                }
                DumpSkillCandidates();
                if (TrySelectRecommendedCommandThroughManualPath())
                {
                    return false;
                }
                MelonLogger.Msg(
                    $"[NocturneModernController] SMART-AUTO native command selection; " +
                    $"session={_autoSession}.");
                return true;
            }

            private static void Postfix(string __state)
            {
                _lastSelection = string.Empty;
                _pollUntilTick = unchecked(System.Environment.TickCount + 6000);
                MelonLogger.Msg(
                    $"[NocturneModernController] SMART-AUTO selection detail; " +
                    $"before=[{__state}] after=[{DescribeCurrentSelection()}].");
            }
        }

        private static string DescribeCurrentSelection()
        {
            try
            {
                return DescribeSelection(nbCommSelProcess.GetCommSelProcessData());
            }
            catch (System.Exception exception)
            {
                return "getter unavailable: " + exception.GetType().Name;
            }
        }

        [HarmonyPatch(typeof(nbTarSelProcess), nameof(nbTarSelProcess.CursorSelectAuto))]
        private static class AutoTargetSelectionPatch
        {
            private static void Prefix(ref nbTarSelProcessData_t t)
            {
                if (_pendingSingleTargetForm < 0)
                {
                    return;
                }

                int requestedForm = _pendingSingleTargetForm;
                int requestedSkill = _pendingSingleSkill;

                try
                {
                    int matchedIndex = -1;
                    var enemyNumbers = t.enemyno;
                    int count = enemyNumbers == null ? 0 : Math.Min(t.enemycnt, enemyNumbers.Length);
                    var candidates = new StringBuilder();
                    for (int index = 0; index < count; index++)
                    {
                        int form = enemyNumbers![index];
                        if (candidates.Length > 0)
                        {
                            candidates.Append(',');
                        }
                        candidates.Append(index).Append(':').Append(form);
                        if (form == requestedForm || form + 4 == requestedForm)
                        {
                            matchedIndex = index;
                        }
                    }

                    if (matchedIndex >= 0)
                    {
                        t.nowno = unchecked((sbyte)matchedIndex);
                        t.nowform = unchecked((sbyte)requestedForm);
                        MelonLogger.Msg(
                            "[NocturneModernController] SMART-AUTO single target cursor selected; " +
                            $"skill={requestedSkill} requestedForm={requestedForm} " +
                            $"index={matchedIndex} enemies=[{candidates}].");
                    }
                    else
                    {
                        MelonLogger.Warning(
                            "[NocturneModernController] SMART-AUTO single target unavailable; " +
                            $"skill={requestedSkill} requestedForm={requestedForm} " +
                            $"enemies=[{candidates}]; using native target.");
                    }
                }
                catch (Exception exception)
                {
                    MelonLogger.Warning(
                        "[NocturneModernController] SMART-AUTO single target failed; " +
                        "using native target: " + exception.GetType().Name + ": " + exception.Message);
                }
            }
        }

        private static string DescribeAction(nbActionProcessData_t? action)
        {
            try
            {
                if (action == null || action.Pointer == System.IntPtr.Zero)
                {
                    return "act=null";
                }

                return $"select={action.select} autoskill={action.autoskill} " +
                       $"aiselect={action.aiselect} target={action.target}";
            }
            catch (System.Exception exception)
            {
                return "act unavailable: " + exception.GetType().Name;
            }
        }

        [HarmonyPatch(typeof(nbActionProcess), nameof(nbActionProcess.SetAnalyzePacket))]
        private static class AnalyzeKnowledgePatch
        {
            private static void Postfix(int dformindex)
            {
                try
                {
                    int enemyIndex = dformindex - 4;
                    nbMainProcessData_t main = nbMainProcess.nbGetMainProcessData();
                    if (enemyIndex < 0 || enemyIndex >= main.enemyunit.Length)
                    {
                        return;
                    }

                    Il2Cppnewdata_H.datUnitWork_t enemy = main.enemyunit[enemyIndex];
                    if (enemy == null || enemy.Pointer == IntPtr.Zero)
                    {
                        return;
                    }

                    SmartAutoKnowledgeStore.LearnAll(enemy.id);
                    MelonLogger.Msg(
                        "[NocturneModernController] SMART-AUTO Analyze learned all affinities; " +
                        $"form={dformindex} demon={enemy.id}.");
                }
                catch (Exception exception)
                {
                    MelonLogger.Warning(
                        "[NocturneModernController] SMART-AUTO Analyze learning failed: " +
                        exception.GetType().Name + ": " + exception.Message);
                }
            }
        }

        private static string DecodeAffinity(uint affinity)
        {
            if ((affinity & 0x80000000u) != 0) return "Weak";
            if ((affinity & 0x00040000u) != 0) return "Drain";
            if ((affinity & 0x00020000u) != 0) return "Reflect";
            if ((affinity & 0x00010000u) != 0 || affinity == 0) return "Null";
            if (affinity < 100u) return "Resist";
            return "Normal";
        }

        private static bool IsUnsafeAffinity(string affinityName)
        {
            return affinityName == "Null" ||
                   affinityName == "Reflect" ||
                   affinityName == "Drain";
        }

        private static void DumpSkillCandidates()
        {
            _recommendedSkill = 0;
            _recommendedSingle = false;
            _recommendedSourceForm = -1;
            _recommendedTargetForm = -1;
            _pendingSingleTargetForm = -1;
            _pendingSingleTargetMask = 0;
            _pendingSingleSkill = 0;
            try
            {
                nbCommSelProcessData_t selection = nbCommSelProcess.GetCommSelProcessData();
                nbActionProcessData_t action = selection.act;
                Il2Cppnewdata_H.datUnitWork_t? source = action?.work;
                nbFormation_t? sourceForm = action?.form;
                if (source == null || source.Pointer == IntPtr.Zero ||
                    sourceForm == null || sourceForm.Pointer == IntPtr.Zero)
                {
                    MelonLogger.Msg("[NocturneModernController] SMART-AUTO candidates unavailable; source not initialized.");
                    return;
                }

                nbMainProcessData_t main = nbMainProcess.nbGetMainProcessData();
                int sourceFormIndex = sourceForm.formindex;
                int skillCount = Math.Min(source.skillcnt, source.skill.Length);
                MelonLogger.Msg(
                    "[NocturneModernController] SMART-AUTO candidate source; " +
                    $"form={sourceFormIndex} unit={source.id} hp={source.hp}/{source.maxhp} " +
                    $"mp={source.mp}/{source.maxmp} skills={skillCount}.");

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

                for (int enemyIndex = 0; enemyIndex < main.enemyunit.Length; enemyIndex++)
                {
                    Il2Cppnewdata_H.datUnitWork_t enemy = main.enemyunit[enemyIndex];
                    if (enemy == null || enemy.Pointer == IntPtr.Zero || enemy.hp == 0)
                    {
                        continue;
                    }
                    livingEnemyCount++;
                    int destinationFormIndex = enemyIndex + 4;
                    uint affinity = nbCalc.nbGetAisyo(0, destinationFormIndex, 0);
                    string affinityName = DecodeAffinity(affinity);
                    bool known = SmartAutoKnowledgeStore.IsKnown(enemy.id, 0);
                    if (known && IsUnsafeAffinity(affinityName))
                    {
                        continue;
                    }
                    int rawDamage = nbCalc.nbGetButuriAttack(
                        0, sourceFormIndex, destinationFormIndex, 0);
                    float ratio = nbCalc.nbGetAisyoRitu(
                        0, sourceFormIndex, destinationFormIndex);
                    int damage = Math.Max(0, (int)Math.Floor(rawDamage * ratio));
                    basicIsSafe = true;
                    bestBasicDamage = Math.Max(bestBasicDamage, damage);
                    if (damage >= enemy.hp)
                    {
                        basicCanKill = true;
                        basicKillCount++;
                    }
                }

                bool partyBasicCanClear = TryForecastRemainingBasicAttacks(
                    main,
                    sourceFormIndex,
                    out int forecastActorCount,
                    out int forecastDefeatedCount,
                    out int forecastRemainingHp,
                    out string forecastOrder);

                for (int skillIndex = 0; skillIndex < skillCount; skillIndex++)
                {
                    ushort skillId = unchecked((ushort)source.skill[skillIndex]);
                    int usable;
                    int costCheck;
                    int cost;
                    int single;
                    int attribute;
                    int hpN;
                    try
                    {
                        usable = nbCalc.nbCheckSkillUse(sourceFormIndex, skillId);
                        costCheck = nbCalc.nbCheckSkillCost(sourceFormIndex, skillId);
                        cost = cmpDrawSkill.cmpGetSkillCost(skillId, source);
                        single = nbCalc.nbCheckSingleTargetSkill(skillId);
                        attribute = nbCalc.nbGetNormalSkillAttr(skillId);
                        Il2Cppnewdata_H.datNormalSkill_t skillData = datNormalSkill.tbl[skillId];
                        hpN = skillData.hpn;
                    }
                    catch (Exception exception)
                    {
                        MelonLogger.Msg(
                            "[NocturneModernController] SMART-AUTO candidate; " +
                            $"skill={skillId} inspect={exception.GetType().Name}.");
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
                    for (int enemyIndex = 0; enemyIndex < main.enemyunit.Length; enemyIndex++)
                    {
                        Il2Cppnewdata_H.datUnitWork_t enemy = main.enemyunit[enemyIndex];
                        if (enemy == null || enemy.Pointer == IntPtr.Zero || enemy.hp == 0)
                        {
                            continue;
                        }

                        int destinationFormIndex = enemyIndex + 4;
                        uint destinationMask = 1u << destinationFormIndex;
                        allTargetsMask |= destinationMask;
                        if (enemy.hp < preferredAnyHp)
                        {
                            preferredAnyHp = enemy.hp;
                            preferredAnyTarget = destinationMask;
                        }
                        try
                        {
                            float ratio = nbCalc.nbGetAisyoRitu(skillId, sourceFormIndex, destinationFormIndex);
                            uint affinity = nbCalc.nbGetAisyo(skillId, destinationFormIndex, attribute);
                            float attackRatio = nbCalc.nbGetAisyoRitu(
                                0, sourceFormIndex, destinationFormIndex);
                            int physicalEstimateHpN = nbCalc.nbGetButuriAttack(
                                skillId, sourceFormIndex, destinationFormIndex, hpN);
                            int magicEstimateHpN = nbCalc.nbGetMagicAttack(
                                skillId, sourceFormIndex, destinationFormIndex, hpN);
                            int basicPhysicalEstimate0 = nbCalc.nbGetButuriAttack(
                                0, sourceFormIndex, destinationFormIndex, 0);
                            string affinityName = DecodeAffinity(affinity);
                            bool affinityKnown = SmartAutoKnowledgeStore.IsKnown(enemy.id, attribute);
                            bool unsafeAffinity = IsUnsafeAffinity(affinityName);
                            if (affinityKnown && unsafeAffinity)
                            {
                                groupKnownUnsafe = true;
                            }
                            if (!affinityKnown)
                            {
                                unknownCount++;
                                if (enemy.hp < preferredUnknownHp)
                                {
                                    preferredUnknownHp = enemy.hp;
                                    preferredUnknownTarget = destinationMask;
                                }
                            }
                            else if (affinityName == "Weak")
                            {
                                knownWeakCount++;
                                if (enemy.hp < preferredSingleHp)
                                {
                                    preferredSingleHp = enemy.hp;
                                    preferredSingleTarget = destinationMask;
                                }
                            }
                            else if (affinityName == "Normal")
                            {
                                knownNormalCount++;
                                if (enemy.hp < preferredNormalHp)
                                {
                                    preferredNormalHp = enemy.hp;
                                    preferredNormalTarget = destinationMask;
                                }
                            }
                            else if (affinityName == "Resist")
                            {
                                knownResistCount++;
                                if (enemy.hp < preferredResistHp)
                                {
                                    preferredResistHp = enemy.hp;
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
                                if (predictedSkillDamage >= enemy.hp)
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
                    int mpAfterUse = Math.Max(0, source.mp - cost);
                    bool preservesReserve = source.maxmp == 0 ||
                                            mpAfterUse * 2 >= source.maxmp;
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
                        int mpCostPercent = source.maxmp > 0
                            ? (cost * 1000) / source.maxmp
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
                    _recommendedSkill = -2;
                    _recommendedSingle = false;
                    _recommendedSourceForm = sourceFormIndex;
                    _recommendedTargetForm = -1;
                    MelonLogger.Msg(
                        "[NocturneModernController] SMART-AUTO recommendation; " +
                        $"Attack reason={(basicCanKill ? "basic-kill" : "party-basic-clear")} " +
                        $"damage={bestBasicDamage} " +
                        $"basicKillable={basicKillCount}/{livingEnemyCount} " +
                        $"partyForecast={forecastDefeatedCount}/{livingEnemyCount} " +
                        $"forecastActors={forecastActorCount} remainingHp={forecastRemainingHp} " +
                        $"order=[{forecastOrder}] " +
                        $"bestSkillKills={bestKillCount}.");
                    return;
                }

                if (bestSkill == 0)
                {
                    _recommendedSkill = basicIsSafe ? -2 : -1;
                    _recommendedSingle = false;
                    _recommendedSourceForm = sourceFormIndex;
                    _recommendedTargetForm = -1;
                    MelonLogger.Msg(basicIsSafe
                        ? "[NocturneModernController] SMART-AUTO recommendation; " +
                          $"Attack reason=no-usable-skill damage={bestBasicDamage}."
                        : "[NocturneModernController] SMART-AUTO recommendation; Pass " +
                          "reason=no-safe-attack.");
                }
                else
                {
                    _recommendedSkill = bestSkill;
                    _recommendedSingle = bestIsSingle;
                    _recommendedSourceForm = sourceFormIndex;
                    _recommendedTargetForm = bestIsSingle
                        ? BitMaskToFormIndex(bestTargetMask)
                        : -1;
                    MelonLogger.Msg(
                        "[NocturneModernController] SMART-AUTO recommendation; " +
                        $"skill={bestSkill} targetMask=0x{bestTargetMask:X8} " +
                        $"weakTargets={bestWeakCount} single={bestIsSingle} " +
                        $"exploration={bestIsExploration} predictedKills={bestKillCount} " +
                        $"basicKillable={basicKillCount}/{livingEnemyCount} " +
                        $"score={bestScore}.");
                }
            }
            catch (Exception exception)
            {
                MelonLogger.Warning(
                    "[NocturneModernController] SMART-AUTO candidate evaluation failed: " +
                    exception.GetType().Name + ": " + exception.Message);
            }
        }

        private sealed class BasicForecastTarget
        {
            internal int FormIndex;
            internal int DemonId;
            internal int RemainingHp;
        }

        private static bool TryForecastRemainingBasicAttacks(
            nbMainProcessData_t main,
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
                int orderIndex = Convert.ToInt32(main.orderindex);
                for (int i = Math.Max(0, orderIndex + 1); i < main.order.Length; i++)
                {
                    int formIndex = Convert.ToInt32(main.order[i]);
                    if (formIndex >= 0 && formIndex < 4 && seenActors.Add(formIndex))
                    {
                        actorForms.Add(formIndex);
                    }
                }

                actorCount = actorForms.Count;
                orderText = string.Join(",", actorForms);
                var targets = new List<BasicForecastTarget>();
                for (int enemyIndex = 0; enemyIndex < main.enemyunit.Length; enemyIndex++)
                {
                    Il2Cppnewdata_H.datUnitWork_t enemy = main.enemyunit[enemyIndex];
                    if (enemy == null || enemy.Pointer == IntPtr.Zero || enemy.hp == 0)
                    {
                        continue;
                    }

                    targets.Add(new BasicForecastTarget
                    {
                        FormIndex = enemyIndex + 4,
                        DemonId = enemy.id,
                        RemainingHp = enemy.hp
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
                        uint affinity = nbCalc.nbGetAisyo(0, target.FormIndex, 0);
                        string affinityName = DecodeAffinity(affinity);
                        bool known = SmartAutoKnowledgeStore.IsKnown(target.DemonId, 0);
                        if (known && IsUnsafeAffinity(affinityName))
                        {
                            continue;
                        }

                        int rawDamage = nbCalc.nbGetButuriAttack(
                            0, actorFormIndex, target.FormIndex, 0);
                        float ratio = nbCalc.nbGetAisyoRitu(
                            0, actorFormIndex, target.FormIndex);
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
                MelonLogger.Msg(
                    "[NocturneModernController] SMART-AUTO party forecast unavailable; " +
                    $"{exception.GetType().Name}.");
                return false;
            }
        }

        private static bool TrySelectRecommendedCommandThroughManualPath()
        {
            if (_manualSelectionInProgress ||
                ControllerSettings.Current.AutoBattleMode != AutoBattleMode.SkillPriority ||
                _recommendedSkill == 0)
            {
                return false;
            }

            try
            {
                nbCommSelProcessData_t selection = nbCommSelProcess.GetCommSelProcessData();
                const int commandType = 0;
                if (selection.commlist == null || selection.nowcursor == null ||
                    commandType >= selection.commlist.Length ||
                    commandType >= selection.nowcursor.Length)
                {
                    return false;
                }

                var commands = selection.commlist[commandType];
                if (commands == null)
                {
                    return false;
                }

                int commandCount = commandType < selection.commcnt.Length
                    ? Math.Min(selection.commcnt[commandType], commands.Length)
                    : commands.Length;
                int cursor = -1;
                int selectedCommand = _recommendedSkill == -2
                    ? 32768
                    : _recommendedSkill < 0 ? 32770 : _recommendedSkill;
                for (int index = 0; index < commandCount; index++)
                {
                    if (commands[index] == selectedCommand)
                    {
                        cursor = index;
                        break;
                    }
                }
                if (cursor < 0)
                {
                    return false;
                }

                selection.nowtype = commandType;
                selection.nowcursor[commandType] = cursor;
                if (_recommendedSkill > 0 && _recommendedSingle)
                {
                    _pendingSingleTargetForm = _recommendedTargetForm;
                    _pendingSingleTargetMask = _recommendedTargetForm >= 0
                        ? 1u << _recommendedTargetForm
                        : 0;
                    _pendingSingleSkill = _recommendedSkill;
                }
                _manualSelectionInProgress = true;
                try
                {
                    nbCommSelProcess.SelectCommandList(ref selection);
                }
                finally
                {
                    _manualSelectionInProgress = false;
                }
                MelonLogger.Msg(
                    "[NocturneModernController] SMART-AUTO manual-path selected; " +
                    $"form={_recommendedSourceForm} type={commandType} cursor={cursor} " +
                    $"command={selectedCommand} skill={_recommendedSkill}.");
                return true;
            }
                catch (Exception exception)
                {
                    _pendingSingleTargetForm = -1;
                    _pendingSingleTargetMask = 0;
                    _pendingSingleSkill = 0;
                    MelonLogger.Warning(
                    "[NocturneModernController] SMART-AUTO manual-path failed; " +
                    "using native Auto: " + exception.GetType().Name + ": " + exception.Message);
                return false;
            }
        }

        private static int BitMaskToFormIndex(uint mask)
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

        private static void LearnActionAffinity(nbActionProcessData_t action, int command, int skillId)
        {
            if (command != 1 || skillId <= 0 || action == null ||
                action.Pointer == IntPtr.Zero || action.form == null ||
                action.form.Pointer == IntPtr.Zero || action.form.formindex < 0 ||
                action.form.formindex >= 4)
            {
                return;
            }

            try
            {
                int attribute = nbCalc.nbGetNormalSkillAttr(unchecked((ushort)skillId));
                if (attribute < 0 || attribute >= 7)
                {
                    return;
                }

                uint targetMask = unchecked((uint)action.select);
                nbMainProcessData_t main = nbMainProcess.nbGetMainProcessData();
                for (int enemyIndex = 0; enemyIndex < main.enemyunit.Length; enemyIndex++)
                {
                    int formIndex = enemyIndex + 4;
                    if ((targetMask & (1u << formIndex)) == 0)
                    {
                        continue;
                    }

                    Il2Cppnewdata_H.datUnitWork_t enemy = main.enemyunit[enemyIndex];
                    if (enemy != null && enemy.Pointer != IntPtr.Zero)
                    {
                        SmartAutoKnowledgeStore.Learn(enemy.id, attribute);
                    }
                }
            }
            catch (Exception exception)
            {
                MelonLogger.Warning(
                    "[NocturneModernController] SMART-AUTO learning failed: " +
                    exception.GetType().Name + ": " + exception.Message);
            }
        }

        private static void ApplyPendingSingleTarget(
            nbActionProcessData_t action,
            int command,
            int skillId)
        {
            if (command != 1 || skillId <= 0 || skillId != _pendingSingleSkill ||
                _pendingSingleTargetMask == 0 || action == null || action.Pointer == IntPtr.Zero)
            {
                return;
            }

            uint targetMask = _pendingSingleTargetMask;
            int targetForm = _pendingSingleTargetForm;
            _pendingSingleTargetForm = -1;
            _pendingSingleTargetMask = 0;
            _pendingSingleSkill = 0;
            action.select = targetMask;
            MelonLogger.Msg(
                "[NocturneModernController] SMART-AUTO single target applied; " +
                $"skill={skillId} form={targetForm} select=0x{targetMask:X8}.");
        }

        private static void DumpCommandLists(nbCommSelProcessData_t selection)
        {
            try
            {
                for (int type = 0; type < selection.commlist.Length; type++)
                {
                    var list = selection.commlist[type];
                    if (list == null)
                    {
                        continue;
                    }

                    int count = type < selection.commcnt.Length
                        ? Math.Min(selection.commcnt[type], list.Length)
                        : list.Length;
                    if (count <= 0)
                    {
                        continue;
                    }

                    var values = new StringBuilder();
                    for (int index = 0; index < count; index++)
                    {
                        if (values.Length > 0)
                        {
                            values.Append(',');
                        }
                        values.Append(index).Append(':').Append(list[index]);
                    }

                    int cursor = type < selection.nowcursor.Length
                        ? selection.nowcursor[type]
                        : -1;
                    MelonLogger.Msg(
                        "[NocturneModernController] SMART-AUTO command list; " +
                        $"type={type} count={count} cursor={cursor} values=[{values}].");
                }
            }
            catch (Exception exception)
            {
                MelonLogger.Warning(
                    "[NocturneModernController] SMART-AUTO command-list inspection failed: " +
                    exception.GetType().Name + ": " + exception.Message);
            }
        }

        [HarmonyPatch(typeof(nbActionProcess), nameof(nbActionProcess.SetNowActionStat))]
        private static class ActionCommandPatch
        {
            private static void Prefix(
                ref nbActionProcessData_t a,
                int comm,
                int nskill)
            {
                ApplyPendingSingleTarget(a, comm, nskill);
                LearnActionAffinity(a, comm, nskill);
                MelonLogger.Msg(
                    "[NocturneModernController] SMART-AUTO action command; " +
                    $"comm={comm} nskill={nskill} before=[{DescribeAction(a)}].");
            }

            private static void Postfix(
                ref nbActionProcessData_t a,
                int comm,
                int nskill)
            {
                MelonLogger.Msg(
                    "[NocturneModernController] SMART-AUTO action command applied; " +
                    $"comm={comm} nskill={nskill} after=[{DescribeAction(a)}].");
            }
        }

        [HarmonyPatch(typeof(nbActionProcess), nameof(nbActionProcess.SetAction_SKILL))]
        private static class SkillActionPatch
        {
            private static void Prefix(ref nbActionProcessData_t a)
            {
                MelonLogger.Msg(
                    "[NocturneModernController] SMART-AUTO skill action entered; " +
                    $"before=[{DescribeAction(a)}].");
            }

            private static void Postfix(ref nbActionProcessData_t a)
            {
                MelonLogger.Msg(
                    "[NocturneModernController] SMART-AUTO skill action prepared; " +
                    $"after=[{DescribeAction(a)}].");
            }
        }

        [HarmonyPatch(typeof(nbActionProcess), nameof(nbActionProcess.SetTarget))]
        private static class ActionTargetPatch
        {
            private static void Postfix(ref nbActionProcessData_t a, int __result)
            {
                MelonLogger.Msg(
                    "[NocturneModernController] SMART-AUTO action target resolved; " +
                    $"result={__result} [{DescribeAction(a)}].");
            }
        }

        [HarmonyPatch(typeof(nbCommSelProcess), nameof(nbCommSelProcess.nbInitCommSelProcess))]
        private static class ManualCommandStartPatch
        {
            private static void Postfix()
            {
                _lastSelection = string.Empty;
                _pollUntilTick = unchecked(System.Environment.TickCount + 15000);
                MelonLogger.Msg(
                    "[NocturneModernController] SMART-AUTO manual command observation started.");

                if (!SmartAutoBattleRuntime.IsAutoActive ||
                    ControllerSettings.Current.AutoBattleMode != AutoBattleMode.SkillPriority)
                {
                    return;
                }

                DumpSkillCandidates();
                if (!TrySelectRecommendedCommandThroughManualPath())
                {
                    MelonLogger.Msg(
                        "[NocturneModernController] SMART-AUTO next actor uses native command.");
                }
            }
        }

        [HarmonyPatch(typeof(nbPanelProcess), nameof(nbPanelProcess.nbPanelAutoShow))]
        private static class AutoPanelShowPatch
        {
            private static void Postfix()
            {
                MelonLogger.Msg(
                    $"[NocturneModernController] SMART-AUTO panel shown; session={_autoSession}.");
            }
        }

        [HarmonyPatch(typeof(nbPanelProcess), nameof(nbPanelProcess.nbPanelAutoHide))]
        private static class AutoPanelHidePatch
        {
            private static void Postfix()
            {
                MelonLogger.Msg(
                    $"[NocturneModernController] SMART-AUTO panel hidden; session={_autoSession}.");
            }
        }
    }
}
