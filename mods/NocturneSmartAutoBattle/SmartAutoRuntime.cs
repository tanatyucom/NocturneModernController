using System;
using System.Collections.Generic;

namespace NocturneSmartAutoBattle
{
    // When each part of Smart Auto may act. Enabled=false turns every Smart
    // Auto effect off: no speed change, no command or target override, no
    // knowledge learning. (Controller's built-in version kept overriding
    // commands in Skill Priority mode even when disabled.) Normal Attack Only
    // leaves the game's own Auto choice untouched.
    internal static class SmartAutoPolicy
    {
        internal static bool OverridesCommands(bool enabled, SmartAutoMode mode) =>
            enabled && mode == SmartAutoMode.SkillPriority;

        internal static bool AppliesTargets(bool enabled) => enabled;

        internal static bool Learns(bool enabled) => enabled;
    }

    // Single-target skill chosen by the decision, waiting to be applied when
    // the game resolves the Auto target (CursorSelectAuto) and sets the action
    // (SetNowActionStat). As in Controller's built-in version it is cleared by
    // the next decision or once applied, not at battle end.
    internal sealed class PendingSingleTarget
    {
        internal int Form { get; private set; } = -1;
        internal uint Mask { get; private set; }
        internal int Skill { get; private set; }

        internal void Clear()
        {
            Form = -1;
            Mask = 0;
            Skill = 0;
        }

        internal void Set(int skill, int form)
        {
            Form = form;
            Mask = form >= 0 ? 1u << form : 0;
            Skill = skill;
        }

        // SetNowActionStat: comm 1 is a skill; only the pending skill takes the
        // pending target, and only once.
        internal bool TryTake(int command, int skillId, out uint mask, out int form)
        {
            mask = 0;
            form = -1;
            if (command != 1 || skillId <= 0 || skillId != Skill || Mask == 0)
            {
                return false;
            }

            mask = Mask;
            form = Form;
            Clear();
            return true;
        }

        // CursorSelectAuto: index of the requested enemy in the target list
        // (entries may be either form indices or enemy slots); the last match
        // wins. -1 when the enemy is not in the list.
        internal static int FindCursorIndex(IReadOnlyList<int> enemyNumbers, int count, int requestedForm)
        {
            int matchedIndex = -1;
            for (int index = 0; index < Math.Min(count, enemyNumbers.Count); index++)
            {
                int form = enemyNumbers[index];
                if (form == requestedForm || form + 4 == requestedForm)
                {
                    matchedIndex = index;
                }
            }
            return matchedIndex;
        }
    }

    internal interface ITimeScale
    {
        float Value { get; set; }
    }

    // Mirror of the game's Auto state (toggled by the Auto button, which this
    // mod observes but never presses) and the battle speed it owns while Auto
    // runs: Time.timeScale = baseline x Speed, restored when Auto stops, the
    // battle ends, exploration resumes, Settings opens, Smart Auto is disabled
    // or the mod shuts down.
    internal sealed class SmartAutoState
    {
        private readonly Action<string> _log;
        private bool _ownsTimeScale;
        private float _baselineTimeScale = 1.0f;

        internal SmartAutoState(Action<string> log)
        {
            _log = log;
        }

        internal bool AutoActive { get; private set; }
        internal bool OwnsTimeScale => _ownsTimeScale;

        internal void OnAutoButton(ITimeScale timeScale) =>
            SetAutoActive(!AutoActive, "native Auto button", timeScale);

        internal void SetAutoActive(bool active, string reason, ITimeScale timeScale)
        {
            if (AutoActive == active)
            {
                return;
            }

            AutoActive = active;
            _log($"Smart Auto {(active ? "ON" : "OFF")}: {reason}.");
            if (!active)
            {
                RestoreTimeScale(timeScale);
            }
        }

        internal void Sample(bool enabled, float speed, bool explorationActive, bool settingsOpen, ITimeScale timeScale)
        {
            if (explorationActive || settingsOpen)
            {
                SetAutoActive(false, "non-battle context", timeScale);
                return;
            }

            if (!enabled || !AutoActive || speed <= 1.0f)
            {
                RestoreTimeScale(timeScale);
                return;
            }

            if (!_ownsTimeScale)
            {
                _baselineTimeScale = Math.Max(0.01f, timeScale.Value);
                _ownsTimeScale = true;
                _log($"Smart Auto speed enabled: {speed:0.0}x.");
            }

            float desired = _baselineTimeScale * speed;
            if (Math.Abs(timeScale.Value - desired) > 0.001f)
            {
                timeScale.Value = desired;
            }
        }

        internal void Shutdown(ITimeScale timeScale)
        {
            AutoActive = false;
            RestoreTimeScale(timeScale);
        }

        private void RestoreTimeScale(ITimeScale timeScale)
        {
            if (!_ownsTimeScale)
            {
                return;
            }

            timeScale.Value = _baselineTimeScale;
            _ownsTimeScale = false;
            _log("Smart Auto speed restored.");
        }
    }
}
