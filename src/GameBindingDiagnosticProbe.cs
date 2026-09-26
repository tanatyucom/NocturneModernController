using System;
using System.Diagnostics;
using Il2Cpp;
using MelonLoader;

namespace NocturneModernController
{
    // TEMPORARY read-only diagnostic (native GAME Key Config X->Y investigation,
    // investigations/NATIVE_GAMEPAD_CONFIG_DATAFLOW_20260922.md). Only calls
    // dds3ConfigGamePadSteam.GetConfigGamePad(7) (read-only; ChangeKey and other
    // write APIs are never called, no Harmony patches, no new native hooks).
    //
    // Deliberately independent from ModernControllerApi's authoritative-snapshot
    // retry logic (RetryGameActionBindingsIfNeeded / _gameActionBindingsReady):
    // this probe never reads or sets that flag and keeps polling for the whole
    // mod lifetime, including after the authoritative snapshot has already been
    // written. It never writes bindings.json/actions.json/features.json itself.
    //
    // There is no existing reliable managed-side signal for "still on the Title
    // screen" or "native Controller Key Config screen is open" (see
    // investigations/NATIVE_GAMEPAD_CONFIG_DATAFLOW_20260922.md §17-18 for why
    // those would require a new native hook to detect safely). The only signal
    // with real-hardware confirmation is FieldDashPatch.IsExplorationActive
    // (already Harmony-patched elsewhere for unrelated features; reused here,
    // not re-hooked). Phases this probe cannot honestly distinguish are reported
    // as UNKNOWN rather than guessed as TITLE/CONFIG; the elapsed-time field lets
    // the tester correlate log lines against the wall-clock time of each manual
    // test step (see NATIVE_GAMEPAD_CONFIG_DATAFLOW_20260922.md §18.6).
    internal static class GameBindingDiagnosticProbe
    {
        private const string Prefix = "[NMC-GAMECFG-DIAG]";
        private const int Index = 7;
        private const int SampleIntervalFrames = 60;

        private static readonly Stopwatch Clock = Stopwatch.StartNew();

        private static int _frameCounter;
        private static bool _hasSampledOnce;
        private static bool _hasEverExplored;
        private static bool _lastReadFailed;
        private static int _lastRaw = int.MinValue;
        private static string? _lastPhase;
        private static bool _lastExplorationActive;

        internal static void Sample()
        {
            bool explorationActive = FieldDashPatch.IsExplorationActive;

            // Checked every tick (not throttled) so the value right at the FIELD
            // boundary is never missed, per the diagnostic spec's request to
            // capture state transitions even between regular samples. This is a
            // single extra read only at the instant the flag flips, not a
            // per-frame poll.
            if (_hasSampledOnce && explorationActive != _lastExplorationActive)
            {
                LogTransition(explorationActive);
            }
            _lastExplorationActive = explorationActive;
            if (explorationActive)
            {
                _hasEverExplored = true;
            }

            _frameCounter++;
            if (_frameCounter % SampleIntervalFrames != 0)
            {
                return;
            }

            LogRegularSample(explorationActive);
        }

        private static void LogRegularSample(bool explorationActive)
        {
            int raw;
            try
            {
                raw = dds3ConfigGamePadSteam.GetConfigGamePad(Index);
            }
            catch (Exception ex)
            {
                if (!_lastReadFailed)
                {
                    MelonLogger.Msg(
                        $"{Prefix} phase=UNKNOWN index={Index} ERROR {ex.GetType().Name}: {ex.Message} " +
                        $"elapsedMs={ElapsedMs()} exploration={explorationActive}");
                    _lastReadFailed = true;
                }
                return;
            }

            _lastReadFailed = false;

            string phase = CurrentPhase(explorationActive);

            if (!_hasSampledOnce)
            {
                MelonLogger.Msg(
                    $"{Prefix} phase={phase} index={Index} raw={raw} button={DescribeButton(raw)} " +
                    $"elapsedMs={ElapsedMs()} exploration={explorationActive}");
                _lastRaw = raw;
                _lastPhase = phase;
                _hasSampledOnce = true;
                return;
            }

            if (raw != _lastRaw)
            {
                // Value change is the more specific event; report it even if the
                // phase also changed in the same sample.
                MelonLogger.Msg(
                    $"{Prefix} phase=VALUE_CHANGED index={Index} before={_lastRaw} after={raw} " +
                    $"button={DescribeButton(raw)} elapsedMs={ElapsedMs()} exploration={explorationActive}");
                _lastRaw = raw;
                _lastPhase = phase;
                return;
            }

            if (phase != _lastPhase)
            {
                MelonLogger.Msg(
                    $"{Prefix} phase={phase} index={Index} raw={raw} button={DescribeButton(raw)} " +
                    $"elapsedMs={ElapsedMs()} exploration={explorationActive}");
                _lastPhase = phase;
                return;
            }

            // Neither the value nor the phase changed since the last sample --
            // suppress the duplicate line.
        }

        private static void LogTransition(bool enteringField)
        {
            try
            {
                int raw = dds3ConfigGamePadSteam.GetConfigGamePad(Index);
                string phase = enteringField ? "FIELD_ENTER" : "FIELD_EXIT";
                MelonLogger.Msg(
                    $"{Prefix} phase={phase} index={Index} raw={raw} button={DescribeButton(raw)} " +
                    $"elapsedMs={ElapsedMs()} exploration={enteringField}");
                _lastRaw = raw;
            }
            catch (Exception)
            {
                // Native side may be transiently unavailable right at this edge;
                // the next regular sample will pick the value back up. Do not
                // log here to avoid duplicating the rate-limited error above.
            }
        }

        private static long ElapsedMs() => Clock.ElapsedMilliseconds;

        // Phase naming: FIELD while exploring. Before exploration has ever
        // become active even once, everything is reported as STARTUP (this
        // covers early init and whatever the Title screen looks like -- both
        // honestly indistinguishable from here). After exploration has been
        // active at least once, any later not-exploring sample is reported as
        // UNKNOWN (covers battle, menus, and the native Config screen -- none
        // of which have a confirmed managed-side signal, so none are guessed).
        private static string CurrentPhase(bool explorationActive)
        {
            if (explorationActive)
            {
                return "FIELD";
            }
            return _hasEverExplored ? "UNKNOWN" : "STARTUP";
        }

        // Only the two raw values independently CONFIRMED by real-hardware A/B/A
        // testing (SESSION_RESUME_NOTES.md §17, GAMEBINDING_INDEX_MAP) are named.
        // Any other raw value is reported as-is rather than guessed.
        private static string DescribeButton(int raw)
        {
            switch (raw)
            {
                case 11: return "Y";
                case 12: return "X";
                default: return "?";
            }
        }
    }
}
