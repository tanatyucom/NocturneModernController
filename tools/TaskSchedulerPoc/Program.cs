using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace NocturneModernController.TaskSchedulerPoc
{
    // Root-26 Task Scheduler Helper PoC (Phase 1, per instruction document).
    //
    // Purpose: test whether Windows Task Scheduler's COM API (Schedule.Service)
    // can replace NocturneModernController.Launcher.exe as the out-of-process-
    // tree root that starts NocturneModernController.Broker.exe, so that the
    // Broker (and, via its existing, UNMODIFIED launch-request mechanism, the
    // InputHelper) ends up with a process ancestry rooted at the Task
    // Scheduler service rather than at smt3hd.exe / Steam - the same property
    // that makes the existing Launcher->Broker->Helper chain work today.
    //
    // Design decision (Phase A review of the existing architecture):
    //   - ExternalInputBridge.cs (runs inside smt3hd.exe) never spawns Helper
    //     itself; it only checks a ready-marker + a Mutex to confirm a Broker
    //     is alive, then writes a one-shot launch-request JSON file that the
    //     Broker polls for.
    //   - Broker.exe already owns ALL the lifecycle logic that matters here:
    //     single-instance Mutex, ready-marker file, launch-request polling,
    //     shutdown-request polling. It does NOT itself watch smt3hd.exe for
    //     exit (Launcher.exe does that separately and then writes the
    //     shutdown-request file).
    //   Therefore this PoC targets Option B (Task Scheduler -> Broker.exe),
    //   NOT Option A (Task Scheduler -> InputHelper.exe directly): it reuses
    //   100% of the existing, already-proven Broker lifecycle code completely
    //   unmodified, and the ExternalInputBridge/Helper/MMF paths are untouched
    //   by this PoC entirely - only "who starts Broker.exe" changes, and only
    //   for this manual Phase 1 test (the PoC tool itself triggers the Task
    //   run - the MOD DLL is NOT modified in this phase).
    //
    // Uses the Task Scheduler 2.0 COM API (Schedule.Service) via late-bound
    // COM interop (dynamic) - no PowerShell, no schtasks.exe, no cmd.exe, no
    // extra NuGet package.
    //
    // Task settings (per instruction document, all intentionally minimal):
    //   - Logon type: TASK_LOGON_INTERACTIVE_TOKEN (current interactive user,
    //     "run only when user is logged on" - no stored credentials)
    //   - Run level: TASK_RUNLEVEL_LUA (highest privileges = false)
    //   - Hidden: false
    //   - Triggers: NONE (on-demand only - registering the task does nothing
    //     by itself; only an explicit Run() call starts anything)
    //   - ExecutionTimeLimit: PT0S (no limit) - the default 72h limit would
    //     otherwise let Task Scheduler kill the Broker/Helper process tree
    //     mid-session on a long play session.
    internal static class Program
    {
        private const string TaskName = "NocturneModernController_InputHelper_PoC";
        private const string TaskFolderPath = "\\";

        // TaskDefinition / registration constants (Task Scheduler 2.0 COM API).
        private const int TASK_CREATE_OR_UPDATE = 6;
        private const int TASK_LOGON_INTERACTIVE_TOKEN = 3;
        private const int TASK_RUNLEVEL_LUA = 0;
        private const int TASK_ACTION_EXEC = 0;

        private static readonly string LogPath = Path.Combine(
            Path.GetTempPath(), "NocturneModernController.TaskSchedulerPoc.log");

        private static int Main(string[] args)
        {
            if (args.Length < 1)
            {
                Console.WriteLine("Usage: NocturneModernController.TaskSchedulerPoc.exe <register|readback|run|status|cleanup|verify-deleted> [brokerExePath]");
                return 1;
            }

            string command = args[0].ToLowerInvariant();
            try
            {
                switch (command)
                {
                    case "register":
                        if (args.Length < 2)
                        {
                            Console.WriteLine("register requires the Broker.exe path as the second argument.");
                            return 1;
                        }
                        Register(args[1]);
                        return 0;
                    case "readback":
                        Readback();
                        return 0;
                    case "run":
                        RunTask();
                        return 0;
                    case "status":
                        Status();
                        return 0;
                    case "cleanup":
                        Cleanup();
                        return 0;
                    case "verify-deleted":
                        VerifyDeleted();
                        return 0;
                    default:
                        Console.WriteLine("Unknown command: " + command);
                        return 1;
                }
            }
            catch (Exception ex)
            {
                Log("FATAL " + ex.GetType().FullName + ": " + ex.Message);
                Console.WriteLine("FATAL: " + ex.GetType().FullName + ": " + ex.Message);
                return 99;
            }
        }

        private static dynamic ConnectService()
        {
            Type? serviceType = Type.GetTypeFromProgID("Schedule.Service");
            if (serviceType == null)
            {
                throw new InvalidOperationException("Schedule.Service COM ProgID not found.");
            }
            dynamic service = Activator.CreateInstance(serviceType)!;
            service.Connect();
            return service;
        }

        private static void Register(string brokerExePath)
        {
            if (!File.Exists(brokerExePath))
            {
                throw new FileNotFoundException("Broker executable not found.", brokerExePath);
            }
            string workingDir = Path.GetDirectoryName(Path.GetFullPath(brokerExePath)) ?? string.Empty;

            dynamic service = ConnectService();
            dynamic rootFolder = service.GetFolder(TaskFolderPath);
            dynamic taskDefinition = service.NewTask(0);

            taskDefinition.RegistrationInfo.Description =
                "NocturneModernController - on-demand launcher for the independent input Broker (PoC). " +
                "On-demand only: no triggers, no autostart, no elevation. Registered only while the user has " +
                "explicitly enabled this feature; safe to delete at any time.";
            taskDefinition.RegistrationInfo.Author = "NocturneModernController";

            taskDefinition.Principal.LogonType = TASK_LOGON_INTERACTIVE_TOKEN;
            taskDefinition.Principal.RunLevel = TASK_RUNLEVEL_LUA;

            taskDefinition.Settings.Enabled = true;
            taskDefinition.Settings.Hidden = false;
            taskDefinition.Settings.AllowDemandStart = true;
            taskDefinition.Settings.DisallowStartIfOnBatteries = false;
            taskDefinition.Settings.StopIfGoingOnBatteries = false;
            taskDefinition.Settings.ExecutionTimeLimit = "PT0S"; // no limit
            taskDefinition.Settings.MultipleInstances = 3; // TASK_INSTANCES_STOP_EXISTING is overkill; 2=Queue, 3=IgnoreNew - broker itself has its own single-instance Mutex already.

            dynamic action = taskDefinition.Actions.Create(TASK_ACTION_EXEC);
            action.Path = Path.GetFullPath(brokerExePath);
            action.WorkingDirectory = workingDir;

            // No triggers added at all - taskDefinition.Triggers stays empty.

            dynamic registered = rootFolder.RegisterTaskDefinition(
                TaskName, taskDefinition, TASK_CREATE_OR_UPDATE, null!, null!, TASK_LOGON_INTERACTIVE_TOKEN, "");

            string msg = $"REGISTERED task='{TaskName}' action='{action.Path}' workingDir='{workingDir}'";
            Log(msg);
            Console.WriteLine(msg);
        }

        private static void Readback()
        {
            dynamic service = ConnectService();
            dynamic rootFolder = service.GetFolder(TaskFolderPath);
            dynamic task = rootFolder.GetTask(TaskName);
            dynamic def = task.Definition;

            int triggerCount = def.Triggers.Count;
            int actionCount = def.Actions.Count;
            string actionPath = actionCount >= 1 ? def.Actions[1].Path : "(none)";
            string actionWorkingDir = actionCount >= 1 ? def.Actions[1].WorkingDirectory : "(none)";
            int logonType = def.Principal.LogonType;
            int runLevel = def.Principal.RunLevel;
            bool hidden = def.Settings.Hidden;
            string execTimeLimit = def.Settings.ExecutionTimeLimit;
            string state = DescribeState((int)task.State);

            string report =
                $"READBACK task='{TaskName}'\n" +
                $"  Path (action)      = {actionPath}\n" +
                $"  WorkingDirectory   = {actionWorkingDir}\n" +
                $"  LogonType          = {logonType} (3=INTERACTIVE_TOKEN expected)\n" +
                $"  RunLevel           = {runLevel} (0=LUA/least-privilege expected)\n" +
                $"  Hidden             = {hidden} (false expected)\n" +
                $"  Triggers.Count     = {triggerCount} (0 expected)\n" +
                $"  ExecutionTimeLimit = {execTimeLimit}\n" +
                $"  Current State      = {state}\n" +
                $"  User               = {Environment.UserDomainName}\\{Environment.UserName}";

            Log(report);
            Console.WriteLine(report);

            bool ok = triggerCount == 0 && !hidden && runLevel == TASK_RUNLEVEL_LUA;
            Console.WriteLine(ok
                ? "SAFETY CHECK: PASS (triggers=0, hidden=false, runLevel=LUA)"
                : "SAFETY CHECK: FAILED - unexpected settings, do not proceed with Run.");
        }

        private static void RunTask()
        {
            dynamic service = ConnectService();
            dynamic rootFolder = service.GetFolder(TaskFolderPath);
            dynamic task = rootFolder.GetTask(TaskName);

            dynamic runningTask = task.Run(null!);
            System.Threading.Thread.Sleep(500);

            int enginePid = 0;
            try
            {
                enginePid = (int)runningTask.EnginePID;
            }
            catch (Exception ex)
            {
                Log("EnginePID read failed: " + ex.Message);
            }

            string msg = $"RUN REQUESTED task='{TaskName}' initialEnginePid={enginePid}";
            Log(msg);
            Console.WriteLine(msg);

            System.Threading.Thread.Sleep(1500);
            DumpBrokerProcessTree();
        }

        private static void Status()
        {
            dynamic service = ConnectService();
            dynamic rootFolder = service.GetFolder(TaskFolderPath);
            try
            {
                dynamic task = rootFolder.GetTask(TaskName);
                Console.WriteLine("Task exists. State=" + DescribeState((int)task.State));
            }
            catch (COMException)
            {
                Console.WriteLine("Task does not exist.");
            }
            DumpBrokerProcessTree();
        }

        private static void Cleanup()
        {
            dynamic service = ConnectService();
            dynamic rootFolder = service.GetFolder(TaskFolderPath);
            try
            {
                rootFolder.DeleteTask(TaskName, 0);
                Log("DELETED task=" + TaskName);
                Console.WriteLine("Deleted task: " + TaskName);
            }
            catch (COMException ex)
            {
                Log("DELETE FAILED (may already be absent): " + ex.Message);
                Console.WriteLine("Delete failed (task may already be absent): " + ex.Message);
            }
        }

        private static void VerifyDeleted()
        {
            dynamic service = ConnectService();
            dynamic rootFolder = service.GetFolder(TaskFolderPath);
            try
            {
                rootFolder.GetTask(TaskName);
                Console.WriteLine("STILL EXISTS: " + TaskName);
            }
            catch (COMException)
            {
                Console.WriteLine("CONFIRMED ABSENT: " + TaskName);
            }
        }

        private static string DescribeState(int state) => state switch
        {
            0 => "Unknown",
            1 => "Disabled",
            2 => "Queued",
            3 => "Ready",
            4 => "Running",
            _ => "State(" + state + ")"
        };

        // ---- Process tree inspection (ToolHelp32 snapshot; no System.Management dependency) ----

        [StructLayout(LayoutKind.Sequential)]
        private struct PROCESSENTRY32
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
        private static extern IntPtr CreateToolhelp32Snapshot(uint dwFlags, uint th32ProcessID);

        [DllImport("kernel32.dll")]
        private static extern bool Process32First(IntPtr hSnapshot, ref PROCESSENTRY32 lppe);

        [DllImport("kernel32.dll")]
        private static extern bool Process32Next(IntPtr hSnapshot, ref PROCESSENTRY32 lppe);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);

        private const uint TH32CS_SNAPPROCESS = 0x00000002;

        private static void DumpBrokerProcessTree()
        {
            var all = SnapshotProcesses();
            foreach (var p in all)
            {
                if (string.Equals(p.szExeFile, "NocturneModernController.Broker.exe", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(p.szExeFile, "NocturneModernController.InputHelper.exe", StringComparison.OrdinalIgnoreCase))
                {
                    string chain = DescribeAncestry(all, p.th32ProcessID);
                    string line = $"PROCESS {p.szExeFile} pid={p.th32ProcessID} ancestry: {chain}";
                    Log(line);
                    Console.WriteLine(line);
                }
            }
            if (all.Count == 0)
            {
                Console.WriteLine("(process snapshot failed or empty)");
            }
        }

        private static string DescribeAncestry(System.Collections.Generic.List<PROCESSENTRY32> all, uint pid)
        {
            var names = new System.Collections.Generic.List<string>();
            uint current = pid;
            for (int depth = 0; depth < 10; depth++)
            {
                var entry = all.Find(e => e.th32ProcessID == current);
                if (entry.th32ProcessID == 0 && depth > 0)
                {
                    names.Add($"(pid {current} not found - process exited or ID reused)");
                    break;
                }
                names.Add($"{entry.szExeFile}(pid={entry.th32ProcessID})");
                if (entry.th32ParentProcessID == 0 || entry.th32ParentProcessID == current)
                {
                    break;
                }
                current = entry.th32ParentProcessID;
            }
            return string.Join(" <- ", names);
        }

        private static System.Collections.Generic.List<PROCESSENTRY32> SnapshotProcesses()
        {
            var result = new System.Collections.Generic.List<PROCESSENTRY32>();
            IntPtr snap = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
            if (snap == IntPtr.Zero || snap.ToInt64() == -1)
            {
                return result;
            }
            try
            {
                var entry = new PROCESSENTRY32 { dwSize = (uint)Marshal.SizeOf<PROCESSENTRY32>() };
                if (Process32First(snap, ref entry))
                {
                    do
                    {
                        result.Add(entry);
                        entry.dwSize = (uint)Marshal.SizeOf<PROCESSENTRY32>();
                    } while (Process32Next(snap, ref entry));
                }
            }
            finally
            {
                CloseHandle(snap);
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
    }
}
