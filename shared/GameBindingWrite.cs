using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace NocturneModernController
{
    // Native GAME binding editing: Settings -> Core request/result protocol and the
    // write transaction proven by the 2026-09-26 Y<->X round-trip PoC
    // (investigations/NATIVE_GAMEPAD_EDIT_DESIGN_20260924.md §12-§14).
    //
    // Everything in this file is pure C#: the native calls go through
    // INativeGameBindingPort so the protocol, validation, transaction order and
    // rollback can be unit tested without the game.

    internal sealed class GameBindingWriteRequest
    {
        public int? SchemaVersion { get; set; }
        public string? RequestId { get; set; }
        public string? Operation { get; set; }
        public int? ActionIndex { get; set; }
        public int? ExpectedCurrentRaw { get; set; }
        public int? NewRaw { get; set; }

        // Runs every precheck and reports "validated" without any native write.
        public bool DryRun { get; set; }
    }

    internal sealed class GameBindingWriteResult
    {
        public int SchemaVersion { get; set; } = GameBindingWriteProtocol.SchemaVersion;
        public string RequestId { get; set; } = string.Empty;
        public string Status { get; set; } = GameBindingWriteStatus.Rejected;
        public string? ErrorCode { get; set; }
        public int ActionIndex { get; set; } = -1;
        public int? BeforeRaw { get; set; }
        public int? AfterRaw { get; set; }
        public int? RequestedRaw { get; set; }

        // Native duplicate check: the action that already uses NewRaw.
        public int? ConflictActionIndex { get; set; }
        public bool DryRun { get; set; }
        public string Message { get; set; } = string.Empty;
    }

    internal static class GameBindingWriteStatus
    {
        internal const string Success = "success";
        internal const string Validated = "validated";
        internal const string Rejected = "rejected";
        internal const string Failed = "failed";
        internal const string RollbackSuccess = "rollback_success";
        internal const string RollbackFailed = "rollback_failed";
    }

    internal static class GameBindingWriteError
    {
        internal const string MalformedRequest = "MALFORMED_REQUEST";
        internal const string UnsupportedSchema = "UNSUPPORTED_SCHEMA";
        internal const string UnsupportedOperation = "UNSUPPORTED_OPERATION";
        internal const string DuplicateRequestId = "DUPLICATE_REQUEST_ID";
        internal const string UnsupportedAction = "UNSUPPORTED_ACTION";
        internal const string UnsupportedButton = "UNSUPPORTED_BUTTON";
        internal const string NoChange = "NO_CHANGE";
        internal const string NotReady = "NOT_READY";
        internal const string UnsupportedGameBuild = "UNSUPPORTED_GAME_BUILD";
        internal const string StaleCurrentValue = "STALE_CURRENT_VALUE";
        internal const string InconsistentState = "INCONSISTENT_STATE";
        internal const string WritesDisabled = "WRITES_DISABLED";
        internal const string SessionLocked = "SESSION_LOCKED";
        internal const string Duplicate = "DUPLICATE";
        internal const string NativeWriteFailed = "NATIVE_WRITE_FAILED";
        internal const string PersistFailed = "PERSIST_FAILED";
        internal const string RuntimeApplyFailed = "RUNTIME_APPLY_FAILED";
        internal const string ReadbackFailed = "READBACK_FAILED";
        internal const string InternalError = "INTERNAL_ERROR";
    }

    internal static class GameBindingWriteProtocol
    {
        internal const int SchemaVersion = 1;
        internal const string RebindOperation = "rebind";
        internal const int MaxRequestIdLength = 64;

        // GetConfigGamePad index i is config_data[4][i + 3] (all 33 persisted
        // entries, design doc §12.3) and ChangeKeyDuplicate's Type argument.
        internal const int ConfigTypeOffset = 3;
        internal const int ConfigArrayLength = 256;
        internal const int SlotCount = GameActionBindingSnapshotReader.SlotCount;

        internal static bool IsSupportedAction(int actionIndex) =>
            GameActionBindingSnapshotReader.IsConfirmedAction(actionIndex);

        internal static bool IsValidRequestId(string? requestId) =>
            !string.IsNullOrEmpty(requestId) &&
            requestId.Length <= MaxRequestIdLength &&
            requestId.All(c =>
                (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') ||
                c == '-' || c == '_');

        internal static GameBindingWriteRequest? Parse(string json, out string? error)
        {
            error = null;
            try
            {
                GameBindingWriteRequest? request = JsonSerializer.Deserialize<GameBindingWriteRequest>(json);
                if (request == null)
                {
                    error = "request is null";
                }
                return request;
            }
            catch (JsonException ex)
            {
                error = "invalid JSON: " + ex.Message;
                return null;
            }
        }

        internal static string Serialize(GameBindingWriteResult result) =>
            JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });

        // Pure checks that need no game state. Returns null when the request is
        // well-formed and targets a supported action/button.
        internal static GameBindingWriteResult? ValidateShape(GameBindingWriteRequest request)
        {
            if (!IsValidRequestId(request.RequestId))
            {
                return Reject(request, GameBindingWriteError.MalformedRequest, "RequestId is missing or invalid");
            }
            if (request.SchemaVersion == null ||
                request.Operation == null ||
                request.ActionIndex == null ||
                request.ExpectedCurrentRaw == null ||
                request.NewRaw == null)
            {
                return Reject(request, GameBindingWriteError.MalformedRequest, "a required field is missing");
            }
            if (request.SchemaVersion != SchemaVersion)
            {
                return Reject(request, GameBindingWriteError.UnsupportedSchema,
                    $"SchemaVersion {request.SchemaVersion} is not supported (expected {SchemaVersion})");
            }
            if (!string.Equals(request.Operation, RebindOperation, StringComparison.Ordinal))
            {
                return Reject(request, GameBindingWriteError.UnsupportedOperation,
                    $"Operation '{request.Operation}' is not supported");
            }
            if (!IsSupportedAction(request.ActionIndex.Value))
            {
                return Reject(request, GameBindingWriteError.UnsupportedAction,
                    $"ActionIndex {request.ActionIndex} is not a confirmed action");
            }
            if (!GameBindingSupportedButtons.IsSupported(request.NewRaw.Value))
            {
                return Reject(request, GameBindingWriteError.UnsupportedButton,
                    $"NewRaw {request.NewRaw} is not a supported button");
            }
            if (request.NewRaw == request.ExpectedCurrentRaw)
            {
                return Reject(request, GameBindingWriteError.NoChange, "NewRaw equals ExpectedCurrentRaw");
            }
            return null;
        }

        internal static GameBindingWriteResult Reject(GameBindingWriteRequest request, string errorCode, string message) =>
            new()
            {
                RequestId = request.RequestId ?? string.Empty,
                Status = GameBindingWriteStatus.Rejected,
                ErrorCode = errorCode,
                ActionIndex = request.ActionIndex ?? -1,
                RequestedRaw = request.NewRaw,
                DryRun = request.DryRun,
                Message = message
            };
    }

    // The native surface the transaction needs. Implemented against Il2Cpp in
    // src/NativeGameBindingPort.cs and by a fake in the tests.
    internal interface INativeGameBindingPort
    {
        // FieldDashPatch.IsExplorationActive && authoritative snapshot captured.
        bool IsReady { get; }
        bool IsSupportedGameBuild { get; }
        int GetConfigGamePad(int index);
        int[] ReadConfigData();
        int[] ReadSaveLocal();
        bool GamePadDoneChk();

        // ChangeKeyDuplicate(type, 0, 0, raw, ref dup); returns dup.
        bool ChangeKeyDuplicate(int configType, int raw);

        // g_dstidx after a duplicate report (config type of the conflicting action).
        int DuplicateDestinationType { get; }

        // FsSaveData.SetConfigLocal(4, config_data[4]) -> writes SMT3HDCONFIG.
        void PersistFromConfigData();

        // FsSaveData.SetConfigLocal(4, <copy of values>) (rollback fallback only).
        void PersistFromArray(int[] values);

        // FsSaveData.SteamConfigLocalCopy(4, false) -> config_data/SelText from FsSaveData.
        void RuntimeApply();
    }

    internal sealed class GameBindingWriteService
    {
        private readonly INativeGameBindingPort _port;
        private readonly HashSet<string> _processedRequestIds = new(StringComparer.Ordinal);

        internal GameBindingWriteService(INativeGameBindingPort port, bool writesEnabled)
        {
            _port = port;
            WritesEnabled = writesEnabled;
        }

        internal bool WritesEnabled { get; }

        // Set after a failed rollback: no further writes this session.
        internal bool SessionLocked { get; private set; }

        private sealed class Snapshot
        {
            internal int[] ConfigData = Array.Empty<int>();
            internal int[] SaveLocal = Array.Empty<int>();
            internal int[] GetConfig = Array.Empty<int>();
        }

        private sealed class StageFailure : Exception
        {
            internal StageFailure(string errorCode, string message) : base(message)
            {
                ErrorCode = errorCode;
            }

            internal string ErrorCode { get; }
        }

        internal GameBindingWriteResult HandleJson(string json)
        {
            GameBindingWriteRequest? request = GameBindingWriteProtocol.Parse(json, out string? error);
            if (request == null)
            {
                return new GameBindingWriteResult
                {
                    Status = GameBindingWriteStatus.Rejected,
                    ErrorCode = GameBindingWriteError.MalformedRequest,
                    Message = error ?? "request could not be parsed"
                };
            }
            return Handle(request);
        }

        internal GameBindingWriteResult Handle(GameBindingWriteRequest request)
        {
            GameBindingWriteResult? shape = GameBindingWriteProtocol.ValidateShape(request);
            if (shape != null)
            {
                return shape;
            }

            // One-shot: a request id is never processed twice in this session,
            // whatever the outcome.
            if (!_processedRequestIds.Add(request.RequestId!))
            {
                return GameBindingWriteProtocol.Reject(request, GameBindingWriteError.DuplicateRequestId,
                    "RequestId was already processed in this session");
            }

            try
            {
                return Execute(request);
            }
            catch (Exception ex)
            {
                // Only reachable from the read-only precheck stage; write stages
                // catch their own exceptions and roll back.
                return GameBindingWriteProtocol.Reject(request, GameBindingWriteError.InternalError,
                    $"{ex.GetType().Name}: {ex.Message}");
            }
        }

        private GameBindingWriteResult Execute(GameBindingWriteRequest request)
        {
            int index = request.ActionIndex!.Value;
            int type = index + GameBindingWriteProtocol.ConfigTypeOffset;
            int expected = request.ExpectedCurrentRaw!.Value;
            int target = request.NewRaw!.Value;

            if (SessionLocked)
            {
                return GameBindingWriteProtocol.Reject(request, GameBindingWriteError.SessionLocked,
                    "a previous rollback failed; restart the game before editing again");
            }
            if (!_port.IsReady)
            {
                return GameBindingWriteProtocol.Reject(request, GameBindingWriteError.NotReady,
                    "field exploration with an authoritative GAME binding snapshot is required");
            }
            if (!_port.IsSupportedGameBuild)
            {
                return GameBindingWriteProtocol.Reject(request, GameBindingWriteError.UnsupportedGameBuild,
                    "GameAssembly.dll is not the analysed build");
            }

            int current = _port.GetConfigGamePad(index);
            if (current != expected)
            {
                GameBindingWriteResult stale = GameBindingWriteProtocol.Reject(request,
                    GameBindingWriteError.StaleCurrentValue,
                    $"current value is {current}, request expected {expected}");
                stale.BeforeRaw = current;
                return stale;
            }

            Snapshot snapshot = CaptureSnapshot();
            if (Diff(snapshot.ConfigData, snapshot.SaveLocal).Count != 0)
            {
                return RejectAt(request, current, GameBindingWriteError.InconsistentState,
                    "config_data[4] differs from FsSaveData (unsaved native Config state)");
            }
            if (snapshot.ConfigData[type] != current)
            {
                return RejectAt(request, current, GameBindingWriteError.InconsistentState,
                    $"config_data[4][{type}]={snapshot.ConfigData[type]} does not match GetConfigGamePad({index})={current}");
            }
            if (!_port.GamePadDoneChk())
            {
                return RejectAt(request, current, GameBindingWriteError.InconsistentState,
                    "GamePadDoneChk()=false (an action is unassigned)");
            }

            if (request.DryRun)
            {
                return new GameBindingWriteResult
                {
                    RequestId = request.RequestId!,
                    Status = GameBindingWriteStatus.Validated,
                    ActionIndex = index,
                    BeforeRaw = current,
                    AfterRaw = current,
                    RequestedRaw = target,
                    DryRun = true,
                    Message = "all prechecks passed; nothing was written"
                };
            }
            if (!WritesEnabled)
            {
                return RejectAt(request, current, GameBindingWriteError.WritesDisabled,
                    "native GAME binding writes are disabled in this build");
            }

            return Write(request, snapshot, index, type, target);
        }

        private GameBindingWriteResult Write(
            GameBindingWriteRequest request, Snapshot snapshot, int index, int type, int target)
        {
            int before = snapshot.GetConfig[index];
            bool persistStarted = false;
            try
            {
                bool duplicate = Stage(GameBindingWriteError.NativeWriteFailed,
                    () => _port.ChangeKeyDuplicate(type, target));
                int[] afterRebind = Stage(GameBindingWriteError.NativeWriteFailed, _port.ReadConfigData);
                if (duplicate)
                {
                    int conflictType = _port.DuplicateDestinationType;
                    if (Diff(snapshot.ConfigData, afterRebind).Count == 0)
                    {
                        GameBindingWriteResult rejected = RejectAt(request, before, GameBindingWriteError.Duplicate,
                            "the native duplicate check refused the button; nothing was written");
                        rejected.ConflictActionIndex = conflictType - GameBindingWriteProtocol.ConfigTypeOffset;
                        return rejected;
                    }
                    throw new StageFailure(GameBindingWriteError.NativeWriteFailed,
                        "duplicate reported but config_data[4] changed");
                }
                List<int> diff = Diff(snapshot.ConfigData, afterRebind);
                if (diff.Count != 1 || diff[0] != type || afterRebind[type] != target ||
                    !Stage(GameBindingWriteError.NativeWriteFailed, _port.GamePadDoneChk))
                {
                    throw new StageFailure(GameBindingWriteError.NativeWriteFailed,
                        "config_data[4] did not change exactly at the target element");
                }

                persistStarted = true;
                Stage(GameBindingWriteError.PersistFailed, _port.PersistFromConfigData);
                if (Diff(Stage(GameBindingWriteError.PersistFailed, _port.ReadConfigData),
                        Stage(GameBindingWriteError.PersistFailed, _port.ReadSaveLocal)).Count != 0)
                {
                    throw new StageFailure(GameBindingWriteError.PersistFailed,
                        "FsSaveData does not match config_data[4] after SetConfigLocal");
                }

                Stage(GameBindingWriteError.RuntimeApplyFailed, _port.RuntimeApply);

                int[] getConfig = Stage(GameBindingWriteError.ReadbackFailed, ReadGetConfigAll);
                List<int> getDiff = Diff(snapshot.GetConfig, getConfig);
                int[] configData = Stage(GameBindingWriteError.ReadbackFailed, _port.ReadConfigData);
                int[] saveLocal = Stage(GameBindingWriteError.ReadbackFailed, _port.ReadSaveLocal);
                List<int> configDiff = Diff(snapshot.ConfigData, configData);
                if (getDiff.Count != 1 || getDiff[0] != index || getConfig[index] != target ||
                    configDiff.Count != 1 || configDiff[0] != type ||
                    Diff(configData, saveLocal).Count != 0)
                {
                    throw new StageFailure(GameBindingWriteError.ReadbackFailed,
                        "readback does not show exactly the requested change");
                }

                return new GameBindingWriteResult
                {
                    RequestId = request.RequestId!,
                    Status = GameBindingWriteStatus.Success,
                    ActionIndex = index,
                    BeforeRaw = before,
                    AfterRaw = target,
                    RequestedRaw = target,
                    Message = "binding changed and saved"
                };
            }
            catch (Exception ex)
            {
                string errorCode = ex is StageFailure stage ? stage.ErrorCode : GameBindingWriteError.InternalError;
                bool restored = persistStarted
                    ? RollbackAfterPersist(snapshot, type)
                    : RollbackBeforePersist(snapshot);
                if (!restored)
                {
                    SessionLocked = true;
                }
                return new GameBindingWriteResult
                {
                    RequestId = request.RequestId!,
                    Status = restored ? GameBindingWriteStatus.RollbackSuccess : GameBindingWriteStatus.RollbackFailed,
                    ErrorCode = errorCode,
                    ActionIndex = index,
                    BeforeRaw = before,
                    AfterRaw = SafeGetConfig(index),
                    RequestedRaw = target,
                    Message = restored
                        ? ex.Message + "; restored to the previous binding"
                        : ex.Message + "; ROLLBACK FAILED, restart the game and check the native Controller Config"
                };
            }
        }

        // Before SetConfigLocal, FsSaveData and SMT3HDCONFIG are untouched, so
        // re-deriving config_data/SelText from FsSaveData restores everything.
        private bool RollbackBeforePersist(Snapshot snapshot)
        {
            try
            {
                _port.RuntimeApply();
                return IsRestored(snapshot);
            }
            catch
            {
                return false;
            }
        }

        // After SetConfigLocal: native reverse rebind (fallback: persist the
        // snapshot array directly), persist, then re-derive runtime state.
        private bool RollbackAfterPersist(Snapshot snapshot, int type)
        {
            try
            {
                int original = snapshot.ConfigData[type];
                bool nativeOk = true;
                if (_port.ReadConfigData()[type] != original)
                {
                    bool duplicate = _port.ChangeKeyDuplicate(type, original);
                    nativeOk = !duplicate && Diff(snapshot.ConfigData, _port.ReadConfigData()).Count == 0;
                }
                if (nativeOk)
                {
                    _port.PersistFromConfigData();
                }
                else
                {
                    _port.PersistFromArray((int[])snapshot.ConfigData.Clone());
                }
                _port.RuntimeApply();
                return IsRestored(snapshot);
            }
            catch
            {
                return false;
            }
        }

        private bool IsRestored(Snapshot snapshot) =>
            Diff(snapshot.ConfigData, _port.ReadConfigData()).Count == 0 &&
            Diff(snapshot.SaveLocal, _port.ReadSaveLocal()).Count == 0 &&
            Diff(snapshot.GetConfig, ReadGetConfigAll()).Count == 0;

        private Snapshot CaptureSnapshot()
        {
            int[] configData = _port.ReadConfigData();
            int[] saveLocal = _port.ReadSaveLocal();
            if (configData.Length != GameBindingWriteProtocol.ConfigArrayLength ||
                saveLocal.Length != GameBindingWriteProtocol.ConfigArrayLength)
            {
                throw new InvalidOperationException(
                    $"unexpected config array length {configData.Length}/{saveLocal.Length}");
            }
            return new Snapshot
            {
                ConfigData = configData,
                SaveLocal = saveLocal,
                GetConfig = ReadGetConfigAll()
            };
        }

        private int[] ReadGetConfigAll()
        {
            var values = new int[GameBindingWriteProtocol.SlotCount];
            for (int i = 0; i < values.Length; i++)
            {
                values[i] = _port.GetConfigGamePad(i);
            }
            return values;
        }

        private int? SafeGetConfig(int index)
        {
            try
            {
                return _port.GetConfigGamePad(index);
            }
            catch
            {
                return null;
            }
        }

        private static GameBindingWriteResult RejectAt(
            GameBindingWriteRequest request, int current, string errorCode, string message)
        {
            GameBindingWriteResult result = GameBindingWriteProtocol.Reject(request, errorCode, message);
            result.BeforeRaw = current;
            result.AfterRaw = current;
            return result;
        }

        private static T Stage<T>(string errorCode, Func<T> action)
        {
            try
            {
                return action();
            }
            catch (StageFailure)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new StageFailure(errorCode, $"{ex.GetType().Name}: {ex.Message}");
            }
        }

        private static void Stage(string errorCode, Action action) =>
            Stage(errorCode, () =>
            {
                action();
                return true;
            });

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
    }
}
