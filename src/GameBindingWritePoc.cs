using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Il2Cpp;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using MelonLoader;

namespace NocturneModernController
{
    // TEMPORARY write PoC (native GAME binding edit), not a public feature.
    // Spec: investigations/NATIVE_GAMEPAD_EDIT_DESIGN_20260924.md §12.5-§12.8.
    //
    // Changes exactly one binding: index 7 "Command Menu" (config_data[4]
    // Type 10). STEP A = Y(11) -> X(12), STEP B = X(12) -> Y(11). Each step
    // needs its own explicit keyboard hold, runs at most once per game
    // session, and never chains into the other step.
    //
    // Native APIs used (and nothing else that writes):
    //   dds3ConfigGamePadSteam.ChangeKeyDuplicate  (duplicate check + write)
    //   FsSaveData.SetConfigLocal(4, ...)           (FsSaveData copy + SMT3HDCONFIG)
    //   FsSaveData.SteamConfigLocalCopy(4, false)   (config_data/SelText[0]/[1] from FsSaveData)
    // Never called: ChangeKey, GamePadDuplicateWrite, cfgSetBit, DateSave,
    // DatePrev, ChgConfigGamePad*, SteamConfigLocalLoad, SteamOptionFile.LoadFile.
    // No direct writes to config_data/SelText/g_* and no memory patches.
    internal static class GameBindingWritePoc
    {
        private const string Prefix = "[NMC-GAMEWRITE]";

        private const int TargetIndex = 7;
        private const uint TargetType = TargetIndex + 3;
        private const int RawY = 11;
        private const int RawX = 12;
        private const int GamepadTab = 4;
        private const int ConfigArrayLength = 256;
        private const int GetConfigSlotCount = 34;
        private const int PersistedGamepadEntries = 33;
        private const string CommandMenuKey = "COMMAND_MENU";

        private const string CanonicalGameAssemblySha256 =
            "59ADBB5B18AAEDC7DF6C1672790CA48BE99B5E8559C81DB27132C436FB0A9FC4";
        private const string PocBackupPath =
            @"C:\SMT3Modding\backups\SMT3HDCONFIG\SMT3HDCONFIG.pre-gamewrite-poc-20260924_205938";
        private const string PocBackupSha256 =
            "8C30867747AD6B48AC2D851DE51CE4B1398B08F12AEC83FFB2B3C46B60EDBC5C";

        // Ctrl+Shift+F9 (STEP A) / Ctrl+Shift+F10 (STEP B), held ~1.5 s at 60 fps.
        private const int HoldFrames = 90;
        private const int VkShift = 0x10;
        private const int VkControl = 0x11;
        private const int VkF9 = 0x78;
        private const int VkF10 = 0x79;

        private static readonly int ProcessId = Process.GetCurrentProcess().Id;

        private static bool _disabled;
        private static bool _readyAnnounced;
        private static bool _stepAConsumed;
        private static bool _stepBConsumed;
        private static int _holdA;
        private static int _holdB;
        private static bool _waitForRelease;
        private static string? _sessionFileSha256;
        private static bool? _gameAssemblyVerified;

        // Trigger diagnostics (read-only): logged only on key/gate edges and at
        // hold milestones, never every frame. Does not change when a step fires.
        private static int _traceKeys;
        private static string _traceGate = string.Empty;
        private static int _tracePrevHoldA;
        private static int _tracePrevHoldB;
        private static int _traceMilestoneMs;
        private static readonly Stopwatch TraceHoldClock = new Stopwatch();

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int virtualKey);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

        private sealed class Snapshot
        {
            internal int[] ConfigData = Array.Empty<int>();
            internal int[] SaveLocal = Array.Empty<int>();
            internal int[] GetConfig = Array.Empty<int>();
            internal byte[] FileBytes = Array.Empty<byte>();
            internal string FileSha256 = string.Empty;
            internal DateTime FileMtime;
        }

        private class PocFailure : Exception
        {
            internal PocFailure(string message) : base(message)
            {
            }
        }

        // Duplicate rejected by ChangeKeyDuplicate and nothing was written:
        // stop without calling any further native API.
        private sealed class DuplicateNoWrite : PocFailure
        {
            internal DuplicateNoWrite(string message) : base(message)
            {
            }
        }

        internal static void Sample()
        {
            if (_disabled)
            {
                return;
            }

            TraceTriggerKeys();

            if (!FieldDashPatch.IsExplorationActive ||
                !ModernControllerApi.GameActionBindingsReady ||
                SettingsGuiController.IsOpen)
            {
                _holdA = 0;
                _holdB = 0;
                return;
            }

            if (!_readyAnnounced)
            {
                AnnounceReady();
                if (_disabled)
                {
                    return;
                }
            }

            if (!IsGameForeground())
            {
                _holdA = 0;
                _holdB = 0;
                return;
            }

            bool modifiers = IsDown(VkControl) && IsDown(VkShift);
            bool a = modifiers && IsDown(VkF9) && !IsDown(VkF10);
            bool b = modifiers && IsDown(VkF10) && !IsDown(VkF9);

            if (_waitForRelease)
            {
                if (!IsDown(VkF9) && !IsDown(VkF10))
                {
                    _waitForRelease = false;
                }
                return;
            }

            _holdA = a ? _holdA + 1 : 0;
            _holdB = b ? _holdB + 1 : 0;
            TraceHold();

            if (_holdA == HoldFrames)
            {
                _waitForRelease = true;
                _holdA = 0;
                TryRunStep(isStepA: true);
            }
            else if (_holdB == HoldFrames)
            {
                _waitForRelease = true;
                _holdB = 0;
                TryRunStep(isStepA: false);
            }
        }

        private static void TraceTriggerKeys()
        {
            int keys = (IsDown(VkControl) ? 1 : 0) | (IsDown(VkShift) ? 2 : 0) |
                (IsDown(VkF9) ? 4 : 0) | (IsDown(VkF10) ? 8 : 0);
            if (keys == 0 && _traceKeys == 0)
            {
                return;
            }

            string gate = $"foreground={IsGameForeground()} exploration={FieldDashPatch.IsExplorationActive} " +
                $"bindingsReady={ModernControllerApi.GameActionBindingsReady} settingsOpen={SettingsGuiController.IsOpen} " +
                $"armed={_readyAnnounced} waitForRelease={_waitForRelease}";
            if (keys == _traceKeys && gate == _traceGate)
            {
                return;
            }

            _traceKeys = keys;
            _traceGate = gate;
            Log($"TRIGGER_STATE event=KEYS ctrl={(keys & 1) != 0} shift={(keys & 2) != 0} " +
                $"f9={(keys & 4) != 0} f10={(keys & 8) != 0} {gate} holdA={_holdA} holdB={_holdB}");
        }

        private static void TraceHold()
        {
            int hold = Math.Max(_holdA, _holdB);
            int prev = Math.Max(_tracePrevHoldA, _tracePrevHoldB);
            string step = _holdA > 0 || _tracePrevHoldA > 0 ? "A" : "B";
            _tracePrevHoldA = _holdA;
            _tracePrevHoldB = _holdB;

            if (hold == 1)
            {
                TraceHoldClock.Restart();
                _traceMilestoneMs = 0;
                Log($"TRIGGER_STATE event=HOLD_START step={step} thresholdFrames={HoldFrames}");
                return;
            }
            if (hold == 0)
            {
                if (prev > 0)
                {
                    Log($"TRIGGER_STATE event=HOLD_RELEASE step={step} frames={prev} " +
                        $"holdMs={TraceHoldClock.ElapsedMilliseconds} ready=False");
                }
                return;
            }
            if (hold == HoldFrames)
            {
                Log($"TRIGGER_STATE event=HOLD_THRESHOLD step={step} frames={hold} " +
                    $"holdMs={TraceHoldClock.ElapsedMilliseconds} ready=True");
                // The hold counter is cleared right after firing; skip the release line.
                _tracePrevHoldA = 0;
                _tracePrevHoldB = 0;
                return;
            }

            long ms = TraceHoldClock.ElapsedMilliseconds;
            int milestone = ms >= 1000 ? 1000 : ms >= 500 ? 500 : 0;
            if (milestone > _traceMilestoneMs)
            {
                _traceMilestoneMs = milestone;
                Log($"TRIGGER_STATE event=HOLD_{milestone}MS step={step} frames={hold} holdMs={ms} ready=False");
            }
        }

        private static void AnnounceReady()
        {
            _readyAnnounced = true;
            try
            {
                string path = LocateConfigFile();
                byte[] bytes = ReadFileShared(path);
                _sessionFileSha256 = Sha256(bytes);
                Log($"ARMED file={path} sha256={_sessionFileSha256} " +
                    $"mtime={File.GetLastWriteTime(path):o} matchesPocBackup={_sessionFileSha256 == PocBackupSha256}");
                Log("ARMED STEP A = hold Ctrl+Shift+F9 (~1.5s): Command Menu Y->X. " +
                    "STEP B = hold Ctrl+Shift+F10 (~1.5s): Command Menu X->Y. " +
                    "Run only in field exploration with the native Config screen CLOSED. " +
                    "Each step runs at most once per game session.");
            }
            catch (Exception ex)
            {
                _disabled = true;
                Log($"FAIL ARMED could not read SMT3HDCONFIG ({ex.GetType().Name}: {ex.Message}); PoC disabled, WRITE ZERO");
            }
        }

        private static void TryRunStep(bool isStepA)
        {
            string step = isStepA ? "STEP_A" : "STEP_B";
            if (isStepA ? _stepAConsumed : _stepBConsumed)
            {
                Log($"{step} ignored: already executed in this game session (restart the game to run it again)");
                return;
            }
            if (isStepA)
            {
                _stepAConsumed = true;
            }
            else
            {
                _stepBConsumed = true;
            }

            int expectedCurrent = isStepA ? RawY : RawX;
            int target = isStepA ? RawX : RawY;
            Log($"{step} BEGIN index={TargetIndex} type={TargetType} {RawName(expectedCurrent)}({expectedCurrent})->{RawName(target)}({target})");

            Snapshot snapshot;
            FsSaveData saveData;
            string filePath;
            try
            {
                (snapshot, saveData, filePath) = Precheck(expectedCurrent);
            }
            catch (Exception ex)
            {
                Log($"FAIL {step} PRECHECK {Describe(ex)}; WRITE ZERO (nothing was called)");
                return;
            }

            bool persistStarted = false;
            try
            {
                Rebind(snapshot, target);
                VerifyConfigDiff(snapshot, target);

                persistStarted = true;
                Persist(saveData, snapshot, filePath, target);

                RuntimeApply(saveData);
                Readback(saveData, snapshot, target);

                _sessionFileSha256 = Sha256(ReadFileShared(filePath));
                Log($"SUCCESS {step} index={TargetIndex} now {RawName(target)}({target})");
                Log($"{step}_SUCCESS");
                UiRefreshDiagnosticProbe.OnStepSuccess(step);
                Log(isStepA
                    ? "MANUAL CHECK: (1) X opens Command Menu (2) Y no longer opens it " +
                      "(3) native Controller Config shows X (4) button guide icon shows X. " +
                      "Then fully quit and restart before STEP B."
                    : "MANUAL CHECK: (1) Y opens Command Menu (2) X no longer opens it " +
                      "(3) native Controller Config shows Y (4) button guide icon shows Y. " +
                      "Then fully quit and restart to confirm Y persists.");
            }
            catch (Exception ex)
            {
                Log($"FAIL {step} {Describe(ex)}");
                _disabled = true;
                if (ex is DuplicateNoWrite)
                {
                    Log("No rollback needed (nothing was written). PoC disabled for this session.");
                    return;
                }
                if (persistStarted)
                {
                    Rollback2(saveData, snapshot, filePath);
                }
                else
                {
                    Rollback1(saveData, snapshot, filePath);
                }
                Log("PoC disabled for this session (no automatic retry).");
            }
        }

        // ---------------------------------------------------------------- PRECHECK / SNAPSHOT

        private static (Snapshot, FsSaveData, string) Precheck(int expectedCurrent)
        {
            Log("PRECHECK NOTICE: the native Config screen cannot be detected safely; " +
                "this PoC only accepts triggers while field exploration is active. Keep the Config screen closed.");

            if (!FieldDashPatch.IsExplorationActive || !ModernControllerApi.GameActionBindingsReady)
            {
                throw new PocFailure("exploration/authoritative readiness lost");
            }
            Log("PRECHECK readiness ok (exploration active, authoritative GAME binding snapshot ready)");

            if (!VerifyGameAssembly())
            {
                throw new PocFailure("GameAssembly.dll SHA-256 is not the analysed canonical build");
            }
            Log("PRECHECK GameAssembly.dll sha256 ok (canonical)");

            // index 7 is in the A/B/A-CONFIRMED set (GAMEBINDING_INDEX_MAP_20260921.md).
            Log($"PRECHECK target index={TargetIndex} (Command Menu) is CONFIRMED; type=index+3={TargetType}");

            int getConfig = dds3ConfigGamePadSteam.GetConfigGamePad(TargetIndex);
            if (getConfig != expectedCurrent)
            {
                throw new PocFailure($"GetConfigGamePad({TargetIndex})={getConfig}, expected {expectedCurrent}");
            }
            Log($"PRECHECK GetConfigGamePad({TargetIndex})={getConfig} ({RawName(getConfig)}) ok");

            int[] configData = ReadConfigData();
            Log($"PRECHECK config_data[4] read ok length={configData.Length}");

            FsSaveData saveData = GlobalData.saveData
                ?? throw new PocFailure("GlobalData.saveData is null");
            int[] saveLocal = ReadSaveLocal(saveData);
            Log($"PRECHECK FsSaveData.GetConfigLocal(4) read ok length={saveLocal.Length}");

            List<int> mismatch = Diff(configData, saveLocal);
            if (mismatch.Count != 0)
            {
                throw new PocFailure($"config_data[4] != FsSaveData local at {FormatIndices(mismatch)} (unsaved state present)");
            }
            if (configData[TargetType] != expectedCurrent)
            {
                throw new PocFailure($"config_data[4][{TargetType}]={configData[TargetType]}, expected {expectedCurrent}");
            }
            Log($"PRECHECK config_data[4] == FsSaveData local (256/256), [{TargetType}]={expectedCurrent} ok");

            if (!dds3ConfigGamePadSteam.GamePadDoneChk())
            {
                throw new PocFailure("GamePadDoneChk()=false (an action is already unassigned)");
            }
            Log("PRECHECK GamePadDoneChk()=true ok");

            string filePath = LocateConfigFile();
            byte[] fileBytes = ReadFileShared(filePath);
            string fileSha = Sha256(fileBytes);
            if (_sessionFileSha256 == null || fileSha != _sessionFileSha256)
            {
                throw new PocFailure($"SMT3HDCONFIG sha256 {fileSha} != session baseline {_sessionFileSha256}");
            }
            int[] fileValues = ParseGamepadSection(Encoding.ASCII.GetString(fileBytes));
            for (int i = 1; i <= PersistedGamepadEntries; i++)
            {
                if (fileValues[i] != configData[i])
                {
                    throw new PocFailure($"SMT3HDCONFIG [GAMEPAD] entry {i}={fileValues[i]} != config_data[4][{i}]={configData[i]}");
                }
            }
            Log($"PRECHECK SMT3HDCONFIG sha256={fileSha} matches session baseline; [GAMEPAD] 33/33 == config_data[4] ok");

            var snapshot = new Snapshot
            {
                ConfigData = configData,
                SaveLocal = saveLocal,
                GetConfig = ReadGetConfigAll(),
                FileBytes = fileBytes,
                FileSha256 = fileSha,
                FileMtime = File.GetLastWriteTime(filePath)
            };
            Log($"SNAPSHOT config_data[4][1..33]={FormatRange(snapshot.ConfigData, 1, PersistedGamepadEntries)}");
            Log($"SNAPSHOT config_data[4] nonzero outside 1..33: {FormatNonZeroOutside(snapshot.ConfigData)}");
            Log($"SNAPSHOT GetConfigGamePad[0..33]={string.Join(",", snapshot.GetConfig)}");
            Log($"SNAPSHOT file={filePath} size={fileBytes.Length} sha256={fileSha} mtime={snapshot.FileMtime:o}");
            Log($"SNAPSHOT target: config_data[4][{TargetType}]={configData[TargetType]} " +
                $"FsSaveData[{TargetType}]={saveLocal[TargetType]} GetConfigGamePad({TargetIndex})={snapshot.GetConfig[TargetIndex]} " +
                $"file {CommandMenuKey}={fileValues[TargetType]}");
            Log($"SNAPSHOT external backup={PocBackupPath} expectedSha256={PocBackupSha256}");
            return (snapshot, saveData, filePath);
        }

        // ---------------------------------------------------------------- WRITE TRANSACTION

        private static void Rebind(Snapshot snapshot, int target)
        {
            Log($"REBIND_BEGIN ChangeKeyDuplicate(Type={TargetType}, st_idx=0, ed_idx=0, chgkey={target}, ref dupflg) " +
                $"g_before chgkey={dds3ConfigGamePadSteam.g_chgkey} srcidx={dds3ConfigGamePadSteam.g_srcidx} dstidx={dds3ConfigGamePadSteam.g_dstidx}");

            bool duplicate = false;
            dds3ConfigGamePadSteam.ChangeKeyDuplicate(TargetType, 0, 0, target, ref duplicate);

            int[] after = ReadConfigData();
            Log($"REBIND_RESULT dupflg={duplicate} g_after chgkey={dds3ConfigGamePadSteam.g_chgkey} " +
                $"srcidx={dds3ConfigGamePadSteam.g_srcidx} dstidx={dds3ConfigGamePadSteam.g_dstidx} " +
                $"config_data[4][{TargetType}] {snapshot.ConfigData[TargetType]}->{after[TargetType]}");

            if (duplicate)
            {
                // Native duplicate path writes nothing except g_*; GamePadDuplicateWrite is never called.
                string detail = $"duplicate reported (dstidx={dds3ConfigGamePadSteam.g_dstidx}, " +
                    $"index={dds3ConfigGamePadSteam.g_dstidx - 3}); rebind rejected";
                if (Diff(snapshot.ConfigData, after).Count == 0)
                {
                    throw new DuplicateNoWrite(detail + ", config_data[4] unchanged (WRITE ZERO)");
                }
                throw new PocFailure(detail + ", but config_data[4] changed");
            }
        }

        private static void VerifyConfigDiff(Snapshot snapshot, int target)
        {
            int[] after = ReadConfigData();
            List<int> diff = Diff(snapshot.ConfigData, after);
            Log($"CONFIG_DIFF count={diff.Count} " +
                string.Join(" ", diff.Select(i => $"[{i}]{snapshot.ConfigData[i]}->{after[i]}")));
            if (diff.Count != 1 || diff[0] != TargetType || after[TargetType] != target)
            {
                throw new PocFailure("unexpected config_data[4] diff after rebind");
            }
            if (!dds3ConfigGamePadSteam.GamePadDoneChk())
            {
                throw new PocFailure("GamePadDoneChk()=false after rebind");
            }
            Log("CONFIG_DIFF ok (only the target element changed, GamePadDoneChk()=true)");
        }

        private static void Persist(FsSaveData saveData, Snapshot snapshot, string filePath, int target)
        {
            Il2CppStructArray<int> gamepad = ConfigDataTab();
            Log($"PERSIST_BEGIN FsSaveData.SetConfigLocal(tabpos=4, cfg=dds3GlobalWork.config_data[4] length={gamepad.Length})");
            saveData.SetConfigLocal(GamepadTab, gamepad);

            int[] configData = ReadConfigData();
            int[] saveLocal = ReadSaveLocal(saveData);
            List<int> diff = Diff(configData, saveLocal);
            Log($"PERSIST_RESULT FsSaveData local vs config_data[4] mismatches={diff.Count} " +
                $"FsSaveData[{TargetType}]={saveLocal[TargetType]}");
            if (diff.Count != 0)
            {
                throw new PocFailure("FsSaveData local does not match config_data[4] after SetConfigLocal");
            }

            byte[] bytes = ReadFileShared(filePath);
            string sha = Sha256(bytes);
            DateTime mtime = File.GetLastWriteTime(filePath);
            Log($"FILE_HASH after persist size={bytes.Length} sha256={sha} mtime={mtime:o} " +
                $"(before sha256={snapshot.FileSha256} mtime={snapshot.FileMtime:o})");
            string expected = ReplaceCommandMenuLine(Encoding.ASCII.GetString(snapshot.FileBytes), target);
            string actual = Encoding.ASCII.GetString(bytes);
            if (!string.Equals(expected, actual, StringComparison.Ordinal))
            {
                throw new PocFailure("SMT3HDCONFIG content is not exactly the snapshot with only COMMAND_MENU changed");
            }
            if (mtime <= snapshot.FileMtime)
            {
                throw new PocFailure("SMT3HDCONFIG timestamp did not advance");
            }
            Log($"FILE_HASH ok: only {CommandMenuKey} line changed to {target}");
        }

        private static void RuntimeApply(FsSaveData saveData)
        {
            Log("RUNTIME_APPLY FsSaveData.SteamConfigLocalCopy(tabpos=4, gamelvlflg=false)");
            saveData.SteamConfigLocalCopy(GamepadTab, false);
        }

        private static void Readback(FsSaveData saveData, Snapshot snapshot, int target)
        {
            int[] getConfig = ReadGetConfigAll();
            List<int> diff = Diff(snapshot.GetConfig, getConfig);
            Log($"READBACK GetConfigGamePad({TargetIndex})={getConfig[TargetIndex]} ({RawName(getConfig[TargetIndex])}) " +
                $"34-slot diffs={diff.Count} " + string.Join(" ", diff.Select(i => $"[{i}]{snapshot.GetConfig[i]}->{getConfig[i]}")));
            if (diff.Count != 1 || diff[0] != TargetIndex || getConfig[TargetIndex] != target)
            {
                throw new PocFailure("GetConfigGamePad readback mismatch");
            }

            int[] configData = ReadConfigData();
            int[] saveLocal = ReadSaveLocal(saveData);
            List<int> configDiff = Diff(snapshot.ConfigData, configData);
            if (configDiff.Count != 1 || configDiff[0] != TargetType || Diff(configData, saveLocal).Count != 0)
            {
                throw new PocFailure("config_data[4]/FsSaveData mismatch after runtime apply");
            }
            Log("READBACK ok (34-slot, config_data[4], FsSaveData all consistent)");
        }

        // ---------------------------------------------------------------- ROLLBACK

        // Failure before SetConfigLocal: FsSaveData and the file are untouched,
        // so re-deriving config_data/SelText from FsSaveData restores everything.
        private static void Rollback1(FsSaveData saveData, Snapshot snapshot, string filePath)
        {
            Log("ROLLBACK_BEGIN kind=1 (pre-persist): FsSaveData.SteamConfigLocalCopy(4, false) only, no disk write");
            try
            {
                saveData.SteamConfigLocalCopy(GamepadTab, false);
                bool ok = VerifyRestored(saveData, snapshot, filePath);
                Log($"ROLLBACK_RESULT kind=1 {(ok ? "OK (restored to snapshot)" : "MISMATCH")}");
                if (!ok)
                {
                    Rollback3();
                }
            }
            catch (Exception ex)
            {
                Log($"ROLLBACK_RESULT kind=1 EXCEPTION {Describe(ex)}");
                Rollback3();
            }
        }

        // Failure at/after SetConfigLocal: native reverse rebind (fallback:
        // SetConfigLocal with a fresh array built from the snapshot), persist,
        // then re-derive runtime state from FsSaveData.
        private static void Rollback2(FsSaveData saveData, Snapshot snapshot, string filePath)
        {
            Log("ROLLBACK_BEGIN kind=2 (post-persist)");
            try
            {
                int original = snapshot.ConfigData[TargetType];
                int[] current = ReadConfigData();
                bool nativeOk = true;
                if (current[TargetType] != original)
                {
                    bool duplicate = false;
                    Log($"ROLLBACK native rebind ChangeKeyDuplicate(Type={TargetType}, 0, 0, chgkey={original}, ref dupflg)");
                    dds3ConfigGamePadSteam.ChangeKeyDuplicate(TargetType, 0, 0, original, ref duplicate);
                    nativeOk = !duplicate && Diff(snapshot.ConfigData, ReadConfigData()).Count == 0;
                    Log($"ROLLBACK native rebind dupflg={duplicate} configRestored={nativeOk}");
                }

                if (nativeOk)
                {
                    Log("ROLLBACK persist FsSaveData.SetConfigLocal(4, config_data[4])");
                    saveData.SetConfigLocal(GamepadTab, ConfigDataTab());
                }
                else
                {
                    var restore = new Il2CppStructArray<int>(snapshot.ConfigData.Length);
                    for (int i = 0; i < snapshot.ConfigData.Length; i++)
                    {
                        restore[i] = snapshot.ConfigData[i];
                    }
                    Log("ROLLBACK persist fallback FsSaveData.SetConfigLocal(4, <new array from snapshot>)");
                    saveData.SetConfigLocal(GamepadTab, restore);
                }

                Log("ROLLBACK runtime FsSaveData.SteamConfigLocalCopy(4, false)");
                saveData.SteamConfigLocalCopy(GamepadTab, false);

                bool ok = VerifyRestored(saveData, snapshot, filePath);
                Log($"ROLLBACK_RESULT kind=2 {(ok ? "OK (restored to snapshot)" : "MISMATCH")}");
                if (!ok)
                {
                    Rollback3();
                }
            }
            catch (Exception ex)
            {
                Log($"ROLLBACK_RESULT kind=2 EXCEPTION {Describe(ex)}");
                Rollback3();
            }
        }

        private static void Rollback3()
        {
            _disabled = true;
            Log("ROLLBACK_RESULT kind=3 MANUAL RECOVERY REQUIRED: no further writes will be attempted. " +
                "Quit the game, then restore SMT3HDCONFIG from " + PocBackupPath +
                " (sha256 " + PocBackupSha256 + ").");
        }

        private static bool VerifyRestored(FsSaveData saveData, Snapshot snapshot, string filePath)
        {
            int[] configData = ReadConfigData();
            int[] saveLocal = ReadSaveLocal(saveData);
            int[] getConfig = ReadGetConfigAll();
            string fileSha = Sha256(ReadFileShared(filePath));
            int configDiff = Diff(snapshot.ConfigData, configData).Count;
            int saveDiff = Diff(snapshot.SaveLocal, saveLocal).Count;
            int getDiff = Diff(snapshot.GetConfig, getConfig).Count;
            bool fileOk = fileSha == snapshot.FileSha256;
            Log($"ROLLBACK verify config_data diffs={configDiff} FsSaveData diffs={saveDiff} " +
                $"GetConfigGamePad diffs={getDiff} file sha256={fileSha} matchesSnapshot={fileOk}");
            return configDiff == 0 && saveDiff == 0 && getDiff == 0 && fileOk;
        }

        // ---------------------------------------------------------------- READ HELPERS

        private static Il2CppStructArray<int> ConfigDataTab()
        {
            Il2Cppdds3GlobalWork_H.dds3GlobalWork_t global = dds3GlobalWork.DDS3_GBWK
                ?? throw new PocFailure("dds3GlobalWork.DDS3_GBWK is null");
            Il2CppReferenceArray<Il2CppStructArray<int>> configData = global.config_data
                ?? throw new PocFailure("config_data is null");
            if (configData.Length <= GamepadTab)
            {
                throw new PocFailure($"config_data length {configData.Length}");
            }
            Il2CppStructArray<int> tab = configData[GamepadTab]
                ?? throw new PocFailure("config_data[4] is null");
            if (tab.Length != ConfigArrayLength)
            {
                throw new PocFailure($"config_data[4] length {tab.Length}, expected {ConfigArrayLength}");
            }
            return tab;
        }

        private static int[] ReadConfigData() => ToArray(ConfigDataTab());

        private static int[] ReadSaveLocal(FsSaveData saveData)
        {
            Il2CppStructArray<int> local = saveData.GetConfigLocal(GamepadTab)
                ?? throw new PocFailure("FsSaveData.GetConfigLocal(4) is null");
            if (local.Length != ConfigArrayLength)
            {
                throw new PocFailure($"FsSaveData local length {local.Length}, expected {ConfigArrayLength}");
            }
            return ToArray(local);
        }

        private static int[] ReadGetConfigAll()
        {
            var values = new int[GetConfigSlotCount];
            for (int i = 0; i < GetConfigSlotCount; i++)
            {
                values[i] = dds3ConfigGamePadSteam.GetConfigGamePad(i);
            }
            return values;
        }

        private static int[] ToArray(Il2CppStructArray<int> source)
        {
            var values = new int[source.Length];
            for (int i = 0; i < values.Length; i++)
            {
                values[i] = source[i];
            }
            return values;
        }

        private static List<int> Diff(int[] before, int[] after)
        {
            var result = new List<int>();
            if (before.Length != after.Length)
            {
                result.Add(-1);
                return result;
            }
            for (int i = 0; i < before.Length; i++)
            {
                if (before[i] != after[i])
                {
                    result.Add(i);
                }
            }
            return result;
        }

        // ---------------------------------------------------------------- FILE HELPERS

        private static string LocateConfigFile()
        {
            string root = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SEGA", "smt3hd");
            string[] matches = Directory.Exists(root)
                ? Directory.GetFiles(root, "SMT3HDCONFIG", SearchOption.AllDirectories)
                : Array.Empty<string>();
            if (matches.Length != 1)
            {
                throw new PocFailure($"expected exactly one SMT3HDCONFIG under {root}, found {matches.Length}");
            }
            return matches[0];
        }

        private static byte[] ReadFileShared(string path)
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            return memory.ToArray();
        }

        private static string Sha256(byte[] bytes)
        {
            using SHA256 sha = SHA256.Create();
            return Convert.ToHexString(sha.ComputeHash(bytes));
        }

        // [GAMEPAD] lines are written in enum order starting at config_data[4][1]
        // (design doc §12.3), so the n-th KEY=VALUE line is element n.
        private static int[] ParseGamepadSection(string text)
        {
            var values = new int[PersistedGamepadEntries + 1];
            int count = 0;
            bool inSection = false;
            foreach (string rawLine in text.Split('\n'))
            {
                string line = rawLine.TrimEnd('\r');
                if (line.StartsWith("[", StringComparison.Ordinal))
                {
                    if (inSection)
                    {
                        break;
                    }
                    inSection = line == "[GAMEPAD]";
                    continue;
                }
                if (!inSection || line.Length == 0)
                {
                    continue;
                }
                int eq = line.IndexOf('=');
                if (eq <= 0 || !int.TryParse(line.Substring(eq + 1), out int value))
                {
                    throw new PocFailure($"unparseable [GAMEPAD] line '{line}'");
                }
                count++;
                if (count > PersistedGamepadEntries)
                {
                    throw new PocFailure("[GAMEPAD] has more than 33 entries");
                }
                if (count == TargetType && line.Substring(0, eq) != CommandMenuKey)
                {
                    throw new PocFailure($"[GAMEPAD] entry {TargetType} is '{line.Substring(0, eq)}', expected {CommandMenuKey}");
                }
                values[count] = value;
            }
            if (count != PersistedGamepadEntries)
            {
                throw new PocFailure($"[GAMEPAD] has {count} entries, expected {PersistedGamepadEntries}");
            }
            return values;
        }

        private static string ReplaceCommandMenuLine(string text, int target)
        {
            string[] lines = text.Split('\n');
            bool inSection = false;
            int replaced = 0;
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].TrimEnd('\r');
                if (line.StartsWith("[", StringComparison.Ordinal))
                {
                    inSection = line == "[GAMEPAD]";
                    continue;
                }
                if (inSection && line.StartsWith(CommandMenuKey + "=", StringComparison.Ordinal))
                {
                    string ending = lines[i].EndsWith("\r", StringComparison.Ordinal) ? "\r" : string.Empty;
                    lines[i] = $"{CommandMenuKey}={target}{ending}";
                    replaced++;
                }
            }
            if (replaced != 1)
            {
                throw new PocFailure($"{CommandMenuKey} line found {replaced} times in snapshot");
            }
            return string.Join("\n", lines);
        }

        // ---------------------------------------------------------------- MISC

        private static bool VerifyGameAssembly()
        {
            if (_gameAssemblyVerified.HasValue)
            {
                return _gameAssemblyVerified.Value;
            }
            string modDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? string.Empty;
            string gameAssembly = Path.Combine(Path.GetDirectoryName(modDirectory) ?? string.Empty, "GameAssembly.dll");
            using FileStream stream = File.OpenRead(gameAssembly);
            using SHA256 sha = SHA256.Create();
            string hash = Convert.ToHexString(sha.ComputeHash(stream));
            Log($"PRECHECK GameAssembly.dll path={gameAssembly} sha256={hash}");
            _gameAssemblyVerified = hash == CanonicalGameAssemblySha256;
            return _gameAssemblyVerified.Value;
        }

        private static bool IsGameForeground()
        {
            IntPtr window = GetForegroundWindow();
            if (window == IntPtr.Zero)
            {
                return false;
            }
            GetWindowThreadProcessId(window, out uint processId);
            return processId == (uint)ProcessId;
        }

        private static bool IsDown(int virtualKey) => (GetAsyncKeyState(virtualKey) & 0x8000) != 0;

        private static string RawName(int raw) => raw switch
        {
            RawY => "Y",
            RawX => "X",
            _ => "?"
        };

        private static string FormatRange(int[] values, int start, int count) =>
            string.Join(",", values.Skip(start).Take(count));

        private static string FormatNonZeroOutside(int[] values)
        {
            IEnumerable<string> entries = values
                .Select((value, index) => (value, index))
                .Where(e => (e.index == 0 || e.index > PersistedGamepadEntries) && e.value != 0)
                .Select(e => $"[{e.index}]={e.value}");
            string joined = string.Join(" ", entries);
            return joined.Length == 0 ? "none" : joined;
        }

        private static string FormatIndices(List<int> indices) => string.Join(",", indices);

        private static string Describe(Exception ex) => ex is PocFailure
            ? ex.Message
            : $"{ex.GetType().Name}: {ex.Message}";

        private static void Log(string message) => MelonLogger.Msg($"{Prefix} {message}");
    }
}
