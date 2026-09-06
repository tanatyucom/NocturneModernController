using System;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace NocturneModernController.Launcher
{
    // Production launcher for NocturneModernController. Starts the
    // independent broker (NocturneModernController.Broker.exe, its own
    // child - never a child of smt3hd.exe and never launched via
    // explorer.exe), confirms it is ready, then asks Steam (via its own
    // steam:// URI handler - Steam's normal launch path, never bypassed)
    // to start SMT3HD. Waits for the game to exit, tells the broker to
    // shut down cleanly, then exits itself.
    //
    // No UI, no settings, no installer, no persistence (no autostart
    // registration of any kind). Run this instead of Steam's own Play
    // button to enable full controller input; using Steam's Play button
    // directly still works, just without this MOD's controller feature
    // (see ExternalInputBridge.Start's broker-ready check).
    internal static class Program
    {
        private const string SteamAppId = "1413480";

        private static readonly string LogPath = Path.Combine(
            Path.GetTempPath(),
            "NocturneModernController.Launcher.log");

        private static readonly string BrokerReadyMarkerPath = Path.Combine(
            Path.GetTempPath(),
            "NocturneModernController.Broker.ready.json");

        private static readonly string BrokerShutdownRequestPath = Path.Combine(
            Path.GetTempPath(),
            "NocturneModernController.Broker.ShutdownRequest.json");

        private static Mutex? _singleInstanceMutex;

        private static int Main()
        {
            File.WriteAllText(
                LogPath,
                $"LAUNCHER START pid={Environment.ProcessId} {DateTimeOffset.Now:O}{Environment.NewLine}");

            bool createdNew;
            _singleInstanceMutex = new Mutex(true, "Local\\NocturneModernController.Launcher.SingleInstance", out createdNew);
            if (!createdNew)
            {
                Log("SINGLE_INSTANCE_CHECK_FAILED: another Launcher instance is already running. Exiting.");
                Console.WriteLine("NocturneModernController Launcher is already running.");
                return 1;
            }

            string exeDir = AppContext.BaseDirectory;
            string brokerPath = Path.Combine(exeDir, "Mods", "NocturneModernController.Helper", "NocturneModernController.Broker.exe");
            if (!File.Exists(brokerPath))
            {
                Log("BROKER_NOT_FOUND at " + brokerPath);
                Console.WriteLine("NocturneModernController.Broker.exe not found. Expected at: " + brokerPath);
                return 2;
            }

            Process? brokerProcess;
            try
            {
                if (File.Exists(BrokerReadyMarkerPath))
                {
                    File.Delete(BrokerReadyMarkerPath);
                }
                var brokerStart = new ProcessStartInfo(brokerPath)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                };
                brokerProcess = Process.Start(brokerStart);
                if (brokerProcess == null)
                {
                    Log("BROKER_LAUNCH_RETURNED_NULL");
                    return 3;
                }
                Log($"BROKER_LAUNCHED pid={brokerProcess.Id}");
            }
            catch (Exception ex)
            {
                Log("BROKER_LAUNCH_FAILED " + ex.Message);
                return 3;
            }

            if (!WaitForFile(BrokerReadyMarkerPath, TimeSpan.FromSeconds(10)))
            {
                Log("BROKER_READY_TIMEOUT");
                Console.WriteLine("Broker did not report ready in time.");
                return 4;
            }
            Console.WriteLine("Broker is ready.");

            try
            {
                var steamStart = new ProcessStartInfo("steam://run/" + SteamAppId)
                {
                    UseShellExecute = true
                };
                Process.Start(steamStart)?.Dispose();
                Log("STEAM_RUN_REQUESTED appid=" + SteamAppId);
                Console.WriteLine("Requested Steam to launch SMT3HD.");
            }
            catch (Exception ex)
            {
                Log("STEAM_RUN_REQUEST_FAILED " + ex.Message);
                Console.WriteLine("Failed to invoke steam:// URI: " + ex.Message);
                return 5;
            }

            Console.WriteLine("Waiting for SMT3HD to start...");
            int? gamePid = WaitForProcessStart("smt3hd", TimeSpan.FromSeconds(90));
            if (gamePid == null)
            {
                Log("GAME_START_TIMEOUT");
                Console.WriteLine("SMT3HD did not appear within the timeout.");
                RequestBrokerShutdown(brokerProcess);
                return 6;
            }
            Log("GAME_STARTED pid=" + gamePid.Value);
            Console.WriteLine("SMT3HD started. Play normally; this window will wait for you to close the game.");

            WaitForProcessExit("smt3hd");
            Log("GAME_EXITED");
            Console.WriteLine("SMT3HD has exited.");

            // Grace period: Helper detects the game's exit itself (via the
            // existing MMF-communicated gamePid) and terminates on its own;
            // give it a moment before asking the broker to shut down too.
            Thread.Sleep(3000);

            RequestBrokerShutdown(brokerProcess);

            Log("LAUNCHER_DONE");
            Console.WriteLine("Done.");
            _singleInstanceMutex.ReleaseMutex();
            return 0;
        }

        private static void RequestBrokerShutdown(Process brokerProcess)
        {
            try
            {
                File.WriteAllText(BrokerShutdownRequestPath, "{}");
                Log("BROKER_SHUTDOWN_REQUESTED");
            }
            catch (Exception ex)
            {
                Log("BROKER_SHUTDOWN_REQUEST_WRITE_FAILED " + ex.Message);
            }

            // Clean exit only - the broker is never forcibly killed from here.
            try
            {
                bool exited = brokerProcess.WaitForExit(15000);
                Log("BROKER_EXITED=" + exited);
            }
            catch (Exception ex)
            {
                Log("BROKER_WAITFOREXIT_FAILED " + ex.Message);
            }
        }

        private static bool WaitForFile(string path, TimeSpan timeout)
        {
            DateTime deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                if (File.Exists(path))
                {
                    return true;
                }
                Thread.Sleep(200);
            }
            return false;
        }

        private static int? WaitForProcessStart(string processName, TimeSpan timeout)
        {
            DateTime deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                Process[] procs = Process.GetProcessesByName(processName);
                if (procs.Length > 0)
                {
                    int pid = procs[0].Id;
                    foreach (Process p in procs) p.Dispose();
                    return pid;
                }
                Thread.Sleep(500);
            }
            return null;
        }

        private static void WaitForProcessExit(string processName)
        {
            while (true)
            {
                Process[] procs = Process.GetProcessesByName(processName);
                bool anyRunning = procs.Length > 0;
                foreach (Process p in procs) p.Dispose();
                if (!anyRunning)
                {
                    return;
                }
                Thread.Sleep(1000);
            }
        }

        private static void Log(string message)
        {
            try
            {
                File.AppendAllText(LogPath, $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}");
            }
            catch
            {
            }
        }
    }
}
