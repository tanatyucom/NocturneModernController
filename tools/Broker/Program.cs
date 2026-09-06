using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading;

namespace NocturneModernController.Broker
{
    // Independent broker for NocturneModernController. Started by
    // NocturneModernController.Launcher.exe (never as a child of smt3hd.exe
    // and never via explorer.exe). It waits for a one-shot JSON request
    // file from the MOD DLL (running inside smt3hd.exe) asking it to start
    // the existing InputHelper.exe on the MOD's behalf, and for a separate
    // one-shot shutdown request so its own lifecycle ends cleanly (never
    // via a forced kill) once the Launcher is done with it.
    //
    // The reason this exists at all: since THIS process launches Helper.exe
    // via Process.Start, Helper's parent becomes the broker - never
    // smt3hd.exe - without using explorer.exe and without any parent-PID
    // spoofing. Real-machine testing found this necessary for the physical
    // controller (in particular the right stick) to reach Helper at all;
    // see docs/research for the investigation history.
    internal static class Program
    {
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

        private sealed class LaunchRequest
        {
            public string? HelperPath { get; set; }
        }

        private static int Main()
        {
            File.WriteAllText(
                LogPath,
                $"BROKER START pid={Environment.ProcessId} {DateTimeOffset.Now:O}{Environment.NewLine}");

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

            while (true)
            {
                if (File.Exists(ShutdownRequestPath))
                {
                    try { File.Delete(ShutdownRequestPath); } catch { }
                    Log("SHUTDOWN REQUESTED - exiting cleanly.");
                    _singleInstanceMutex.ReleaseMutex();
                    return 0;
                }
                if (File.Exists(RequestPath))
                {
                    HandleRequest();
                }
                Thread.Sleep(300);
            }
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
                    proc.Dispose();
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
