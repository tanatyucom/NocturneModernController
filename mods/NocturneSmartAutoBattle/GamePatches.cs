using System;
using System.Collections.Generic;
using System.Text;
using HarmonyLib;
using Il2Cpp;
using Il2Cppnewbattle_H;
using Il2Cppnewdata_H;
using NocturneModernController;

namespace NocturneSmartAutoBattle
{
    // Smart Auto's hooks into the game. Moved from Controller's built-in
    // SmartAutoBattleRuntime / SmartAutoBattleTelemetry (logging-only patches
    // were left out), plus an exploration tracker so it works without
    // Controller.

    // Field exploration: same rule as Controller's ExplorationState (the field
    // update ran within the last 100 ms; the rule is compiled in from
    // shared/ExplorationWindow.cs, not referenced).
    [HarmonyPatch(typeof(fldPlayer), nameof(fldPlayer.fldPlayerCalc))]
    internal static class ExplorationTracker
    {
        private static int _lastFieldTick;

        internal static bool IsExplorationActive =>
            ExplorationWindow.IsActive(_lastFieldTick, Environment.TickCount);

        private static void Prefix()
        {
            _lastFieldTick = Environment.TickCount;
        }
    }

    internal sealed class UnityTimeScale : ITimeScale
    {
        internal static UnityTimeScale Instance { get; } = new();

        public float Value
        {
            get => UnityEngine.Time.timeScale;
            set => UnityEngine.Time.timeScale = value;
        }
    }

    // The game's Auto button (pad 0, TRIG) toggles the game's Auto; Smart Auto
    // mirrors it. It never presses the button or changes the game's Auto flag.
    [HarmonyPatch(
        typeof(SteamInputAssign),
        nameof(SteamInputAssign.padcheck),
        new Type[] { typeof(int), typeof(SIActionName), typeof(SIPressType) })]
    internal static class AutoButtonPatch
    {
        private static void Postfix(int __0, SIActionName __1, SIPressType __2, bool __result)
        {
            if (__result && __0 == 0 && __1 == SIActionName.BTL_AutoBattle && __2 == SIPressType.TRIG)
            {
                SmartAutoMod.State.OnAutoButton(UnityTimeScale.Instance);
            }
        }
    }

    [HarmonyPatch(typeof(nbMainProcess), nameof(nbMainProcess.nbMainShutdown))]
    internal static class BattleShutdownPatch
    {
        private static void Prefix() =>
            SmartAutoMod.State.SetAutoActive(false, "battle shutdown", UnityTimeScale.Instance);
    }

    [HarmonyPatch(typeof(nbMainProcess), nameof(nbMainProcess.nbMainProcessClear))]
    internal static class BattleClearPatch
    {
        private static void Prefix() =>
            SmartAutoMod.State.SetAutoActive(false, "battle cleared", UnityTimeScale.Instance);
    }

    // First actor under the game's Auto: choose through the manual command path
    // and skip the game's own Auto choice.
    [HarmonyPatch(typeof(nbCommSelProcess), nameof(nbCommSelProcess.nbSetAutoCommSel))]
    internal static class AutoCommandSelectionPatch
    {
        private static bool Prefix()
        {
            if (CommandSelection.ManualSelectionInProgress)
            {
                return false;
            }
            if (!CommandSelection.OverridesCommands)
            {
                return true;
            }
            if (CommandSelection.TrySelectRecommended())
            {
                return false;
            }
            SmartAutoMod.Log("native command selection.");
            return true;
        }
    }

    // Following actors while the game's Auto runs.
    [HarmonyPatch(typeof(nbCommSelProcess), nameof(nbCommSelProcess.nbInitCommSelProcess))]
    internal static class NextActorCommandPatch
    {
        private static void Postfix()
        {
            if (!SmartAutoMod.State.AutoActive || !CommandSelection.OverridesCommands)
            {
                return;
            }
            if (!CommandSelection.TrySelectRecommended())
            {
                SmartAutoMod.Log("next actor uses native command.");
            }
        }
    }

    [HarmonyPatch(typeof(nbTarSelProcess), nameof(nbTarSelProcess.CursorSelectAuto))]
    internal static class AutoTargetSelectionPatch
    {
        private static void Prefix(ref nbTarSelProcessData_t t)
        {
            PendingSingleTarget pending = SmartAutoMod.Pending;
            if (!SmartAutoPolicy.AppliesTargets(SmartAutoSettings.Current.Enabled) || pending.Form < 0)
            {
                return;
            }

            int requestedForm = pending.Form;
            int requestedSkill = pending.Skill;
            try
            {
                var enemyNumbers = t.enemyno;
                int count = enemyNumbers == null ? 0 : Math.Min(t.enemycnt, enemyNumbers.Length);
                var forms = new List<int>(count);
                var candidates = new StringBuilder();
                for (int index = 0; index < count; index++)
                {
                    int form = enemyNumbers![index];
                    forms.Add(form);
                    if (candidates.Length > 0)
                    {
                        candidates.Append(',');
                    }
                    candidates.Append(index).Append(':').Append(form);
                }

                int matchedIndex = PendingSingleTarget.FindCursorIndex(forms, count, requestedForm);
                if (matchedIndex >= 0)
                {
                    t.nowno = unchecked((sbyte)matchedIndex);
                    t.nowform = unchecked((sbyte)requestedForm);
                    SmartAutoMod.Log(
                        $"single target cursor selected; skill={requestedSkill} requestedForm={requestedForm} " +
                        $"index={matchedIndex} enemies=[{candidates}].");
                }
                else
                {
                    SmartAutoMod.Warn(
                        $"single target unavailable; skill={requestedSkill} requestedForm={requestedForm} " +
                        $"enemies=[{candidates}]; using native target.");
                }
            }
            catch (Exception exception)
            {
                SmartAutoMod.Warn("single target failed; using native target: " +
                    exception.GetType().Name + ": " + exception.Message);
            }
        }
    }

    [HarmonyPatch(typeof(nbActionProcess), nameof(nbActionProcess.SetNowActionStat))]
    internal static class ActionCommandPatch
    {
        private static void Prefix(ref nbActionProcessData_t a, int comm, int nskill)
        {
            bool enabled = SmartAutoSettings.Current.Enabled;
            if (SmartAutoPolicy.AppliesTargets(enabled))
            {
                ApplyPendingSingleTarget(a, comm, nskill);
            }
            if (SmartAutoPolicy.Learns(enabled))
            {
                LearnActionAffinity(a, comm, nskill);
            }
        }

        private static void ApplyPendingSingleTarget(nbActionProcessData_t action, int command, int skillId)
        {
            if (action == null || action.Pointer == IntPtr.Zero ||
                !SmartAutoMod.Pending.TryTake(command, skillId, out uint targetMask, out int targetForm))
            {
                return;
            }

            action.select = targetMask;
            SmartAutoMod.Log($"single target applied; skill={skillId} form={targetForm} select=0x{targetMask:X8}.");
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

                    datUnitWork_t enemy = main.enemyunit[enemyIndex];
                    if (enemy != null && enemy.Pointer != IntPtr.Zero)
                    {
                        SmartAutoMod.Knowledge.Learn(enemy.id, attribute);
                    }
                }
            }
            catch (Exception exception)
            {
                SmartAutoMod.Warn("learning failed: " + exception.GetType().Name + ": " + exception.Message);
            }
        }
    }

    [HarmonyPatch(typeof(nbActionProcess), nameof(nbActionProcess.SetAnalyzePacket))]
    internal static class AnalyzeKnowledgePatch
    {
        private static void Postfix(int dformindex)
        {
            if (!SmartAutoPolicy.Learns(SmartAutoSettings.Current.Enabled))
            {
                return;
            }

            try
            {
                int enemyIndex = dformindex - 4;
                nbMainProcessData_t main = nbMainProcess.nbGetMainProcessData();
                if (enemyIndex < 0 || enemyIndex >= main.enemyunit.Length)
                {
                    return;
                }

                datUnitWork_t enemy = main.enemyunit[enemyIndex];
                if (enemy == null || enemy.Pointer == IntPtr.Zero)
                {
                    return;
                }

                SmartAutoMod.Knowledge.LearnAll(enemy.id);
                SmartAutoMod.Log($"Analyze learned all affinities; form={dformindex} demon={enemy.id}.");
            }
            catch (Exception exception)
            {
                SmartAutoMod.Warn("Analyze learning failed: " + exception.GetType().Name + ": " + exception.Message);
            }
        }
    }

    // Evaluates the acting unit and selects the recommended command through
    // the game's manual command path (cursor on command list 0, then
    // SelectCommandList), exactly as Controller's built-in version did.
    internal static class CommandSelection
    {
        private const int CommandType = 0;
        private const int AttackCommand = 32768;
        private const int PassCommand = 32770;

        internal static bool ManualSelectionInProgress { get; private set; }

        internal static bool OverridesCommands =>
            SmartAutoPolicy.OverridesCommands(SmartAutoSettings.Current.Enabled, SmartAutoSettings.Current.Mode);

        internal static bool TrySelectRecommended()
        {
            SmartAutoMod.Pending.Clear();
            SmartAutoRecommendation recommendation = SmartAutoDecision.Evaluate(
                new GameBattleView(), SmartAutoMod.Knowledge.IsKnown, SmartAutoMod.Log);
            return TrySelectThroughManualPath(recommendation);
        }

        private static bool TrySelectThroughManualPath(SmartAutoRecommendation recommendation)
        {
            if (ManualSelectionInProgress || recommendation.Skill == SmartAutoRecommendation.None)
            {
                return false;
            }

            try
            {
                nbCommSelProcessData_t selection = nbCommSelProcess.GetCommSelProcessData();
                if (selection.commlist == null || selection.nowcursor == null ||
                    CommandType >= selection.commlist.Length ||
                    CommandType >= selection.nowcursor.Length)
                {
                    return false;
                }

                var commands = selection.commlist[CommandType];
                if (commands == null)
                {
                    return false;
                }

                int commandCount = CommandType < selection.commcnt.Length
                    ? Math.Min(selection.commcnt[CommandType], commands.Length)
                    : commands.Length;
                int cursor = -1;
                int selectedCommand = recommendation.Skill == SmartAutoRecommendation.Attack
                    ? AttackCommand
                    : recommendation.Skill < 0 ? PassCommand : recommendation.Skill;
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

                selection.nowtype = CommandType;
                selection.nowcursor[CommandType] = cursor;
                if (recommendation.Skill > 0 && recommendation.Single)
                {
                    SmartAutoMod.Pending.Set(recommendation.Skill, recommendation.TargetForm);
                }
                ManualSelectionInProgress = true;
                try
                {
                    nbCommSelProcess.SelectCommandList(ref selection);
                }
                finally
                {
                    ManualSelectionInProgress = false;
                }
                SmartAutoMod.Log(
                    $"manual-path selected; form={recommendation.SourceForm} type={CommandType} cursor={cursor} " +
                    $"command={selectedCommand} skill={recommendation.Skill}.");
                return true;
            }
            catch (Exception exception)
            {
                SmartAutoMod.Pending.Clear();
                SmartAutoMod.Warn("manual-path failed; using native Auto: " +
                    exception.GetType().Name + ": " + exception.Message);
                return false;
            }
        }
    }

    // ISmartAutoBattleView over the running battle: each member reads the
    // game's data or calls the matching native function.
    internal sealed class GameBattleView : ISmartAutoBattleView
    {
        // Read on first use, inside the decision's error handling.
        private nbMainProcessData_t? _mainData;
        private datUnitWork_t? _source;

        private nbMainProcessData_t Main => _mainData ??= nbMainProcess.nbGetMainProcessData();

        public bool TryGetSource(out SmartAutoSource source)
        {
            source = new SmartAutoSource();
            nbCommSelProcessData_t selection = nbCommSelProcess.GetCommSelProcessData();
            nbActionProcessData_t action = selection.act;
            datUnitWork_t? work = action?.work;
            nbFormation_t? form = action?.form;
            if (work == null || work.Pointer == IntPtr.Zero || form == null || form.Pointer == IntPtr.Zero)
            {
                return false;
            }

            _source = work;
            int skillCount = Math.Min(work.skillcnt, work.skill.Length);
            var skills = new int[Math.Max(0, skillCount)];
            for (int index = 0; index < skills.Length; index++)
            {
                skills[index] = work.skill[index];
            }
            source = new SmartAutoSource
            {
                FormIndex = form.formindex,
                UnitId = work.id,
                Hp = work.hp,
                MaxHp = work.maxhp,
                Mp = work.mp,
                MaxMp = work.maxmp,
                Skills = skills
            };
            return true;
        }

        public int EnemySlotCount => Main.enemyunit.Length;

        public SmartAutoEnemy? GetEnemy(int enemyIndex)
        {
            datUnitWork_t enemy = Main.enemyunit[enemyIndex];
            return enemy == null || enemy.Pointer == IntPtr.Zero
                ? null
                : new SmartAutoEnemy { Id = enemy.id, Hp = enemy.hp };
        }

        public int OrderIndex => Convert.ToInt32(Main.orderindex);
        public int OrderLength => Main.order.Length;
        public int OrderAt(int index) => Convert.ToInt32(Main.order[index]);

        public uint GetAisyo(int skill, int formIndex, int attribute) => nbCalc.nbGetAisyo(skill, formIndex, attribute);
        public float GetAisyoRitu(int skill, int sourceForm, int destinationForm) =>
            nbCalc.nbGetAisyoRitu(skill, sourceForm, destinationForm);
        public int GetButuriAttack(int skill, int sourceForm, int destinationForm, int hpN) =>
            nbCalc.nbGetButuriAttack(skill, sourceForm, destinationForm, hpN);
        public int GetMagicAttack(int skill, int sourceForm, int destinationForm, int hpN) =>
            nbCalc.nbGetMagicAttack(skill, sourceForm, destinationForm, hpN);
        public int CheckSkillUse(int sourceForm, int skill) => nbCalc.nbCheckSkillUse(sourceForm, skill);
        public int CheckSkillCost(int sourceForm, int skill) => nbCalc.nbCheckSkillCost(sourceForm, skill);
        public int GetSkillCost(int skill) => cmpDrawSkill.cmpGetSkillCost(unchecked((ushort)skill), _source);
        public int CheckSingleTargetSkill(int skill) => nbCalc.nbCheckSingleTargetSkill(skill);
        public int GetNormalSkillAttr(int skill) => nbCalc.nbGetNormalSkillAttr(skill);
        public int GetSkillHpN(int skill) => datNormalSkill.tbl[skill].hpn;
    }
}
