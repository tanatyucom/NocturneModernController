using System;
using System.Collections.Generic;
using System.Linq;
using NocturneModernController;

// Pure tests for shared/GameBindingEdit.cs (Settings GAME Bindings tab model).
internal static class GameBindingEditTests
{
    internal static void Run()
    {
        RowsSurviveRawChanges();
        UnknownCurrent();
        ApplyCounts();
        RequestContents();
        CancelAndUnsupported();
        NotAuthoritative();
        ResultMessages();
        MessagesHideErrorCodes();
        ResultConsumedOnce();
        RequestRoundTrip();
    }

    private static void MessagesHideErrorCodes()
    {
        string[] codes =
        {
            "MALFORMED_REQUEST", "UNSUPPORTED_SCHEMA", "UNSUPPORTED_OPERATION", "DUPLICATE_REQUEST_ID",
            "UNSUPPORTED_ACTION", "UNSUPPORTED_BUTTON", "NO_CHANGE", "NOT_READY", "UNSUPPORTED_GAME_BUILD",
            "STALE_CURRENT_VALUE", "INCONSISTENT_STATE", "WRITES_DISABLED", "SESSION_LOCKED", "DUPLICATE",
            "NATIVE_WRITE_FAILED", "PERSIST_FAILED", "RUNTIME_APPLY_FAILED", "READBACK_FAILED", "INTERNAL_ERROR"
        };
        foreach (string status in new[] { "rejected", "rollback_success", "rollback_failed" })
        {
            foreach (string code in codes)
            {
                foreach (bool japanese in new[] { true, false })
                {
                    string text = GameBindingResultMessages.Describe(new GameBindingWriteResult
                    {
                        Status = status, ErrorCode = code, ActionIndex = 7, RequestedRaw = 12, ConflictActionIndex = 23
                    }, japanese);
                    Check(text.Length > 0 && codes.All(c => !text.Contains(c)),
                        $"{status}/{code} message must not expose an error code: {text}");
                }
            }
        }
    }

    private static void ResultConsumedOnce()
    {
        string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "nmc-game-binding-result-test.json");
        System.IO.File.WriteAllText(path, GameBindingWriteProtocol.Serialize(new GameBindingWriteResult
        {
            RequestId = "r1", Status = "success", ActionIndex = 7, BeforeRaw = 12, AfterRaw = 11, RequestedRaw = 11
        }));
        GameBindingWriteResult? first = GameBindingResultFile.Consume(path);
        Check(first != null && first.Status == "success" && first.AfterRaw == 11, "result is read once");
        Check(!System.IO.File.Exists(path), "result file is deleted after it is read");
        Check(GameBindingResultFile.Consume(path) == null, "a consumed result is not shown again");

        System.IO.File.WriteAllText(path, "{ broken");
        Check(GameBindingResultFile.Consume(path) == null && !System.IO.File.Exists(path),
            "a broken result file is discarded");
    }

    private static void RequestRoundTrip()
    {
        var model = new GameBindingEditModel(Snapshot(), authoritative: true);
        model.SetSelection(7, 12);
        GameBindingWriteRequest request = model.BuildRequest(() => "rt-1")!;
        string json = System.Text.Json.JsonSerializer.Serialize(request);
        GameBindingWriteRequest? parsed = GameBindingWriteProtocol.Parse(json, out string? error);
        Check(error == null && parsed != null && parsed.RequestId == "rt-1" && parsed.ActionIndex == 7 &&
            parsed.ExpectedCurrentRaw == 11 && parsed.NewRaw == 12 && parsed.Operation == "rebind" &&
            parsed.SchemaVersion == 1 && !parsed.DryRun, "Settings request JSON parses back identically in Core");
    }

    // Snapshot as Core publishes it: the 15 confirmed actions (+ one unconfirmed index).
    private static List<GameActionBindingRawEntry> Snapshot(int commandMenuRaw = 11, int autoBattleRaw = 11) =>
        GameActionBindingSnapshotReader.Capture(index => index switch
        {
            7 => commandMenuRaw,
            18 => autoBattleRaw,
            _ => 9
        }).Entries.ToList();

    private static void RowsSurviveRawChanges()
    {
        var model = new GameBindingEditModel(Snapshot(commandMenuRaw: 13), authoritative: true);
        Check(model.Rows.Count == 15, "all 15 confirmed actions are listed after a raw change");
        GameActionBindingDisplayRow menu = model.Rows.Single(row => row.Index == 7);
        Check(menu.CurrentButton == "LB" && menu.CurrentSupported, "raw 13 shows as LB");
    }

    private static void UnknownCurrent()
    {
        var model = new GameBindingEditModel(Snapshot(commandMenuRaw: 24), authoritative: true);
        GameActionBindingDisplayRow menu = model.Rows.Single(row => row.Index == 7);
        Check(menu.CurrentButton == "Unknown (raw=24)" && !menu.CurrentSupported, "unknown raw stays visible");
        Check(!model.IsEditable(7), "unknown current is not editable");
        Check(!model.SetSelection(7, 12), "unknown current rejects a selection");
        Check(model.BuildRequest(NewId) == null, "unknown current never builds a request");
    }

    private static void ApplyCounts()
    {
        var model = new GameBindingEditModel(Snapshot(), authoritative: true);
        Check(model.ApplyState == GameBindingApplyState.NoChanges && model.BuildRequest(NewId) == null, "0 changes: no apply");

        model.SetSelection(7, 12);
        Check(model.ApplyState == GameBindingApplyState.Ready, "1 change: apply ready");

        model.SetSelection(18, 12);
        Check(model.ApplyState == GameBindingApplyState.MultipleChanges && model.BuildRequest(NewId) == null,
            "2 changes: no apply");

        model.SetSelection(18, 11);
        Check(model.ApplyState == GameBindingApplyState.Ready, "selecting the current value again is not a change");
    }

    private static void RequestContents()
    {
        var model = new GameBindingEditModel(Snapshot(), authoritative: true);
        model.SetSelection(7, 13);
        GameBindingWriteRequest? request = model.BuildRequest(() => "req-1");
        Check(request != null, "1 change builds a request");
        Check(request!.SchemaVersion == 1 && request.RequestId == "req-1" && request.Operation == "rebind" &&
            request.ActionIndex == 7 && !request.DryRun, "request uses the Step 1 schema");
        Check(request.ExpectedCurrentRaw == 11, "ExpectedCurrentRaw is the snapshot value");
        Check(request.NewRaw == 13, "NewRaw is the selected value");
        Check(GameBindingWriteProtocol.ValidateShape(request) == null, "request passes Core shape validation");
    }

    private static void CancelAndUnsupported()
    {
        var model = new GameBindingEditModel(Snapshot(), authoritative: true);
        model.SetSelection(7, 12);
        model.Cancel();
        Check(model.PendingCount == 0 && model.BuildRequest(NewId) == null && model.GetSelection(7) == 11,
            "cancel drops the pending change and builds nothing");

        Check(!model.SetSelection(7, 99), "an unsupported raw cannot be selected");
        Check(GameBindingEditModel.Options.Count == 12 &&
            GameBindingEditModel.Options.All(option => GameBindingSupportedButtons.IsSupported(option.Raw)),
            "the dropdown offers exactly the 12 supported buttons");
        Check(!model.SetSelection(8, 12), "an unconfirmed action cannot be edited");
    }

    private static void NotAuthoritative()
    {
        var model = new GameBindingEditModel(Snapshot(), authoritative: false);
        Check(!model.IsEditable(7) && !model.SetSelection(7, 12), "non-authoritative snapshot is read-only");
        Check(model.ApplyState == GameBindingApplyState.NotAuthoritative && model.BuildRequest(NewId) == null,
            "non-authoritative snapshot cannot apply");
    }

    private static void ResultMessages()
    {
        string success = GameBindingResultMessages.Describe(new GameBindingWriteResult
        {
            Status = "success", ActionIndex = 7, BeforeRaw = 11, AfterRaw = 12, RequestedRaw = 12
        }, japanese: true);
        Check(success.Contains("コマンドメニュー") && success.Contains("Y") && success.Contains("X"), "success message");

        string duplicate = GameBindingResultMessages.Describe(new GameBindingWriteResult
        {
            Status = "rejected", ErrorCode = "DUPLICATE", ActionIndex = 7, RequestedRaw = 12, ConflictActionIndex = 23
        }, japanese: true);
        Check(duplicate.Contains("X") && duplicate.Contains("メニュー"), "duplicate message names button and conflict");

        foreach (string code in new[]
                 {
                     "NOT_READY", "STALE_CURRENT_VALUE", "UNSUPPORTED_ACTION", "UNSUPPORTED_BUTTON",
                     "PERSIST_FAILED", "READBACK_FAILED"
                 })
        {
            string text = GameBindingResultMessages.Describe(
                new GameBindingWriteResult { Status = "rejected", ErrorCode = code, ActionIndex = 7 }, japanese: false);
            Check(text.Length > 0 && !text.StartsWith(code, StringComparison.Ordinal), $"{code} has a user message");
        }
        string rollbackFailed = GameBindingResultMessages.Describe(new GameBindingWriteResult
        {
            Status = "rollback_failed", ErrorCode = "READBACK_FAILED", ActionIndex = 7
        }, japanese: false);
        Check(rollbackFailed.Contains("could not be undone"), "rollback failure message");
    }

    private static string NewId() => Guid.NewGuid().ToString("N");

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
