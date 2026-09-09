using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Il2Cpp;
using Il2CppSteamworks;
using MelonLoader;

namespace NocturneModernController
{
    // Root-26 Chapter 72/73 follow-up (layout-neutral redesign): read-only,
    // same-frame comparison of
    //   managed  Il2CppSteamworks.SteamInput.GetAnalogActionData(inputHandle, analogActionHandle)
    //   flat     SteamAPI_ISteamInput_GetAnalogActionData(v002self, inputHandle, analogActionHandle)
    // for the RSTICK analog action, on the SAME controller handle and SAME
    // analog action handle each Sample() call.
    //
    // Chapter 72.3 self-correction: an earlier version of this probe
    // assumed a specific field ORDER for the flat struct
    // (x@0x0/y@0x4/eMode@0x8/bActive@0xc), inferred from the byte-copy
    // SIZES seen in the flat thunk's disassembly. That inference was
    // withdrawn: a copy pattern of "8 bytes, then 4 bytes, then 1 byte"
    // is consistent with more than one possible field ordering and does
    // NOT by itself prove which field lives at which offset. This probe
    // no longer assumes any field order for the flat side - it reads the
    // raw bytes as three untyped 4-byte words plus one byte, and logs
    // each word both as an Int32 and as a bit-reinterpreted Single,
    // alongside the managed struct's fields (whose semantic field NAMES
    // are already CONFIRMED via ECMA-335 metadata, chapter 72.4). The
    // actual field-to-offset mapping is then determined empirically at
    // runtime by comparing raw word values against the managed values
    // frame-by-frame (most conclusively while the stick is actively being
    // moved, giving x/y distinct non-trivial values) - not assumed ahead
    // of time.
    //
    // Purpose (per docs/research/RIGHT_STICK_VIEW_AND_DASH_INVESTIGATION.md
    // chapter 71-73): NOT to re-interpret GetCurrentActionSet=0 (chapter 71
    // already CONFIRMED ActivateActionSet(handle,1) really fires from the
    // game itself while GetCurrentActionSet(handle) still reports 0
    // afterward - "Case A"). This probe instead tests whether the flat
    // v002 self pointer observes the SAME live Steam Input state as the
    // game's own managed binding, using RSTICK's already-known DEAD(x=y=0)
    // -> Guide -> LIVE(x/y real) signal as ground truth.
    //
    // ABI (chapter 72.2, CONFIRMED via capstone disassembly to the ret
    // instruction): the flat export SteamAPI_ISteamInput_GetAnalogActionData
    // returns its struct via the standard Windows x64 hidden-return-pointer
    // convention (struct size > 8 bytes). The CLR's P/Invoke marshaller is
    // expected to handle this automatically for a blittable
    // [StructLayout(LayoutKind.Sequential)] struct declared as the extern
    // method's return type, but this has not yet been independently
    // confirmed against actual runtime results - treated as HYPOTHESIS
    // until this probe's own output is checked for sane, non-garbage
    // values.
    //
    // No ActivateActionSet/ActivateActionSetLayer/ResetController/
    // SteamControllerReStart/Shutdown/Init/UpdateConnectedControllers call
    // of any kind. No native detour/patch/injection/hook. Controller
    // handles and the RSTICK analog action handle are read each frame from
    // the same existing managed state other Root-26 probes already use
    // (pad.Controller.Keys / ActionSets[Current].Analog[1]) - never
    // hardcoded.
    internal static class Root26AnalogActionDataCompareProbe
    {
        private const string Tag = "[NocturneModernController][Root26AnalogDataCompare]";
        private const long HeartbeatIntervalMs = 5000;
        private const int RstickAnalogIndex = 1; // Same convention as Root26Phase2AnalogActionDataProbe (i=1 -> IG_RSTICK).

        // Deliberately layout-NEUTRAL: three untyped 4-byte words plus one
        // byte, matching only the byte-copy SIZES/OFFSETS CONFIRMED in
        // chapter 72.2 (0x0..0x7 as two words, 0x8 as one word, 0xc as one
        // byte) - no assumption about which word is x/y/eMode.
        [StructLayout(LayoutKind.Sequential)]
        private struct RawInputAnalogActionData
        {
            public uint raw0; // offset 0x0
            public uint raw1; // offset 0x4
            public uint raw2; // offset 0x8
            public byte raw3; // offset 0xc
        }

        [DllImport("steam_api64.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr SteamAPI_SteamInput_v002();

        // "Old" struct-return declaration - kept unmodified as the control
        // group (chapter 76). Relies on the CLR's P/Invoke marshaller to
        // automatically apply the Windows x64 hidden-return-pointer
        // convention for a struct return value > 8 bytes. Chapter 75
        // observed this always returning all-zero, even while managed
        // reported real, changing x/y values - this is now suspected
        // (HYPOTHESIS, chapter 75.4) to reflect a marshalling failure
        // rather than a genuine Steam Input state difference.
        [DllImport("steam_api64.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern RawInputAnalogActionData SteamAPI_ISteamInput_GetAnalogActionData(
            IntPtr self, ulong inputHandle, ulong analogActionHandle);

        // Chapter 76 diagnostic shim (experimental group): explicitly
        // reproduces the machine-level ABI CONFIRMED via capstone
        // disassembly in chapter 72.2 -
        //   RCX = hidden return buffer, RDX = self, R8 = inputHandle,
        //   R9 = analogActionHandle
        // - as an explicit `out` parameter instead of relying on the
        // CLR's automatic struct-return marshalling. Same native export
        // (EntryPoint), same RawInputAnalogActionData layout. This is NOT
        // the API's natural C signature; it exists purely to test whether
        // the "old" declaration above was failing to invoke the hidden
        // sret convention correctly.
        [DllImport(
            "steam_api64.dll",
            EntryPoint = "SteamAPI_ISteamInput_GetAnalogActionData",
            CallingConvention = CallingConvention.Cdecl)]
        private static extern void SteamAPI_ISteamInput_GetAnalogActionData_ExplicitSret(
            out RawInputAnalogActionData result,
            IntPtr self,
            ulong inputHandle,
            ulong analogActionHandle);

        private sealed class PerHandleState
        {
            internal string? LastSummary;
        }

        private static readonly Dictionary<ulong, PerHandleState> PerHandle = new();

        private static bool _selfPointerResolved;
        private static IntPtr _selfPointer = IntPtr.Zero;
        private static bool _disabledLogged;
        private static bool _errorLogged;
        private static long _lastHeartbeatMs = -1;
        private static bool _sizeLogged;

        internal static void Sample()
        {
            if (!ResolveSelfPointer())
            {
                return;
            }

            if (!_sizeLogged)
            {
                _sizeLogged = true;
                int size = Marshal.SizeOf<RawInputAnalogActionData>();
                MelonLogger.Msg($"{Tag} Marshal.SizeOf<RawInputAnalogActionData>()={size} bytes at {DateTimeOffset.Now:O}");
            }

            SteamInputUtil util;
            SteamPad pad;
            try
            {
                util = SteamInputUtil.instance;
                if (util == null)
                {
                    return;
                }
                pad = util.steam_pad;
                if (pad == null)
                {
                    return;
                }
            }
            catch (Exception ex)
            {
                LogErrorOnce("SteamInputUtil.instance/steam_pad access threw " + Describe(ex));
                return;
            }

            try
            {
                foreach (var handleRaw in pad.Controller.Keys)
                {
                    if (handleRaw == 0)
                    {
                        continue;
                    }
                    SampleHandle(pad, handleRaw);
                }
            }
            catch (Exception ex)
            {
                LogErrorOnce("pad.Controller.Keys iteration threw " + Describe(ex));
            }

            MaybeLogHeartbeat();
        }

        private static bool ResolveSelfPointer()
        {
            if (_selfPointerResolved)
            {
                return _selfPointer != IntPtr.Zero;
            }
            _selfPointerResolved = true;

            try
            {
                _selfPointer = SteamAPI_SteamInput_v002();
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"{Tag} SteamAPI_SteamInput_v002() threw {ex.GetType().FullName}: {ex.Message} - probe disabled for this session.");
                _selfPointer = IntPtr.Zero;
                return false;
            }

            if (_selfPointer == IntPtr.Zero)
            {
                if (!_disabledLogged)
                {
                    _disabledLogged = true;
                    MelonLogger.Warning($"{Tag} SteamAPI_SteamInput_v002() returned IntPtr.Zero - probe disabled for this session.");
                }
                return false;
            }

            return true;
        }

        private static void SampleHandle(SteamPad pad, ulong handleRaw)
        {
            SteamPad.InputInfo info;
            try
            {
                info = pad.Controller[handleRaw];
            }
            catch (Exception ex)
            {
                LogErrorOnce("pad.Controller[handle] access threw " + Describe(ex));
                return;
            }

            var actionSets = info.ActionSets;
            if (actionSets == null || info.Current < 0 || info.Current >= actionSets.Count)
            {
                return;
            }
            var analogList = actionSets[info.Current].Analog;
            if (analogList == null || RstickAnalogIndex >= analogList.Count)
            {
                return;
            }

            ulong rstickAnalogHandleRaw = analogList[RstickAnalogIndex].Handle.m_InputAnalogActionHandle;

            InputAnalogActionData_t managedData;
            try
            {
                managedData = SteamInput.GetAnalogActionData(
                    new InputHandle_t(handleRaw), new InputAnalogActionHandle_t(rstickAnalogHandleRaw));
            }
            catch (Exception ex)
            {
                LogErrorOnce("SteamInput.GetAnalogActionData threw " + Describe(ex));
                return;
            }

            RawInputAnalogActionData flatRawOld;
            try
            {
                flatRawOld = SteamAPI_ISteamInput_GetAnalogActionData(_selfPointer, handleRaw, rstickAnalogHandleRaw);
            }
            catch (Exception ex)
            {
                LogErrorOnce("SteamAPI_ISteamInput_GetAnalogActionData (struct-return) threw " + Describe(ex));
                return;
            }

            RawInputAnalogActionData flatRawSret;
            try
            {
                SteamAPI_ISteamInput_GetAnalogActionData_ExplicitSret(
                    out flatRawSret, _selfPointer, handleRaw, rstickAnalogHandleRaw);
            }
            catch (Exception ex)
            {
                LogErrorOnce("SteamAPI_ISteamInput_GetAnalogActionData_ExplicitSret threw " + Describe(ex));
                return;
            }

            bool managedBActive = managedData.bActive != 0;
            int managedEMode = (int)managedData.eMode;
            int managedXBits = BitConverter.SingleToInt32Bits(managedData.x);
            int managedYBits = BitConverter.SingleToInt32Bits(managedData.y);

            string summary =
                $"managed(eMode={managedEMode} x={managedData.x} y={managedData.y} bActive={managedBActive}) " +
                $"OLD:{DescribeRaw(flatRawOld, managedEMode, managedXBits, managedYBits, managedBActive)} " +
                $"SRET:{DescribeRaw(flatRawSret, managedEMode, managedXBits, managedYBits, managedBActive)}";

            if (!PerHandle.TryGetValue(handleRaw, out PerHandleState? state))
            {
                state = new PerHandleState();
                PerHandle[handleRaw] = state;
            }

            if (state.LastSummary != summary)
            {
                MelonLogger.Msg(
                    $"{Tag} STATE-CHANGE handle={handleRaw} analogHandle={rstickAnalogHandleRaw} {summary} at {DateTimeOffset.Now:O}");
                state.LastSummary = summary;
            }
        }

        // Layout-neutral: checks every raw word against every managed
        // field rather than assuming which raw word corresponds to which
        // field ahead of time. Shared by both the OLD (struct-return) and
        // SRET (explicit out-parameter) call paths so their outputs are
        // directly comparable in the same log line.
        private static string DescribeRaw(RawInputAnalogActionData raw, int managedEMode, int managedXBits, int managedYBits, bool managedBActive)
        {
            int raw0AsInt32 = unchecked((int)raw.raw0);
            int raw1AsInt32 = unchecked((int)raw.raw1);
            int raw2AsInt32 = unchecked((int)raw.raw2);
            float raw0AsFloat = BitConverter.Int32BitsToSingle(raw0AsInt32);
            float raw1AsFloat = BitConverter.Int32BitsToSingle(raw1AsInt32);
            float raw2AsFloat = BitConverter.Int32BitsToSingle(raw2AsInt32);

            bool raw0EqEModeBits = raw0AsInt32 == managedEMode;
            bool raw0EqXBits = raw0AsInt32 == managedXBits;
            bool raw0EqYBits = raw0AsInt32 == managedYBits;
            bool raw1EqEModeBits = raw1AsInt32 == managedEMode;
            bool raw1EqXBits = raw1AsInt32 == managedXBits;
            bool raw1EqYBits = raw1AsInt32 == managedYBits;
            bool raw2EqEModeBits = raw2AsInt32 == managedEMode;
            bool raw2EqXBits = raw2AsInt32 == managedXBits;
            bool raw2EqYBits = raw2AsInt32 == managedYBits;
            bool raw3EqBActive = (raw.raw3 != 0) == managedBActive;

            return
                $"flatRaw(raw0=0x{raw.raw0:X8}[int={raw0AsInt32} float={raw0AsFloat}] " +
                $"raw1=0x{raw.raw1:X8}[int={raw1AsInt32} float={raw1AsFloat}] " +
                $"raw2=0x{raw.raw2:X8}[int={raw2AsInt32} float={raw2AsFloat}] " +
                $"raw3={raw.raw3}) " +
                $"match(raw0==eMode:{raw0EqEModeBits} raw0==xBits:{raw0EqXBits} raw0==yBits:{raw0EqYBits} " +
                $"raw1==eMode:{raw1EqEModeBits} raw1==xBits:{raw1EqXBits} raw1==yBits:{raw1EqYBits} " +
                $"raw2==eMode:{raw2EqEModeBits} raw2==xBits:{raw2EqXBits} raw2==yBits:{raw2EqYBits} " +
                $"raw3==bActive:{raw3EqBActive})";
        }

        private static void MaybeLogHeartbeat()
        {
            long now = Environment.TickCount64;
            if (_lastHeartbeatMs >= 0 && now - _lastHeartbeatMs < HeartbeatIntervalMs)
            {
                return;
            }
            _lastHeartbeatMs = now;

            if (PerHandle.Count == 0)
            {
                return;
            }

            var parts = new List<string>();
            foreach (var kv in PerHandle)
            {
                parts.Add($"[handle={kv.Key} {kv.Value.LastSummary}]");
            }
            MelonLogger.Msg($"{Tag} HEARTBEAT {string.Join(" ", parts)} at {DateTimeOffset.Now:O}");
        }

        private static string Describe(Exception ex) => ex.GetType().FullName + ": " + ex.Message;

        private static void LogErrorOnce(string message)
        {
            if (_errorLogged)
            {
                return;
            }
            _errorLogged = true;
            MelonLogger.Warning($"{Tag} {message}");
        }
    }
}
