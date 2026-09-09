using System;
using System.Collections.Generic;
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
    private static string LogPath = Path.Combine(
        Path.GetTempPath(),
        "NocturneModernController.InputHelper.log");

    // SDL3 gamepad axis indices. EMPIRICALLY confirmed (not assumed) against
    // the shipped SDL3.dll (3.4.14) via SDL_GetGamepadStringForAxis(n):
    // 0=leftx, 1=lefty, 2=rightx, 3=righty, 4=lefttrigger, 5=righttrigger.
    private const int AxisLeftX = 0;
    private const int AxisLeftY = 1;
    private const int AxisRightX = 2;
    private const int AxisRightY = 3;

    // SDL3 gamepad buttons monitored for minimal support logging (button
    // press/release). EMPIRICALLY confirmed against the shipped SDL3.dll via
    // SDL_GetGamepadStringForButton(n): 0=a,1=b,2=x,3=y,4=back,5=guide,
    // 6=start,7=leftstick,8=rightstick,9=leftshoulder,10=rightshoulder,
    // 11=dpup,12=dpdown,13=dpleft,14=dpright. Start/Back/Guide are
    // intentionally excluded - pressing them can change the game's screen.
    private static readonly (int Code, string Name)[] MonitoredButtons =
    {
        (0, "A"), (1, "B"), (2, "X"), (3, "Y"),
        (9, "LB"), (10, "RB"),
        (11, "DpadUp"), (12, "DpadDown"), (13, "DpadLeft"), (14, "DpadRight"),
        (7, "LeftStickClick"), (8, "RightStickClick")
    };

    private const int DiagEngageThreshold = 10000;
    private const int DiagReleaseThreshold = 6000;

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
        bool cursorHidden = false;
        int trackedGamePid = 0;
        bool lastForegroundState = false;
        bool lastCameraContextActiveState = false;
        bool diagRightXEngaged = false;
        bool diagRightYEngaged = false;
        bool diagLeftXEngaged = false;
        bool diagLeftYEngaged = false;
        var buttonPressedState = new Dictionary<string, bool>();
        foreach ((int _, string name) in MonitoredButtons)
        {
            buttonPressedState[name] = false;
        }

        try
        {
            while (ParentIsAlive(parentPid) && view.ReadInt32(24) != StopRequested)
            {
                SdlNative.SDL_UpdateJoysticks();

                // DIAGNOSTIC ONLY: detect the device going away mid-session so
                // disconnect/reconnect timing shows up in the log instead of
                // silently freezing on a stale handle.
                if (gamepad != IntPtr.Zero && !SdlNative.SDL_GamepadConnected(gamepad))
                {
                    Log("DEVICE DISCONNECTED");
                    SdlNative.SDL_CloseGamepad(gamepad);
                    gamepad = IntPtr.Zero;
                    diagRightXEngaged = false;
                    diagRightYEngaged = false;
                    diagLeftXEngaged = false;
                    diagLeftYEngaged = false;
                    retry = 0;
                }

                if (gamepad == IntPtr.Zero && retry-- <= 0)
                {
                    gamepad = TryOpenValidatedGamepad(out int deviceCount);
                    if (gamepad == IntPtr.Zero)
                    {
                        Log("TARGET WAIT");
                    }
                    else
                    {
                        short openX = SdlNative.SDL_GetGamepadAxis(gamepad, AxisRightX);
                        short openY = SdlNative.SDL_GetGamepadAxis(gamepad, AxisRightY);
                        Log($"TARGET OPEN initialAxis x={openX} y={openY}");

                        // Basic device identity, kept for support/troubleshooting.
                        uint selectedInstanceId = SdlNative.SDL_GetGamepadID(gamepad);
                        string selectedName = SdlNative.SDL_GetGamepadName(gamepad) ?? "UNAVAILABLE";
                        string selectedPath = SdlNative.SDL_GetGamepadPath(gamepad) ?? "UNAVAILABLE";
                        ushort selectedVid = SdlNative.SDL_GetGamepadVendor(gamepad);
                        ushort selectedPid = SdlNative.SDL_GetGamepadProduct(gamepad);
                        int selectedType = SdlNative.SDL_GetGamepadType(gamepad);
                        int connectionState = SdlNative.SDL_GetGamepadConnectionState(gamepad);
                        ushort productVersion = SdlNative.SDL_GetGamepadProductVersion(gamepad);
                        string serial = SdlNative.SDL_GetGamepadSerial(gamepad) ?? "UNAVAILABLE";
                        int playerIndex = SdlNative.SDL_GetGamepadPlayerIndex(gamepad);

                        Log($"DEVICE SELECTED instanceId={selectedInstanceId} name=\"{selectedName}\" " +
                            $"path=\"{selectedPath}\" vid=0x{selectedVid:X4} pid=0x{selectedPid:X4} " +
                            $"type={selectedType} deviceCount={deviceCount} connectionState={connectionState} " +
                            $"productVersion={productVersion} serial=\"{serial}\" playerIndex={playerIndex}");
                    }
                    retry = 120;
                }

                short x = gamepad == IntPtr.Zero
                    ? (short)0
                    : SdlNative.SDL_GetGamepadAxis(gamepad, AxisRightX);
                short y = gamepad == IntPtr.Zero
                    ? (short)0
                    : SdlNative.SDL_GetGamepadAxis(gamepad, AxisRightY);

                int gamePid = view.ReadInt32(32);
                if (gamePid > 0 && trackedGamePid == 0)
                {
                    Log("GAME PID DETECTED pid=" + gamePid);
                }

                if (gamePid > 0)
                {
                    trackedGamePid = gamePid;
                }
                if (trackedGamePid > 0 && !ParentIsAlive(trackedGamePid))
                {
                    Log("GAME PROCESS EXIT detected; helper stopping. pid=" + trackedGamePid);
                    break;
                }

                bool gameForegroundNow = gamePid > 0 && IsForegroundProcess(gamePid);
                if (gameForegroundNow != lastForegroundState)
                {
                    Log(gameForegroundNow ? "GAME FOREGROUND" : "GAME BACKGROUND");
                    lastForegroundState = gameForegroundNow;
                }

                bool cameraContextActive = view.ReadInt32(28) != 0 &&
                    gamePid > 0 &&
                    IsForegroundProcess(gamePid);
                SetCursorHidden(cameraContextActive, ref cursorHidden);

                if (cameraContextActive != lastCameraContextActiveState)
                {
                    Log(cameraContextActive ? "CAMERA CONTEXT ACTIVE" : "CAMERA CONTEXT INACTIVE");
                    lastCameraContextActiveState = cameraContextActive;
                }

                // Log only on deadzone-crossing/press-release transitions
                // (not every frame) so a long play session does not produce
                // excessive log volume.
                if (gamepad != IntPtr.Zero)
                {
                    bool rightXEngagedNow = diagRightXEngaged
                        ? Math.Abs((int)x) > DiagReleaseThreshold
                        : Math.Abs((int)x) >= DiagEngageThreshold;
                    if (rightXEngagedNow != diagRightXEngaged)
                    {
                        Log(rightXEngagedNow ? $"AXIS X ENGAGED value={x}" : $"AXIS X NEUTRAL value={x}");
                        diagRightXEngaged = rightXEngagedNow;
                    }

                    bool rightYEngagedNow = diagRightYEngaged
                        ? Math.Abs((int)y) > DiagReleaseThreshold
                        : Math.Abs((int)y) >= DiagEngageThreshold;
                    if (rightYEngagedNow != diagRightYEngaged)
                    {
                        Log(rightYEngagedNow ? $"AXIS Y ENGAGED value={y}" : $"AXIS Y NEUTRAL value={y}");
                        diagRightYEngaged = rightYEngagedNow;
                    }

                    short lx = SdlNative.SDL_GetGamepadAxis(gamepad, AxisLeftX);
                    short ly = SdlNative.SDL_GetGamepadAxis(gamepad, AxisLeftY);

                    bool leftXEngagedNow = diagLeftXEngaged
                        ? Math.Abs((int)lx) > DiagReleaseThreshold
                        : Math.Abs((int)lx) >= DiagEngageThreshold;
                    if (leftXEngagedNow != diagLeftXEngaged)
                    {
                        Log(leftXEngagedNow ? $"LEFTSTICK X ENGAGED value={lx}" : $"LEFTSTICK X NEUTRAL value={lx}");
                        diagLeftXEngaged = leftXEngagedNow;
                    }

                    bool leftYEngagedNow = diagLeftYEngaged
                        ? Math.Abs((int)ly) > DiagReleaseThreshold
                        : Math.Abs((int)ly) >= DiagEngageThreshold;
                    if (leftYEngagedNow != diagLeftYEngaged)
                    {
                        Log(leftYEngagedNow ? $"LEFTSTICK Y ENGAGED value={ly}" : $"LEFTSTICK Y NEUTRAL value={ly}");
                        diagLeftYEngaged = leftYEngagedNow;
                    }

                    foreach ((int code, string name) in MonitoredButtons)
                    {
                        bool pressedNow = SdlNative.SDL_GetGamepadButton(gamepad, code);
                        if (pressedNow != buttonPressedState[name])
                        {
                            Log(pressedNow ? $"BUTTON {name} PRESSED" : $"BUTTON {name} RELEASED");
                            buttonPressedState[name] = pressedNow;
                        }
                    }
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

            // SDL_Quit is intentionally omitted: it can block indefinitely
            // with reWASD and Steam Input active.
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

    private static IntPtr TryOpenValidatedGamepad(out int deviceCount)
    {
        IntPtr ids = SdlNative.SDL_GetJoysticks(out int count);
        deviceCount = count;
        Log("ENUMERATION deviceCount=" + count); // DIAGNOSTIC ONLY
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
                // DIAGNOSTIC ONLY: name/path/type help identify which SDL
                // backend (XInput/RawInput/HIDAPI) actually exposed this device.
                string candidateName = SdlNative.SDL_GetGamepadNameForID(id) ?? "UNAVAILABLE";
                string candidatePath = SdlNative.SDL_GetGamepadPathForID(id) ?? "UNAVAILABLE";
                int candidateType = SdlNative.SDL_GetGamepadTypeForID(id);
                Log($"CANDIDATE index={index} id={id} name=\"{candidateName}\" path=\"{candidatePath}\" type={candidateType} vid=0x{vendor:X4} pid=0x{product:X4} gamepad={SdlNative.SDL_IsGamepad(id)}");
                if (!SdlNative.SDL_IsGamepad(id))
                {
                    continue;
                }

                IntPtr candidate = SdlNative.SDL_OpenGamepad(id);
                if (candidate == IntPtr.Zero)
                {
                    continue;
                }

                short x = SdlNative.SDL_GetGamepadAxis(candidate, AxisRightX);
                short y = SdlNative.SDL_GetGamepadAxis(candidate, AxisRightY);
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

        [DllImport("SDL3", CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.LPUTF8Str)]
        internal static extern string? SDL_GetGamepadNameForID(uint instanceId);

        [DllImport("SDL3", CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.LPUTF8Str)]
        internal static extern string? SDL_GetGamepadPathForID(uint instanceId);

        [DllImport("SDL3", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int SDL_GetGamepadTypeForID(uint instanceId);

        [DllImport("SDL3", CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        internal static extern bool SDL_GamepadConnected(IntPtr gamepad);

        // Button index-to-name mapping verified empirically via
        // SDL_GetGamepadStringForButton against the shipped SDL3.dll
        // (3.4.14) (see MonitoredButtons comment above).
        [DllImport("SDL3", CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        internal static extern bool SDL_GetGamepadButton(IntPtr gamepad, int button);

        [DllImport("SDL3", CallingConvention = CallingConvention.Cdecl)]
        internal static extern uint SDL_GetGamepadID(IntPtr gamepad);

        [DllImport("SDL3", CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.LPUTF8Str)]
        internal static extern string? SDL_GetGamepadName(IntPtr gamepad);

        [DllImport("SDL3", CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.LPUTF8Str)]
        internal static extern string? SDL_GetGamepadPath(IntPtr gamepad);

        [DllImport("SDL3", CallingConvention = CallingConvention.Cdecl)]
        internal static extern ushort SDL_GetGamepadVendor(IntPtr gamepad);

        [DllImport("SDL3", CallingConvention = CallingConvention.Cdecl)]
        internal static extern ushort SDL_GetGamepadProduct(IntPtr gamepad);

        [DllImport("SDL3", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int SDL_GetGamepadType(IntPtr gamepad);

        [DllImport("SDL3", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int SDL_GetGamepadConnectionState(IntPtr gamepad);

        [DllImport("SDL3", CallingConvention = CallingConvention.Cdecl)]
        internal static extern ushort SDL_GetGamepadProductVersion(IntPtr gamepad);

        [DllImport("SDL3", CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.LPUTF8Str)]
        internal static extern string? SDL_GetGamepadSerial(IntPtr gamepad);

        [DllImport("SDL3", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int SDL_GetGamepadPlayerIndex(IntPtr gamepad);
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
        internal static extern int ShowCursor([MarshalAs(UnmanagedType.Bool)] bool show);
    }
}
