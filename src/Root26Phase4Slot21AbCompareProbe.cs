using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using HarmonyLib;
using Il2Cpp;
using MelonLoader;

namespace NocturneModernController
{
    // Root-26 "A1" Phase 4 (REVISED after a reproducible crash - see
    // history below): minimal, single-shot A/B comparison of
    // GetAnalogActionData results between oldSelf(v002) and newSelf
    // (SteamInput007), using the CORRECT outer vtable slot for each -
    // slot15 for oldSelf (existing, years-proven-safe flat export path),
    // slot21 for newSelf (called directly through its own vtable pointer).
    //
    // History:
    //  - Phase 4 v1 (REJECTED, crashed): declared the native slot21
    //    function pointer as
    //      delegate void F(IntPtr self, ulong inputHandle,
    //                      ulong analogActionHandle,
    //                      out RawInputAnalogActionData result)
    //    - i.e. assumed the CLR's Cdecl parameter order (self, in1, in2,
    //    out-as-pointer) mapped directly onto RCX/RDX/R8/R9. This crashed
    //    with the SAME coreclr.dll+0x1d4089 access violation seen in the
    //    earlier (wrong-slot) Phase 2 crash, even though slot21 was
    //    verified correct at runtime before the call.
    //  - Root-cause static re-check (this session): disassembled the
    //    actual flat export SteamAPI_ISteamInput_GetAnalogActionData in
    //    steam_api64.dll end-to-end and traced exactly how it reorders
    //    its own (sret, self, inputHandle, analogActionHandle) arguments
    //    before calling through self's vtable slot. CONFIRMED native
    //    virtual-call ABI for steamclient64.dll+0x71F120 (both oldSelf's
    //    slot15 and newSelf's slot21 - same code address):
    //      RCX = self
    //      RDX = pointer to a 16-byte OUTPUT buffer (sret-like, but in
    //            the RDX slot, not RCX - confirmed by tracing that the
    //            flat export's own RDX is completely unused for "self"
    //            after being saved to r10, and instead gets overwritten
    //            with `lea rdx,[rsp+0x20]` - a raw stack buffer address -
    //            immediately before the vtable call)
    //      R8  = inputHandle
    //      R9  = analogActionHandle
    //      RAX (on return) = the SAME output buffer pointer, echoed back
    //    Phase 4 v1's delegate put inputHandle in RDX instead of the
    //    output buffer pointer - i.e. it handed the native code a raw
    //    Steam handle integer to write eMode/x/y/bActive through as if
    //    it were a valid memory address. That wild write is a complete,
    //    concrete explanation for the access violation. ABI1 CONFIRMED
    //    (see investigation notes) - not a "newSelf is unsafe" issue.
    //  - Phase 4 v2 (this version): expresses the native ABI literally -
    //    no `out struct`/CLR struct marshaller involved at all. A plain
    //    16-byte unmanaged buffer (Marshal.AllocHGlobal) is passed as an
    //    IntPtr in the RDX argument slot, exactly where the disassembly
    //    showed the native code expects it. The delegate's declared
    //    return type is IntPtr (matching RAX's confirmed role: echoing
    //    the output buffer pointer back), and that return value is
    //    verified to equal the buffer we passed before trusting the
    //    buffer contents at all.
    //
    // Prerequisite facts this PoC treats as CONFIRMED (all from this
    // session's read-only static/runtime work - see
    // Root26NewInterfacePoc.cs Phase 3 / Phase 3.5):
    //   oldSelf(v002) outer vtable slot15 -> steamclient64.dll+0x71F120
    //   newSelf(v007) outer vtable slot21 -> steamclient64.dll+0x71F120  (SAME code address)
    //   *(oldSelf+0x8) == *(newSelf+0x8)                                 (SAME inner object)
    //   inner vtable slots 0..31: fully identical between old/new
    //   inner vtable slot15 (0x78) -> steamclient64.dll+0x600D70          (SAME final target)
    //
    // Safety gates (ALL re-verified at runtime, immediately before the
    // one and only call through newSelf):
    //   1. newSelf.vtable[21] == steamclient64.dll base + 0x71F120 (exact)
    //   2. *(newSelf + 0x8) == *(oldSelf + 0x8)  (inner object identity)
    //   If either fails, the call is skipped entirely and a warning is
    //   logged - no call is ever made against an unverified address or
    //   an unexpectedly-different inner object.
    //
    // Scope / safety:
    //  - Default DISABLED (Enabled = false). Flip to true and rebuild
    //    only for the deliberate test run.
    //  - Runs AT MOST ONCE per session, for the first non-zero controller
    //    handle seen (guarded by _done). No per-frame logging, no retry.
    //  - Only Steam Input APIs called: SteamAPI_SteamInput_v002,
    //    SteamAPI_GetHSteamUser, SteamInternal_FindOrCreateUserInterface
    //    (all read-only resolvers), the existing flat
    //    SteamAPI_ISteamInput_GetAnalogActionData_ExplicitSret export
    //    called ONLY with oldSelf (the same call already proven safe
    //    every frame by Root26AnalogActionDataCompareProbe), and the raw
    //    function pointer at newSelf's OWN slot21 - called only after
    //    BOTH safety gates above pass.
    //  - No ActivateActionSet/ActivateActionSetLayer/ResetController/
    //    SteamControllerReStart/Shutdown/Init/UpdateConnectedControllers/
    //    RunFrame or any other state-changing Steam Input API. No native
    //    hook/injection/patch of steamclient64.dll/GameOverlayRenderer64.dll/
    //    steam_api64.dll. The only memory write is into our own
    //    Marshal.AllocHGlobal'd 16-byte scratch buffer, freed immediately
    //    after use.
    //  - Guide button is not required and should not be pressed for this
    //    test.
    [HarmonyPatch(typeof(SteamInputUtil), nameof(SteamInputUtil.UpdateInput))]
    internal static class Root26Phase4Slot21AbCompareProbe
    {
        // Flip to true and rebuild for the test run. Leave false otherwise.
        // Result (this session): N3 CONFIRMED - OLD(v002 slot15) and
        // NEW(SteamInput007 slot21) returned IDENTICAL DEAD data
        // (eMode=0 x=0 y=0 bActive=False) for the same handle/frame, no
        // crash, RAX==outBuf confirmed. Interface-version alone is NOT
        // the DEAD/LIVE root cause (both share the same inner object and
        // final implementation - see Root26NewInterfacePoc Phase 3.5).
        // Do NOT re-enable without explicit approval.
        private static readonly bool Enabled = false;

        private const string Tag = "[NocturneModernController][Root26Phase4Slot21AB]";
        private const string CandidateVersion = "SteamInput007";
        private const int NewSlotIndex = 21;
        private const long ExpectedRva = 0x71F120;
        private const int RstickAnalogIndex = 1; // Same convention as other Root-26 probes (i=1 -> IG_RSTICK).
        private const int OutputBufferSize = 16; // eMode(4) + x(4) + y(4) + bActive(1), padded.

        // Native ABI (CONFIRMED this session via disassembly of both the
        // flat export and steamclient64.dll+0x71F120 itself):
        //   RCX = self, RDX = output buffer pointer, R8 = inputHandle,
        //   R9 = analogActionHandle, RAX(return) = output buffer pointer.
        // Expressed literally - no struct marshalling involved.
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr GetAnalogActionDataDirectDelegate(
            IntPtr self, IntPtr outputBuffer, ulong inputHandle, ulong analogActionHandle);

        [StructLayout(LayoutKind.Sequential)]
        private struct RawInputAnalogActionData
        {
            public uint raw0; // eMode bits (confirmed layout, chapter 72-76)
            public uint raw1; // x bits
            public uint raw2; // y bits
            public byte raw3; // bActive
        }

        [DllImport("steam_api64.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr SteamAPI_SteamInput_v002();

        [DllImport("steam_api64.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int SteamAPI_GetHSteamUser();

        [DllImport("steam_api64.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        private static extern IntPtr SteamInternal_FindOrCreateUserInterface(int hSteamUser, string pszVersion);

        // Existing, years-proven-safe flat export path - used ONLY with
        // oldSelf, exactly as Root26AnalogActionDataCompareProbe already
        // calls it every frame.
        [DllImport(
            "steam_api64.dll",
            EntryPoint = "SteamAPI_ISteamInput_GetAnalogActionData",
            CallingConvention = CallingConvention.Cdecl)]
        private static extern void SteamAPI_ISteamInput_GetAnalogActionData_ExplicitSret(
            out RawInputAnalogActionData result,
            IntPtr self,
            ulong inputHandle,
            ulong analogActionHandle);

        private static bool _done;

        private static void Prefix()
        {
            if (!Enabled || _done)
            {
                return;
            }
            _done = true;

            try
            {
                RunOnce();
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"{Tag} threw {ex.GetType().FullName}: {ex.Message} - PoC disabled for rest of session.");
            }
        }

        private static void RunOnce()
        {
            IntPtr oldSelf = SteamAPI_SteamInput_v002();
            int hUser = SteamAPI_GetHSteamUser();
            IntPtr newSelf = SteamInternal_FindOrCreateUserInterface(hUser, CandidateVersion);

            if (oldSelf == IntPtr.Zero || newSelf == IntPtr.Zero)
            {
                MelonLogger.Warning($"{Tag} oldSelf or newSelf is NULL (oldSelf=0x{oldSelf.ToInt64():X} newSelf=0x{newSelf.ToInt64():X}) - aborting.");
                return;
            }

            IntPtr steamclientBase = FindSteamclientBase();
            if (steamclientBase == IntPtr.Zero)
            {
                MelonLogger.Warning($"{Tag} could not locate steamclient64.dll base address - aborting.");
                return;
            }

            // Safety gate 1: newSelf.vtable[21] must equal
            // steamclient64.dll base + 0x71F120, re-verified at runtime.
            IntPtr newVtable = Marshal.ReadIntPtr(newSelf);
            IntPtr slot21Value = Marshal.ReadIntPtr(newVtable, NewSlotIndex * 8);
            IntPtr expectedSlot21 = IntPtr.Add(steamclientBase, (int)ExpectedRva);

            MelonLogger.Msg(
                $"{Tag} GATE1 steamclientBase=0x{steamclientBase.ToInt64():X} " +
                $"newSelf.slot{NewSlotIndex}=0x{slot21Value.ToInt64():X} expected=0x{expectedSlot21.ToInt64():X}");

            if (slot21Value != expectedSlot21)
            {
                MelonLogger.Warning(
                    $"{Tag} SAFETY GATE 1 FAILED: newSelf.slot{NewSlotIndex} does not match steamclient64.dll+0x{ExpectedRva:X} at runtime - call skipped, PoC aborted.");
                return;
            }

            // Safety gate 2: *(newSelf+8) must equal *(oldSelf+8) (same
            // shared "inner" object), re-verified at runtime.
            IntPtr innerOld = Marshal.ReadIntPtr(oldSelf, 8);
            IntPtr innerNew = Marshal.ReadIntPtr(newSelf, 8);

            MelonLogger.Msg($"{Tag} GATE2 innerOld=0x{innerOld.ToInt64():X} innerNew=0x{innerNew.ToInt64():X}");

            if (innerOld != innerNew)
            {
                MelonLogger.Warning($"{Tag} SAFETY GATE 2 FAILED: inner objects differ at runtime - call skipped, PoC aborted.");
                return;
            }

            // Find one live controller handle + the RSTICK analog action
            // handle, the same safe/read-only way
            // Root26AnalogActionDataCompareProbe already does every frame.
            SteamInputUtil util = SteamInputUtil.instance;
            if (util == null)
            {
                MelonLogger.Warning($"{Tag} SteamInputUtil.instance is null - aborting.");
                return;
            }
            SteamPad pad = util.steam_pad;
            if (pad == null)
            {
                MelonLogger.Warning($"{Tag} steam_pad is null - aborting.");
                return;
            }

            ulong handleRaw = 0;
            foreach (var key in pad.Controller.Keys)
            {
                if (key != 0)
                {
                    handleRaw = key;
                    break;
                }
            }
            if (handleRaw == 0)
            {
                MelonLogger.Warning($"{Tag} no non-zero controller handle found - aborting.");
                return;
            }

            SteamPad.InputInfo info = pad.Controller[handleRaw];
            var actionSets = info.ActionSets;
            if (actionSets == null || info.Current < 0 || info.Current >= actionSets.Count)
            {
                MelonLogger.Warning($"{Tag} actionSets unavailable for handle={handleRaw} - aborting.");
                return;
            }
            var analogList = actionSets[info.Current].Analog;
            if (analogList == null || RstickAnalogIndex >= analogList.Count)
            {
                MelonLogger.Warning($"{Tag} analog action list unavailable for handle={handleRaw} - aborting.");
                return;
            }
            ulong analogActionHandle = analogList[RstickAnalogIndex].Handle.m_InputAnalogActionHandle;

            // OLD: existing safe path (oldSelf, correct slot15 via the
            // flat export's own internal vtable dispatch).
            RawInputAnalogActionData oldResult;
            SteamAPI_ISteamInput_GetAnalogActionData_ExplicitSret(out oldResult, oldSelf, handleRaw, analogActionHandle);
            LogOldResult(handleRaw, analogActionHandle, oldResult);

            // NEW: direct call through newSelf's OWN slot21 pointer,
            // expressing the CONFIRMED native ABI literally (RCX=self,
            // RDX=output buffer pointer, R8=inputHandle,
            // R9=analogActionHandle, RAX=output buffer pointer echoed
            // back) - no CLR struct marshaller involved.
            IntPtr outBuf = Marshal.AllocHGlobal(OutputBufferSize);
            try
            {
                // Zero the buffer first so a partial/failed write is
                // still distinguishable from genuine zero data.
                for (int i = 0; i < OutputBufferSize; i++)
                {
                    Marshal.WriteByte(outBuf, i, 0);
                }

                var direct = Marshal.GetDelegateForFunctionPointer<GetAnalogActionDataDirectDelegate>(slot21Value);
                IntPtr returnedPtr = direct(newSelf, outBuf, handleRaw, analogActionHandle);

                bool returnedPtrMatches = returnedPtr == outBuf;
                MelonLogger.Msg($"{Tag} NEW call returned RAX=0x{returnedPtr.ToInt64():X} outBuf=0x{outBuf.ToInt64():X} match={returnedPtrMatches}");

                uint raw0 = (uint)Marshal.ReadInt32(outBuf, 0);
                uint raw1 = (uint)Marshal.ReadInt32(outBuf, 4);
                uint raw2 = (uint)Marshal.ReadInt32(outBuf, 8);
                byte raw3 = Marshal.ReadByte(outBuf, 12);

                LogNewResult(handleRaw, analogActionHandle, raw0, raw1, raw2, raw3);
            }
            finally
            {
                Marshal.FreeHGlobal(outBuf);
            }
        }

        private static void LogOldResult(ulong handleRaw, ulong analogActionHandle, RawInputAnalogActionData raw)
        {
            int eMode = unchecked((int)raw.raw0);
            float x = BitConverter.Int32BitsToSingle(unchecked((int)raw.raw1));
            float y = BitConverter.Int32BitsToSingle(unchecked((int)raw.raw2));
            bool bActive = raw.raw3 != 0;
            MelonLogger.Msg(
                $"{Tag} OLD(v002 slot15) handle={handleRaw} analogHandle={analogActionHandle} " +
                $"eMode={eMode} x={x} y={y} bActive={bActive} " +
                $"[raw0=0x{raw.raw0:X8} raw1=0x{raw.raw1:X8} raw2=0x{raw.raw2:X8} raw3={raw.raw3}]");
        }

        private static void LogNewResult(ulong handleRaw, ulong analogActionHandle, uint raw0, uint raw1, uint raw2, byte raw3)
        {
            int eMode = unchecked((int)raw0);
            float x = BitConverter.Int32BitsToSingle(unchecked((int)raw1));
            float y = BitConverter.Int32BitsToSingle(unchecked((int)raw2));
            bool bActive = raw3 != 0;
            MelonLogger.Msg(
                $"{Tag} NEW({CandidateVersion} slot{NewSlotIndex}) handle={handleRaw} analogHandle={analogActionHandle} " +
                $"eMode={eMode} x={x} y={y} bActive={bActive} " +
                $"[raw0=0x{raw0:X8} raw1=0x{raw1:X8} raw2=0x{raw2:X8} raw3={raw3}]");
        }

        private static IntPtr FindSteamclientBase()
        {
            using Process current = Process.GetCurrentProcess();
            foreach (ProcessModule module in current.Modules)
            {
                try
                {
                    if (string.Equals(module.ModuleName, "steamclient64.dll", StringComparison.OrdinalIgnoreCase))
                    {
                        return module.BaseAddress;
                    }
                }
                finally
                {
                    module.Dispose();
                }
            }
            return IntPtr.Zero;
        }
    }
}
