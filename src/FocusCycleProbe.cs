using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using MelonLoader;

namespace NocturneModernController
{
    // Root-26 diagnostic-only PoC.
    //
    // Two independent one-shot mechanisms live in this file:
    //
    // 1. RunWinEscProbe() - the "Win-Esc, no programmatic return" synthetic
    //    input experiment (VK_LWIN down+up, wait, VK_ESCAPE down+up via
    //    SendInput). Currently DISABLED (ProbeEnabled = false) so it cannot
    //    contaminate the passive observation experiment below. Left in
    //    place, unmodified otherwise, for later re-enabling if needed.
    //
    // 2. RunForegroundMonitor() - "Physical Win -> Physical Esc passive
    //    observation". Purely read-only: it never calls SendInput,
    //    SetForegroundWindow, AttachThreadInput, or GetShellWindow, and
    //    never changes any window state. It only polls GetForegroundWindow()
    //    once per OnUpdate tick (called from ModMain.OnUpdate() via
    //    FocusCycleProbe.Sample(), i.e. every frame), starting the moment
    //    FieldDashPatch.IsExplorationActive first becomes true and
    //    continuing for as long as exploration stays active (the whole
    //    exploration "session", not a fixed duration), and logs a line only
    //    when the foreground window actually changes from the last logged
    //    value - never once per frame. This lets a user press the physical
    //    Windows key, wait, then press physical Escape at any point during
    //    the session, with every real foreground transition captured with a
    //    timestamp, entirely independent of and without touching the
    //    Broker/Launcher/Helper/SDL bridge or the Startup shortcut feature.
    //    A new session re-arms the next time exploration becomes active.
    internal static class FocusCycleProbe
    {
        private const int InputKeyboard = 1;
        private const uint KeyEventFKeyUp = 0x0002;
        private const ushort VkLeftWin = 0x5B;
        private const ushort VkEscape = 0x1B;
        // 2600ms (Root-26 "Synthetic Win->Esc / 2600ms dwell" experiment):
        // deliberately matches the ~2.6s SearchHost dwell time measured in
        // the Physical Win->Esc PASS run, to test the dwell-time hypothesis
        // without relying on a user's manual timing. PostEscape delay is
        // left at its original 150ms per the approved experiment scope.
        private const int ShellActivationSettleDelayMilliseconds = 2600;
        private const int PostEscapeSettleDelayMilliseconds = 150;

        // Disabled again for the "Window Message Observer - Physical PASS
        // baseline" experiment (Root-26): this run observes only the
        // physical Win->Esc path, so the synthetic probe must not fire and
        // contend for the same window/activation sequence. A
        // `static readonly` (not `const`) field so the compiler does not
        // flag RunWinEscProbe() as unreachable.
        private static readonly bool ProbeEnabled = false;

        private static bool _fired;

        // --- Passive foreground monitor state (independent of ProbeEnabled) ---
        private static bool _monitorArmed;
        private static bool _monitorActive;
        private static IntPtr _lastLoggedForeground;

        // --- Window Message Observer state (independent of ProbeEnabled) ---
        // Observes only WM_ACTIVATE/WM_ACTIVATEAPP/WM_SETFOCUS/WM_KILLFOCUS/
        // WM_NCACTIVATE actually delivered to smt3hd's own UnityWndClass
        // window, via a temporary same-process window subclass
        // (SetWindowLongPtr(GWLP_WNDPROC)). Every message is logged only,
        // then unconditionally forwarded to the original WndProc via
        // CallWindowProc - never consumed or modified. No synthetic input,
        // no foreground manipulation, no SetForegroundWindow/
        // AttachThreadInput/GetShellWindow of any kind.
        private const uint WmActivate = 0x0006;
        private const uint WmActivateApp = 0x001C;
        private const uint WmSetFocus = 0x0007;
        private const uint WmKillFocus = 0x0008;
        private const uint WmNcActivate = 0x0086;
        private const int GwlpWndProc = -4;

        // WM_INPUT (Root-26 follow-up): message-arrival only, no
        // GetRawInputData/HID/XInput decoding yet. To avoid flooding the log
        // (WM_INPUT can fire continuously while the window has focus), it is
        // only logged for WmInputPostReactivationWindowMilliseconds after the
        // game window last regained activation (WM_ACTIVATE with a nonzero
        // low word), and detailed per-message lines are capped at
        // MaxWmInputLogEntriesPerReactivation; the running/final count is
        // tracked regardless of the detail cap and reported in a summary
        // line once the window elapses.
        private const uint WmInput = 0x00FF;
        private const int WmInputPostReactivationWindowMilliseconds = 3000;
        private const int MaxWmInputLogEntriesPerReactivation = 20;
        private static int _lastReactivationTick;

        // Minimal read-only accessor for RightStickPollingProbe (Root-26):
        // lets that independent diagnostic share this exact WM_ACTIVATE-based
        // reactivation timestamp, so native-polling data and window-message
        // data use the same time reference. 0 means no reactivation observed
        // yet this session. Never set from outside this file.
        internal static int LastReactivationTick => _lastReactivationTick;
        private static int _wmInputCountSinceReactivation;
        private static int _wmInputLoggedSinceReactivation;
        private static bool _wmInputSummaryEmitted;

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate IntPtr WndProcDelegate(IntPtr windowHandle, uint msg, IntPtr wParam, IntPtr lParam);

        // Kept alive as a static field for the observer's whole lifetime so
        // the GC never collects the delegate while native code still holds
        // its function pointer.
        private static WndProcDelegate? _observerWndProcDelegate;

        private static bool _observerArmed;
        private static bool _observerActive;
        private static IntPtr _observedWindow;
        private static IntPtr _observerOriginalWndProc;
        private static IntPtr _observerReplacementWndProcPtr;

        internal static void Shutdown()
        {
            if (_monitorActive)
            {
                MelonLogger.Msg("[NocturneModernController][Root26FocusProbe] MONITOR-END (mod shutdown / game exit).");
            }
            _monitorActive = false;
            _monitorArmed = false;
            RestoreWindowMessageObserver("mod shutdown / game exit");
            _observerArmed = false;
        }

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
        private static extern IntPtr SetWindowLongPtr(IntPtr windowHandle, int index, IntPtr newValue);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
        private static extern IntPtr GetWindowLongPtr(IntPtr windowHandle, int index);

        [DllImport("user32.dll", EntryPoint = "CallWindowProcW")]
        private static extern IntPtr CallWindowProc(IntPtr previousWndProc, IntPtr windowHandle, uint msg, IntPtr wParam, IntPtr lParam);

        // Native x64 layout: sizeof(INPUT) == 40 bytes (confirmed via
        // Marshal.SizeOf<Input>() logging in the earlier SendInput ABI fix).
        // MOUSEINPUT/HARDWAREINPUT are never populated - they exist purely
        // so the union's size/alignment match the native ABI. Only used by
        // RunWinEscProbe(), which is currently disabled.
        [StructLayout(LayoutKind.Sequential)]
        private struct Input
        {
            public int Type;
            public InputUnion Union;
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct InputUnion
        {
            [FieldOffset(0)]
            public MouseInput Mouse;
            [FieldOffset(0)]
            public KeyboardInput Keyboard;
            [FieldOffset(0)]
            public HardwareInput Hardware;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MouseInput
        {
            public int Dx;
            public int Dy;
            public uint MouseData;
            public uint Flags;
            public uint Time;
            public IntPtr ExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct KeyboardInput
        {
            public ushort VirtualKey;
            public ushort ScanCode;
            public uint Flags;
            public uint Time;
            public IntPtr ExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct HardwareInput
        {
            public uint Msg;
            public ushort ParamL;
            public ushort ParamH;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint numberOfInputs, Input[] inputs, int inputSize);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr windowHandle, out uint processId);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowText(IntPtr windowHandle, StringBuilder text, int maxCount);

        // Read-only WINDOW-AUDIT diagnostic (kept from the D1-out
        // investigation; observational only, used by RunWinEscProbe() which
        // is currently disabled).
        private delegate bool EnumWindowsProc(IntPtr windowHandle, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc enumProc, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetClassName(IntPtr windowHandle, StringBuilder className, int maxCount);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr windowHandle);

        [DllImport("user32.dll")]
        private static extern bool IsWindowEnabled(IntPtr windowHandle);

        [DllImport("user32.dll")]
        private static extern IntPtr GetWindow(IntPtr windowHandle, uint command);

        private const uint GwOwner = 4;

        internal static void Sample()
        {
            if (ProbeEnabled)
            {
                RunWinEscProbe();
            }

            RunForegroundMonitor();
            RunWindowMessageObserver();
        }

        // Purely observational, read-only. Arms itself once, the first time
        // FieldDashPatch.IsExplorationActive is true, subclasses smt3hd's own
        // UnityWndClass foreground window to log activation-family messages
        // it actually receives, and restores the original WndProc once
        // exploration ends (or sooner on install failure). A new session
        // re-subclasses the next time exploration becomes active. Never
        // calls SetForegroundWindow, AttachThreadInput, GetShellWindow, or
        // SendInput, and never consumes/modifies any message.
        private static void RunWindowMessageObserver()
        {
            if (!_observerArmed)
            {
                if (!FieldDashPatch.IsExplorationActive)
                {
                    return;
                }

                _observerArmed = true;

                IntPtr candidate = GetForegroundWindow();
                uint threadId = GetWindowThreadProcessId(candidate, out uint processId);
                var classBuffer = new StringBuilder(256);
                GetClassName(candidate, classBuffer, classBuffer.Capacity);
                string className = classBuffer.ToString();

                MelonLogger.Msg(
                    $"[NocturneModernController][Root26FocusProbe] WNDPROC-OBSERVER candidate hWnd=0x{candidate.ToInt64():X} " +
                    $"class=\"{className}\" pid={processId} threadId={threadId}");

                if (!string.Equals(className, "UnityWndClass", StringComparison.Ordinal))
                {
                    MelonLogger.Warning(
                        "[NocturneModernController][Root26FocusProbe] WNDPROC-OBSERVER aborted: " +
                        "foreground window is not UnityWndClass.");
                    return;
                }

                try
                {
                    _observerWndProcDelegate = ObserverWndProc;
                    _observerReplacementWndProcPtr = Marshal.GetFunctionPointerForDelegate(_observerWndProcDelegate);
                    _observedWindow = candidate;
                    _observerOriginalWndProc = SetWindowLongPtr(_observedWindow, GwlpWndProc, _observerReplacementWndProcPtr);
                    int error = _observerOriginalWndProc == IntPtr.Zero ? Marshal.GetLastWin32Error() : 0;
                    _observerActive = _observerOriginalWndProc != IntPtr.Zero;
                    MelonLogger.Msg(
                        $"[NocturneModernController][Root26FocusProbe] WNDPROC-OBSERVER install hWnd=0x{_observedWindow.ToInt64():X} " +
                        $"originalWndProc=0x{_observerOriginalWndProc.ToInt64():X} success={_observerActive} error={error}");
                }
                catch (Exception ex)
                {
                    MelonLogger.Warning(
                        "[NocturneModernController][Root26FocusProbe] WNDPROC-OBSERVER install failed: " +
                        ex.GetType().FullName + ": " + ex.Message);
                    _observerActive = false;
                }
                return;
            }

            if (!_observerActive)
            {
                return;
            }

            if (_lastReactivationTick != 0 && !_wmInputSummaryEmitted &&
                unchecked(Environment.TickCount - _lastReactivationTick) > WmInputPostReactivationWindowMilliseconds)
            {
                _wmInputSummaryEmitted = true;
                MelonLogger.Msg(
                    $"[NocturneModernController][Root26FocusProbe] WM_INPUT summary: total={_wmInputCountSinceReactivation} " +
                    $"logged={_wmInputLoggedSinceReactivation} within {WmInputPostReactivationWindowMilliseconds}ms after reactivation.");
            }

            if (!FieldDashPatch.IsExplorationActive)
            {
                RestoreWindowMessageObserver("exploration ended");
                _observerArmed = false; // allow re-arming on the next exploration session
            }
        }

        private static void RestoreWindowMessageObserver(string reason)
        {
            if (!_observerActive)
            {
                return;
            }
            _observerActive = false;

            try
            {
                IntPtr current = GetWindowLongPtr(_observedWindow, GwlpWndProc);
                if (current == _observerReplacementWndProcPtr)
                {
                    IntPtr previous = SetWindowLongPtr(_observedWindow, GwlpWndProc, _observerOriginalWndProc);
                    MelonLogger.Msg(
                        $"[NocturneModernController][Root26FocusProbe] WNDPROC-OBSERVER restore ({reason}) " +
                        $"previous=0x{previous.ToInt64():X} restoredTo=0x{_observerOriginalWndProc.ToInt64():X} success=True");
                }
                else
                {
                    MelonLogger.Warning(
                        $"[NocturneModernController][Root26FocusProbe] WNDPROC-OBSERVER restore skipped ({reason}): " +
                        $"current WndProc (0x{current.ToInt64():X}) no longer matches our replacement " +
                        $"(0x{_observerReplacementWndProcPtr.ToInt64():X}); not overwriting.");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning(
                    "[NocturneModernController][Root26FocusProbe] WNDPROC-OBSERVER restore failed: " +
                    ex.GetType().FullName + ": " + ex.Message);
            }
        }

        // The temporary replacement WndProc. Logs only the requested
        // activation-family messages, then unconditionally forwards every
        // message (including those) to the original WndProc. Never returns
        // early / never suppresses anything.
        private static IntPtr ObserverWndProc(IntPtr windowHandle, uint msg, IntPtr wParam, IntPtr lParam)
        {
            try
            {
                if (msg == WmActivate || msg == WmActivateApp || msg == WmSetFocus ||
                    msg == WmKillFocus || msg == WmNcActivate)
                {
                    LogWindowMessage(windowHandle, msg, wParam, lParam);

                    if (msg == WmActivate)
                    {
                        int low = (int)((long)wParam & 0xFFFF);
                        if (low != 0) // WA_ACTIVE or WA_CLICKACTIVE: game regained activation
                        {
                            ArmWmInputObservationWindow();
                        }
                    }
                }
                else if (msg == WmInput)
                {
                    HandleWmInput(windowHandle, wParam, lParam);
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning(
                    "[NocturneModernController][Root26FocusProbe] WNDPROC-OBSERVER handler failed: " +
                    ex.GetType().FullName + ": " + ex.Message);
            }

            return CallWindowProc(_observerOriginalWndProc, windowHandle, msg, wParam, lParam);
        }

        private static void ArmWmInputObservationWindow()
        {
            _lastReactivationTick = Environment.TickCount;
            _wmInputCountSinceReactivation = 0;
            _wmInputLoggedSinceReactivation = 0;
            _wmInputSummaryEmitted = false;
            MelonLogger.Msg(
                $"[NocturneModernController][Root26FocusProbe] WM_INPUT observation window (re)armed for " +
                $"{WmInputPostReactivationWindowMilliseconds}ms after reactivation (max {MaxWmInputLogEntriesPerReactivation} detailed entries).");
        }

        // Message-arrival only: no GetRawInputData/HID/XInput decoding.
        // Bounded to WmInputPostReactivationWindowMilliseconds after the
        // most recent reactivation, with detailed logging capped at
        // MaxWmInputLogEntriesPerReactivation (the running count is still
        // tracked past the cap and reported via the summary line).
        private static void HandleWmInput(IntPtr windowHandle, IntPtr wParam, IntPtr lParam)
        {
            if (_lastReactivationTick == 0)
            {
                return;
            }

            int elapsed = unchecked(Environment.TickCount - _lastReactivationTick);
            if (elapsed < 0 || elapsed > WmInputPostReactivationWindowMilliseconds)
            {
                return;
            }

            _wmInputCountSinceReactivation++;
            if (_wmInputLoggedSinceReactivation >= MaxWmInputLogEntriesPerReactivation)
            {
                return;
            }
            _wmInputLoggedSinceReactivation++;

            MelonLogger.Msg(
                $"[NocturneModernController][Root26FocusProbe] WNDPROC-MSG WM_INPUT(0x00FF) hWnd=0x{windowHandle.ToInt64():X} " +
                $"wParam=0x{wParam.ToInt64():X} lParam=0x{lParam.ToInt64():X} countSinceReactivation={_wmInputCountSinceReactivation} " +
                $"at {DateTimeOffset.Now:O}");
        }

        private static void LogWindowMessage(IntPtr windowHandle, uint msg, IntPtr wParam, IntPtr lParam)
        {
            string name = MessageName(msg);
            string decoded = string.Empty;

            if (msg == WmActivate)
            {
                int low = (int)((long)wParam & 0xFFFF);
                string state = low switch
                {
                    0 => "WA_INACTIVE",
                    1 => "WA_ACTIVE",
                    2 => "WA_CLICKACTIVE",
                    _ => "UNKNOWN(" + low + ")"
                };
                decoded = $" decoded={state}";
            }
            else if (msg == WmActivateApp)
            {
                bool activating = wParam != IntPtr.Zero;
                decoded = $" decoded={(activating ? "ACTIVATING(TRUE)" : "DEACTIVATING(FALSE)")}";
            }

            MelonLogger.Msg(
                $"[NocturneModernController][Root26FocusProbe] WNDPROC-MSG {name}(0x{msg:X4}) hWnd=0x{windowHandle.ToInt64():X} " +
                $"wParam=0x{wParam.ToInt64():X} lParam=0x{lParam.ToInt64():X}{decoded} at {DateTimeOffset.Now:O}");
        }

        private static string MessageName(uint msg) => msg switch
        {
            WmActivate => "WM_ACTIVATE",
            WmActivateApp => "WM_ACTIVATEAPP",
            WmSetFocus => "WM_SETFOCUS",
            WmKillFocus => "WM_KILLFOCUS",
            WmNcActivate => "WM_NCACTIVATE",
            _ => "WM_" + msg.ToString("X4")
        };

        // Purely observational, read-only. Arms itself once, the first time
        // FieldDashPatch.IsExplorationActive is true, records a baseline,
        // then polls GetForegroundWindow() once per call (i.e. once per
        // OnUpdate tick) for as long as exploration stays active, logging
        // only on actual change. Ends (and re-arms for the next session)
        // once exploration ends. Never calls SetForegroundWindow,
        // AttachThreadInput, GetShellWindow, or SendInput.
        private static void RunForegroundMonitor()
        {
            if (!_monitorArmed)
            {
                if (!FieldDashPatch.IsExplorationActive)
                {
                    return;
                }

                _monitorArmed = true;
                _monitorActive = true;
                _lastLoggedForeground = GetForegroundWindow();
                MelonLogger.Msg(
                    "[NocturneModernController][Root26FocusProbe] MONITOR-START (passive, read-only, " +
                    "continues for the whole exploration session; exploration became active).");
                LogMonitorState("00-BASELINE", _lastLoggedForeground);
                return;
            }

            if (!_monitorActive)
            {
                return;
            }

            if (!FieldDashPatch.IsExplorationActive)
            {
                _monitorActive = false;
                _monitorArmed = false; // allow re-arming on the next exploration session
                MelonLogger.Msg("[NocturneModernController][Root26FocusProbe] MONITOR-END (exploration ended).");
                return;
            }

            IntPtr current = GetForegroundWindow();
            if (current != _lastLoggedForeground)
            {
                _lastLoggedForeground = current;
                LogMonitorState("FOREGROUND-CHANGE", current);
            }
        }

        private static void LogMonitorState(string label, IntPtr windowHandle)
        {
            uint threadId = GetWindowThreadProcessId(windowHandle, out uint processId);
            LogWindowState(label, windowHandle, threadId, processId);
        }

        // Disabled via ProbeEnabled = false. Left otherwise unmodified.
        private static void RunWinEscProbe()
        {
            if (_fired || !FieldDashPatch.IsExplorationActive)
            {
                return;
            }

            // Set before doing any work so a re-entrant OnUpdate call (or an
            // exception partway through) can never trigger a second cycle.
            _fired = true;

            try
            {
                MelonLogger.Msg(
                    "[NocturneModernController][Root26FocusProbe] Firing one-shot focus cycle, Win-Esc " +
                    "(no programmatic return): VK_LWIN down+up, wait, VK_ESCAPE down+up only; " +
                    "exploration became active.");

                IntPtr gameWindow = GetForegroundWindow();
                uint gameThreadId = GetWindowThreadProcessId(gameWindow, out uint gamePid);
                LogWindowState("00-BEFORE (assumed game window)", gameWindow, gameThreadId, gamePid);

                LogProcessWindowAudit(gameWindow);

                MelonLogger.Msg(
                    $"[NocturneModernController][Root26FocusProbe] Marshal.SizeOf<Input>()={Marshal.SizeOf<Input>()} (native x64 sizeof(INPUT) is 40).");

                (uint sent, int error) winDown = SendKey(VkLeftWin, down: true);
                MelonLogger.Msg(
                    $"[NocturneModernController][Root26FocusProbe] SendInput WIN down sent={winDown.sent} error={winDown.error}");

                (uint sent, int error) winUp = SendKey(VkLeftWin, down: false);
                MelonLogger.Msg(
                    $"[NocturneModernController][Root26FocusProbe] SendInput WIN up sent={winUp.sent} error={winUp.error}");

                LogForegroundNow("01-AFTER-WIN-SHELL-ACTIVATION-IMMEDIATE");

                Thread.Sleep(ShellActivationSettleDelayMilliseconds);
                LogForegroundNow("02-AFTER-WIN-SHELL-ACTIVATION-WAIT");

                (uint sent, int error) escDown = SendKey(VkEscape, down: true);
                MelonLogger.Msg(
                    $"[NocturneModernController][Root26FocusProbe] SendInput ESC down sent={escDown.sent} error={escDown.error}");

                (uint sent, int error) escUp = SendKey(VkEscape, down: false);
                MelonLogger.Msg(
                    $"[NocturneModernController][Root26FocusProbe] SendInput ESC up sent={escUp.sent} error={escUp.error}");

                LogForegroundNow("03-AFTER-ESC-IMMEDIATE");

                Thread.Sleep(PostEscapeSettleDelayMilliseconds);
                LogForegroundNow("04-AFTER-ESC-WAIT");
            }
            catch (Exception ex)
            {
                MelonLogger.Warning(
                    "[NocturneModernController][Root26FocusProbe] Probe failed: " +
                    ex.GetType().FullName + ": " + ex.Message);
            }
        }

        private static (uint sent, int error) SendKey(ushort virtualKey, bool down)
        {
            var input = new Input
            {
                Type = InputKeyboard,
                Union = new InputUnion
                {
                    Keyboard = new KeyboardInput
                    {
                        VirtualKey = virtualKey,
                        ScanCode = 0,
                        Flags = down ? 0u : KeyEventFKeyUp,
                        Time = 0,
                        ExtraInfo = IntPtr.Zero
                    }
                }
            };
            uint sent = SendInput(1, new[] { input }, Marshal.SizeOf<Input>());
            int error = sent == 0 ? Marshal.GetLastWin32Error() : 0;
            return (sent, error);
        }

        private static void LogForegroundNow(string label)
        {
            IntPtr windowHandle = GetForegroundWindow();
            uint threadId = GetWindowThreadProcessId(windowHandle, out uint processId);
            LogWindowState(label, windowHandle, threadId, processId);
        }

        // Read-only diagnostic: enumerates every top-level window owned by
        // this same process (smt3hd) and logs its static properties. Used
        // only by RunWinEscProbe() (currently disabled).
        private static void LogProcessWindowAudit(IntPtr currentForeground)
        {
            uint currentPid = (uint)Process.GetCurrentProcess().Id;
            MelonLogger.Msg($"[NocturneModernController][Root26FocusProbe] WINDOW-AUDIT begin (process pid={currentPid}).");

            EnumWindows((windowHandle, _) =>
            {
                try
                {
                    uint threadId = GetWindowThreadProcessId(windowHandle, out uint processId);
                    if (processId != currentPid)
                    {
                        return true; // not ours - keep enumerating
                    }

                    var classNameBuffer = new StringBuilder(256);
                    GetClassName(windowHandle, classNameBuffer, classNameBuffer.Capacity);
                    var titleBuffer = new StringBuilder(256);
                    GetWindowText(windowHandle, titleBuffer, titleBuffer.Capacity);
                    bool visible = IsWindowVisible(windowHandle);
                    bool enabled = IsWindowEnabled(windowHandle);
                    IntPtr owner = GetWindow(windowHandle, GwOwner);
                    bool isForeground = windowHandle == currentForeground;

                    MelonLogger.Msg(
                        $"[NocturneModernController][Root26FocusProbe] WINDOW-AUDIT hWnd=0x{windowHandle.ToInt64():X} " +
                        $"class=\"{classNameBuffer}\" title=\"{titleBuffer}\" visible={visible} enabled={enabled} " +
                        $"owner=0x{owner.ToInt64():X} threadId={threadId} isForeground={isForeground}");
                }
                catch (Exception ex)
                {
                    MelonLogger.Warning(
                        "[NocturneModernController][Root26FocusProbe] WINDOW-AUDIT entry failed: " +
                        ex.GetType().FullName + ": " + ex.Message);
                }
                return true; // keep enumerating
            }, IntPtr.Zero);

            MelonLogger.Msg("[NocturneModernController][Root26FocusProbe] WINDOW-AUDIT end.");
        }

        private static void LogWindowState(string label, IntPtr windowHandle, uint threadId, uint processId)
        {
            string title = string.Empty;
            string processName = string.Empty;
            try
            {
                var buffer = new StringBuilder(256);
                GetWindowText(windowHandle, buffer, buffer.Capacity);
                title = buffer.ToString();
                processName = Process.GetProcessById((int)processId).ProcessName;
            }
            catch (Exception)
            {
                // Best-effort diagnostics only; a failure here must not abort the probe.
            }

            MelonLogger.Msg(
                $"[NocturneModernController][Root26FocusProbe] {label} hWnd=0x{windowHandle.ToInt64():X} " +
                $"pid={processId} threadId={threadId} process=\"{processName}\" title=\"{title}\" at {DateTimeOffset.Now:O}");
        }
    }
}
