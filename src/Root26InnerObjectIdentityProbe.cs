using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using MelonLoader;

namespace NocturneModernController
{
    // Root-26 Chapter 88 Phase B: read-only, one-shot diagnostic that
    // isolates THREE layers between managedSelfCandidate and flatSelf
    // (both already CONFIRMED to numerically differ - chapter 81 - while
    // sharing the same OUTER vtable - chapter 83's Case V1):
    //
    //   outer self       : managedSelfCandidate vs flatSelf (chapter 81/83)
    //   inner pointer     : *(outer self + 0x8)
    //   inner vtable      : *(inner pointer)
    //
    // Chapter 87 CONFIRMED (via capstone disassembly of steamclient64.dll,
    // read-only - the file is never executed/patched/injected/hooked by
    // this probe or by that static analysis) that ALL FOUR already-
    // exercised vtable slots (GetAnalogActionData/GetActionSetHandle/
    // GetCurrentActionSet/GetConnectedControllers) begin by delegating
    // through "mov rcx, [rcx+8]" - i.e. the "self" passed to these APIs
    // is a thin outer wrapper, and +0x8 is the ONLY self-relative offset
    // confirmed (via actual machine code, not guessed) to lead to the
    // real inner object that the API's logic operates on.
    //
    // This probe does NOT yet read any INNER vtable slot (chapter 87.4/88
    // deliberately defer that - the inner vtable's slot layout has not
    // been independently confirmed to match the outer vtable's slot
    // numbering, e.g. "+0x78" may not mean the same thing on the inner
    // vtable). It only compares pointer VALUES at three layers:
    // outer/inner/inner-vtable - never calling through any of them.
    //
    // Runs once per session, right after Root26ManagedSelfTraceProbe (and
    // Root26VtableIdentityProbe) have resolved managedSelfCandidate and
    // flatSelf as non-zero - reuses those exact values rather than
    // re-deriving them, so this is a strict continuation of the same
    // resolved snapshot (no risk of comparing values from different
    // frames).
    //
    // Safety:
    //   - Marshal.ReadIntPtr ONLY. No Marshal.Write* of any kind.
    //   - No new Steam API call of any kind (both selfs are passed in
    //     from the caller, already resolved by Root26ManagedSelfTraceProbe).
    //   - No function pointer is ever invoked - pure pointer-value
    //     comparison.
    //   - Every dereference wrapped in try/catch; on exception this
    //     probe logs a warning and stops (never destabilizes the mod).
    //   - No native detour/patch/injection/hook of any module. Module
    //     range lookup uses only Process.Modules (self-introspection),
    //     the same read-only mechanism as chapters 79/82/86.
    internal static class Root26InnerObjectIdentityProbe
    {
        private const string Tag = "[NocturneModernController][Root26InnerObjectIdentity]";
        private const int InnerObjectOffset = 0x8; // Chapter 87: the ONLY self-relative offset CONFIRMED via disassembly of all 4 vtable-slot implementations.

        // Called once, from Root26ManagedSelfTraceProbe, right after
        // Root26VtableIdentityProbe.Compare(...). Not registered
        // separately in ModMain.OnUpdate() - a follow-up analysis step.
        internal static void Compare(IntPtr managedOuter, IntPtr flatOuter)
        {
            IntPtr managedInner;
            IntPtr flatInner;
            try
            {
                managedInner = Marshal.ReadIntPtr(IntPtr.Add(managedOuter, InnerObjectOffset));
                flatInner = Marshal.ReadIntPtr(IntPtr.Add(flatOuter, InnerObjectOffset));
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"{Tag} reading inner pointers (outer+0x8) threw {ex.GetType().FullName}: {ex.Message} - aborting.");
                return;
            }

            bool sameOuter = managedOuter == flatOuter;
            bool sameInner = managedInner == flatInner;

            MelonLogger.Msg(
                $"{Tag} managedOuter=0x{managedOuter.ToInt64():X} flatOuter=0x{flatOuter.ToInt64():X} sameOuter={sameOuter} " +
                $"managedInner=0x{managedInner.ToInt64():X}[{DescribeAddress(managedInner)}] " +
                $"flatInner=0x{flatInner.ToInt64():X}[{DescribeAddress(flatInner)}] sameInner={sameInner} " +
                $"at {DateTimeOffset.Now:O}");

            if (managedInner == IntPtr.Zero || flatInner == IntPtr.Zero)
            {
                MelonLogger.Msg($"{Tag} at least one inner pointer is null - skipping inner vtable comparison at {DateTimeOffset.Now:O}");
                return;
            }

            IntPtr managedInnerVtable;
            IntPtr flatInnerVtable;
            try
            {
                managedInnerVtable = Marshal.ReadIntPtr(managedInner);
                flatInnerVtable = Marshal.ReadIntPtr(flatInner);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"{Tag} reading inner vtable pointers threw {ex.GetType().FullName}: {ex.Message} - aborting.");
                return;
            }

            bool sameInnerVtable = managedInnerVtable == flatInnerVtable;

            MelonLogger.Msg(
                $"{Tag} managedInnerVtable=0x{managedInnerVtable.ToInt64():X}[{DescribeAddress(managedInnerVtable)}] " +
                $"flatInnerVtable=0x{flatInnerVtable.ToInt64():X}[{DescribeAddress(flatInnerVtable)}] " +
                $"sameInnerVtable={sameInnerVtable} at {DateTimeOffset.Now:O}");
        }

        // Read-only: reports which already-loaded module's address range
        // (if any) contains the given address, or "heap/unknown" if it
        // falls outside every loaded module's range (deliberately not
        // interpreted further - no meaning is guessed for such
        // addresses).
        private static string DescribeAddress(IntPtr address)
        {
            long addr = address.ToInt64();
            if (addr == 0)
            {
                return "null";
            }

            try
            {
                using Process current = Process.GetCurrentProcess();
                foreach (ProcessModule module in current.Modules)
                {
                    try
                    {
                        long baseAddr = module.BaseAddress.ToInt64();
                        long size = module.ModuleMemorySize;
                        if (addr >= baseAddr && addr < baseAddr + size)
                        {
                            string name = module.ModuleName ?? "unnamed-module";
                            return $"{name} rva=0x{addr - baseAddr:X}";
                        }
                    }
                    finally
                    {
                        module.Dispose();
                    }
                }
            }
            catch (Exception ex)
            {
                return "module-lookup-error:" + ex.GetType().Name;
            }

            return "heap/unknown";
        }
    }
}
