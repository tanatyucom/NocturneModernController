using System;
using System.Runtime.InteropServices;
using System.Text;

// Standalone launcher used only by tools/Run-AxisTestComparison.ps1's Case C
// (CreateProcess + CREATE_BREAKAWAY_FROM_JOB). It exists because raw kernel32
// CreateProcess invoked through Windows PowerShell 5.1's Add-Type (dynamically
// JIT-compiled) consistently failed with Win32 error 123 (ERROR_INVALID_NAME)
// in real-machine testing, even for a trivial fully-qualified target with no
// breakaway flag at all - while the identical pattern compiled normally via
// `dotnet build` (this project, and src/ExternalInputBridge.cs in production)
// works. This tool sidesteps that by doing the CreateProcess call in a
// normally-compiled binary instead of PowerShell-hosted reflection-emitted IL.
internal static class Program
{
    private const uint CreateNoWindow = 0x08000000;
    private const uint CreateBreakawayFromJob = 0x01000000;

    private static int Main(string[] args)
    {
        if (args.Length < 1)
        {
            Console.Error.WriteLine("Usage: NocturneModernController.AxisTestBreakawayLaunch.exe <targetExePath> [extra args passed through to target]");
            return 2;
        }

        string targetPath = args[0];
        string extraArgs = args.Length > 1 ? " " + string.Join(" ", args, 1, args.Length - 1) : string.Empty;
        var commandLine = new StringBuilder("\"" + targetPath + "\"" + extraArgs);

        var startupInfo = new STARTUPINFO();
        startupInfo.cb = Marshal.SizeOf<STARTUPINFO>();

        bool created = CreateProcess(
            null,
            commandLine,
            IntPtr.Zero,
            IntPtr.Zero,
            false,
            CreateNoWindow | CreateBreakawayFromJob,
            IntPtr.Zero,
            null,
            ref startupInfo,
            out PROCESS_INFORMATION processInfo);

        if (!created)
        {
            int error = Marshal.GetLastWin32Error();
            Console.Error.WriteLine("CreateProcess with CREATE_BREAKAWAY_FROM_JOB failed, Win32 error=" + error);
            return 1;
        }

        Console.WriteLine("BREAKAWAY_LAUNCH_OK pid=" + processInfo.dwProcessId);
        CloseHandle(processInfo.hThread);
        CloseHandle(processInfo.hProcess);
        return 0;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct STARTUPINFO
    {
        public int cb;
        public string? lpReserved;
        public string? lpDesktop;
        public string? lpTitle;
        public int dwX;
        public int dwY;
        public int dwXSize;
        public int dwYSize;
        public int dwXCountChars;
        public int dwYCountChars;
        public int dwFillAttribute;
        public int dwFlags;
        public short wShowWindow;
        public short cbReserved2;
        public IntPtr lpReserved2;
        public IntPtr hStdInput;
        public IntPtr hStdOutput;
        public IntPtr hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_INFORMATION
    {
        public IntPtr hProcess;
        public IntPtr hThread;
        public int dwProcessId;
        public int dwThreadId;
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CreateProcess(
        string? lpApplicationName,
        StringBuilder lpCommandLine,
        IntPtr lpProcessAttributes,
        IntPtr lpThreadAttributes,
        [MarshalAs(UnmanagedType.Bool)] bool bInheritHandles,
        uint dwCreationFlags,
        IntPtr lpEnvironment,
        string? lpCurrentDirectory,
        ref STARTUPINFO lpStartupInfo,
        out PROCESS_INFORMATION lpProcessInformation);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr hObject);
}
