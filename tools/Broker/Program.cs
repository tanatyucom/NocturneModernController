using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading;

namespace NocturneModernController.Broker
{
    // Independent broker for NocturneModernController. Started either by
    // NocturneModernController.Launcher.exe (FALLBACK route) or by the
    // Windows Task Scheduler task the Settings UI registers (PRIMARY
    // route, "Enable automatic right-stick support") - never as a child of
    // smt3hd.exe and never via explorer.exe. It waits for a one-shot JSON
    // request file from the MOD DLL (running inside smt3hd.exe) asking it
    // to start the existing InputHelper.exe on the MOD's behalf, and for a
    // separate one-shot shutdown request so its own lifecycle ends cleanly
    // (never via a forced kill).
    //
    // The reason this exists at all: since THIS process launches Helper.exe
    // via Process.Start, Helper's parent becomes the broker - never
    // smt3hd.exe - without using explorer.exe and without any parent-PID
    // spoofing. Real-machine testing found this necessary for the physical
    // controller (in particular the right stick) to reach Helper at all;
    // see docs/research for the investigation history.
    //
    // Root-26 Phase P4 (origin-based idle auto-exit): the Task Scheduler
    // route is on-demand by design (no trigger - the MOD Runs it only when
    // needed), so a Broker started that way should not stay resident
    // forever once the game session it was started for has ended. The
    // Launcher route already has its own explicit shutdown-request sent by
    // Launcher.exe once it observes smt3hd.exe exit, so it does not need
    // (and must not gain) any new auto-exit behavior - Launcher.exe still
    // fully owns that Broker's lifecycle exactly as before.
    //
    // Origin is identified via a single command-line argument
    // ("--origin=task-scheduler"), set only in the Scheduled Task's own
    // Action arguments (see BrokerTaskService.Register). Launcher.exe
    // continues to start Broker.exe with no arguments at all, so it is
    // unaffected by this change with zero code changes on its side.
    internal static class Program
    {
        private const string TaskSchedulerOriginArg = "--origin=task-scheduler";

        // Reuses the same 10-second value already established elsewhere in
        // this codebase for a Broker-readiness-related wait (Launcher.exe's
        // WaitForFile(BrokerReadyMarkerPath, ...) and ExternalInputBridge's
        // BrokerReadyTimeoutSeconds) rather than inventing a new constant.
        private const int IdleExitTimeoutSeconds = 10;

        private static readonly string LogPath = Path.Combine(
            Path.GetTempPath(),
            "NocturneModernController.Broker.log");

        private static readonly string RequestPath = Path.Combine(
            Path.GetTempPath(),
            "NocturneModernController.Broker.LaunchRequest.json");

        private static readonly string ShutdownRequestPath = Path.Combine(
            Path.GetTempPath(),
            "NocturneModernController.Broker.ShutdownRequest.json");

        private static readonly string ReadyMarkerPath = Path.Combine(
            Path.GetTempPath(),
            "NocturneModernController.Broker.ready.json");

        private static Mutex? _singleInstanceMutex;
        private static bool _isTaskSchedulerOrigin;

        // Only tracked/used when _isTaskSchedulerOrigin is true. The
        // Launcher route never touches these - it keeps the exact prior
        // behavior (Process object discarded immediately after logging).
        private static Process? _activeHelper;
        private static DateTime? _idleSince;

        private sealed class LaunchRequest
        {
            public string? HelperPath { get; set; }
        }

        private static int Main(string[] args)
        {
            _isTaskSchedulerOrigin = Array.Exists(
                args, a => string.Equals(a, TaskSchedulerOriginArg, StringComparison.OrdinalIgnoreCase));

            File.WriteAllText(
                LogPath,
                $"BROKER START pid={Environment.ProcessId} origin={(_isTaskSchedulerOrigin ? "task-scheduler" : "launcher/legacy")} {DateTimeOffset.Now:O}{Environment.NewLine}");

            bool createdNew;
            _singleInstanceMutex = new Mutex(true, "Local\\NocturneModernController.Broker.SingleInstance", out createdNew);
            if (!createdNew)
            {
                Log("SINGLE_INSTANCE_CHECK_FAILED: another broker instance is already running. Exiting.");
                return 1;
            }

            Log($"Waiting for request file: {RequestPath}");
            Console.WriteLine("NocturneModernController Broker running.");

            try
            {
                if (File.Exists(ReadyMarkerPath))
                {
                    File.Delete(ReadyMarkerPath);
                }
                File.WriteAllText(
                    ReadyMarkerPath,
                    $"{{\"pid\":{Environment.ProcessId},\"readyAtUtc\":\"{DateTimeOffset.UtcNow:O}\"}}");
            }
            catch (Exception ex)
            {
                Log("READY MARKER WRITE FAILED " + ex.Message);
            }

            // Idle clock starts immediately at startup for the Task
            // Scheduler origin, so a Broker that is Run() but never
            // receives a launch-request at all (e.g. the MOD decided it
            // didn't need it after all) also exits after the same timeout,
            // without needing a second constant.
            if (_isTaskSchedulerOrigin)
            {
                _idleSince = DateTime.UtcNow;
            }

            while (true)
            {
                if (File.Exists(ShutdownRequestPath))
                {
                    try { File.Delete(ShutdownRequestPath); } catch { }
                    Log("SHUTDOWN REQUESTED - exiting cleanly.");
                    return CleanExit();
                }
                if (File.Exists(RequestPath))
                {
                    HandleRequest();
                }

                if (_isTaskSchedulerOrigin && CheckIdleTimeoutElapsed())
                {
                    Log($"IDLE TIMEOUT ({IdleExitTimeoutSeconds}s, origin=task-scheduler) - exiting cleanly.");
                    return CleanExit();
                }

                Thread.Sleep(300);
            }
        }

        // Task Scheduler origin only: tracks whether the most recently
        // launched Helper has exited, and how long the broker has had no
        // active Helper. Never called for the Launcher origin.
        private static bool CheckIdleTimeoutElapsed()
        {
            if (_activeHelper != null)
            {
                bool exited;
                try
                {
                    exited = _activeHelper.HasExited;
                }
                catch (Exception ex)
                {
                    Log("HELPER HASEXITED CHECK FAILED " + ex.Message);
                    exited = true; // fail safe: stop tracking a handle we can't query
                }

                if (exited)
                {
                    Log($"HELPER EXITED pid={SafeProcessId(_activeHelper)}");
                    _activeHelper.Dispose();
                    _activeHelper = null;
                    _idleSince = DateTime.UtcNow;
                }
            }

            return _activeHelper == null &&
                   _idleSince.HasValue &&
                   DateTime.UtcNow - _idleSince.Value >= TimeSpan.FromSeconds(IdleExitTimeoutSeconds);
        }

        private static int SafeProcessId(Process process)
        {
            try { return process.Id; } catch { return -1; }
        }

        // Shared clean-exit path for both the existing shutdown-request
        // trigger and the new idle-timeout trigger: releases the
        // single-instance Mutex and removes the ready marker so a stale
        // marker can never outlive this process (previously the marker was
        // only ever cleaned up by the NEXT broker's startup, not by this
        // one's own shutdown - ExternalInputBridge's Mutex-liveness check
        // already tolerated that gap, but removing it here is strictly
        // more correct and costs nothing).
        private static int CleanExit()
        {
            try
            {
                if (File.Exists(ReadyMarkerPath))
                {
                    File.Delete(ReadyMarkerPath);
                }
            }
            catch (Exception ex)
            {
                Log("READY MARKER DELETE FAILED " + ex.Message);
            }

            _activeHelper?.Dispose();
            _singleInstanceMutex?.ReleaseMutex();
            return 0;
        }

        private static void HandleRequest()
        {
            string? helperPath = null;
            try
            {
                string json = File.ReadAllText(RequestPath);
                File.Delete(RequestPath); // one-shot: never re-triggers on its own
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                LaunchRequest? request = JsonSerializer.Deserialize<LaunchRequest>(json, options);
                helperPath = request?.HelperPath;
            }
            catch (Exception ex)
            {
                Log("REQUEST READ FAILED " + ex.Message);
                return;
            }

            if (string.IsNullOrWhiteSpace(helperPath) || !File.Exists(helperPath))
            {
                Log("REQUEST INVALID: helperPath missing or not found: " + (helperPath ?? "(null)"));
                return;
            }

            try
            {
                var startInfo = new ProcessStartInfo(helperPath)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                };
                Process? proc = Process.Start(startInfo);
                if (proc != null)
                {
                    Log($"HELPER LAUNCHED pid={proc.Id}");
                    if (_isTaskSchedulerOrigin)
                    {
                        // Replace any previously-tracked (already-exited)
                        // Helper reference and reset the idle clock - this
                        // broker now has an active Helper again.
                        _activeHelper?.Dispose();
                        _activeHelper = proc;
                        _idleSince = null;
                    }
                    else
                    {
                        // Launcher route: unchanged prior behavior - the
                        // broker never tracks Helper's lifecycle itself;
                        // Launcher.exe owns the broker's own shutdown via
                        // the existing shutdown-request file.
                        proc.Dispose();
                    }
                }
                else
                {
                    Log("HELPER LAUNCH RETURNED NULL PROCESS");
                }
            }
            catch (Exception ex)
            {
                Log("HELPER LAUNCH FAILED " + ex.Message);
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
