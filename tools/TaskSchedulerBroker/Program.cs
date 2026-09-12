using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace NocturneModernController.TaskSchedulerBroker
{
    // Phase P1 acceptance-test harness for BrokerTaskService. This is a
    // TEST DRIVER only - it exercises Register -> Validate -> Run ->
    // ancestry check -> Unregister -> verify-absent against the real,
    // already-built production Broker.exe, and prints a report matching
    // the Phase P1 acceptance criteria. It is not wired into the MOD DLL
    // or the Settings UI - see BrokerTaskService.cs for the reusable class
    // itself, which has no dependency on this file.
    internal static class Program
    {
        private static int Main(string[] args)
        {
            if (args.Length < 1)
            {
                Console.WriteLine("Usage: NocturneModernController.TaskSchedulerBroker.exe <brokerExePath> [register-only|unregister-only]");
                return 1;
            }
            string brokerPath = args[0];
            string mode = args.Length >= 2 ? args[1].ToLowerInvariant() : "full";

            var svc = new BrokerTaskService();

            // Phase P2 test-setup helper: register (or remove) the
            // production task and stop, WITHOUT running the full P1
            // round-trip (which ends by unregistering). Used only to set up
            // preconditions for a P2 runtime-integration test in the
            // absence of the Settings UI checkbox (not yet implemented).
            if (mode == "register-only")
            {
                BrokerRegistrationOutcome outcome = svc.Register(brokerPath);
                Console.WriteLine("Register outcome = " + outcome);
                BrokerTaskValidation setupValidation = svc.Validate(brokerPath);
                Console.WriteLine("Exists=" + setupValidation.Exists + " IsOurs=" + setupValidation.IsOurs + " SettingsValid=" + setupValidation.SettingsValid);
                return setupValidation.IsFullyValid ? 0 : 1;
            }
            if (mode == "unregister-only")
            {
                BrokerUnregisterOutcome outcome = svc.Unregister(brokerPath);
                Console.WriteLine("Unregister outcome = " + outcome);
                return outcome == BrokerUnregisterOutcome.Deleted || outcome == BrokerUnregisterOutcome.AlreadyAbsent ? 0 : 1;
            }

            bool allPass = true;

            Console.WriteLine("=== Phase P1 acceptance test ===");
            Console.WriteLine("TaskName = " + BrokerTaskService.TaskName);
            Console.WriteLine("Broker path = " + Path.GetFullPath(brokerPath));

            Console.WriteLine("\n--- Step 1: IsRegistered (expect false, clean start) ---");
            bool preExisting = svc.IsRegistered(brokerPath);
            Console.WriteLine("IsRegistered = " + preExisting);
            if (preExisting)
            {
                Console.WriteLine("WARNING: task already existed before this run - validating instead of assuming clean state.");
            }

            Console.WriteLine("\n--- Step 2: Register ---");
            BrokerRegistrationOutcome regOutcome = svc.Register(brokerPath);
            Console.WriteLine("Register outcome = " + regOutcome);
            allPass &= regOutcome == BrokerRegistrationOutcome.Registered || regOutcome == BrokerRegistrationOutcome.AlreadyValid || regOutcome == BrokerRegistrationOutcome.Updated;

            Console.WriteLine("\n--- Step 3: Validate (read-back) ---");
            BrokerTaskValidation v = svc.Validate(brokerPath);
            Console.WriteLine($"Exists             = {v.Exists}");
            Console.WriteLine($"IsOurs             = {v.IsOurs}");
            Console.WriteLine($"SettingsValid      = {v.SettingsValid}");
            Console.WriteLine($"ActionPath         = {v.ActionPath}");
            Console.WriteLine($"WorkingDirectory   = {v.WorkingDirectory}");
            Console.WriteLine($"LogonType          = {v.LogonType} (3=INTERACTIVE_TOKEN expected)");
            Console.WriteLine($"RunLevel           = {v.RunLevel} (0=LUA expected)");
            Console.WriteLine($"Hidden             = {v.Hidden} (false expected)");
            Console.WriteLine($"TriggerCount       = {v.TriggerCount} (0 expected)");
            Console.WriteLine($"ExecutionTimeLimit = {v.ExecutionTimeLimit} (PT0S expected)");
            allPass &= v.IsFullyValid;

            Console.WriteLine("\n--- Step 4: Run ---");
            int enginePid = svc.Run();
            Console.WriteLine("Run() reported EnginePID = " + enginePid);
            Thread.Sleep(1500);

            Console.WriteLine("\n--- Step 5: Process ancestry check ---");
            var all = SnapshotProcesses();
            bool foundBroker = false;
            bool ancestryClean = true;
            foreach (var p in all)
            {
                if (!string.Equals(p.szExeFile, "NocturneModernController.Broker.exe", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                foundBroker = true;
                string chain = DescribeAncestry(all, p.th32ProcessID);
                Console.WriteLine($"Broker pid={p.th32ProcessID} ancestry: {chain}");
                if (chain.IndexOf("smt3hd.exe", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    chain.IndexOf("steam.exe", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    ancestryClean = false;
                }
            }
            Console.WriteLine("Broker found running = " + foundBroker);
            Console.WriteLine("Ancestry clean (no smt3hd.exe/steam.exe) = " + ancestryClean);
            allPass &= foundBroker && ancestryClean;

            Console.WriteLine("\n--- Step 6: Unregister ---");
            BrokerUnregisterOutcome unregOutcome = svc.Unregister(brokerPath);
            Console.WriteLine("Unregister outcome = " + unregOutcome);
            allPass &= unregOutcome == BrokerUnregisterOutcome.Deleted;

            Console.WriteLine("\n--- Step 7: Verify absence ---");
            bool stillRegistered = svc.IsRegistered(brokerPath);
            Console.WriteLine("IsRegistered after Unregister = " + stillRegistered);
            allPass &= !stillRegistered;

            Console.WriteLine("\n=== RESULT: " + (allPass ? "ALL PASS" : "FAILURES PRESENT - see above") + " ===");
            return allPass ? 0 : 1;
        }

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

        private static string DescribeAncestry(List<PROCESSENTRY32> all, uint pid)
        {
            var names = new List<string>();
            uint current = pid;
            for (int depth = 0; depth < 10; depth++)
            {
                var entry = all.Find(e => e.th32ProcessID == current);
                if (entry.th32ProcessID == 0 && depth > 0)
                {
                    names.Add($"(pid {current} not found)");
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

        private static List<PROCESSENTRY32> SnapshotProcesses()
        {
            var result = new List<PROCESSENTRY32>();
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
    }
}
