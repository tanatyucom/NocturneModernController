using System;
using System.IO;
using MelonLoader;

namespace NocturneModernController
{
    // Core side of the Settings -> Core GAME binding request protocol
    // (shared/GameBindingWrite.cs). Same convention as feature-requests.json:
    // Settings writes the request atomically, Core handles it once after the
    // Settings process exits, deletes the request and writes the result
    // atomically for Settings to read next time it opens.
    internal static class GameBindingRequestProcessor
    {
        private const string Prefix = "[NMC-GAMEBIND]";

        // The game window is minimized while Settings is open, so field
        // exploration is not active yet on the frame the Settings exit is seen
        // (real-hardware finding: processing on that frame always gave NOT_READY).
        // Wait for exploration to resume, but not indefinitely: a request must
        // never apply at some unrelated later moment.
        private const int ReadyWaitMilliseconds = 5000;

        private static readonly GameBindingWriteService Service =
            new(new NativeGameBindingPort(), writesEnabled: true);

        private static bool _pending;
        private static int _pendingSinceTick;

        internal static void OnSettingsClosed()
        {
            if (File.Exists(ModernControllerApi.GameBindingRequestPath))
            {
                _pending = true;
                _pendingSinceTick = Environment.TickCount;
            }
        }

        internal static void Sample()
        {
            if (!_pending)
            {
                return;
            }
            if (!ExplorationState.IsExplorationActive &&
                unchecked(Environment.TickCount - _pendingSinceTick) < ReadyWaitMilliseconds)
            {
                return;
            }
            _pending = false;
            ProcessPending();
        }

        private static void ProcessPending()
        {
            string requestPath = ModernControllerApi.GameBindingRequestPath;
            if (!File.Exists(requestPath))
            {
                return;
            }

            GameBindingWriteResult result;
            try
            {
                string json = File.ReadAllText(requestPath);
                // Delete first so a crash mid-transaction can never replay it.
                File.Delete(requestPath);
                result = Service.HandleJson(json);
            }
            catch (Exception ex)
            {
                result = new GameBindingWriteResult
                {
                    Status = GameBindingWriteStatus.Rejected,
                    ErrorCode = GameBindingWriteError.InternalError,
                    Message = $"request file could not be processed ({ex.GetType().Name})"
                };
            }

            Log(result);

            if (result.Status == GameBindingWriteStatus.Success ||
                result.Status == GameBindingWriteStatus.RollbackSuccess ||
                result.Status == GameBindingWriteStatus.RollbackFailed)
            {
                // Native state may have changed: publish it for the next Settings session.
                ModernControllerApi.RefreshAuthoritativeGameActionBindings();
            }

            try
            {
                AtomicJsonFile.WriteJsonAtomic(
                    ModernControllerApi.GameBindingResultPath,
                    GameBindingWriteProtocol.Serialize(result));
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"{Prefix} result file could not be written ({ex.GetType().Name}: {ex.Message})");
            }
        }

        private static void Log(GameBindingWriteResult result)
        {
            string line = $"{Prefix} request={result.RequestId} status={result.Status} " +
                $"action={result.ActionIndex} before={result.BeforeRaw} after={result.AfterRaw} dryRun={result.DryRun}";
            if (result.ErrorCode == null)
            {
                MelonLogger.Msg(line);
            }
            else
            {
                MelonLogger.Warning($"{line} error={result.ErrorCode} ({result.Message})");
            }
        }
    }
}
