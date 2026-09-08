using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Il2Cpp;
using MelonLoader;

namespace NocturneModernController
{
    // Root-26 Chapter 56 follow-up: read-only runtime probe answering which
    // managed field (InputHandles / InputHandles_new / InputHandles_id)
    // corresponds to which native SteamPad-instance offset (+0x8, +0x20,
    // +0x30), and whether all three stay in sync (same content, same order)
    // across the DEAD/LIVE boundary. See docs/research/
    // RIGHT_STICK_VIEW_AND_DASH_INVESTIGATION.md chapters 54-56.
    //
    // Ghidra static analysis (this session) confirmed three distinct native
    // array-field reads along the controller-selection path:
    //   - SteamPad.SteamPadSet(int index) reads its handle from
    //     (SteamPad instance) + 0x20.
    //   - SteamInputUtil.UpdateInput() reads its loop-bound/pre-check array
    //     from (SteamPad instance) + 0x30.
    //   - The dds3PadUpdate() per-slot gate call (func_0x182141080) reads its
    //     array from (object at SteamInputUtil.instance-native+0xb8) + 0x8.
    // It is not yet known (a) whether "object at util-native+0xb8" is the
    // same native object as the SteamPad instance itself, nor (b) which of
    // InputHandles/InputHandles_new/InputHandles_id lives at which of these
    // three offsets, nor (c) whether the three ever diverge in content/order.
    // This probe answers all three empirically, purely by reading raw bytes
    // via Marshal.Read* at fixed native offsets and logging alongside the
    // existing managed-property reads - it NEVER writes to any of that
    // memory, and never calls ResetController()/SteamControllerReStart()/
    // Shutdown()/Init()/UpdateConnectedControllers()/any other
    // state-mutating Steam Input API. Runs unconditionally from
    // ModMain.OnUpdate(), NOT gated to FieldDashPatch.IsExplorationActive,
    // so it keeps observing across the Guide/Overlay window. Every risky
    // sub-read is wrapped in its own try/catch with a logged-once failure
    // flag. Logs only on change, plus a periodic heartbeat, to avoid log
    // flooding.
    internal static class Root26FieldOffsetMapProbe
    {
        private const string Tag = "[NocturneModernController][Root26FieldOffsetMap]";
        private const long HeartbeatIntervalMs = 5000;

        private static bool _baseCheckLogged;
        private static bool _errorLogged;
        private static long _lastHeartbeatTickMs = -1;

        private static string? _lastNative0x08;
        private static string? _lastNative0x20;
        private static string? _lastNative0x30;
        private static string? _lastManagedInputHandles;
        private static string? _lastManagedInputHandlesNew;
        private static string? _lastManagedInputHandlesId;

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
                IntPtr utilPtr = util.Pointer;
                IntPtr padPtr = pad.Pointer;
                if (utilPtr == IntPtr.Zero || padPtr == IntPtr.Zero)
                {
                    return;
                }

                // Reproduces func_0x182141080's own pointer chase:
                //   obj = *(SteamInputUtil-native + 0xb8)
                IntPtr objViaUtil0xb8 = Marshal.ReadIntPtr(utilPtr, 0xb8);

                if (!_baseCheckLogged)
                {
                    _baseCheckLogged = true;
                    bool sameAsPad = objViaUtil0xb8 == padPtr;
                    MelonLogger.Msg(
                        $"{Tag} BASE-CHECK util.Pointer=0x{utilPtr.ToInt64():X} " +
                        $"util.Pointer+0xb8=0x{objViaUtil0xb8.ToInt64():X} " +
                        $"pad.Pointer=0x{padPtr.ToInt64():X} SAME-AS-PAD={sameAsPad} " +
                        $"at {DateTimeOffset.Now:O}");
                }

                string native0x08 = ReadNativeHandleArray(objViaUtil0xb8, 0x8);
                string native0x20 = ReadNativeHandleArray(padPtr, 0x20);
                string native0x30 = ReadNativeHandleArray(padPtr, 0x30);

                // Label convention (deliberately not "SteamPad+0x08" until
                // BASE-CHECK above confirms util.Pointer+0xb8 == pad.Pointer):
                //   util+b8:+0x08 - reached via util.Pointer -> +0xb8 -> +0x08 (func_0x182141080's array)
                //   steamPad:+0x20 - reached directly via pad.Pointer -> +0x20 (SteamPadSet's handle-fetch array)
                //   steamPad:+0x30 - reached directly via pad.Pointer -> +0x30 (UpdateInput's loop/pre-check array)
                LogIfChanged("util+b8:+0x08 (func_0x182141080's array)", ref _lastNative0x08, native0x08);
                LogIfChanged("steamPad:+0x20 (SteamPadSet's handle-fetch array)", ref _lastNative0x20, native0x20);
                LogIfChanged("steamPad:+0x30 (UpdateInput's loop/pre-check array)", ref _lastNative0x30, native0x30);

                string managedInputHandles = JoinInputHandleArray(pad.InputHandles);
                string managedInputHandlesNew = JoinInputHandleArray(pad.InputHandles_new);
                string managedInputHandlesId = JoinUlongArray(pad.InputHandles_id);

                LogIfChanged("managed pad.InputHandles", ref _lastManagedInputHandles, managedInputHandles);
                LogIfChanged("managed pad.InputHandles_new", ref _lastManagedInputHandlesNew, managedInputHandlesNew);
                LogIfChanged("managed pad.InputHandles_id", ref _lastManagedInputHandlesId, managedInputHandlesId);

                long now = Environment.TickCount64;
                if (_lastHeartbeatTickMs < 0 || now - _lastHeartbeatTickMs >= HeartbeatIntervalMs)
                {
                    _lastHeartbeatTickMs = now;
                    MelonLogger.Msg(
                        $"{Tag} HEARTBEAT util+b8:+0x08=[{native0x08}] steamPad:+0x20=[{native0x20}] " +
                        $"steamPad:+0x30=[{native0x30}] managed.InputHandles=[{managedInputHandles}] " +
                        $"managed.InputHandles_new=[{managedInputHandlesNew}] " +
                        $"managed.InputHandles_id=[{managedInputHandlesId}] at {DateTimeOffset.Now:O}");
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

        // Read-only: reproduces the exact native array layout confirmed by
        // Ghidra decompile (IL2CPP SZ array): length at +0x18, elements
        // starting at +0x20, 8 bytes each. Never writes.
        private static string ReadNativeHandleArray(IntPtr objPtr, int fieldOffset)
        {
            if (objPtr == IntPtr.Zero)
            {
                return "OBJ-NULL";
            }

            IntPtr arrPtr = Marshal.ReadIntPtr(objPtr, fieldOffset);
            if (arrPtr == IntPtr.Zero)
            {
                return "ARR-NULL";
            }

            int length = Marshal.ReadInt32(arrPtr, 0x18);
            if (length < 0 || length > 64)
            {
                return $"ARR-SUSPECT-LENGTH({length})";
            }

            var sb = new StringBuilder();
            for (int i = 0; i < length; i++)
            {
                if (i > 0)
                {
                    sb.Append(',');
                }
                long v = Marshal.ReadInt64(arrPtr, 0x20 + i * 8);
                sb.Append(v);
            }
            return sb.ToString();
        }

        private static string JoinInputHandleArray(
            Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<Il2CppSteamworks.InputHandle_t>? arr)
        {
            if (arr == null)
            {
                return "null";
            }
            return string.Join(",", Enumerable.Range(0, arr.Length).Select(i => arr[i].m_InputHandle.ToString()));
        }

        private static string JoinUlongArray(ulong[]? arr)
        {
            if (arr == null)
            {
                return "null";
            }
            return string.Join(",", arr.Select(v => v.ToString()));
        }

        private static void LogIfChanged(string label, ref string? last, string current)
        {
            if (last != current)
            {
                MelonLogger.Msg($"{Tag} STATE-CHANGE {label} [{last ?? "INITIAL"}] -> [{current}] at {DateTimeOffset.Now:O}");
                last = current;
            }
        }
    }
}
