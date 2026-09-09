using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace NocturneModernController.Root22Wrapper
{
    // Root-22: THROWAWAY validation-only PoC. Not part of production.
    //
    // Invoked by Steam via Launch Options as:
    //   "<this exe>" %command%
    // so Steam launches THIS process directly (its own child), and %command%
    // arrives as this process's own argv - the real SMT3HD launch command
    // that this process must itself execute for the game to actually start.
    //
    // Purpose: find out whether NocturneModernController.Broker.exe / .Helper.exe,
    // when launched as children of THIS wrapper (i.e. as siblings of smt3hd.exe
    // under a process tree that Steam itself directly launched), still see the
    // real physical XInput device, or get swept into Steam Input's virtual-device
    // substitution the same way a direct child of smt3hd.exe does (Root-10/14/17).
    //
    // Uses the EXISTING production Broker.exe/Helper.exe/MOD DLL unmodified -
    // this wrapper only reproduces the small subset of NocturneModernController
    // .Launcher.exe's job (start Broker, wait ready, clean shutdown) plus
    // temporary diagnostic capture (process ancestry + Helper module list) that
    // does not exist in - and is not being added to - production code.
    internal static class Program
    {
        private const string BrokerPath =
            @"C:\Program Files (x86)\Steam\steamapps\common\smt3hd\Mods\NocturneModernController.Helper\NocturneModernController.Broker.exe";

        private const string HelperProcessName = "NocturneModernController.InputHelper";

        private static readonly string LogPath = Path.Combine(
            Path.GetTempPath(), "NocturneModernController.Root22Wrapper.log");

        private static readonly string BrokerReadyMarkerPath = Path.Combine(
            Path.GetTempPath(), "NocturneModernController.Broker.ready.json");

        private static readonly string BrokerShutdownRequestPath = Path.Combine(
            Path.GetTempPath(), "NocturneModernController.Broker.ShutdownRequest.json");

        private static int Main(string[] args)
        {
            File.WriteAllText(
                LogPath,
                $"ROOT22 WRAPPER START pid={Environment.ProcessId} {DateTimeOffset.Now:O}{Environment.NewLine}");
            Log("RECEIVED ARGS: [" + string.Join("] [", args) + "]");
            LogAncestorChain("wrapper(self)", Environment.ProcessId);

            if (args.Length == 0)
            {
                Log("FATAL: no game command received via %command% - aborting, NOT launching the game.");
                return 10;
            }

            if (!File.Exists(BrokerPath))
            {
                Log("FATAL: production Broker.exe not found at " + BrokerPath);
                return 11;
            }

            try
            {
                if (File.Exists(BrokerReadyMarkerPath)) File.Delete(BrokerReadyMarkerPath);
            }
            catch (Exception ex)
            {
                Log("READY_MARKER_PRECLEAN_FAILED " + ex.Message);
            }

            Process broker;
            try
            {
                var brokerStart = new ProcessStartInfo(BrokerPath)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                };
                broker = Process.Start(brokerStart)!;
                Log("BROKER_LAUNCHED pid=" + broker.Id);
            }
            catch (Exception ex)
            {
                Log("BROKER_LAUNCH_FAILED " + ex);
                return 12;
            }

            if (!WaitForFile(BrokerReadyMarkerPath, TimeSpan.FromSeconds(10)))
            {
                Log("BROKER_READY_TIMEOUT");
                return 13;
            }
            Log("BROKER_READY");
            LogAncestorChain("broker", broker.Id);

            string gameExe = args[0];
            string gameDir = Path.GetDirectoryName(gameExe) ?? string.Empty;

            Process game;
            try
            {
                var gameStart = new ProcessStartInfo(gameExe)
                {
                    UseShellExecute = false,
                    WorkingDirectory = gameDir
                };
                for (int i = 1; i < args.Length; i++)
                {
                    gameStart.ArgumentList.Add(args[i]);
                }
                game = Process.Start(gameStart)!;
                Log($"GAME_LAUNCHED pid={game.Id} exe=\"{gameExe}\" workingDir=\"{gameDir}\"");
            }
            catch (Exception ex)
            {
                Log("GAME_LAUNCH_FAILED " + ex);
                RequestBrokerShutdown(broker);
                return 14;
            }
            LogAncestorChain("game(smt3hd)", game.Id);

            // Diagnostic-only: watch for Helper to appear and snapshot its
            // ancestry + loaded modules once, then stop watching. This is
            // wrapper-local throwaway code, not a change to production Helper.
            bool helperObserved = false;
            DateTime helperWaitDeadline = DateTime.UtcNow.AddSeconds(60);
            while (!game.HasExited && !helperObserved && DateTime.UtcNow < helperWaitDeadline)
            {
                using Process? helper = Process.GetProcessesByName(HelperProcessName).FirstOrDefault();
                if (helper != null)
                {
                    helperObserved = true;
                    Log("HELPER_DETECTED pid=" + helper.Id);
                    LogAncestorChain("helper", helper.Id);
                    LogHelperModules(helper.Id);
                }
                else
                {
                    Thread.Sleep(500);
                }
            }
            if (!helperObserved)
            {
                Log("HELPER_NOT_DETECTED_WITHIN_TIMEOUT");
            }

            game.WaitForExit();
            Log("GAME_EXITED pid=" + game.Id + " exitCode=" + SafeExitCode(game));

            Thread.Sleep(3000);
            RequestBrokerShutdown(broker);

            Log("WRAPPER_DONE");
            return 0;
        }

        private static int SafeExitCode(Process p)
        {
            try { return p.ExitCode; } catch { return -1; }
        }

        private static void RequestBrokerShutdown(Process broker)
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

            try
            {
                bool exited = broker.WaitForExit(15000);
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
                if (File.Exists(path)) return true;
                Thread.Sleep(200);
            }
            return false;
        }

        // ------------------------------------------------------------------
        // Diagnostic-only helpers (Root-22 wrapper-local; NOT added to
        // production Helper/Broker/Launcher/MOD).
        // ------------------------------------------------------------------

        private static void LogAncestorChain(string label, int startPid, int maxDepth = 10)
        {
            try
            {
                Dictionary<int, (int ParentPid, string ExeName)> table = SnapshotProcesses();
                var sb = new StringBuilder();
                sb.Append("ANCESTRY[" + label + "] ");
                int pid = startPid;
                for (int depth = 0; depth < maxDepth; depth++)
                {
                    if (!table.TryGetValue(pid, out var entry))
                    {
                        sb.Append($"pid={pid}(unknown)");
                        break;
                    }
                    sb.Append($"{entry.ExeName}(pid={pid})");
                    if (entry.ParentPid == 0 || entry.ParentPid == pid)
                    {
                        break;
                    }
                    sb.Append(" <- ");
                    pid = entry.ParentPid;
                }
                Log(sb.ToString());
            }
            catch (Exception ex)
            {
                Log($"ANCESTRY[{label}] FAILED " + ex.Message);
            }
        }

        private static void LogHelperModules(int helperPid)
        {
            try
            {
                using Process helper = Process.GetProcessById(helperPid);
                var names = helper.Modules
                    .Cast<ProcessModule>()
                    .Select(m => m.ModuleName ?? "(unknown)")
                    .ToArray();
                bool overlayLoaded = names.Any(n => n.IndexOf("gameoverlayrenderer", StringComparison.OrdinalIgnoreCase) >= 0);
                Log($"HELPER_MODULES count={names.Length} gameoverlayrenderer64={overlayLoaded}");
                Log("HELPER_MODULE_LIST: " + string.Join(", ", names));
            }
            catch (Exception ex)
            {
                Log("HELPER_MODULES_FAILED " + ex.Message);
            }
        }

        private static Dictionary<int, (int ParentPid, string ExeName)> SnapshotProcesses()
        {
            var result = new Dictionary<int, (int, string)>();
            IntPtr snapshot = NativeMethods.CreateToolhelp32Snapshot(NativeMethods.Th32csSnapProcess, 0);
            if (snapshot == IntPtr.Zero || snapshot.ToInt64() == -1)
            {
                return result;
            }
            try
            {
                var entry = new NativeMethods.PROCESSENTRY32();
                entry.dwSize = (uint)Marshal.SizeOf<NativeMethods.PROCESSENTRY32>();
                if (NativeMethods.Process32First(snapshot, ref entry))
                {
                    do
                    {
                        result[(int)entry.th32ProcessID] = ((int)entry.th32ParentProcessID, entry.szExeFile);
                    } while (NativeMethods.Process32Next(snapshot, ref entry));
                }
            }
            finally
            {
                NativeMethods.CloseHandle(snapshot);
            }
            return result;
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

        private static class NativeMethods
        {
            internal const uint Th32csSnapProcess = 0x00000002;

            [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
            internal struct PROCESSENTRY32
            {
                public uint dwSize;
                public uint cntUsage;
                public uint th32ProcessID;
                public IntPtr th32DefaultHeapID;
                public uint th32ModuleID;
                public uint cntThreads;
                public uint th32ParentProcessID;
                public int pcPriClassBase;
                public uint dwFlags;
                [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
                public string szExeFile;
            }

            [DllImport("kernel32.dll", SetLastError = true)]
            internal static extern IntPtr CreateToolhelp32Snapshot(uint dwFlags, uint th32ProcessID);

            [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
            [return: MarshalAs(UnmanagedType.Bool)]
            internal static extern bool Process32First(IntPtr hSnapshot, ref PROCESSENTRY32 lppe);

            [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
            [return: MarshalAs(UnmanagedType.Bool)]
            internal static extern bool Process32Next(IntPtr hSnapshot, ref PROCESSENTRY32 lppe);

            [DllImport("kernel32.dll")]
            [return: MarshalAs(UnmanagedType.Bool)]
            internal static extern bool CloseHandle(IntPtr hObject);
        }
    }
}
