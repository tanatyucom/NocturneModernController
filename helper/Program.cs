using System;
using System.Diagnostics;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using System.Threading;

internal static class Program
{
    private const string MapName = "NocturneModernController_SDL_v2";
    private const int Magic = 0x4E4D4332;
    private const int StopRequested = 0x53544F50;
    private const uint InitGamepad = 0x00002000;
    private const uint MouseEventMove = 0x0001;
    private const int VerticalEngageThreshold = 10000;
    private const bool SyntheticVerticalMouseEnabled = false;
    private static readonly string LogPath = Path.Combine(
        Path.GetTempPath(),
        "NocturneModernController.InputHelper.log");

    private static int Main(string[] args)
    {
        File.WriteAllText(LogPath, $"START {DateTimeOffset.Now:O}{Environment.NewLine}");
        int parentPid = args.Length > 0 && int.TryParse(args[0], out int parsed)
            ? parsed
            : 0;

        if (!SdlNative.SDL_Init(InitGamepad))
        {
            Log("SDL_Init failed");
            return 2;
        }
        Log("SDL_Init OK; parentPid=" + parentPid);

        using MemoryMappedFile map = MemoryMappedFile.CreateOrOpen(MapName, 64);
        using MemoryMappedViewAccessor view = map.CreateViewAccessor();
        IntPtr gamepad = IntPtr.Zero;
        int retry = 0;
        int sequence = 0;
        view.Write(24, 0);
        view.Write(28, 0);
        view.Write(32, 0);
        int lastMouseTick = 0;
        bool cursorHidden = false;
        int trackedGamePid = 0;

        try
        {
            while (ParentIsAlive(parentPid) && view.ReadInt32(24) != StopRequested)
            {
                SdlNative.SDL_UpdateJoysticks();
                if (gamepad == IntPtr.Zero && retry-- <= 0)
                {
                    gamepad = TryOpenValidatedGamepad();
                    Log(gamepad == IntPtr.Zero ? "TARGET WAIT" : "TARGET OPEN");
                    retry = 120;
                }

                short x = gamepad == IntPtr.Zero
                    ? (short)0
                    : SdlNative.SDL_GetGamepadAxis(gamepad, 2);
                short y = gamepad == IntPtr.Zero
                    ? (short)0
                    : SdlNative.SDL_GetGamepadAxis(gamepad, 3);

                int gamePid = view.ReadInt32(32);
                if (gamePid > 0)
                {
                    trackedGamePid = gamePid;
                }
                if (trackedGamePid > 0 && !ParentIsAlive(trackedGamePid))
                {
                    Log("GAME PROCESS EXIT detected; helper stopping. pid=" + trackedGamePid);
                    break;
                }
                bool cameraContextActive = view.ReadInt32(28) != 0 &&
                    gamePid > 0 &&
                    IsForegroundProcess(gamePid);
                SetCursorHidden(cameraContextActive, ref cursorHidden);
                if (SyntheticVerticalMouseEnabled && cameraContextActive &&
                    Math.Abs((int)y) >= VerticalEngageThreshold &&
                    unchecked(Environment.TickCount - lastMouseTick) >= 4)
                {
                    lastMouseTick = Environment.TickCount;
                    int deltaY = y < 0 ? -4 : 4;
                    NativeMethods.mouse_event(
                        MouseEventMove,
                        0,
                        unchecked((uint)deltaY),
                        0,
                        UIntPtr.Zero);
                }

                view.Write(4, gamepad == IntPtr.Zero ? 0 : 1);
                view.Write(8, (int)x);
                view.Write(12, (int)y);
                view.Write(16, ++sequence);
                view.Write(20, Environment.TickCount);
                view.Write(0, Magic);
                Thread.Sleep(4);
            }
        }
        finally
        {
            SetCursorHidden(false, ref cursorHidden);
            if (gamepad != IntPtr.Zero)
            {
                SdlNative.SDL_CloseGamepad(gamepad);
            }

            // SDL_Quit is intentionally omitted because Q2 found that it can
            // block indefinitely with reWASD and Steam Input active.
        }
        Log("STOP");

        return 0;
    }

    private static void SetCursorHidden(bool shouldHide, ref bool cursorHidden)
    {
        if (shouldHide == cursorHidden)
        {
            return;
        }

        if (shouldHide)
        {
            while (NativeMethods.ShowCursor(false) >= 0)
            {
            }
        }
        else
        {
            while (NativeMethods.ShowCursor(true) < 0)
            {
            }
        }

        cursorHidden = shouldHide;
    }

    private static bool IsForegroundProcess(int expectedPid)
    {
        IntPtr window = NativeMethods.GetForegroundWindow();
        if (window == IntPtr.Zero)
        {
            return false;
        }

        NativeMethods.GetWindowThreadProcessId(window, out uint foregroundPid);
        return foregroundPid == unchecked((uint)expectedPid);
    }

    private static bool ParentIsAlive(int parentPid)
    {
        if (parentPid <= 0)
        {
            return true;
        }

        try
        {
            return !Process.GetProcessById(parentPid).HasExited;
        }
        catch
        {
            return false;
        }
    }

    private static IntPtr TryOpenValidatedGamepad()
    {
        IntPtr ids = SdlNative.SDL_GetJoysticks(out int count);
        try
        {
            // Prefer a controller that already reports stick activity. This
            // avoids selecting an idle virtual pad when both a physical pad
            // and a remapper/Steam virtual device are enumerated. If every
            // candidate is neutral, fall back to the first SDL gamepad so
            // ordinary Xbox, PlayStation, Switch and third-party pads work.
            IntPtr fallback = IntPtr.Zero;
            for (int index = 0; index < count; index++)
            {
                uint id = unchecked((uint)Marshal.ReadInt32(ids, index * sizeof(uint)));
                ushort vendor = SdlNative.SDL_GetJoystickVendorForID(id);
                ushort product = SdlNative.SDL_GetJoystickProductForID(id);
                Log($"CANDIDATE index={index} id={id} vid=0x{vendor:X4} pid=0x{product:X4} gamepad={SdlNative.SDL_IsGamepad(id)}");
                if (!SdlNative.SDL_IsGamepad(id))
                {
                    continue;
                }

                IntPtr candidate = SdlNative.SDL_OpenGamepad(id);
                if (candidate == IntPtr.Zero)
                {
                    continue;
                }

                short x = SdlNative.SDL_GetGamepadAxis(candidate, 2);
                short y = SdlNative.SDL_GetGamepadAxis(candidate, 3);
                if (Math.Abs((int)x) >= 2000 || Math.Abs((int)y) >= 2000)
                {
                    if (fallback != IntPtr.Zero)
                    {
                        SdlNative.SDL_CloseGamepad(fallback);
                    }
                    Log($"INPUT SOURCE active SDL gamepad vid=0x{vendor:X4} pid=0x{product:X4}");
                    return candidate;
                }

                if (fallback == IntPtr.Zero)
                {
                    fallback = candidate;
                    Log($"INPUT FALLBACK SDL gamepad vid=0x{vendor:X4} pid=0x{product:X4}");
                }
                else
                {
                    SdlNative.SDL_CloseGamepad(candidate);
                }
            }

            return fallback;
        }
        finally
        {
            if (ids != IntPtr.Zero)
            {
                SdlNative.SDL_free(ids);
            }
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

    private static class SdlNative
    {
        [DllImport("SDL3", CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        internal static extern bool SDL_Init(uint initFlags);

        [DllImport("SDL3", CallingConvention = CallingConvention.Cdecl)]
        internal static extern void SDL_UpdateJoysticks();

        [DllImport("SDL3", CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr SDL_GetJoysticks(out int count);

        [DllImport("SDL3", CallingConvention = CallingConvention.Cdecl)]
        internal static extern void SDL_free(IntPtr memory);

        [DllImport("SDL3", CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        internal static extern bool SDL_IsGamepad(uint instanceId);

        [DllImport("SDL3", CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr SDL_OpenGamepad(uint instanceId);

        [DllImport("SDL3", CallingConvention = CallingConvention.Cdecl)]
        internal static extern void SDL_CloseGamepad(IntPtr gamepad);

        [DllImport("SDL3", CallingConvention = CallingConvention.Cdecl)]
        internal static extern short SDL_GetGamepadAxis(IntPtr gamepad, int axis);

        [DllImport("SDL3", CallingConvention = CallingConvention.Cdecl)]
        internal static extern ushort SDL_GetJoystickVendorForID(uint instanceId);

        [DllImport("SDL3", CallingConvention = CallingConvention.Cdecl)]
        internal static extern ushort SDL_GetJoystickProductForID(uint instanceId);
    }

    private static class NativeMethods
    {
        [DllImport("user32.dll")]
        internal static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        internal static extern uint GetWindowThreadProcessId(
            IntPtr window,
            out uint processId);

        [DllImport("user32.dll")]
        internal static extern void mouse_event(
            uint flags,
            uint dx,
            uint dy,
            uint data,
            UIntPtr extraInfo);

        [DllImport("user32.dll")]
        internal static extern int ShowCursor([MarshalAs(UnmanagedType.Bool)] bool show);
    }
}
