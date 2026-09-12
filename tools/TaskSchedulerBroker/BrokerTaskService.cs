using System;
using System.IO;
using System.Runtime.InteropServices;

namespace NocturneModernController.TaskSchedulerBroker
{
    // Production Task Scheduler integration for NocturneModernController's
    // "Automatic Right Stick Helper" feature (Root-26). This class is the
    // sole owner of the on-demand Task registration used to start
    // NocturneModernController.Broker.exe with a process ancestry rooted at
    // the Task Scheduler service (svchost.exe/services.exe/wininit.exe)
    // rather than at smt3hd.exe or steam.exe - confirmed necessary and
    // sufficient for the physical right stick to reach the existing
    // Broker -> launch-request -> Launcher/InputHelper pipeline (see the
    // Phase 1 PoC evidence in tools/TaskSchedulerPoc and docs/research).
    //
    // This file intentionally has ZERO dependency on the rest of this
    // project's Program.cs (which is only a P1 acceptance-test harness) so
    // it can be linked as-is into the MOD DLL project and/or the Settings
    // UI project in a later phase without modification.
    //
    // Design (see instruction doc section 5, "ON/OFF semantics"):
    //   - Register() is idempotent and NEVER destroys a same-named task it
    //     does not recognize as its own (a "foreign" task with the same
    //     name is reported as a conflict, never overwritten or deleted).
    //   - Unregister() likewise refuses to delete a task it does not own.
    //   - No trigger is ever added - the task does nothing until something
    //     explicitly calls Run().
    //   - No PowerShell, cmd.exe, or schtasks.exe - Schedule.Service (Task
    //     Scheduler 2.0 COM API) only, via late-bound interop (no extra
    //     NuGet dependency).
    public sealed class BrokerTaskService
    {
        public const string TaskName = "NocturneModernController_RightStickHelper";

        private const string TaskFolderPath = "\\";
        private const string ExpectedAuthor = "NocturneModernController";
        private const string DescriptionMarker =
            "NocturneModernController managed task - on-demand launcher for the independent input Broker. " +
            "Registered only while the user has enabled Automatic Right Stick Helper; safe to delete at any time.";

        // Task Scheduler 2.0 COM API constants.
        private const int TASK_CREATE_OR_UPDATE = 6;
        private const int TASK_LOGON_INTERACTIVE_TOKEN = 3;
        private const int TASK_RUNLEVEL_LUA = 0;
        private const int TASK_ACTION_EXEC = 0;
        private const int TASK_INSTANCES_IGNORE_NEW = 3;

        // Expected settings - the ONLY shape this class will ever create or
        // accept as "ours and valid". Deliberately conservative: no
        // triggers, no elevation, not hidden, no execution time limit.
        private const int ExpectedLogonType = TASK_LOGON_INTERACTIVE_TOKEN;
        private const int ExpectedRunLevel = TASK_RUNLEVEL_LUA;
        private const bool ExpectedHidden = false;
        private const int ExpectedTriggerCount = 0;
        private const string ExpectedExecutionTimeLimit = "PT0S";

        // Root-26 Phase P4: passed to Broker.exe so it can identify this as
        // the Task Scheduler origin (as opposed to Launcher.exe, which
        // always starts Broker.exe with no arguments at all) and apply its
        // idle auto-exit policy only in that case. Not a new IPC channel -
        // just this task's own Action arguments.
        public const string TaskSchedulerOriginArg = "--origin=task-scheduler";

        public bool IsRegistered(string expectedBrokerExePath)
        {
            return Validate(expectedBrokerExePath).Exists;
        }

        // Reads back the current state of the task (if any) without
        // modifying anything.
        public BrokerTaskValidation Validate(string expectedBrokerExePath)
        {
            var result = new BrokerTaskValidation();
            dynamic service = ConnectService();
            dynamic rootFolder = service.GetFolder(TaskFolderPath);

            dynamic? task;
            try
            {
                task = rootFolder.GetTask(TaskName);
            }
            catch (COMException)
            {
                task = null;
            }
            catch (FileNotFoundException)
            {
                task = null;
            }

            if (task == null)
            {
                result.Exists = false;
                return result;
            }

            dynamic def = task.Definition;
            result.Exists = true;
            result.Description = (string)def.RegistrationInfo.Description;
            result.Author = (string)def.RegistrationInfo.Author;
            result.TriggerCount = (int)def.Triggers.Count;
            int actionCount = (int)def.Actions.Count;
            result.ActionPath = actionCount >= 1 ? (string)def.Actions[1].Path : null;
            result.WorkingDirectory = actionCount >= 1 ? (string)def.Actions[1].WorkingDirectory : null;
            result.ActionArguments = actionCount >= 1 ? (string)(def.Actions[1].Arguments ?? string.Empty) : null;
            result.LogonType = (int)def.Principal.LogonType;
            result.RunLevel = (int)def.Principal.RunLevel;
            result.Hidden = (bool)def.Settings.Hidden;
            result.ExecutionTimeLimit = (string)def.Settings.ExecutionTimeLimit;

            result.IsOurs =
                string.Equals(result.Author, ExpectedAuthor, StringComparison.Ordinal) &&
                string.Equals(result.Description, DescriptionMarker, StringComparison.Ordinal);

            string expectedFull = Path.GetFullPath(expectedBrokerExePath);
            result.SettingsValid =
                result.TriggerCount == ExpectedTriggerCount &&
                result.LogonType == ExpectedLogonType &&
                result.RunLevel == ExpectedRunLevel &&
                result.Hidden == ExpectedHidden &&
                string.Equals(result.ExecutionTimeLimit, ExpectedExecutionTimeLimit, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(result.ActionPath, expectedFull, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(result.ActionArguments, TaskSchedulerOriginArg, StringComparison.Ordinal);

            return result;
        }

        // Idempotent: safe to call every time the feature is toggled ON or
        // every game launch. Never overwrites a same-named task that was
        // not created by this class.
        public BrokerRegistrationOutcome Register(string brokerExePath)
        {
            if (!File.Exists(brokerExePath))
            {
                throw new FileNotFoundException("Broker executable not found.", brokerExePath);
            }
            string fullBrokerPath = Path.GetFullPath(brokerExePath);
            string workingDir = Path.GetDirectoryName(fullBrokerPath) ?? string.Empty;

            BrokerTaskValidation before = Validate(fullBrokerPath);
            if (before.Exists && !before.IsOurs)
            {
                return BrokerRegistrationOutcome.ForeignConflict;
            }
            if (before.Exists && before.IsOurs && before.SettingsValid)
            {
                return BrokerRegistrationOutcome.AlreadyValid;
            }

            // Either not registered at all, or registered by us but with
            // stale/incorrect settings (e.g. the mod moved) - (re)create.
            dynamic service = ConnectService();
            dynamic rootFolder = service.GetFolder(TaskFolderPath);
            dynamic taskDefinition = service.NewTask(0);

            taskDefinition.RegistrationInfo.Description = DescriptionMarker;
            taskDefinition.RegistrationInfo.Author = ExpectedAuthor;

            taskDefinition.Principal.LogonType = ExpectedLogonType;
            taskDefinition.Principal.RunLevel = ExpectedRunLevel;

            taskDefinition.Settings.Enabled = true;
            taskDefinition.Settings.Hidden = ExpectedHidden;
            taskDefinition.Settings.AllowDemandStart = true;
            taskDefinition.Settings.DisallowStartIfOnBatteries = false;
            taskDefinition.Settings.StopIfGoingOnBatteries = false;
            taskDefinition.Settings.ExecutionTimeLimit = ExpectedExecutionTimeLimit;
            taskDefinition.Settings.MultipleInstances = TASK_INSTANCES_IGNORE_NEW;

            dynamic action = taskDefinition.Actions.Create(TASK_ACTION_EXEC);
            action.Path = fullBrokerPath;
            action.Arguments = TaskSchedulerOriginArg;
            action.WorkingDirectory = workingDir;

            // No triggers added - taskDefinition.Triggers stays empty.

            rootFolder.RegisterTaskDefinition(
                TaskName, taskDefinition, TASK_CREATE_OR_UPDATE, null!, null!, ExpectedLogonType, "");

            BrokerTaskValidation after = Validate(fullBrokerPath);
            if (!after.Exists || !after.IsOurs || !after.SettingsValid)
            {
                return BrokerRegistrationOutcome.Failed;
            }
            return before.Exists ? BrokerRegistrationOutcome.Updated : BrokerRegistrationOutcome.Registered;
        }

        // TASK_STATE_* (Task Scheduler 2.0 COM API, IRegisteredTask::State).
        public const int TaskStateUnknown = 0;
        public const int TaskStateDisabled = 1;
        public const int TaskStateQueued = 2;
        public const int TaskStateReady = 3;
        public const int TaskStateRunning = 4;

        // Read-only: returns the task's current State (see TaskState*
        // constants above) without starting or stopping anything. Throws if
        // the task does not exist - callers should call Validate() first.
        public int GetState()
        {
            dynamic service = ConnectService();
            dynamic rootFolder = service.GetFolder(TaskFolderPath);
            dynamic task = rootFolder.GetTask(TaskName);
            return (int)task.State;
        }

        // Starts the already-registered task on demand. Does not itself
        // wait for the Broker's ready-marker - callers that need that
        // should poll the existing Broker ready-marker mechanism
        // (ExternalInputBridge already does this).
        public int Run()
        {
            dynamic service = ConnectService();
            dynamic rootFolder = service.GetFolder(TaskFolderPath);
            dynamic task = rootFolder.GetTask(TaskName);
            dynamic runningTask = task.Run(null!);
            try
            {
                return (int)runningTask.EnginePID;
            }
            catch (COMException)
            {
                return 0;
            }
        }

        // Refuses to delete a task it does not recognize as its own.
        // Idempotent: deleting an already-absent task is treated as
        // success. Never touches any already-running process spawned by a
        // previous Run() - task definition lifecycle and already-spawned
        // process lifecycle are intentionally independent.
        public BrokerUnregisterOutcome Unregister(string expectedBrokerExePath)
        {
            BrokerTaskValidation before = Validate(expectedBrokerExePath);
            if (!before.Exists)
            {
                return BrokerUnregisterOutcome.AlreadyAbsent;
            }
            if (!before.IsOurs)
            {
                return BrokerUnregisterOutcome.RefusedForeignTask;
            }

            dynamic service = ConnectService();
            dynamic rootFolder = service.GetFolder(TaskFolderPath);
            rootFolder.DeleteTask(TaskName, 0);

            BrokerTaskValidation after = Validate(expectedBrokerExePath);
            return after.Exists ? BrokerUnregisterOutcome.Failed : BrokerUnregisterOutcome.Deleted;
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
    }

    public enum BrokerRegistrationOutcome
    {
        Registered,
        Updated,
        AlreadyValid,
        ForeignConflict,
        Failed
    }

    public enum BrokerUnregisterOutcome
    {
        Deleted,
        AlreadyAbsent,
        RefusedForeignTask,
        Failed
    }

    public sealed class BrokerTaskValidation
    {
        public bool Exists;
        public bool IsOurs;
        public bool SettingsValid;
        public string? Description;
        public string? Author;
        public string? ActionPath;
        public string? ActionArguments;
        public string? WorkingDirectory;
        public int LogonType;
        public int RunLevel;
        public bool Hidden;
        public int TriggerCount;
        public string? ExecutionTimeLimit;

        public bool IsFullyValid => Exists && IsOurs && SettingsValid;
    }
}
