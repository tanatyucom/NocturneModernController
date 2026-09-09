using System;
using System.Runtime.InteropServices;
using Il2Cpp;
using MelonLoader;

namespace NocturneModernController
{
    // Root-26 diagnostic-only PoC: read-only observation of the raw native
    // memory at SteamPad-instance + 0x18 ("condition B" in
    // docs/research/RIGHT_STICK_VIEW_AND_DASH_INVESTIGATION.md section 20/21
    // - the field UpdateControl()/UpdateConnectedControllers() both gate on).
    // Static analysis (Cpp2IL ISIL) could not resolve who writes this field
    // at runtime, nor confirm it changes across the DEAD/LIVE boundary - this
    // probe answers that empirically, without any patching or writing.
    //
    // Purely observational: this reads raw bytes via Marshal.ReadByte /
    // Marshal.ReadInt64 at a fixed native offset from the SteamPad object's
    // Il2CppObjectBase.Pointer. It NEVER writes to that memory, never calls
    // ResetController()/SteamControllerReStart()/UpdateConnectedControllers(),
    // and never touches any other controller state. Runs unconditionally
    // from ModMain.OnUpdate() (NOT gated to
    // FieldDashPatch.IsExplorationActive), so it keeps observing during the
    // Guide/Overlay window where RightStickPollingProbe itself is not
    // sampling. Logs use the same DateTimeOffset.Now:O timestamp format as
    // RightStickPollingProbe's STATE-TRANSITION lines for direct
    // correlation. No focus/WM_INPUT/Method D instrumentation is added here.
    internal static class Root26SteamPadOffset18Probe
    {
        private const int Offset = 0x18;
        private const string Tag = "[NocturneModernController][Root26SteamPadOffset18]";

        private static bool _lastByteKnown;
        private static byte _lastByte;
        private static bool _lastInt64Known;
        private static long _lastInt64;
        private static bool _errorLogged;

        internal static void Sample()
        {
            SteamInputUtil util;
            try
            {
                util = SteamInputUtil.instance;
            }
            catch
            {
                return;
            }
            if (util == null)
            {
                return;
            }

            SteamPad pad;
            try
            {
                pad = util.steam_pad;
            }
            catch
            {
                return;
            }
            if (pad == null)
            {
                return;
            }

            try
            {
                IntPtr nativePtr = pad.Pointer;
                if (nativePtr == IntPtr.Zero)
                {
                    return;
                }

                byte rawByte = Marshal.ReadByte(nativePtr, Offset);
                long rawInt64 = Marshal.ReadInt64(nativePtr, Offset);

                if (!_lastByteKnown)
                {
                    _lastByteKnown = true;
                    _lastByte = rawByte;
                    MelonLogger.Msg($"{Tag} STATE-CHANGE byte@+0x18 INITIAL -> {rawByte} at {DateTimeOffset.Now:O}");
                }
                else if (rawByte != _lastByte)
                {
                    MelonLogger.Msg($"{Tag} STATE-CHANGE byte@+0x18 {_lastByte} -> {rawByte} at {DateTimeOffset.Now:O}");
                    _lastByte = rawByte;
                }

                if (!_lastInt64Known)
                {
                    _lastInt64Known = true;
                    _lastInt64 = rawInt64;
                    MelonLogger.Msg($"{Tag} STATE-CHANGE int64@+0x18 INITIAL -> {rawInt64} at {DateTimeOffset.Now:O}");
                }
                else if (rawInt64 != _lastInt64)
                {
                    MelonLogger.Msg($"{Tag} STATE-CHANGE int64@+0x18 {_lastInt64} -> {rawInt64} at {DateTimeOffset.Now:O}");
                    _lastInt64 = rawInt64;
                }
            }
            catch (Exception ex)
            {
                if (!_errorLogged)
                {
                    _errorLogged = true;
                    MelonLogger.Warning($"{Tag} read threw {ex.GetType().FullName}: {ex.Message}");
                }
            }
        }
    }
}
