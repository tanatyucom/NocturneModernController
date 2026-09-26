using System;
using System.Collections.Generic;
using System.Text.Json;
using NocturneModernController;

// Pure tests for shared/GameBindingWrite.cs. The fake port models the three
// native layers the PoC verified: config_data[4], FsSaveData local, and the
// runtime copy GetConfigGamePad reads (SelText[0][4]).
internal static class GameBindingWriteTests
{
    private const int CommandMenu = 7;
    private const int Y = 11;
    private const int X = 12;

    internal static void Run()
    {
        Shape();
        RequestIdOneShot();
        Readiness();
        StaleAndInconsistent();
        DryRunAndWritesDisabled();
        Duplicate();
        Success();
        RollbackAfterPersist();
        RollbackBeforePersist();
        RollbackFailureLocksSession();
        Serialization();
    }

    private sealed class FakePort : INativeGameBindingPort
    {
        internal int[] Config = new int[256];
        internal int[] Save = new int[256];
        internal int[] Runtime = new int[256];
        internal bool Ready = true;
        internal bool DoneChk = true;
        internal readonly Dictionary<int, int> ConflictTypeByRaw = new();
        internal int NativeWrites;
        internal int FailRuntimeApplyCalls;
        internal bool FailAllRuntimeApply;
        internal bool BreakPersist;

        internal FakePort()
        {
            for (int type = 3; type <= 33; type++)
            {
                Config[type] = 1;
            }
            Config[CommandMenu + 3] = Y;
            Config[23 + 3] = X;
            Array.Copy(Config, Save, 256);
            Array.Copy(Config, Runtime, 256);
        }

        public bool IsReady => Ready;
        public bool IsSupportedGameBuild => true;
        public int GetConfigGamePad(int index) => index + 3 <= 33 ? Runtime[index + 3] : 0;
        public int[] ReadConfigData() => (int[])Config.Clone();
        public int[] ReadSaveLocal() => (int[])Save.Clone();
        public bool GamePadDoneChk() => DoneChk;
        public int DuplicateDestinationType { get; private set; }

        public bool ChangeKeyDuplicate(int configType, int raw)
        {
            NativeWrites++;
            if (ConflictTypeByRaw.TryGetValue(raw, out int conflict) && conflict != configType)
            {
                DuplicateDestinationType = conflict;
                return true;
            }
            Config[configType] = raw;
            return false;
        }

        public void PersistFromConfigData()
        {
            NativeWrites++;
            Array.Copy(Config, Save, 256);
            if (BreakPersist)
            {
                Save[5] = 99;
            }
        }

        public void PersistFromArray(int[] values)
        {
            NativeWrites++;
            Array.Copy(values, Save, 256);
        }

        public void RuntimeApply()
        {
            NativeWrites++;
            if (FailAllRuntimeApply || FailRuntimeApplyCalls-- > 0)
            {
                throw new InvalidOperationException("injected RuntimeApply failure");
            }
            Array.Copy(Save, Config, 256);
            Array.Copy(Save, Runtime, 256);
        }
    }

    private static GameBindingWriteRequest Request(string id, int newRaw = X, int expected = Y, bool dryRun = false) =>
        new()
        {
            SchemaVersion = 1,
            RequestId = id,
            Operation = "rebind",
            ActionIndex = CommandMenu,
            ExpectedCurrentRaw = expected,
            NewRaw = newRaw,
            DryRun = dryRun
        };

    private static void Shape()
    {
        var service = new GameBindingWriteService(new FakePort(), writesEnabled: true);
        Expect(service.HandleJson("{ not json"), "rejected", "MALFORMED_REQUEST", "malformed JSON");
        Expect(service.HandleJson("{\"SchemaVersion\":1,\"RequestId\":\"a1\",\"Operation\":\"rebind\"}"),
            "rejected", "MALFORMED_REQUEST", "missing fields");
        Expect(service.Handle(With(Request("bad id!"), r => { })), "rejected", "MALFORMED_REQUEST", "invalid request id");
        Expect(service.Handle(With(Request("s1"), r => r.SchemaVersion = 2)), "rejected", "UNSUPPORTED_SCHEMA", "schema");
        Expect(service.Handle(With(Request("s2"), r => r.Operation = "swap")), "rejected", "UNSUPPORTED_OPERATION", "operation");
        Expect(service.Handle(With(Request("s3"), r => r.ActionIndex = 8)), "rejected", "UNSUPPORTED_ACTION", "unconfirmed action");
        Expect(service.Handle(Request("s4", newRaw: 99)), "rejected", "UNSUPPORTED_BUTTON", "unsupported raw");
        Expect(service.Handle(Request("s5", newRaw: Y)), "rejected", "NO_CHANGE", "no change");
    }

    private static void RequestIdOneShot()
    {
        var port = new FakePort();
        var service = new GameBindingWriteService(port, writesEnabled: true);
        Expect(service.Handle(Request("once", dryRun: true)), "validated", null, "first use");
        Expect(service.Handle(Request("once", dryRun: true)), "rejected", "DUPLICATE_REQUEST_ID", "replayed request id");
    }

    private static void Readiness()
    {
        var port = new FakePort { Ready = false };
        var service = new GameBindingWriteService(port, writesEnabled: true);
        Expect(service.Handle(Request("r1")), "rejected", "NOT_READY", "not ready");
        Check(port.NativeWrites == 0, "NOT_READY must not write");
    }

    private static void StaleAndInconsistent()
    {
        var port = new FakePort();
        var service = new GameBindingWriteService(port, writesEnabled: true);
        GameBindingWriteResult stale = service.Handle(Request("st1", newRaw: 13, expected: X));
        Expect(stale, "rejected", "STALE_CURRENT_VALUE", "stale expected value");
        Check(stale.BeforeRaw == Y, "stale result reports the actual current value");

        port.Save[20] = 5;
        Expect(service.Handle(Request("st2")), "rejected", "INCONSISTENT_STATE", "config_data != FsSaveData");
        port.Save[20] = port.Config[20];

        port.DoneChk = false;
        Expect(service.Handle(Request("st3")), "rejected", "INCONSISTENT_STATE", "GamePadDoneChk false");
        Check(port.NativeWrites == 0, "rejections must not write");
    }

    private static void DryRunAndWritesDisabled()
    {
        var port = new FakePort();
        GameBindingWriteResult dry = new GameBindingWriteService(port, writesEnabled: true)
            .Handle(Request("d1", dryRun: true));
        Expect(dry, "validated", null, "dry run");
        Check(dry.DryRun && dry.BeforeRaw == Y && dry.AfterRaw == Y, "dry run reports unchanged value");

        var disabled = new GameBindingWriteService(port, writesEnabled: false);
        Expect(disabled.Handle(Request("d2")), "rejected", "WRITES_DISABLED", "writes disabled");
        Expect(disabled.Handle(Request("d3", dryRun: true)), "validated", null, "dry run allowed while writes disabled");
        Check(port.NativeWrites == 0, "dry run / disabled must not write");
    }

    private static void Duplicate()
    {
        var port = new FakePort();
        port.ConflictTypeByRaw[X] = 23 + 3;
        GameBindingWriteResult result = new GameBindingWriteService(port, writesEnabled: true).Handle(Request("dup"));
        Expect(result, "rejected", "DUPLICATE", "native duplicate");
        Check(result.ConflictActionIndex == 23, "duplicate reports the conflicting action index");
        Check(port.Config[CommandMenu + 3] == Y && port.Save[CommandMenu + 3] == Y, "duplicate leaves state unchanged");
    }

    private static void Success()
    {
        var port = new FakePort();
        GameBindingWriteResult result = new GameBindingWriteService(port, writesEnabled: true).Handle(Request("ok"));
        Expect(result, "success", null, "success");
        Check(result.BeforeRaw == Y && result.AfterRaw == X, "success before/after");
        Check(port.Config[10] == X && port.Save[10] == X && port.GetConfigGamePad(CommandMenu) == X,
            "success updates config_data, FsSaveData and runtime");
    }

    private static void RollbackAfterPersist()
    {
        var port = new FakePort { FailRuntimeApplyCalls = 1 };
        GameBindingWriteResult result = new GameBindingWriteService(port, writesEnabled: true).Handle(Request("rb1"));
        Expect(result, "rollback_success", "RUNTIME_APPLY_FAILED", "runtime apply failure rolls back");
        Check(port.Config[10] == Y && port.Save[10] == Y && port.GetConfigGamePad(CommandMenu) == Y,
            "post-persist rollback restores all layers");
    }

    private static void RollbackBeforePersist()
    {
        var port = new FakePort();
        // GamePadDoneChk turns false after the rebind: failure before SetConfigLocal.
        var service = new GameBindingWriteService(new DoneChkAfterWritePort(port), writesEnabled: true);
        GameBindingWriteResult result = service.Handle(Request("rb2"));
        Expect(result, "rollback_success", "NATIVE_WRITE_FAILED", "pre-persist failure rolls back");
        Check(port.Config[10] == Y && port.Save[10] == Y, "pre-persist rollback restores config_data");
        Check(!service.SessionLocked, "a successful rollback does not lock the session");
    }

    private static void RollbackFailureLocksSession()
    {
        var port = new FakePort { BreakPersist = true, FailAllRuntimeApply = true };
        var service = new GameBindingWriteService(port, writesEnabled: true);
        GameBindingWriteResult result = service.Handle(Request("rb3"));
        Expect(result, "rollback_failed", "PERSIST_FAILED", "unrecoverable failure");
        Check(service.SessionLocked, "failed rollback locks the session");
        port.BreakPersist = false;
        Expect(service.Handle(Request("rb4", dryRun: true)), "rejected", "SESSION_LOCKED", "locked session rejects");
    }

    private static void Serialization()
    {
        var success = new GameBindingWriteResult
        {
            RequestId = "r-1", Status = "success", ActionIndex = 7, BeforeRaw = 11, AfterRaw = 12
        };
        string json = GameBindingWriteProtocol.Serialize(success);
        GameBindingWriteResult? back = JsonSerializer.Deserialize<GameBindingWriteResult>(json);
        Check(back != null && back.Status == "success" && back.ErrorCode == null &&
            back.BeforeRaw == 11 && back.AfterRaw == 12 && back.SchemaVersion == 1, "success result round trip");

        var rollback = new GameBindingWriteResult
        {
            RequestId = "r-2", Status = "rollback_failed", ErrorCode = "READBACK_FAILED", ActionIndex = 7
        };
        back = JsonSerializer.Deserialize<GameBindingWriteResult>(GameBindingWriteProtocol.Serialize(rollback));
        Check(back != null && back.Status == "rollback_failed" && back.ErrorCode == "READBACK_FAILED",
            "rollback result round trip");
    }

    // Reports GamePadDoneChk()=false once a native write has happened.
    private sealed class DoneChkAfterWritePort : INativeGameBindingPort
    {
        private readonly FakePort _inner;
        internal DoneChkAfterWritePort(FakePort inner) => _inner = inner;
        public bool IsReady => _inner.IsReady;
        public bool IsSupportedGameBuild => true;
        public int GetConfigGamePad(int index) => _inner.GetConfigGamePad(index);
        public int[] ReadConfigData() => _inner.ReadConfigData();
        public int[] ReadSaveLocal() => _inner.ReadSaveLocal();
        public bool GamePadDoneChk() => _inner.NativeWrites == 0;
        public bool ChangeKeyDuplicate(int configType, int raw) => _inner.ChangeKeyDuplicate(configType, raw);
        public int DuplicateDestinationType => _inner.DuplicateDestinationType;
        public void PersistFromConfigData() => _inner.PersistFromConfigData();
        public void PersistFromArray(int[] values) => _inner.PersistFromArray(values);
        public void RuntimeApply() => _inner.RuntimeApply();
    }

    private static GameBindingWriteRequest With(GameBindingWriteRequest request, Action<GameBindingWriteRequest> edit)
    {
        edit(request);
        return request;
    }

    private static void Expect(GameBindingWriteResult result, string status, string? errorCode, string message)
    {
        if (result.Status != status || result.ErrorCode != errorCode)
        {
            throw new InvalidOperationException(
                $"{message}: expected {status}/{errorCode ?? "null"}, got {result.Status}/{result.ErrorCode ?? "null"} ({result.Message})");
        }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
