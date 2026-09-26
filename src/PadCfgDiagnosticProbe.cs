using System;
using Il2Cpp;
using Il2Cpplibsdf_H;
using MelonLoader;

namespace NocturneModernController
{
    // Read-only diagnostic PoC (temporary, not part of the public API surface).
    // Confirms whether SteamInputAssign.PadCfg is indexed by SIActionName and
    // whether ConfigSet.pad1/pad2 match the player's current native Controller
    // Key Config (e.g. Menu, Map). No writes. No Harmony patches. Logs once.
    internal static class PadCfgDiagnosticProbe
    {
        private static bool _logged;

        internal static void Sample()
        {
            if (_logged || !ExplorationState.IsExplorationActive)
            {
                return;
            }

            try
            {
                SteamInputUtil util = SteamInputUtil.Instance;
                if (util == null)
                {
                    return;
                }

                SteamInputAssign assign = util.GetSteamInputAssign();
                if (assign == null)
                {
                    return;
                }

                var padCfg = assign.PadCfg;
                int length = padCfg != null ? padCfg.Length : -1;
                if (padCfg == null || length <= 0)
                {
                    return;
                }

                // Reached a fully-initialized state: capture once and stop retrying.
                _logged = true;
                MelonLogger.Msg($"[PadCfgProbe] PadCfg.Length={length}");

                foreach (SIActionName action in Enum.GetValues(typeof(SIActionName)))
                {
                    if (action == SIActionName.MAX)
                    {
                        continue;
                    }

                    bool priority = action == SIActionName.FD_CmdMenu || action == SIActionName.FD_Automap;
                    int index = (int)action;
                    if (index < 0 || index >= padCfg.Length)
                    {
                        MelonLogger.Msg(
                            $"[PadCfgProbe]{(priority ? " *PRIORITY*" : string.Empty)} " +
                            $"index={index} SIActionName={action} OUT OF RANGE (PadCfg.Length={length})");
                        continue;
                    }

                    SteamInputAssign.ConfigSet cfg = padCfg[index];
                    LogEntry(index, action, cfg, priority);
                }

                LogCommonConfig(assign);
            }
            catch (Exception ex)
            {
                // Native singleton may still be mid-initialization on early frames;
                // do not latch _logged so the next OnUpdate tick retries.
                MelonLogger.Msg($"[PadCfgProbe] transient error, will retry: {ex.GetType().Name}: {ex.Message}");
            }
        }

        private static void LogCommonConfig(SteamInputAssign assign)
        {
            // Hypothesis (not yet confirmed): CFG_TYPE_GAMEPAD is a separate,
            // 34-entry native index space (per docs/research/RIGHT_STICK_VIEW_AND_DASH_INVESTIGATION.md,
            // sourced from user-provided images, not independently verified here)
            // distinct from SIActionName (31 entries). PadCfg.Length=31 is too
            // short to hold it; CommonConfig is untested and is the next candidate.
            // Length==34 alone is NOT proof of a live binding table — only an
            // A/B change against the native Controller Key Config can confirm that.
            var commonConfig = assign.CommonConfig;
            int length = commonConfig != null ? commonConfig.Length : -1;
            MelonLogger.Msg($"[PadCfgProbe] CommonConfig.Length={length}");

            if (commonConfig == null)
            {
                return;
            }

            for (int i = 0; i < commonConfig.Length; i++)
            {
                SteamInputAssign.ConfigSet cfg = commonConfig[i];
                MelonLogger.Msg(
                    $"[PadCfgProbe][CommonConfig] index={i} " +
                    $"pad1(raw)={cfg.pad1} pad2(raw)={cfg.pad2} type={cfg.type}");
            }
        }

        private static void LogEntry(int index, SIActionName action, SteamInputAssign.ConfigSet cfg, bool priority)
        {
            string tag = priority ? " *PRIORITY*" : string.Empty;
            MelonLogger.Msg(
                $"[PadCfgProbe]{tag} index={index} SIActionName={action} " +
                $"pad1(raw)={cfg.pad1} pad2(raw)={cfg.pad2} type={cfg.type}");
            MelonLogger.Msg(
                $"[PadCfgProbe]{tag}   pad1 candidates: " +
                $"SDF_PADMAP={Describe<SDF_PADMAP>(cfg.pad1)} " +
                $"InputAssign.KeyID={Describe<InputAssign.KeyID>(cfg.pad1)} " +
                $"InputAssign.AssignCode={Describe<InputAssign.AssignCode>(cfg.pad1)} " +
                $"ControllerButton(mod, non-native)={Describe<ControllerButton>(cfg.pad1)}");
            MelonLogger.Msg(
                $"[PadCfgProbe]{tag}   pad2 candidates: " +
                $"SDF_PADMAP={Describe<SDF_PADMAP>(cfg.pad2)} " +
                $"InputAssign.KeyID={Describe<InputAssign.KeyID>(cfg.pad2)} " +
                $"InputAssign.AssignCode={Describe<InputAssign.AssignCode>(cfg.pad2)} " +
                $"ControllerButton(mod, non-native)={Describe<ControllerButton>(cfg.pad2)}");
        }

        private static string Describe<TEnum>(short raw) where TEnum : struct, Enum
        {
            // Prints the enum member name when the raw value matches a defined
            // constant, or "raw:<value>" when it does not. No candidate is
            // assumed correct; all four are reported side by side.
            object boxed = Enum.ToObject(typeof(TEnum), (int)raw);
            var value = (TEnum)boxed;
            return Enum.IsDefined(typeof(TEnum), value)
                ? $"{value}({raw})"
                : $"raw:{raw}";
        }
    }
}
