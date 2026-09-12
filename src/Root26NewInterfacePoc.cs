using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using HarmonyLib;
using Il2Cpp;
using MelonLoader;

namespace NocturneModernController
{
    // Root-26 "A1" PoC.
    //
    // Phase 1 (CONFIRMED, runtime): SteamInternal_FindOrCreateUserInterface
    // (HSteamUser, "SteamInput007") returns a NON-NULL pointer that is
    // DIFFERENT from SteamAPI_SteamInput_v002()'s pointer.
    //
    // Phase 2 attempt (REJECTED, runtime): calling
    // SteamAPI_ISteamInput_GetAnalogActionData with the "new" (v007) self
    // pointer, via the same flat export/vtable-slot-0x78 assumption
    // validated for v002 vs RAIDOU's v006 flat wrapper, caused a
    // reproducible access violation (0xc0000005) inside coreclr.dll at the
    // SAME faulting offset across two crashes. The ROOT CAUSE is
    // UNRESOLVED - it could be an ABI/P-Invoke declaration mismatch, a
    // different vtable layout for whatever object FindOrCreateUserInterface
    // actually returned, slot 0x78 being a different method on that
    // object, or the returned pointer being a different kind of object
    // entirely (e.g. a session-scoped proxy vs the raw singleton). Calling
    // through newSelf is now PERMANENTLY REJECTED for this PoC file.
    //
    // Phase 3 (this version): pure read-only vtable IDENTITY inspection.
    // NO function pointer is ever invoked here - only Marshal.ReadIntPtr
    // is used, first to read each self pointer's own vtable pointer
    // (offset 0, the standard C++/COM object-header convention already
    // relied upon elsewhere in this codebase - e.g.
    // Root26LastInputIndexVtableTargetProbe's vtableSlot0 read), then to
    // read a run of vtable SLOT VALUES (function pointers, still never
    // called) for both oldSelf(v002) and newSelf(SteamInput007), then to
    // resolve which loaded module (if any) each slot value's address falls
    // inside via a one-time Process.Modules snapshot (same read-only
    // pattern as Root26LastInputIndexVtableTargetProbe - no per-frame
    // re-enumeration).
    //
    // Phase 3.5 (this version, additive): static disassembly of
    // steamclient64.dll+0x71F120 (confirmed this session to be the SAME
    // code address as both oldSelf's slot15 AND newSelf's slot21) showed
    // it dereferences self+0x8 ("inner") and tail-dispatches through
    // inner's own vtable slot 0x78. To check whether that "inner" object
    // has the same shape for oldSelf and newSelf before ever calling
    // slot21, this version ALSO read-only reads *(self+0x8) ("inner") for
    // both self pointers, then inner's own vtable pointer (offset 0), then
    // a run of inner vtable SLOT VALUES - again, only Marshal.ReadIntPtr,
    // nothing is ever invoked.
    //
    // Scope / safety:
    //  - Only Steam Input APIs called: SteamAPI_SteamInput_v002 (existing,
    //    read-only getter), SteamAPI_GetHSteamUser (read-only getter),
    //    SteamInternal_FindOrCreateUserInterface (read-only resolver, same
    //    mechanism steam_api64.dll uses internally for every interface -
    //    confirmed via a real call site this session). NOTHING is called
    //    through either resulting self pointer - only Marshal.ReadIntPtr
    //    against their own memory (vtable pointer, then vtable slot
    //    values as raw addresses).
    //  - No ActivateActionSet/GetAnalogActionData/RunFrame/any other
    //    Steam Input API. No native hook/injection/patch of
    //    steamclient64.dll/GameOverlayRenderer64.dll/steam_api64.dll.
    //    No memory writes anywhere.
    //  - Default DISABLED (Enabled = false). Flip to true and rebuild only
    //    for the deliberate test run.
    //  - Runs at most once per session (guarded by _done). No per-frame
    //    logging.
    [HarmonyPatch(typeof(SteamInputUtil), nameof(SteamInputUtil.UpdateInput))]
    internal static class Root26NewInterfacePoc
    {
        // Flip to true and rebuild for the test run. Leave false otherwise.
        private static readonly bool Enabled = false;

        private const string Tag = "[NocturneModernController][Root26NewInterfacePoc]";
        private const string CandidateVersion = "SteamInput007";
        private const int VtableSlotsToRead = 32;

        [DllImport("steam_api64.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr SteamAPI_SteamInput_v002();

        [DllImport("steam_api64.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int SteamAPI_GetHSteamUser();

        [DllImport("steam_api64.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        private static extern IntPtr SteamInternal_FindOrCreateUserInterface(int hSteamUser, string pszVersion);

        private static bool _done;
        private static bool _moduleSnapshotTaken;
        private static (string Name, long Base, long Size)[] _moduleSnapshot = Array.Empty<(string, long, long)>();

        private static void Prefix()
        {
            if (!Enabled || _done)
            {
                return;
            }
            _done = true;

            try
            {
                IntPtr oldSelf = SteamAPI_SteamInput_v002();
                int hUser = SteamAPI_GetHSteamUser();
                IntPtr newSelf = SteamInternal_FindOrCreateUserInterface(hUser, CandidateVersion);

                MelonLogger.Msg(
                    $"{Tag} hSteamUser={hUser} " +
                    $"oldSelf(v002)=0x{oldSelf.ToInt64():X} " +
                    $"newSelf({CandidateVersion})=0x{newSelf.ToInt64():X} " +
                    $"newSelf==NULL:{newSelf == IntPtr.Zero} " +
                    $"oldSelf==newSelf:{oldSelf == newSelf}");

                LogVtable("oldSelf(v002)", oldSelf);
                LogVtable($"newSelf({CandidateVersion})", newSelf);

                LogInner("oldSelf(v002)", oldSelf);
                LogInner($"newSelf({CandidateVersion})", newSelf);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"{Tag} threw {ex.GetType().FullName}: {ex.Message}");
            }
        }

        // Read-only: reads the object's own vtable pointer (offset 0,
        // standard C++ object header convention), then reads
        // VtableSlotsToRead consecutive slot VALUES (raw addresses) -
        // never dereferences or calls any of them.
        private static void LogVtable(string label, IntPtr self)
        {
            if (self == IntPtr.Zero)
            {
                MelonLogger.Msg($"{Tag} {label}: self is NULL, skipping vtable read.");
                return;
            }

            IntPtr vtable;
            try
            {
                vtable = Marshal.ReadIntPtr(self);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"{Tag} {label}: reading vtable pointer threw {ex.GetType().FullName}: {ex.Message}");
                return;
            }

            MelonLogger.Msg($"{Tag} {label}: self=0x{self.ToInt64():X} vtable=0x{vtable.ToInt64():X}");

            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < VtableSlotsToRead; i++)
            {
                IntPtr slotValue;
                try
                {
                    slotValue = Marshal.ReadIntPtr(vtable, i * 8);
                }
                catch (Exception ex)
                {
                    sb.Append($"slot{i}=READ-ERROR({ex.GetType().Name}) ");
                    continue;
                }
                string moduleDesc = ResolveModuleAndRva(slotValue);
                sb.Append($"slot{i}=0x{slotValue.ToInt64():X}[{moduleDesc}] ");
            }
            MelonLogger.Msg($"{Tag} {label}: {sb}");
        }

        // Read-only: reads self+0x8 (the "inner" pointer used by
        // steamclient64.dll+0x71F120's "mov rcx,[rcx+8]" - confirmed via
        // static disassembly this session), then that inner object's own
        // vtable pointer (offset 0) and its first VtableSlotsToRead slot
        // VALUES - never dereferences or calls any of them.
        private static void LogInner(string label, IntPtr self)
        {
            if (self == IntPtr.Zero)
            {
                MelonLogger.Msg($"{Tag} {label}: self is NULL, skipping inner read.");
                return;
            }

            IntPtr inner;
            try
            {
                inner = Marshal.ReadIntPtr(self, 8);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"{Tag} {label}: reading self+0x8 threw {ex.GetType().FullName}: {ex.Message}");
                return;
            }

            MelonLogger.Msg($"{Tag} {label}: inner=*(self+0x8)=0x{inner.ToInt64():X}");

            if (inner == IntPtr.Zero)
            {
                MelonLogger.Msg($"{Tag} {label}: inner is NULL, skipping inner vtable read.");
                return;
            }

            IntPtr innerVtable;
            try
            {
                innerVtable = Marshal.ReadIntPtr(inner);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"{Tag} {label}: reading inner vtable pointer threw {ex.GetType().FullName}: {ex.Message}");
                return;
            }

            MelonLogger.Msg($"{Tag} {label}: innerVtable=0x{innerVtable.ToInt64():X}");

            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < VtableSlotsToRead; i++)
            {
                IntPtr slotValue;
                try
                {
                    slotValue = Marshal.ReadIntPtr(innerVtable, i * 8);
                }
                catch (Exception ex)
                {
                    sb.Append($"innerSlot{i}=READ-ERROR({ex.GetType().Name}) ");
                    continue;
                }
                string moduleDesc = ResolveModuleAndRva(slotValue);
                sb.Append($"innerSlot{i}=0x{slotValue.ToInt64():X}[{moduleDesc}] ");
            }
            MelonLogger.Msg($"{Tag} {label}: {sb}");
        }

        private static void EnsureModuleSnapshot()
        {
            if (_moduleSnapshotTaken)
            {
                return;
            }
            _moduleSnapshotTaken = true;
            try
            {
                using Process current = Process.GetCurrentProcess();
                var list = new System.Collections.Generic.List<(string, long, long)>();
                foreach (ProcessModule module in current.Modules)
                {
                    try
                    {
                        list.Add((module.ModuleName ?? string.Empty, module.BaseAddress.ToInt64(), module.ModuleMemorySize));
                    }
                    finally
                    {
                        module.Dispose();
                    }
                }
                _moduleSnapshot = list.ToArray();
            }
            catch
            {
                _moduleSnapshot = Array.Empty<(string, long, long)>();
            }
        }

        private static string ResolveModuleAndRva(IntPtr address)
        {
            if (address == IntPtr.Zero)
            {
                return "NULL";
            }
            EnsureModuleSnapshot();
            long addr = address.ToInt64();
            foreach (var module in _moduleSnapshot)
            {
                if (addr >= module.Base && addr < module.Base + module.Size)
                {
                    return $"{module.Name}+0x{(addr - module.Base):X}";
                }
            }
            return $"UNKNOWN-MODULE(0x{addr:X})";
        }
    }
}
