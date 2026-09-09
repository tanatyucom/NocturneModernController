using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using MelonLoader;

namespace NocturneModernController
{
    // Root-26 Chapter 82 Phase A: read-only vtable identity comparison
    // between the two candidate "ISteamInput* self" pointers CONFIRMED
    // (chapter 81) to be numerically different at runtime:
    //   managedSelfCandidate (from GameAssembly.dll's cached pointer
    //   chain, chapter 78/79) vs. flatSelf (SteamAPI_SteamInput_v002()).
    //
    // Purpose: determine whether the two selfs point to the SAME
    // ISteamInput vtable (i.e. the same implementation, just a different
    // instance/session - Case V1) or to DIFFERENT vtables (Case V2/V3 -
    // a different wrapper/interface object entirely). This does NOT call
    // any function through either vtable - it only reads and compares
    // pointer VALUES.
    //
    // Only vtable slot offsets already CONFIRMED via capstone
    // disassembly of steam_api64.dll's flat thunks in prior chapters are
    // used here - no new/guessed offsets:
    //   GetConnectedControllers : +0x18 (chapter 65.3)
    //   GetActionSetHandle      : +0x20 (chapter 68.4/74)
    //   GetCurrentActionSet     : +0x30 (chapter 65.3)
    //   GetAnalogActionData     : +0x78 (chapter 72.2)
    //
    // Safety:
    //   - Marshal.ReadIntPtr ONLY. No Marshal.Write* of any kind.
    //   - Function pointer values are read and logged, but NEVER called/
    //     invoked (no Marshal.GetDelegateForFunctionPointer, no function
    //     pointer invocation of any kind) - this is a pure data
    //     comparison, not an alternate call path.
    //   - Every dereference wrapped in try/catch; on exception this
    //     probe just logs a warning and stops (does not destabilize the
    //     mod or the caller).
    //   - No native detour/patch/injection/hook of any module. Module
    //     range lookup uses only Process.Modules (self-introspection of
    //     the mod's own process), the same read-only mechanism already
    //     used by Root26ManagedSelfTraceProbe.
    //   - No Steam API call of any kind is made from this class.
    internal static class Root26VtableIdentityProbe
    {
        private const string Tag = "[NocturneModernController][Root26VtableIdentity]";

        private const int SlotGetConnectedControllers = 0x18;
        private const int SlotGetActionSetHandle = 0x20;
        private const int SlotGetCurrentActionSet = 0x30;
        private const int SlotGetAnalogActionData = 0x78;

        // Called once, from Root26ManagedSelfTraceProbe, right after both
        // selfs are confirmed non-zero. Not registered separately in
        // ModMain.OnUpdate() - this is a follow-up analysis step, not an
        // independent polling probe.
        internal static void Compare(IntPtr managedSelfCandidate, IntPtr flatSelf)
        {
            IntPtr managedVtable;
            IntPtr flatVtable;
            try
            {
                managedVtable = Marshal.ReadIntPtr(managedSelfCandidate);
                flatVtable = Marshal.ReadIntPtr(flatSelf);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"{Tag} reading vtable pointers threw {ex.GetType().FullName}: {ex.Message} - aborting.");
                return;
            }

            bool sameVtable = managedVtable == flatVtable;
            MelonLogger.Msg(
                $"{Tag} managedSelf=0x{managedSelfCandidate.ToInt64():X} flatSelf=0x{flatSelf.ToInt64():X} " +
                $"managedVtable=0x{managedVtable.ToInt64():X} flatVtable=0x{flatVtable.ToInt64():X} " +
                $"sameVtable={sameVtable} at {DateTimeOffset.Now:O}");

            LogSlotComparison("GetConnectedControllers", managedVtable, flatVtable, SlotGetConnectedControllers);
            LogSlotComparison("GetActionSetHandle", managedVtable, flatVtable, SlotGetActionSetHandle);
            LogSlotComparison("GetCurrentActionSet", managedVtable, flatVtable, SlotGetCurrentActionSet);
            LogSlotComparison("GetAnalogActionData", managedVtable, flatVtable, SlotGetAnalogActionData);
        }

        private static void LogSlotComparison(string apiName, IntPtr managedVtable, IntPtr flatVtable, int slotOffset)
        {
            IntPtr managedFn;
            IntPtr flatFn;
            try
            {
                managedFn = Marshal.ReadIntPtr(IntPtr.Add(managedVtable, slotOffset));
                flatFn = Marshal.ReadIntPtr(IntPtr.Add(flatVtable, slotOffset));
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"{Tag} {apiName} slot+0x{slotOffset:X} read threw {ex.GetType().FullName}: {ex.Message} - skipping this slot.");
                return;
            }

            bool sameFn = managedFn == flatFn;
            string managedModule = DescribeModuleRange(managedFn);
            string flatModule = DescribeModuleRange(flatFn);

            MelonLogger.Msg(
                $"{Tag} {apiName}(+0x{slotOffset:X}) managedFn=0x{managedFn.ToInt64():X}[{managedModule}] " +
                $"flatFn=0x{flatFn.ToInt64():X}[{flatModule}] sameFn={sameFn} at {DateTimeOffset.Now:O}");
        }

        // Read-only: checks which already-loaded module's address range
        // (if any) a function pointer value falls within, using only
        // Process.Modules (no injection, no external tool, no writing).
        // Chapter 86 follow-up: also reports the module's runtime
        // BaseAddress and the resulting RVA (address - BaseAddress), so
        // that the static VA seen in one session's log can be mapped back
        // to a file offset in the on-disk DLL for later static
        // disassembly - without ever assuming/guessing a fixed load
        // address (ASLR relocates the module differently each run).
        private static string DescribeModuleRange(IntPtr address)
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
                            long rva = addr - baseAddr;
                            return $"{name} moduleBase=0x{baseAddr:X} rva=0x{rva:X}";
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

            return "unknown-module";
        }
    }
}
