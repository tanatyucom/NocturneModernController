using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Il2Cpp;
using Il2CppSteamworks;
using MelonLoader;

namespace NocturneModernController
{
    // Root-26 Chapter 84: read-only, same-frame, three-way comparison for
    // the RSTICK analog action, isolating whether the "self" pointer
    // ALONE explains why the flat GetAnalogActionData export returns
    // all-zero (chapters 75-77) while the managed binding returns real
    // data:
    //
    //   A) managed binding: Il2CppSteamworks.SteamInput.GetAnalogActionData(inputHandle, analogActionHandle)
    //   B) SAME flat export, self = flatSelf              (= SteamAPI_SteamInput_v002())
    //   C) SAME flat export, self = managedSelfCandidate  (chapter 78/79/81 cached-pointer-chain self)
    //
    // B and C call the EXACT SAME native code path (same EntryPoint, same
    // explicit-sret ABI shim confirmed in chapter 76) - the ONLY thing
    // that differs between B and C is which self pointer is passed. This
    // isolates the self-pointer variable from every other variable in the
    // investigation (thunk ABI, struct layout, P/Invoke marshalling, etc,
    // all already exercised and re-used unchanged from chapters 72/76).
    //
    // managedSelfCandidate is re-derived from the SAME read-only pointer
    // chain used in chapters 78/79/81 (GameAssembly.dll module base + RVA
    // of the confirmed global slot, dereferenced at +0xB8 then +0xB0) on
    // EVERY Sample() call (not just once), specifically so this probe can
    // keep observing across the DEAD -> Guide -> LIVE window requested for
    // this test (chapter 81's one-shot probe deliberately stops after its
    // first successful resolution, which is fine for a static self-
    // identity check but not for a probe that must track live per-frame
    // analog data). Each pointer read is cheap (a handful of
    // Marshal.ReadIntPtr calls) and read-only.
    //
    // Field layout for the flat raw struct (chapter 72.2/74/76): layout-
    // neutral three 4-byte words + one byte at offsets 0x0/0x4/0x8/0xc,
    // read via the SAME explicit-sret diagnostic shim already validated
    // in chapter 76 (Marshal.SizeOf confirmed 16 bytes, chapter 77).
    //
    // Getter-only. No ActivateActionSet/ActivateActionSetLayer/
    // ResetController/SteamControllerReStart/Shutdown/Init/
    // UpdateConnectedControllers call of any kind. No Marshal.Write* of
    // any kind. No native detour/patch/injection/hook of GameAssembly.dll,
    // steamclient64.dll, GameOverlayRenderer64.dll, or steam_api64.dll -
    // this probe only reads already-accessible process memory (the same
    // read-only mechanism as chapters 78-83) and calls two already-used,
    // side-effect-free flat getters (SteamAPI_SteamInput_v002 and the
    // explicit-sret GetAnalogActionData shim). No F9/F10, no SendInput, no
    // Guide spoofing.
    internal static class Root26AnalogDataSelfSwapProbe
    {
        private const string Tag = "[NocturneModernController][Root26AnalogDataSelfSwap]";
        private const long HeartbeatIntervalMs = 5000;
        private const int RstickAnalogIndex = 1; // Same convention as Root26Phase2AnalogActionDataProbe (i=1 -> IG_RSTICK).

        // Chapter 78.2/79.1: cached-pointer-chain constants, identical to
        // Root26ManagedSelfTraceProbe. GameAssembly.dll's actual PE
        // ImageBase (0x180000000) was confirmed via pefile inspection,
        // not assumed.
        private const long StaticImageBase = 0x180000000L;
        private const long TargetVa = 0x182E4F3E0L;
        private const long TargetRva = TargetVa - StaticImageBase;
        private const int Offset1 = 0xB8;
        private const int Offset2 = 0xB0;

        // Layout-neutral, matching Root26AnalogActionDataCompareProbe
        // (chapter 74's redesign) - no assumption about which raw word is
        // x/y/eMode.
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

        // Chapter 76 explicit-sret diagnostic shim, reused unchanged -
        // same EntryPoint as the real flat export, self/inputHandle/
        // analogActionHandle passed as explicit arguments matching the
        // machine-level ABI confirmed in chapter 72.2.
        [DllImport(
            "steam_api64.dll",
            EntryPoint = "SteamAPI_ISteamInput_GetAnalogActionData",
            CallingConvention = CallingConvention.Cdecl)]
        private static extern void SteamAPI_ISteamInput_GetAnalogActionData_ExplicitSret(
            out RawInputAnalogActionData result,
            IntPtr self,
            ulong inputHandle,
            ulong analogActionHandle);

        // Chapter 95 CONFIRMED (via capstone disassembly of the outer
        // GetAnalogActionData wrapper in steamclient64.dll, read-only)
        // that the "effective R8" value passed to the inner
        // implementation is computed as:
        //   globalA (uint32) masked to its low 24 bits,
        //   UNLESS the selector byte equals 2, in which case
        //   globalC (uint32) is used instead (unmasked -
        //   the machine code's "cmove r8d, [globalC]" replaces the
        //   already-masked value with the raw 4-byte globalC value, so
        //   this probe reproduces that literally, not a simplified
        //   version).
        // Chapter 90.3/95 CONFIRMED that inner+0x109708 is compared
        // against this "effective R8" (not inputHandle, per chapter 95's
        // correction). Chapter 98 found the two known WRITE sites for
        // globalA belong to an unrelated subsystem (E2) - this probe does
        // NOT assume any semantic meaning for these values, it only
        // reproduces the machine-code-confirmed read/compare literally.
        //
        // RVA CORRECTION (Root-26 "current DLL" re-verification session,
        // steamclient64.dll SHA-256
        // ea23997e2b376df52bf9bbd3f6d2ba628c3b669b45381c03948fe209a7a0e36f,
        // FileVersion 10.98.06.80, compiled 2026-09-09): the Chapter95
        // RVAs above (0x17E4160/0x17E4163/0x17E4164) were derived from a
        // steamclient64.dll build that predates a Steam client update
        // (confirmed this session - the field116780 ctor zero-init write
        // site alone moved by ~0x12E80 bytes between builds). Those old
        // RVAs are STALE on the current DLL: fresh disassembly this
        // session found the exact same "and edx,0xffffff" /
        // "cmove edx,[globalC]" instruction pattern at 152 independent
        // call sites throughout .text, ALL of which point to the SAME
        // triple of addresses below (cross-validated, not a single-site
        // guess). The stale RVAs read unrelated/incorrect current data,
        // which is why a prior session's effectiveR8 reading stayed
        // constant across an entire DEAD->LIVE transition even though it
        // never once matched inner+0x109708 (including during confirmed
        // gate-pass frames) - a strong tell that it was reading the wrong
        // address rather than a genuinely-invariant value.
        private const long SteamclientGlobalARva = 0x10106D0L;
        private const long SteamclientGlobalSelectorRva = 0x10106D3L;
        private const long SteamclientGlobalCRva = 0x10106D4L;
        private const int InnerStoredFieldOffset = 0x109708; // Chapter 90.3: CONFIRMED via capstone.
        private const int InnerObjectOffset = 0x8; // Chapter 87/88: CONFIRMED outer->inner delegation offset.

        private static bool _steamclientBaseResolved;
        private static IntPtr _steamclientBase = IntPtr.Zero;

        private sealed class PerHandleState
        {
            internal string? LastSummary;
        }

        private static readonly Dictionary<ulong, PerHandleState> PerHandle = new();

        private static bool _gameAssemblyBaseResolved;
        private static IntPtr _gameAssemblyBase = IntPtr.Zero;
        private static bool _errorLogged;
        private static long _lastHeartbeatMs = -1;

        internal static void Sample()
        {
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

            IntPtr managedSelfCandidate = ResolveManagedSelfCandidate();

            IntPtr flatSelf;
            try
            {
                flatSelf = SteamAPI_SteamInput_v002();
            }
            catch (Exception ex)
            {
                LogErrorOnce("SteamAPI_SteamInput_v002() threw " + Describe(ex));
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
                    SampleHandle(pad, handleRaw, flatSelf, managedSelfCandidate);
                }
            }
            catch (Exception ex)
            {
                LogErrorOnce("pad.Controller.Keys iteration threw " + Describe(ex));
            }

            MaybeLogHeartbeat();
        }

        // Re-derived every call (cheap, read-only) rather than cached
        // once, so this probe keeps working across the whole DEAD->
        // Guide->LIVE observation window requested for chapter 84 - not
        // just a single one-shot snapshot like chapter 81's probe.
        // Returns IntPtr.Zero if any stage of the chain is not yet
        // available (never throws for that reason).
        private static IntPtr ResolveManagedSelfCandidate()
        {
            if (!_gameAssemblyBaseResolved)
            {
                _gameAssemblyBase = FindGameAssemblyBase();
                if (_gameAssemblyBase != IntPtr.Zero)
                {
                    _gameAssemblyBaseResolved = true;
                }
                else
                {
                    return IntPtr.Zero;
                }
            }

            try
            {
                IntPtr globalSlotAddress = IntPtr.Add(_gameAssemblyBase, checked((int)TargetRva));
                IntPtr p0 = Marshal.ReadIntPtr(globalSlotAddress);
                if (p0 == IntPtr.Zero)
                {
                    return IntPtr.Zero;
                }
                IntPtr p1 = Marshal.ReadIntPtr(IntPtr.Add(p0, Offset1));
                if (p1 == IntPtr.Zero)
                {
                    return IntPtr.Zero;
                }
                return Marshal.ReadIntPtr(IntPtr.Add(p1, Offset2));
            }
            catch (Exception ex)
            {
                LogErrorOnce("managedSelfCandidate pointer-chain read threw " + Describe(ex));
                return IntPtr.Zero;
            }
        }

        private static void SampleHandle(SteamPad pad, ulong handleRaw, IntPtr flatSelf, IntPtr managedSelfCandidate)
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

            RawInputAnalogActionData rawWithFlatSelf = default;
            bool haveFlatSelfResult = false;
            if (flatSelf != IntPtr.Zero)
            {
                try
                {
                    SteamAPI_ISteamInput_GetAnalogActionData_ExplicitSret(
                        out rawWithFlatSelf, flatSelf, handleRaw, rstickAnalogHandleRaw);
                    haveFlatSelfResult = true;
                }
                catch (Exception ex)
                {
                    LogErrorOnce("GetAnalogActionData(flatSelf) threw " + Describe(ex));
                }
            }

            RawInputAnalogActionData rawWithManagedSelf = default;
            bool haveManagedSelfResult = false;
            if (managedSelfCandidate != IntPtr.Zero)
            {
                try
                {
                    SteamAPI_ISteamInput_GetAnalogActionData_ExplicitSret(
                        out rawWithManagedSelf, managedSelfCandidate, handleRaw, rstickAnalogHandleRaw);
                    haveManagedSelfResult = true;
                }
                catch (Exception ex)
                {
                    LogErrorOnce("GetAnalogActionData(managedSelfCandidate) threw " + Describe(ex));
                }
            }

            bool haveChapter99Values = TryComputeChapter99Values(
                managedSelfCandidate, flatSelf,
                out uint effectiveR8, out uint managedField, out uint flatField,
                out uint globalA, out byte selector, out uint globalC);
            string chapter99Summary = haveChapter99Values
                ? $"effectiveR8=0x{effectiveR8:X8}(globalA=0x{globalA:X8} selector={selector} globalC=0x{globalC:X8}) " +
                  $"managedField(inner+0x109708)=0x{managedField:X8} flatField(inner+0x109708)=0x{flatField:X8} " +
                  $"effectiveR8==managedField:{effectiveR8 == managedField} effectiveR8==flatField:{effectiveR8 == flatField}"
                : "n/a (steamclient64.dll base or inner pointers not yet resolvable)";

            string summary =
                $"managed(eMode={(int)managedData.eMode} x={managedData.x} y={managedData.y} bActive={managedData.bActive != 0}) " +
                $"viaFlatSelf({(haveFlatSelfResult ? DescribeRaw(rawWithFlatSelf) : "n/a")}) " +
                $"viaManagedSelf({(haveManagedSelfResult ? DescribeRaw(rawWithManagedSelf) : "n/a")}) " +
                $"chapter99({chapter99Summary})";

            if (!PerHandle.TryGetValue(handleRaw, out PerHandleState? state))
            {
                state = new PerHandleState();
                PerHandle[handleRaw] = state;
            }

            if (state.LastSummary != summary)
            {
                MelonLogger.Msg($"{Tag} STATE-CHANGE handle={handleRaw} analogHandle={rstickAnalogHandleRaw} {summary} at {DateTimeOffset.Now:O}");
                state.LastSummary = summary;
            }
        }

        private static string DescribeRaw(RawInputAnalogActionData raw)
        {
            int raw0AsInt32 = unchecked((int)raw.raw0);
            int raw1AsInt32 = unchecked((int)raw.raw1);
            int raw2AsInt32 = unchecked((int)raw.raw2);
            float raw0AsFloat = BitConverter.Int32BitsToSingle(raw0AsInt32);
            float raw1AsFloat = BitConverter.Int32BitsToSingle(raw1AsInt32);
            float raw2AsFloat = BitConverter.Int32BitsToSingle(raw2AsInt32);
            return $"raw0=0x{raw.raw0:X8}[int={raw0AsInt32} float={raw0AsFloat}] raw1=0x{raw.raw1:X8}[int={raw1AsInt32} float={raw1AsFloat}] raw2=0x{raw.raw2:X8}[int={raw2AsInt32} float={raw2AsFloat}] raw3={raw.raw3}";
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

        // Read-only self-process module enumeration, identical mechanism
        // to Root26ManagedSelfTraceProbe (chapter 79).
        private static IntPtr FindGameAssemblyBase()
        {
            return FindModuleBase("GameAssembly.dll");
        }

        // Chapter 99: same read-only mechanism, resolved once and cached.
        private static IntPtr ResolveSteamclientBase()
        {
            if (_steamclientBaseResolved)
            {
                return _steamclientBase;
            }
            _steamclientBase = FindModuleBase("steamclient64.dll");
            if (_steamclientBase != IntPtr.Zero)
            {
                _steamclientBaseResolved = true;
            }
            return _steamclientBase;
        }

        private static IntPtr FindModuleBase(string moduleName)
        {
            using Process current = Process.GetCurrentProcess();
            foreach (ProcessModule module in current.Modules)
            {
                try
                {
                    if (string.Equals(module.ModuleName, moduleName, StringComparison.OrdinalIgnoreCase))
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

        // Chapter 99: literal, read-only reproduction of the outer
        // GetAnalogActionData wrapper's "effective R8" computation
        // (chapter 95), plus the two candidate inner+0x109708 fields it
        // is compared against. Returns false (and does not populate the
        // out values) if steamclient64.dll's base or either inner
        // pointer is not yet resolvable this frame - this frame's
        // attempt is then simply skipped (no state mutation), consistent
        // with every other Root-26 probe's retry philosophy.
        private static bool TryComputeChapter99Values(
            IntPtr managedSelfCandidate, IntPtr flatSelf,
            out uint effectiveR8, out uint managedField, out uint flatField,
            out uint globalA, out byte selector, out uint globalC)
        {
            effectiveR8 = 0;
            managedField = 0;
            flatField = 0;
            globalA = 0;
            selector = 0;
            globalC = 0;

            IntPtr steamclientBase = ResolveSteamclientBase();
            if (steamclientBase == IntPtr.Zero)
            {
                return false;
            }

            try
            {
                globalA = unchecked((uint)Marshal.ReadInt32(IntPtr.Add(steamclientBase, checked((int)SteamclientGlobalARva))));
                selector = Marshal.ReadByte(IntPtr.Add(steamclientBase, checked((int)SteamclientGlobalSelectorRva)));
                globalC = unchecked((uint)Marshal.ReadInt32(IntPtr.Add(steamclientBase, checked((int)SteamclientGlobalCRva))));
            }
            catch (Exception ex)
            {
                LogErrorOnce("reading steamclient64.dll globals threw " + Describe(ex));
                return false;
            }

            // Chapter 95: "and r8d, 0xffffff" then, only if selector==2,
            // "cmove r8d, [globalC]" - reproduced literally, not
            // simplified.
            uint maskedGlobalA = globalA & 0x00FFFFFFU;
            effectiveR8 = selector == 2 ? globalC : maskedGlobalA;

            if (managedSelfCandidate == IntPtr.Zero || flatSelf == IntPtr.Zero)
            {
                return false;
            }

            IntPtr managedInner;
            IntPtr flatInner;
            try
            {
                managedInner = Marshal.ReadIntPtr(IntPtr.Add(managedSelfCandidate, InnerObjectOffset));
                flatInner = Marshal.ReadIntPtr(IntPtr.Add(flatSelf, InnerObjectOffset));
            }
            catch (Exception ex)
            {
                LogErrorOnce("reading inner pointers (outer+0x8) threw " + Describe(ex));
                return false;
            }

            if (managedInner == IntPtr.Zero || flatInner == IntPtr.Zero)
            {
                return false;
            }

            try
            {
                managedField = unchecked((uint)Marshal.ReadInt32(IntPtr.Add(managedInner, InnerStoredFieldOffset)));
                flatField = unchecked((uint)Marshal.ReadInt32(IntPtr.Add(flatInner, InnerStoredFieldOffset)));
            }
            catch (Exception ex)
            {
                LogErrorOnce("Marshal.ReadInt32(inner+0x109708) threw " + Describe(ex));
                return false;
            }

            return true;
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
