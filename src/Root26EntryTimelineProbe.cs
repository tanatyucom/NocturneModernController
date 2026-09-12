using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Il2Cpp;
using MelonLoader;

namespace NocturneModernController
{
    // Root-26 Chapter 103: read-only runtime timeline observation of the
    // per-(controllerSlot, analogAction) entry that 101章CONFIRMED as the
    // sole source of GetAnalogActionData's eMode/x/y/bActive output for
    // the managed route:
    //
    //   entryOffset = (controllerSlotIndex * 0x4e + (analogActionHandle - 1)) * 0xd
    //   entry = managedInner + 0x1097a2 + entryOffset
    //     +0x0 eMode (int32)
    //     +0x4 x     (float)
    //     +0x8 y     (float)
    //     +0xc bActive (byte)
    //
    // managedInner = *(managedSelfCandidate + 0x8) (chapter 87/88's
    // CONFIRMED outer->inner delegation offset). flatSelf is explicitly
    // OUT OF SCOPE this chapter - chapter 100 already closed that
    // question (F1 CONFIRMED). This probe tracks the MANAGED route only.
    //
    // controllerSlotIndex is NOT assumed - it is found at runtime by
    // linearly searching managedInner + 0x10970d (16 entries, stride
    // 0x3f6 bytes, chapter 90/95/101 CONFIRMED table of controller
    // inputHandles) for the RSTICK's actual inputHandle, read fresh each
    // frame from the same existing managed state other Root-26 probes
    // use (pad.Controller.Keys). analogActionHandle is likewise read
    // from the existing managed binding
    // (ActionSets[Current].Analog[1].Handle, the same i=1->IG_RSTICK
    // convention established since chapter 66/74/84) - never hardcoded
    // to a literal 2.
    //
    // Chapter 102 found a related "change-detection" function using the
    // SAME entryOffset formula, comparing entry against a candidate cache
    // at managedInner + 0x10d706 + entryOffset (same 0/4/8/0xc field
    // layout as entry). Chapter 102 could not confirm this is a "previous
    // value" cache via symbols, so this probe reports it as
    // "cacheCandidate" - a neutral name, not asserting semantics. Also
    // observed: managedInner+0x190 (a pointer/handler reference, null-
    // checked in chapter 102's function) and managedInner+0x116780 (a
    // 4-byte counter read via [self+0x116780] in the same function) -
    // both read here as-is, read-only, without asserting meaning.
    //
    // No new Steam API call of any kind. No ActivateActionSet/
    // ActivateActionSetLayer/ResetController/SteamControllerReStart/
    // Shutdown/Init/UpdateConnectedControllers call. No SendInput, no
    // Guide spoofing, no native detour/patch/injection/hook of any
    // module. Only Marshal.Read* is used - never Marshal.Write*.
    // Cross-correlation with existing probes (OverlayActivated/
    // ResetController/SteamControllerReStart/GetAnalogActionData) is done
    // purely via shared DateTimeOffset.Now:O timestamp format matching
    // existing log conventions - no new calls are made to observe them.
    internal static class Root26EntryTimelineProbe
    {
        private const string Tag = "[NocturneModernController][Root26EntryTimeline]";
        private const int RstickAnalogIndex = 1; // Same convention as Root26Phase2AnalogActionDataProbe (i=1 -> IG_RSTICK).

        // Chapter 78.2/79.1: cached-pointer-chain constants, identical to
        // every prior Root-26 probe using this chain.
        private const long StaticImageBase = 0x180000000L;
        private const long TargetVa = 0x182E4F3E0L;
        private const long TargetRva = TargetVa - StaticImageBase;
        private const int ChainOffset1 = 0xB8;
        private const int ChainOffset2 = 0xB0;
        private const int InnerObjectOffset = 0x8; // Chapter 87/88 CONFIRMED.

        // Chapter 90/95/101 CONFIRMED controller-slot table.
        private const int ControllerTableOffset = 0x10970d;
        private const int ControllerTableStride = 0x3f6;
        private const int ControllerTableCount = 16;

        // Chapter 101 CONFIRMED entry table.
        private const int EntryTableBase = 0x1097a2;
        private const int EntryStride = 0xd;
        private const int ControllerSlotMultiplier = 0x4e;

        // Chapter 102: neutral "cacheCandidate" - semantics NOT asserted.
        private const int CacheTableBase = 0x10d706;

        // Chapter 102: additional related fields, semantics NOT asserted.
        private const int Field190Offset = 0x190;
        private const int Field116780Offset = 0x116780;

        // Root-26 "current DLL" re-verification session (steamclient64.dll
        // SHA-256 ea23997e2b376df52bf9bbd3f6d2ba628c3b669b45381c03948fe209a7a0e36f,
        // FileVersion 10.98.06.80): fresh disassembly of the CURRENT
        // GetAnalogActionData implementation (found via vtable fingerprint,
        // not a stale RVA) re-confirmed the exact same field-offset layout
        // this probe already used (entry base/stride/controller table all
        // unchanged across the Steam update). Two NEW fields were found in
        // that pass, unconditionally touched by every single
        // GetAnalogActionData call:
        //   self+0x11677c (int32) is read and copied into self+0x11678c
        //     every call ("mov eax,[self+0x11677c]; mov [self+0x11678c],eax").
        //   self+0x1167a0 is the base of a 16-slot*24-action (384 byte)
        //     per-(controllerSlot,analogAction) BYTE flag array, indexed as
        //     self+0x1167a0+(controllerSlotIndex*24+actionIndex) - written
        //     to 1 by GetAnalogActionData itself every call (a downstream
        //     "have I cached a previous value for this combo" bookkeeping
        //     flag, not the real entry). The OLD rawDump below already
        //     covers +0x11677c/+0x116780/+0x11678c as fixed-offset reads
        //     (labeled raw11677C/raw116780/raw11678C), but its
        //     raw1167A0 entry is a FIXED read of slot=0/action=0's flag -
        //     not necessarily this entry's own flag. EntryFlagOffset below
        //     reads the CORRECT per-entry flag byte using the same
        //     controllerSlotIndex/actionIndex this probe already computes
        //     for entry/cacheCandidate.
        private const int Field11677cOffset = 0x11677c;
        private const int Field11678cOffset = 0x11678c;
        private const int EntryFlagTableBase = 0x1167a0;
        private const int EntryFlagStride = 24;

        // Chapter 90/95/101/100 CONFIRMED "effectiveR8" gate field,
        // re-verified this session on the current DLL
        // ("cmp r14d, dword ptr [self+0x109708]; jne early-exit").
        private const int EffectiveR8GateOffset = 0x109708;

        // Chapter 107: raw, layout-neutral dump of the neighborhood around
        // +0x116780 (106章でCONFIRMED済みの唯一のdirect write site,
        // an object-construction-time zero-clear, does not explain the
        // 0x100->0x104 progression 105章observed - so this probe now also
        // watches the surrounding 4-byte words, read-only, without
        // asserting meaning for any of them). Range chosen to comfortably
        // bracket +0x116780 on both sides while staying within the
        // constructor's zero-cleared block (106.2's disassembly showed
        // adjacent fields +0x116778/+0x116788/+0x116775 also zero-cleared
        // there, all within this range).
        private const int RawDumpStart = 0x116760;
        private const int RawDumpEnd = 0x1167A0; // inclusive
        private const int RawDumpStep = 4;

        private sealed class PerHandleState
        {
            internal string? LastSummary;
        }

        private static readonly Dictionary<ulong, PerHandleState> PerHandle = new();

        private static bool _gameAssemblyBaseResolved;
        private static IntPtr _gameAssemblyBase = IntPtr.Zero;
        private static bool _errorLogged;

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
            if (managedSelfCandidate == IntPtr.Zero)
            {
                return;
            }

            IntPtr managedInner;
            try
            {
                managedInner = Marshal.ReadIntPtr(IntPtr.Add(managedSelfCandidate, InnerObjectOffset));
            }
            catch (Exception ex)
            {
                LogErrorOnce("reading managedInner (managedSelf+0x8) threw " + Describe(ex));
                return;
            }
            if (managedInner == IntPtr.Zero)
            {
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
                    SampleHandle(pad, handleRaw, managedSelfCandidate, managedInner);
                }
            }
            catch (Exception ex)
            {
                LogErrorOnce("pad.Controller.Keys iteration threw " + Describe(ex));
            }
        }

        private static void SampleHandle(SteamPad pad, ulong handleRaw, IntPtr managedOuter, IntPtr managedInner)
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

            // Read the actual RSTICK analogActionHandle from existing
            // managed state - never hardcoded to a literal 2.
            ulong analogActionHandleRaw = analogList[RstickAnalogIndex].Handle.m_InputAnalogActionHandle;
            if (analogActionHandleRaw == 0 || analogActionHandleRaw > 0xFFFFFFFFUL)
            {
                // Out of the plausible int range this table indexes with;
                // skip this frame rather than compute a nonsensical offset.
                return;
            }

            int controllerSlotIndex = FindControllerSlotIndex(managedInner, handleRaw);
            if (controllerSlotIndex < 0)
            {
                // Not found this frame (chapter 90/95/101 table not yet
                // populated for this handle, or a transient state) -
                // simply skip; no state mutation, retried next frame.
                return;
            }

            int actionIndex = checked((int)(analogActionHandleRaw - 1));
            long entryOffset = (controllerSlotIndex * (long)ControllerSlotMultiplier + actionIndex) * EntryStride;
            int entryFlagIndex = controllerSlotIndex * EntryFlagStride + actionIndex;

            IntPtr entryBase;
            IntPtr cacheBase;
            IntPtr entryFlagAddress;
            try
            {
                entryBase = IntPtr.Add(managedInner, checked((int)(EntryTableBase + entryOffset)));
                cacheBase = IntPtr.Add(managedInner, checked((int)(CacheTableBase + entryOffset)));
                entryFlagAddress = IntPtr.Add(managedInner, checked(EntryFlagTableBase + entryFlagIndex));
            }
            catch (Exception ex)
            {
                LogErrorOnce("computing entry/cache/flag addresses threw " + Describe(ex));
                return;
            }

            int eMode, cacheEMode;
            float x, y, cacheX, cacheY;
            byte bActive, cacheBActive;
            IntPtr field190;
            int field116780;
            int field11677c;
            int field11678c;
            byte entryFlag;
            int effectiveR8Gate;
            try
            {
                eMode = Marshal.ReadInt32(entryBase);
                x = BitConverter.Int32BitsToSingle(Marshal.ReadInt32(IntPtr.Add(entryBase, 4)));
                y = BitConverter.Int32BitsToSingle(Marshal.ReadInt32(IntPtr.Add(entryBase, 8)));
                bActive = Marshal.ReadByte(IntPtr.Add(entryBase, 0xc));

                cacheEMode = Marshal.ReadInt32(cacheBase);
                cacheX = BitConverter.Int32BitsToSingle(Marshal.ReadInt32(IntPtr.Add(cacheBase, 4)));
                cacheY = BitConverter.Int32BitsToSingle(Marshal.ReadInt32(IntPtr.Add(cacheBase, 8)));
                cacheBActive = Marshal.ReadByte(IntPtr.Add(cacheBase, 0xc));

                field190 = Marshal.ReadIntPtr(IntPtr.Add(managedInner, Field190Offset));
                field116780 = Marshal.ReadInt32(IntPtr.Add(managedInner, Field116780Offset));
                field11677c = Marshal.ReadInt32(IntPtr.Add(managedInner, Field11677cOffset));
                field11678c = Marshal.ReadInt32(IntPtr.Add(managedInner, Field11678cOffset));
                entryFlag = Marshal.ReadByte(entryFlagAddress);
                effectiveR8Gate = Marshal.ReadInt32(IntPtr.Add(managedInner, EffectiveR8GateOffset));
            }
            catch (Exception ex)
            {
                LogErrorOnce("reading entry/cache/related fields threw " + Describe(ex));
                return;
            }

            string rawDumpSummary;
            try
            {
                rawDumpSummary = DescribeRawDump(managedInner);
            }
            catch (Exception ex)
            {
                LogErrorOnce("reading raw neighborhood dump threw " + Describe(ex));
                return;
            }

            string summary =
                $"managedOuter=0x{managedOuter.ToInt64():X} managedInner=0x{managedInner.ToInt64():X} " +
                $"inputHandle={handleRaw} controllerSlotIndex={controllerSlotIndex} analogActionHandle={analogActionHandleRaw} " +
                $"entry(eMode={eMode} x={x} y={y} bActive={bActive}) " +
                $"cacheCandidate(eMode={cacheEMode} x={cacheX} y={cacheY} bActive={cacheBActive}) " +
                $"field190=0x{field190.ToInt64():X} field116780=0x{field116780:X8} " +
                $"field11677c=0x{field11677c:X8} field11678c=0x{field11678c:X8} " +
                $"entryFlag1167a0[{entryFlagIndex}]={entryFlag} effectiveR8Gate109708=0x{effectiveR8Gate:X8} " +
                $"rawDump({rawDumpSummary})";

            if (!PerHandle.TryGetValue(handleRaw, out PerHandleState? state))
            {
                state = new PerHandleState();
                PerHandle[handleRaw] = state;
            }

            if (state.LastSummary != summary)
            {
                MelonLogger.Msg($"{Tag} STATE-CHANGE handle={handleRaw} {summary} at {DateTimeOffset.Now:O}");
                state.LastSummary = summary;
            }
        }

        // Chapter 107: layout-neutral raw dump of the neighborhood around
        // +0x116780, read-only, with neutral field names ("raw" + hex
        // offset) - no semantics asserted. Used to find which nearby
        // 4-byte word(s), if any, change in lockstep with field116780's
        // 0x100->0x104 progression.
        private static string DescribeRawDump(IntPtr managedInner)
        {
            var parts = new List<string>();
            for (int off = RawDumpStart; off <= RawDumpEnd; off += RawDumpStep)
            {
                int value = Marshal.ReadInt32(IntPtr.Add(managedInner, off));
                parts.Add($"raw{off:X}=0x{value:X8}");
            }
            return string.Join(" ", parts);
        }

        // Chapter 90/95/101 CONFIRMED table: managedInner+0x10970d, up to
        // 16 entries of stride 0x3f6 bytes, first 8 bytes of each entry =
        // that slot's controller inputHandle. Returns -1 if not found
        // this frame (never throws for that reason).
        private static int FindControllerSlotIndex(IntPtr managedInner, ulong inputHandle)
        {
            try
            {
                IntPtr tableBase = IntPtr.Add(managedInner, ControllerTableOffset);
                for (int i = 0; i < ControllerTableCount; i++)
                {
                    IntPtr entryAddr = IntPtr.Add(tableBase, i * ControllerTableStride);
                    long candidate = Marshal.ReadInt64(entryAddr);
                    if (unchecked((ulong)candidate) == inputHandle)
                    {
                        return i;
                    }
                }
            }
            catch (Exception ex)
            {
                LogErrorOnce("controller-slot table scan threw " + Describe(ex));
            }
            return -1;
        }

        // Re-derived every call (cheap, read-only), same chain as
        // Root26ManagedSelfTraceProbe/Root26AnalogDataSelfSwapProbe/
        // Root26StoredHandleCompareProbe.
        private static IntPtr ResolveManagedSelfCandidate()
        {
            if (!_gameAssemblyBaseResolved)
            {
                _gameAssemblyBase = FindModuleBase("GameAssembly.dll");
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
                IntPtr p1 = Marshal.ReadIntPtr(IntPtr.Add(p0, ChainOffset1));
                if (p1 == IntPtr.Zero)
                {
                    return IntPtr.Zero;
                }
                return Marshal.ReadIntPtr(IntPtr.Add(p1, ChainOffset2));
            }
            catch (Exception ex)
            {
                LogErrorOnce("managedSelfCandidate pointer-chain read threw " + Describe(ex));
                return IntPtr.Zero;
            }
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
