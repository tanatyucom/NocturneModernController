using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace NocturneModernController
{
    internal enum GameBindingApplyState
    {
        Ready,
        NotAuthoritative,
        NoChanges,
        MultipleChanges,
        UnknownCurrent
    }

    // Settings-side edit state for the GAME Bindings tab. Pure: it never writes
    // files or touches the game; the form turns BuildRequest() into the request
    // file (shared/GameBindingWrite.cs protocol) and Core does the native write.
    internal sealed class GameBindingEditModel
    {
        private readonly Dictionary<int, GameActionBindingDisplayRow> _rows;
        private readonly Dictionary<int, int> _pending = new();

        internal GameBindingEditModel(IEnumerable<GameActionBindingRawEntry> entries, bool authoritative)
        {
            Authoritative = authoritative;
            Rows = GameActionBindingDisplayFormatter.GetConfirmedRows(entries);
            _rows = Rows.ToDictionary(row => row.Index);
        }

        internal bool Authoritative { get; }
        internal IReadOnlyList<GameActionBindingDisplayRow> Rows { get; }

        internal static IReadOnlyList<(int Raw, string Name)> Options => GameBindingSupportedButtons.All;

        internal bool IsEditable(int index) =>
            Authoritative && _rows.TryGetValue(index, out GameActionBindingDisplayRow? row) && row.CurrentSupported;

        // Selecting the current value clears the pending change for that row.
        internal bool SetSelection(int index, int raw)
        {
            if (!IsEditable(index) || !GameBindingSupportedButtons.IsSupported(raw))
            {
                return false;
            }
            if (raw == _rows[index].CurrentRaw)
            {
                _pending.Remove(index);
            }
            else
            {
                _pending[index] = raw;
            }
            return true;
        }

        internal int GetSelection(int index) =>
            _pending.TryGetValue(index, out int raw) ? raw : _rows[index].CurrentRaw;

        internal int PendingCount => _pending.Count;

        internal void Cancel() => _pending.Clear();

        internal GameBindingApplyState ApplyState
        {
            get
            {
                if (!Authoritative)
                {
                    return GameBindingApplyState.NotAuthoritative;
                }
                if (_pending.Count == 0)
                {
                    return GameBindingApplyState.NoChanges;
                }
                if (_pending.Count > 1)
                {
                    return GameBindingApplyState.MultipleChanges;
                }
                return _rows[_pending.Keys.Single()].CurrentSupported
                    ? GameBindingApplyState.Ready
                    : GameBindingApplyState.UnknownCurrent;
            }
        }

        // v1: exactly one action per request. ExpectedCurrentRaw is the value
        // from the snapshot this Settings session opened with.
        internal GameBindingWriteRequest? BuildRequest(Func<string> newRequestId)
        {
            if (ApplyState != GameBindingApplyState.Ready)
            {
                return null;
            }
            KeyValuePair<int, int> change = _pending.Single();
            return new GameBindingWriteRequest
            {
                SchemaVersion = GameBindingWriteProtocol.SchemaVersion,
                RequestId = newRequestId(),
                Operation = GameBindingWriteProtocol.RebindOperation,
                ActionIndex = change.Key,
                ExpectedCurrentRaw = _rows[change.Key].CurrentRaw,
                NewRaw = change.Value
            };
        }
    }

    // One-shot hand-off of Core's result file to the next Settings session:
    // read it, delete it, and never show the same result twice.
    internal static class GameBindingResultFile
    {
        internal static GameBindingWriteResult? Consume(string path)
        {
            GameBindingWriteResult? result = null;
            try
            {
                if (File.Exists(path))
                {
                    result = JsonSerializer.Deserialize<GameBindingWriteResult>(File.ReadAllText(path));
                }
            }
            catch (JsonException)
            {
            }
            catch (IOException)
            {
            }
            try
            {
                File.Delete(path);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
            return result;
        }
    }

    // errorCode -> user-facing text. Kept apart from the protocol constants so
    // wording can change without touching the machine-readable codes.
    internal static class GameBindingResultMessages
    {
        internal static string Describe(GameBindingWriteResult result, bool japanese)
        {
            string action = result.ActionIndex >= 0 ? ActionLabel(result.ActionIndex) : string.Empty;
            switch (result.Status)
            {
                case GameBindingWriteStatus.Success:
                    return japanese
                        ? $"{action} を {Button(result.BeforeRaw)} → {Button(result.AfterRaw)} に変更しました。"
                        : $"{action} changed from {Button(result.BeforeRaw)} to {Button(result.AfterRaw)}.";
                case GameBindingWriteStatus.Validated:
                    return japanese ? "確認のみ行いました（変更していません）。" : "Validated only; nothing was changed.";
                case GameBindingWriteStatus.RollbackSuccess:
                    return japanese
                        ? $"{action} の変更に失敗したため、元の設定に戻しました。"
                        : $"Changing {action} failed and the previous binding was restored.";
                case GameBindingWriteStatus.RollbackFailed:
                    return japanese
                        ? $"{action} の変更に失敗し、元に戻せませんでした。ゲームを再起動し、純正のキーコンフィグで確認してください。"
                        : $"Changing {action} failed and could not be undone. Restart the game and check the in-game key config.";
            }

            return result.ErrorCode switch
            {
                GameBindingWriteError.Duplicate => japanese
                    ? $"{Button(result.RequestedRaw)} は {ConflictLabel(result)} に使われているため変更できませんでした。"
                    : $"{Button(result.RequestedRaw)} is already used by {ConflictLabel(result)}; nothing was changed.",
                GameBindingWriteError.NotReady => japanese
                    ? "ゲーム内でフィールドへ移動してから、もう一度変更してください。"
                    : "Move to the field in game, then try again.",
                GameBindingWriteError.StaleCurrentValue => japanese
                    ? "ゲーム側の設定が変更されています。Settingsを開き直してから変更してください。"
                    : "The in-game binding changed meanwhile. Reopen Settings and try again.",
                GameBindingWriteError.UnsupportedAction => japanese
                    ? "この項目は変更に対応していません。"
                    : "This action cannot be changed.",
                GameBindingWriteError.UnsupportedButton => japanese
                    ? "このボタンは割り当てに対応していません。"
                    : "This button is not supported.",
                GameBindingWriteError.WritesDisabled => japanese
                    ? "このバージョンではGAMEキーコンフィグの変更は無効です（変更していません）。"
                    : "GAME binding changes are disabled in this version; nothing was changed.",
                GameBindingWriteError.SessionLocked => japanese
                    ? "前回の変更で問題が起きたため、ゲームを再起動するまで変更できません。"
                    : "A previous change failed; restart the game before changing bindings again.",
                GameBindingWriteError.InconsistentState => japanese
                    ? "ゲーム側の設定が保存前の状態です。純正のキーコンフィグを閉じてから、もう一度試してください。"
                    : "The in-game key config has unsaved changes. Close it and try again.",
                GameBindingWriteError.UnsupportedGameBuild => japanese
                    ? "このゲームのバージョンには対応していません。"
                    : "This game version is not supported.",
                GameBindingWriteError.NoChange => japanese
                    ? "現在と同じボタンのため、変更はありません。"
                    : "The button is already assigned; nothing was changed.",
                _ => japanese
                    ? "変更要求を処理できなかったため、変更していません。"
                    : "The change request could not be processed; nothing was changed."
            };
        }

        private static string ActionLabel(int index) =>
            GameActionBindingSnapshotReader.GetConfirmedActionName(index) ?? $"#{index}";

        private static string ConflictLabel(GameBindingWriteResult result) =>
            result.ConflictActionIndex is int conflict ? ActionLabel(conflict) : "?";

        private static string Button(int? raw) => raw is int value ? GameBindingSupportedButtons.Describe(value) : "?";
    }
}
