using System;
using System.Diagnostics;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using MelonLoader;

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

            if (!File.Exists(BrokerReadyMarkerPath))
            {
                logger.Warning(
                    "[NocturneModernController] Independent broker not detected - controller input will not be available this session. " +
                    "Start the game via NocturneModernController.Launcher.exe (instead of Steam's own Play button) to enable it.");
                return;
            }

            if (!IsBrokerAlive(logger))
            {
                logger.Warning(
                    "[NocturneModernController] Broker marker file exists but the broker process is not alive (stale marker from a " +
                    "crashed or terminated broker) - external right-stick input will not be available this session; other controller " +
                    "features are unaffected. Restart the broker (e.g. via NocturneModernController.Launcher.exe) to enable it.");
                return;
            }

            RequestBrokerLaunch(path, logger);
        }

        // Confirms the Broker process behind BrokerReadyMarkerPath is actually
        // alive by opening its existing single-instance Mutex, rather than
        // trusting the marker file's mere presence (see BrokerMutexName above
        // for why). The handle is never used to gate anything else and is
        // always closed here - ownership of the Mutex itself always stays with
        // the Broker.
        private static bool IsBrokerAlive(MelonLogger.Instance logger)
        {
            Mutex? mutex = null;
            try
            {
                return Mutex.TryOpenExisting(BrokerMutexName, out mutex);
            }
            catch (Exception ex)
            {
                logger.Warning(
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
        private static void RequestBrokerLaunch(string path, MelonLogger.Instance logger)
        {
            try
            {
                var payload = new { helperPath = path };
                File.WriteAllText(BrokerLaunchRequestPath, JsonSerializer.Serialize(payload));
                logger.Msg("[NocturneModernController] Requested broker to launch the SDL input helper.");
            }
            catch (Exception ex)
            {
                logger.Warning("[NocturneModernController] Failed to write broker launch request: " + ex.Message);
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
