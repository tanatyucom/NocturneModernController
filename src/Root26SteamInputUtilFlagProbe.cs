using System;
using System.Runtime.InteropServices;
using Il2Cpp;
using MelonLoader;

namespace NocturneModernController
{
    // Root-26 Chapter 59.4 follow-up: read-only runtime observation of the
    // single byte field at (SteamInputUtil instance native pointer) + 0x20,
    // which chapter 59's zero-base Ghidra re-derivation of
    // SteamInputUtil.SetAnalog() found gates whether the "index" argument
    // passed in from SteamPad.SteamPadSet(int index) is used unmodified as
    // AnalogStickLRval's first-dimension subscript, or forced to 0
    // regardless of index (see docs/research/
    // RIGHT_STICK_VIEW_AND_DASH_INVESTIGATION.md chapter 59.2/59.4). This
    // probe does NOT guess or assert what the field means or is named - it
    // only reports its raw byte value over time, correlated against the
    // existing DEAD/LIVE observation probes (Root26Phase2, Root26NativePoll)
    // via the same DateTimeOffset.Now:O timestamp format.
    //
    // Purely observational: Marshal.ReadByte only, never writes to this or
    // any other memory. Never calls ResetController()/
    // SteamControllerReStart()/Shutdown()/Init()/
    // UpdateConnectedControllers()/any other state-mutating Steam Input API.
    // Runs unconditionally from ModMain.OnUpdate() (NOT gated to
    // FieldDashPatch.IsExplorationActive), so it keeps observing across the
    // Guide/Overlay window and any DEAD/LIVE transition.
    internal static class Root26SteamInputUtilFlagProbe
    {
        private const int Offset = 0x20;
        private const string Tag = "[NocturneModernController][Root26SteamInputUtilFlag]";
        private const long HeartbeatIntervalMs = 5000;

        private static bool _lastByteKnown;
        private static byte _lastByte;
        private static long _lastHeartbeatTickMs = -1;
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

            try
            {
                IntPtr utilPtr = util.Pointer;
                if (utilPtr == IntPtr.Zero)
                {
                    return;
                }

                byte rawByte = Marshal.ReadByte(utilPtr, Offset);

                if (!_lastByteKnown)
                {
                    _lastByteKnown = true;
                    _lastByte = rawByte;
                    MelonLogger.Msg($"{Tag} STATE-CHANGE byte@+0x20 INITIAL -> {rawByte} at {DateTimeOffset.Now:O}");
                }
                else if (rawByte != _lastByte)
                {
                    MelonLogger.Msg($"{Tag} STATE-CHANGE byte@+0x20 {_lastByte} -> {rawByte} at {DateTimeOffset.Now:O}");
                    _lastByte = rawByte;
                }

                long now = Environment.TickCount64;
                if (_lastHeartbeatTickMs < 0 || now - _lastHeartbeatTickMs >= HeartbeatIntervalMs)
                {
                    _lastHeartbeatTickMs = now;
                    MelonLogger.Msg($"{Tag} HEARTBEAT byte@+0x20={rawByte} at {DateTimeOffset.Now:O}");
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
