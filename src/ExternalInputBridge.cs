using System;
using System.Diagnostics;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using MelonLoader;
using NocturneModernController.TaskSchedulerBroker;

namespace NocturneModernController
{
    internal static class ExternalInputBridge
    {
        private const string MapName = "NocturneModernController_SDL_v2";
        private const int Magic = 0x4E4D4332;
        private const int StopRequested = 0x53544F50;

        // Production architecture: the Helper is never launched by this MOD
        // DLL directly, and never via explorer.exe. It is launched by an
        // independent broker process (NocturneModernController.Broker.exe),
        // itself started by NocturneModernController.Launcher.exe before
        // Steam launches the game. This keeps Helper's process ancestry
        // entirely outside smt3hd.exe / Steam's own process tree, which
        // real-machine testing confirmed is required for the physical
        // controller (in particular the right stick) to be visible to
        // Helper at all - see docs/research for the investigation history.
        //
        // This class only ever writes a one-shot request file for the
        // broker to pick up; it never spawns Helper itself and never blocks
        // waiting for the broker to act, so the game's own startup is never
        // delayed by this.
        private static readonly string BrokerReadyMarkerPath = Path.Combine(
            Path.GetTempPath(),
            "NocturneModernController.Broker.ready.json");

        private static readonly string BrokerLaunchRequestPath = Path.Combine(
            Path.GetTempPath(),
            "NocturneModernController.Broker.LaunchRequest.json");

        // Broker liveness check (docs/research/ROOT-23_HANDOFF_TEMP.md sections
        // 12/14/15, Root-24/Root-25): BrokerReadyMarkerPath alone cannot be
        // trusted because the Broker never deletes it on crash or forced
        // termination. Opening the Broker's own single-instance Mutex is an
        // OS-guaranteed, immediate, Broker-side-change-free way to confirm the
        // process behind the marker is actually still alive.
        private const string BrokerMutexName = "Local\\NocturneModernController.Broker.SingleInstance";

        private static MemoryMappedFile? _map;
        private static MemoryMappedViewAccessor? _view;
        private static int _lastSequence;
        private static int _lastX;
        private static int _lastY;
        private static bool _loggedConnected;

        // Root-26 Phase P2/P4/P4.1: Task Scheduler (registered/removed via
        // the Settings UI's "Enable automatic right-stick support"
        // checkbox) is the PRIMARY on-demand entry point for starting the
        // Broker. NocturneModernController.Launcher.exe remains the
        // FALLBACK route (started manually by the user instead of Steam's
        // own Play button). The Windows Startup-folder route that used to
        // exist here has been removed entirely. If the Broker is already
        // ready via the Launcher route, this class never touches Task
        // Scheduler at all - everything below RequestBrokerLaunch
        // (launch-request, MMF, Stop, UpdateGameContext) is unchanged.
        //
        // Phase P4.1: real-machine testing found Windows Task Scheduler's
        // own process-spawn latency can occasionally exceed 10 seconds
        // (observed once: ~12.8s from Run() to the Broker's Main() actually
        // starting). Start() used to block OnInitializeMelon() with a
        // synchronous Thread.Sleep wait loop for up to 10s waiting for the
        // Broker to become ready - which both stalls the game's own startup
        // for that entire window AND, when Task Scheduler is slower than
        // that, gives up right before the Broker actually comes up (wasting
        // it - it then sits idle with no request and self-exits via its own
        // idle timeout). Start() now NEVER blocks: it requests Task.Run()
        // (if needed) and returns immediately, leaving a small waiting
        // state that Tick() (called every frame from ModMain.OnUpdate(),
        // the same way every other Root-26 probe's Sample() is) polls at a
        // throttled interval. This removes the game-startup stall entirely
        // and naturally supports the Broker becoming ready arbitrarily late
        // (the async timeout below is generous specifically because it no
        // longer costs any startup delay to wait longer).
        //
        // This class NEVER calls BrokerTaskService.Register() or
        // Unregister() - only Validate(), GetState(), and Run(). Task
        // registration/removal remains the Settings UI's exclusive
        // responsibility. A missing, foreign, or invalid Task is treated
        // exactly like "no broker available": a warning and a graceful
        // skip, never a fix-it-automatically action and never something
        // that can block or delay the game's own startup.
        //
        // Poll interval: reuses the same 200ms value the old synchronous
        // wait loop already used (Thread.Sleep(200)) - not a new arbitrary
        // number. Async timeout: 60s - long enough to comfortably absorb
        // the observed ~13s Task Scheduler dispatch delay (and a good
        // multiple of it) with margin, which costs nothing in game-startup
        // time now that waiting no longer blocks anything.
        private const int AsyncBrokerReadyTimeoutSeconds = 60;
        private const int PollIntervalMs = 200;

        // Only meaningful while _waitingForBrokerReady is true. Tick() is a
        // cheap no-op (single bool check) whenever it is false, so normal
        // per-frame cost is unaffected once the wait ends (success or
        // timeout) - see Tick() below.
        private static bool _waitingForBrokerReady;
        private static string? _pendingHelperPath;
        private static DateTime _waitStartUtc;
        private static DateTime _lastPollUtc;

        internal static void Start(MelonLogger.Instance logger)
        {
            string directory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? string.Empty;
            string path = Path.Combine(
                directory,
                "NocturneModernController.Helper",
                "NocturneModernController.InputHelper.exe");
            if (!File.Exists(path))
            {
                logger.Warning("[NocturneModernController] SDL input helper is missing: " + path);
                return;
            }

            if (IsBrokerReady())
            {
                logger.Msg("[NocturneModernController] Broker already ready.");
                RequestBrokerLaunch(path);
                return;
            }

            string brokerPath = Path.Combine(directory, "NocturneModernController.Helper", "NocturneModernController.Broker.exe");
            BeginWaitingForBrokerViaTaskScheduler(brokerPath, path, logger);
        }

        // Validates the Task Scheduler helper task and, if valid, requests
        // Task.Run() (skipping the request if an instance is already
        // Running, to avoid a redundant duplicate request), then arms the
        // async waiting state and RETURNS IMMEDIATELY - no wait loop here.
        // Never registers, updates, or unregisters the Task.
        private static void BeginWaitingForBrokerViaTaskScheduler(string brokerPath, string helperPath, MelonLogger.Instance logger)
        {
            BrokerTaskValidation validation;
            try
            {
                validation = new BrokerTaskService().Validate(brokerPath);
            }
            catch (Exception ex)
            {
                logger.Warning("[NocturneModernController] Task Scheduler check failed (" + ex.GetType().FullName + ": " + ex.Message + ") - skipping.");
                return;
            }

            if (!validation.Exists)
            {
                logger.Msg("[NocturneModernController] Task Scheduler helper task is not registered - skipping (enable it in Settings to use this route).");
                return;
            }
            if (!validation.IsOurs)
            {
                logger.Warning("[NocturneModernController] A task named '" + BrokerTaskService.TaskName + "' exists but was not created by this MOD - leaving it untouched and skipping.");
                return;
            }
            if (!validation.SettingsValid)
            {
                logger.Warning("[NocturneModernController] Task Scheduler helper task exists but its settings are unexpected - leaving it untouched and skipping (re-enable it in Settings to fix).");
                return;
            }

            try
            {
                var service = new BrokerTaskService();
                int state = service.GetState();
                if (state == BrokerTaskService.TaskStateRunning)
                {
                    logger.Msg("[NocturneModernController] Task Scheduler helper task is already running.");
                }
                else
                {
                    logger.Msg("[NocturneModernController] Task Scheduler run requested.");
                    service.Run();
                }
            }
            catch (Exception ex)
            {
                logger.Warning("[NocturneModernController] Task Scheduler run failed (" + ex.GetType().FullName + ": " + ex.Message + ") - skipping.");
                return;
            }

            logger.Msg(
                "[NocturneModernController] Waiting asynchronously for broker (checked once per frame, at most every " +
                PollIntervalMs + "ms, for up to " + AsyncBrokerReadyTimeoutSeconds + "s - the game itself is not blocked).");
            _pendingHelperPath = helperPath;
            _waitStartUtc = DateTime.UtcNow;
            _lastPollUtc = DateTime.MinValue; // forces the first Tick() call to poll immediately
            _waitingForBrokerReady = true;
        }

        // Called once per frame from ModMain.OnUpdate(), the same way every
        // other Root-26 probe's Sample() is - no new thread, no Task.Run,
        // no timer. A cheap no-op whenever nothing is being waited for.
        internal static void Tick()
        {
            if (!_waitingForBrokerReady)
            {
                return;
            }

            DateTime now = DateTime.UtcNow;
            if ((now - _lastPollUtc).TotalMilliseconds < PollIntervalMs)
            {
                return;
            }
            _lastPollUtc = now;

            if (IsBrokerReady())
            {
                _waitingForBrokerReady = false;
                MelonLogger.Msg("[NocturneModernController] Broker became ready via Task Scheduler.");
                RequestBrokerLaunch(_pendingHelperPath!);
                _pendingHelperPath = null;
                return;
            }

            if ((now - _waitStartUtc).TotalSeconds >= AsyncBrokerReadyTimeoutSeconds)
            {
                _waitingForBrokerReady = false;
                _pendingHelperPath = null;
                MelonLogger.Warning(
                    "[NocturneModernController] Broker did not become ready within " + AsyncBrokerReadyTimeoutSeconds +
                    "s of the Task Scheduler run request - skipping this session. Controller input via other MOD features is unaffected.");
            }
        }

        // Combines the two pre-existing checks (ready marker + Mutex
        // liveness) that were already both required before Phase P2 - same
        // logic, just factored out so it can be reused for the up-front
        // check in Start() and for the async poll in Tick().
        private static bool IsBrokerReady()
        {
            return File.Exists(BrokerReadyMarkerPath) && IsBrokerAlive();
        }

        // Confirms the Broker process behind BrokerReadyMarkerPath is actually
        // alive by opening its existing single-instance Mutex, rather than
        // trusting the marker file's mere presence (see BrokerMutexName above
        // for why). The handle is never used to gate anything else and is
        // always closed here - ownership of the Mutex itself always stays with
        // the Broker.
        private static bool IsBrokerAlive()
        {
            Mutex? mutex = null;
            try
            {
                return Mutex.TryOpenExisting(BrokerMutexName, out mutex);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning(
                    "[NocturneModernController] Broker liveness check failed (" +
                    ex.GetType().FullName + ": " + ex.Message + "); treating broker as not alive.");
                return false;
            }
            finally
            {
                mutex?.Dispose();
            }
        }

        // Writes a one-shot request that NocturneModernController.Broker.exe
        // (already running, confirmed via BrokerReadyMarkerPath above) is
        // polling for. The broker - not this MOD DLL, not smt3hd.exe - is
        // what actually calls Process.Start on Helper.exe.
        private static void RequestBrokerLaunch(string path)
        {
            try
            {
                var payload = new { helperPath = path };
                File.WriteAllText(BrokerLaunchRequestPath, JsonSerializer.Serialize(payload));
                MelonLogger.Msg("[NocturneModernController] Requested broker to launch the SDL input helper.");
            }
            catch (Exception ex)
            {
                MelonLogger.Warning("[NocturneModernController] Failed to write broker launch request: " + ex.Message);
            }
        }

        internal static bool TryRead(out int x, out int y)
        {
            x = y = 0;
            try
            {
                _map ??= MemoryMappedFile.OpenExisting(MapName);
                _view ??= _map.CreateViewAccessor();
                if (_view.ReadInt32(0) != Magic || _view.ReadInt32(4) == 0)
                {
                    return false;
                }

                int sequence = _view.ReadInt32(16);
                int age = unchecked(Environment.TickCount - _view.ReadInt32(20));
                if (age < 0 || age > 1000)
                {
                    return false;
                }

                if (sequence != _lastSequence)
                {
                    _lastSequence = sequence;
                    _lastX = _view.ReadInt32(8);
                    _lastY = _view.ReadInt32(12);
                }
                x = _lastX;
                y = _lastY;
                if (!_loggedConnected)
                {
                    _loggedConnected = true;
                    MelonLogger.Msg("[NocturneModernController] External SDL gamepad input connected.");
                }
                return true;
            }
            catch (FileNotFoundException)
            {
                return false;
            }
        }

        internal static void UpdateGameContext(bool explorationActive)
        {
            try
            {
                _map ??= MemoryMappedFile.OpenExisting(MapName);
                _view ??= _map.CreateViewAccessor();
                _view.Write(28, explorationActive ? 1 : 0);
                _view.Write(32, Process.GetCurrentProcess().Id);
            }
            catch (FileNotFoundException)
            {
            }
        }

        internal static void Stop()
        {
            // Phase P4.1: if the game is closing while still waiting for a
            // Task-Scheduler-triggered broker to become ready, cancel that
            // wait now - never let a launch-request be sent for a session
            // that has already ended, and never leave Tick() polling after
            // OnDeinitializeMelon.
            if (_waitingForBrokerReady)
            {
                _waitingForBrokerReady = false;
                _pendingHelperPath = null;
                MelonLogger.Msg("[NocturneModernController] Waiting for broker cancelled (game is shutting down).");
            }

            try
            {
                _map ??= MemoryMappedFile.OpenExisting(MapName);
                _view ??= _map.CreateViewAccessor();
                _view.Write(24, StopRequested);
                _view.Write(28, 0);
                _view.Flush();
            }
            catch (FileNotFoundException)
            {
            }
            _view?.Dispose();
            _map?.Dispose();
            _view = null;
            _map = null;
        }
    }
}
