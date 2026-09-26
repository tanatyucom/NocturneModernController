using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using HarmonyLib;
using Il2Cpp;
using Il2CppInterop.Runtime;
using MelonLoader;

namespace NocturneModernController
{
    // TEMPORARY read-only diagnostic for the "Yes/No missing on the quit prompt
    // after a GAME binding write, recovers after a battle" report.
    //
    // Goal: tell apart (a) binding data correct but UI (ChangeTex/button guide)
    // stale, (b) guide-side lookup values stale, (c) only ChangeTex stale.
    //
    // Reads only. Never calls SetChangeTexEXE/ClrChangeTexEXE or any write API.
    // The Harmony patches below are Prefix/Postfix that only record timestamps
    // and counters; they never change arguments, results or control flow.
    //
    // SelText[0][4] is not read directly: its declared type (String[][]) does
    // not match the real Int32 layout (NATIVE_GAMEPAD_CONFIG_DATAFLOW §13), and
    // GetConfigGamePad(7) is the byte-exact reader of SelText[0][4][10]. SelText[1]
    // would need raw pointer reads and is intentionally skipped.
    internal static class UiRefreshDiagnosticProbe
    {
        private const string Prefix = "[NMC-UI-DIAG]";
        private const int DeferredFrames = 30;
        private const int GameEndEpisodeGapFrames = 60;
        private const int MaxGameEndEpisodes = 6;
        private const int MaxBattleSnapshots = 4;
        private const int GuideKidProbeMax = 40;

        private static readonly Stopwatch Clock = Stopwatch.StartNew();

        private static int _frame;
        private static bool _baselineDone;
        private static bool _lastExploration;

        private static int _lastGameEndFrame = int.MinValue;
        private static int _gameEndCallsInEpisode;
        private static int _gameEndEpisodes;

        private static bool _battleSeen;
        private static int _battleSnapshots;

        private static int _guideDispSetCount;
        private static string _guideDispSetLastName = string.Empty;

        private static string? _pendingLabel;
        private static int _pendingFrame;

        internal static void Sample()
        {
            _frame++;

            bool exploration = ExplorationState.IsExplorationActive;
            if (exploration && !_lastExploration)
            {
                if (!_baselineDone)
                {
                    _baselineDone = true;
                    Schedule("BASELINE_FIELD", DeferredFrames);
                }
                else if (_battleSeen && _battleSnapshots < MaxBattleSnapshots)
                {
                    _battleSeen = false;
                    _battleSnapshots++;
                    Snapshot("AFTER_BATTLE_FIELD_ENTER");
                    Schedule("AFTER_BATTLE_FIELD_+30f", DeferredFrames);
                }
            }
            _lastExploration = exploration;

            if (_gameEndCallsInEpisode > 0 && _frame - _lastGameEndFrame > GameEndEpisodeGapFrames)
            {
                Log($"GAME_END_EPISODE_END calls={_gameEndCallsInEpisode}");
                _gameEndCallsInEpisode = 0;
            }

            if (_pendingLabel != null && _frame >= _pendingFrame)
            {
                string label = _pendingLabel;
                _pendingLabel = null;
                Snapshot(label);
            }
        }

        internal static void OnStepSuccess(string step)
        {
            Snapshot($"AFTER_{step}_SUCCESS");
            Schedule($"AFTER_{step}_SUCCESS_+30f", DeferredFrames);
        }

        internal static void OnGameEndUpdate()
        {
            _lastGameEndFrame = _frame;
            _gameEndCallsInEpisode++;
            if (_gameEndEpisodes >= MaxGameEndEpisodes)
            {
                return;
            }
            if (_gameEndCallsInEpisode == 1)
            {
                _gameEndEpisodes++;
                Snapshot($"GAME_END_ENTER#{_gameEndEpisodes}");
            }
            else if (_gameEndCallsInEpisode == DeferredFrames)
            {
                Snapshot($"GAME_END_SHOWN#{_gameEndEpisodes}");
            }
        }

        internal static void OnEncounterStart() => _battleSeen = true;

        internal static void OnButtonGuideDispSet(string? name)
        {
            _guideDispSetCount++;
            _guideDispSetLastName = name ?? "null";
        }

        private static void Schedule(string label, int frames)
        {
            // A newer request replaces an unfired one; each point still logs once.
            _pendingLabel = label;
            _pendingFrame = _frame + frames;
        }

        private static void Snapshot(string point)
        {
            Log($"POINT={point} elapsedMs={Clock.ElapsedMilliseconds} frame={_frame} " +
                $"exploration={ExplorationState.IsExplorationActive}");
            Log($"  FLAGS ChangeTexEXE={Try(() => dds3ConfigMainSteam.IsChangeTexEXE().ToString())} " +
                $"ChangeTexNG={Try(() => dds3ConfigMainSteam.IsChangeTexNG().ToString())}");
            Log($"  BINDING GetConfigGamePad(7)={Try(() => dds3ConfigGamePadSteam.GetConfigGamePad(7).ToString())} " +
                "(= SelText[0][4][10]) SelText[1]=not-read");
            Log($"  GUIDE_KID {DescribeGuideKids()}");
            Log($"  BUTTON_GUIDE current={Try(() => ButtonGuide.GetGuideName() ?? "null")} " +
                $"dispSetCount={_guideDispSetCount} lastDispSet={_guideDispSetLastName} {DescribeButtonGuideUis()}");
            Log($"  CHANGETEX {DescribeChangeTex()}");
        }

        private static string DescribeGuideKids()
        {
            var changed = new StringBuilder();
            int errors = 0;
            for (short k = 0; k <= GuideKidProbeMax; k++)
            {
                try
                {
                    short v = dds3ConfigGamePadSteam.GetConfigGamePadGuideKID(k);
                    if (v != k)
                    {
                        changed.Append(changed.Length == 0 ? "" : " ").Append(k).Append("->").Append(v);
                    }
                }
                catch
                {
                    errors++;
                }
            }
            return $"orgkid0..{GuideKidProbeMax} nonIdentity=[{changed}] errors={errors}";
        }

        private static string DescribeButtonGuideUis()
        {
            try
            {
                var objects = UnityEngine.Object.FindObjectsOfType(Il2CppType.Of<buttonguideUI>());
                var parts = new List<string>();
                foreach (var obj in objects)
                {
                    var ui = obj.TryCast<buttonguideUI>();
                    if (ui == null)
                    {
                        continue;
                    }
                    parts.Add($"{ui.gameObject.name}(idx={ui.guideIndex},redef={ui.bRedefine})");
                }
                return $"activeGuideUIs={parts.Count} [{string.Join(" ", parts)}]";
            }
            catch (Exception ex)
            {
                return $"activeGuideUIs=ERR({ex.GetType().Name})";
            }
        }

        private static string DescribeChangeTex()
        {
            try
            {
                var objects = UnityEngine.Object.FindObjectsOfType(Il2CppType.Of<ChangeTex>());
                var parts = new List<string>();
                int redefinePending = 0;
                foreach (var obj in objects)
                {
                    var ct = obj.TryCast<ChangeTex>();
                    if (ct == null)
                    {
                        continue;
                    }
                    string sprite;
                    try
                    {
                        var spr = ct.CurSpr;
                        sprite = spr == null ? "null" : spr.name;
                    }
                    catch (Exception ex)
                    {
                        sprite = $"ERR({ex.GetType().Name})";
                    }
                    bool redefine = false;
                    try
                    {
                        redefine = ct.bRedefine;
                    }
                    catch
                    {
                    }
                    if (redefine)
                    {
                        redefinePending++;
                    }
                    parts.Add($"{ct.gameObject.name}:{sprite}{(redefine ? "*" : "")}");
                }
                return $"active={parts.Count} redefinePending={redefinePending} [{string.Join(" ", parts)}]";
            }
            catch (Exception ex)
            {
                return $"active=ERR({ex.GetType().Name}: {ex.Message})";
            }
        }

        private static string Try(Func<string> read)
        {
            try
            {
                return read();
            }
            catch (Exception ex)
            {
                return $"ERR({ex.GetType().Name})";
            }
        }

        private static void Log(string message) => MelonLogger.Msg($"{Prefix} {message}");
    }

    [HarmonyPatch(typeof(cmpUpdate), nameof(cmpUpdate.cmpUpdateGameEnd))]
    internal static class UiDiagGameEndPatch
    {
        private static void Prefix()
        {
            try
            {
                UiRefreshDiagnosticProbe.OnGameEndUpdate();
            }
            catch
            {
            }
        }
    }

    [HarmonyPatch(typeof(fldEnc), nameof(fldEnc.encStart))]
    internal static class UiDiagEncounterStartPatch
    {
        private static void Prefix() => UiRefreshDiagnosticProbe.OnEncounterStart();
    }

    [HarmonyPatch(typeof(ButtonGuide), nameof(ButtonGuide.ButtonGuideDispSet))]
    internal static class UiDiagButtonGuideDispSetPatch
    {
        private static void Prefix(string __0)
        {
            try
            {
                UiRefreshDiagnosticProbe.OnButtonGuideDispSet(__0);
            }
            catch
            {
            }
        }
    }
}
