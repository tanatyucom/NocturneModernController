using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using MelonLoader;

namespace NocturneModernController
{
    // Root-26 Chapter 78 Phase B: read-only diagnostic that numerically
    // compares two candidate "ISteamInput* self" values within the SAME
    // running process:
    //
    //   managedSelfCandidate = *(*(GameAssemblyBase + RVA(0x182E4F3E0)) + 0xB8) + 0xB0
    //   flatSelf             = SteamAPI_SteamInput_v002()
    //
    // Chapter 78.2 CONFIRMED (via Cpp2IL ISIL disassembly of GameAssembly.dll,
    // a purely static/read-only decompilation - no game state touched) that
    // the IL2CPP-compiled native implementations of
    // Il2CppSteamworks.SteamInput.GetAnalogActionData / GetActionSetHandle /
    // GetConnectedControllers all obtain their "self" argument from the
    // SAME cached pointer chain - a global slot at static VA 0x182E4F3E0 in
    // GameAssembly.dll, dereferenced at +0xB8 then +0xB0 - and never call
    // SteamAPI_SteamInput_v002() at all. This probe reads that same chain
    // at runtime (from GameAssembly.dll's actual loaded base, accounting
    // for ASLR - the static VA is never used directly) and compares the
    // result against the flat v002 accessor's return value, to determine
    // whether they are numerically the same pointer (Case I2) or different
    // (Case I1, further supporting the "flat v002 sees a different Steam
    // Input interface/session than the game's own managed binding"
    // hypothesis from chapters 75-77).
    //
    // Chapter 79 follow-up: this is NOT a plain "first frame" one-shot.
    // On the very first Sample() call, GameAssembly's own Steam Input
    // static state may not yet be initialized, so any stage of the chain
    // (p0/p1/managedSelfCandidate) or flatSelf could legitimately still be
    // zero. A naive "always mark done after one attempt" design would
    // produce a false INCONCLUSIVE result baked in permanently for the
    // rest of the session. Instead, this probe RETRIES every frame
    // (cheap: a handful of pointer reads) until either (a) the FULL chain
    // resolves to all-non-zero values (managedSelfCandidate != 0 AND
    // flatSelf != 0), at which point it logs the full comparison exactly
    // once and marks itself done, or (b) a timeout (RetryTimeoutMs)
    // elapses without ever achieving a fully-resolved chain, at which
    // point it logs an INCONCLUSIVE result once and marks itself done.
    // While waiting, it logs at most one "waiting" message per
    // WaitLogIntervalMs to avoid log flooding.
    //
    // Safety (explicit, per user instruction for this probe):
    //   - Marshal.ReadIntPtr ONLY. No Marshal.Write* of any kind, ever.
    //   - Every stage of the pointer chain is checked for IntPtr.Zero
    //     before being dereferenced further; if any stage is zero, this
    //     frame's attempt is treated as "not yet initialized" and safely
    //     skipped (no state is written, no crash) - retried next frame.
    //   - Every pointer dereference is wrapped in try/catch. On any
    //     exception, this probe disables itself for the rest of the
    //     session (logs one warning) - it never lets an exception escape
    //     to destabilize the rest of the mod.
    //   - No SteamAPI call other than the already-used, side-effect-free
    //     SteamAPI_SteamInput_v002() accessor. No ActivateActionSet/
    //     ActivateActionSetLayer/ResetController/SteamControllerReStart/
    //     Shutdown/Init/UpdateConnectedControllers call of any kind.
    //   - No native detour/patch/injection/hook of GameAssembly.dll,
    //     steamclient64.dll, GameOverlayRenderer64.dll, or steam_api64.dll.
    //     This probe only READS process memory it already has access to
    //     (its own process), the same way a debugger's memory view would.
    internal static class Root26ManagedSelfTraceProbe
    {
        private const string Tag = "[NocturneModernController][Root26ManagedSelfTrace]";

        // Chapter 78.2: static VA of the cached global slot in
        // GameAssembly.dll, as seen in the ISIL disassembly. Chapter 78's
        // follow-up CONFIRMED (via pefile inspection of the on-disk PE
        // header, not assumed) that GameAssembly.dll's actual PE
        // ImageBase is 0x180000000, giving RVA 0x2E4F3E0. The RVA - not
        // the static VA - is what gets added to the runtime module base
        // below, so this remains correct even if ASLR relocates the
        // module.
        private const long StaticImageBase = 0x180000000L;
        private const long TargetVa = 0x182E4F3E0L;
        private const long TargetRva = TargetVa - StaticImageBase;
        private const int Offset1 = 0xB8;
        private const int Offset2 = 0xB0;

        private const long RetryTimeoutMs = 15000;
        private const long WaitLogIntervalMs = 3000;

        [DllImport("steam_api64.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr SteamAPI_SteamInput_v002();

        private static bool _done;
        private static bool _gameAssemblyBaseResolved;
        private static IntPtr _gameAssemblyBase = IntPtr.Zero;
        private static long _firstAttemptMs = -1;
        private static long _lastWaitLogMs = -1;

        internal static void Sample()
        {
            if (_done)
            {
                return;
            }

            try
            {
                TryOnceOrWait();
            }
            catch (Exception ex)
            {
                _done = true;
                MelonLogger.Warning($"{Tag} threw {ex.GetType().FullName}: {ex.Message} - probe disabled for this session (mod continues normally).");
            }
        }

        private static void TryOnceOrWait()
        {
            long now = Environment.TickCount64;
            if (_firstAttemptMs < 0)
            {
                _firstAttemptMs = now;
            }

            if (!_gameAssemblyBaseResolved)
            {
                _gameAssemblyBase = FindGameAssemblyBase();
                if (_gameAssemblyBase != IntPtr.Zero)
                {
                    _gameAssemblyBaseResolved = true;
                    MelonLogger.Msg($"{Tag} GameAssemblyBase=0x{_gameAssemblyBase.ToInt64():X} targetRva=0x{TargetRva:X} at {DateTimeOffset.Now:O}");
                }
                else
                {
                    MaybeLogWaiting(now, "GameAssembly.dll module base not yet resolvable");
                    CheckTimeout(now);
                    return;
                }
            }

            IntPtr globalSlotAddress = IntPtr.Add(_gameAssemblyBase, checked((int)TargetRva));

            IntPtr p0;
            try
            {
                p0 = Marshal.ReadIntPtr(globalSlotAddress);
            }
            catch (Exception ex)
            {
                _done = true;
                MelonLogger.Warning($"{Tag} Marshal.ReadIntPtr(globalSlotAddress) threw {ex.GetType().FullName}: {ex.Message} - probe disabled for this session.");
                return;
            }
            if (p0 == IntPtr.Zero)
            {
                MaybeLogWaiting(now, "p0 (chain stage 1) still 0x0 - Steam Input static state not yet initialized?");
                CheckTimeout(now);
                return;
            }

            IntPtr p1;
            try
            {
                p1 = Marshal.ReadIntPtr(IntPtr.Add(p0, Offset1));
            }
            catch (Exception ex)
            {
                _done = true;
                MelonLogger.Warning($"{Tag} Marshal.ReadIntPtr(p0+0x{Offset1:X}) threw {ex.GetType().FullName}: {ex.Message} - probe disabled for this session.");
                return;
            }
            if (p1 == IntPtr.Zero)
            {
                MaybeLogWaiting(now, "p1 (chain stage 2) still 0x0");
                CheckTimeout(now);
                return;
            }

            IntPtr managedSelfCandidate;
            try
            {
                managedSelfCandidate = Marshal.ReadIntPtr(IntPtr.Add(p1, Offset2));
            }
            catch (Exception ex)
            {
                _done = true;
                MelonLogger.Warning($"{Tag} Marshal.ReadIntPtr(p1+0x{Offset2:X}) threw {ex.GetType().FullName}: {ex.Message} - probe disabled for this session.");
                return;
            }
            if (managedSelfCandidate == IntPtr.Zero)
            {
                MaybeLogWaiting(now, "managedSelfCandidate (chain stage 3) still 0x0");
                CheckTimeout(now);
                return;
            }

            IntPtr flatSelf;
            try
            {
                flatSelf = SteamAPI_SteamInput_v002();
            }
            catch (Exception ex)
            {
                _done = true;
                MelonLogger.Warning($"{Tag} SteamAPI_SteamInput_v002() threw {ex.GetType().FullName}: {ex.Message} - probe disabled for this session.");
                return;
            }
            if (flatSelf == IntPtr.Zero)
            {
                MaybeLogWaiting(now, "flatSelf (SteamAPI_SteamInput_v002()) still 0x0");
                CheckTimeout(now);
                return;
            }

            // Full chain resolved (all non-zero) - log the complete
            // comparison exactly once and mark done.
            bool samePointer = managedSelfCandidate == flatSelf;
            _done = true;
            MelonLogger.Msg(
                $"{Tag} RESOLVED globalSlotAddress=0x{globalSlotAddress.ToInt64():X} p0=0x{p0.ToInt64():X} " +
                $"p1=0x{p1.ToInt64():X} managedSelfCandidate=0x{managedSelfCandidate.ToInt64():X} " +
                $"flatSelf=0x{flatSelf.ToInt64():X} samePointer={samePointer} at {DateTimeOffset.Now:O}");

            // Chapter 82 Phase A: vtable identity comparison. Read-only -
            // vtable pointers and function-pointer SLOT VALUES are read
            // and logged, but never called/invoked. Only vtable slot
            // offsets already CONFIRMED via capstone disassembly in
            // chapters 65/72 are used (no new/guessed offsets):
            //   GetConnectedControllers : +0x18 (chapter 65)
            //   GetActionSetHandle      : +0x20 (chapter 68/74)
            //   GetCurrentActionSet     : +0x30 (chapter 65)
            //   GetAnalogActionData     : +0x78 (chapter 72.2)
            Root26VtableIdentityProbe.Compare(managedSelfCandidate, flatSelf);

            // Chapter 88 Phase B: inner-object identity comparison. The
            // outer self (managedSelfCandidate/flatSelf) is CONFIRMED
            // (chapter 87) to be a thin wrapper - all four exercised
            // vtable-slot implementations delegate via "mov rcx,
            // [rcx+8]" before doing any real work. This checks whether
            // the wrapped INNER object differs between the two outer
            // selfs, and whether that inner object shares the same
            // vtable (same implementation) - without reading or calling
            // any inner vtable slot yet (deferred to a later chapter).
            Root26InnerObjectIdentityProbe.Compare(managedSelfCandidate, flatSelf);

            // Chapter 93: Root26StoredHandleCompareProbe is no longer
            // called from here. Chapter 92 found that this one-shot
            // resolution can complete BEFORE SteamInputUtil.instance/
            // pad.Controller are populated, making a stored-handle
            // comparison meaningless (both read as 0, not because of a
            // real mismatch but because no controller was registered
            // yet). Root26StoredHandleCompareProbe is now an independent,
            // self-retrying probe registered directly in
            // ModMain.OnUpdate() instead.
        }

        private static void MaybeLogWaiting(long now, string reason)
        {
            if (_lastWaitLogMs >= 0 && now - _lastWaitLogMs < WaitLogIntervalMs)
            {
                return;
            }
            _lastWaitLogMs = now;
            MelonLogger.Msg($"{Tag} WAITING {reason} - will retry at {DateTimeOffset.Now:O}");
        }

        private static void CheckTimeout(long now)
        {
            if (_firstAttemptMs < 0 || now - _firstAttemptMs < RetryTimeoutMs)
            {
                return;
            }
            _done = true;
            MelonLogger.Warning($"{Tag} INCONCLUSIVE: full pointer chain did not resolve within {RetryTimeoutMs}ms - probe stopped at {DateTimeOffset.Now:O}");
        }

        // Read-only enumeration of already-loaded modules in this process
        // (the mod's own process) - no injection, no attaching to another
        // process, no native API beyond what .NET's Process class already
        // exposes for introspecting the current process's own module list.
        private static IntPtr FindGameAssemblyBase()
        {
            using Process current = Process.GetCurrentProcess();
            foreach (ProcessModule module in current.Modules)
            {
                try
                {
                    if (string.Equals(module.ModuleName, "GameAssembly.dll", StringComparison.OrdinalIgnoreCase))
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
