using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Il2Cpp;
using MelonLoader;

namespace NocturneModernController
{
    // Root-26 Chapter 91/93 Phase B: read-only diagnostic that reads the
    // ONE self-relative field CONFIRMED via capstone disassembly of
    // steamclient64.dll (chapter 90) to gate whether
    // ISteamInput::GetAnalogActionData's inner implementation returns
    // real data or an early-zeroed result:
    //
    //   storedInputHandleLow32 = *(uint32*)(inner + 0x109708)
    //
    // where "inner" is *(outer self + 0x8) (chapter 87/88's CONFIRMED
    // outer-to-inner delegation offset), for BOTH candidate outer selfs:
    //   managedInner = *(managedSelfCandidate + 0x8)
    //   flatInner    = *(flatSelf + 0x8)
    //
    // Chapter 93 follow-up: chapter 91's original design piggybacked on
    // Root26ManagedSelfTraceProbe's one-shot resolution. Chapter 92 found
    // that this could complete BEFORE SteamInputUtil.instance/
    // pad.Controller were populated, producing a meaningless "both zero"
    // result (not a real mismatch - just no controller registered yet).
    // This probe is now fully independent: it runs every frame from
    // ModMain.OnUpdate(), re-derives managedSelfCandidate/flatSelf itself
    // (same read-only chain as chapter 78/79/84, not cached from another
    // probe), and RETRIES until every precondition below is met, or a
    // 15-second timeout elapses (INCONCLUSIVE):
    //   - SteamInputUtil.instance != null, pad != null, pad.Controller
    //     contains at least one non-zero handle
    //   - managedSelfCandidate != 0, flatSelf != 0
    //   - managedInner = *(managedSelfCandidate+0x8) != 0
    //   - flatInner    = *(flatSelf+0x8) != 0
    // Only once ALL of these hold does it read inner+0x109708 and log the
    // comparison - exactly once - then mark itself done.
    //
    // Chapter 90.3 CONFIRMED (via capstone, read-only) that the inner
    // implementation does "cmp r14d, [inner+0x109708]" (r14d = the
    // caller-supplied inputHandle's low 32 bits) and returns an all-zero
    // struct immediately if this comparison fails.
    //
    // IMPORTANT (per user instruction): both known controller handles in
    // this investigation (0x0045EB0033E98564 and 0x0145E28E33E98564)
    // share the SAME low 32 bits (0x33E98564). This probe therefore can
    // only confirm/deny "does the stored value match SOME handle's low
    // 32 bits" - it cannot distinguish which of the two physical
    // interfaces (Xbox Elite 2 vs Xbox 360-compatible) a match refers to.
    // This limitation is deliberately reported in the log rather than
    // glossed over.
    //
    // Safety:
    //   - Marshal.ReadIntPtr / Marshal.ReadInt32 ONLY. No Marshal.Write*
    //     of any kind.
    //   - No new Steam API call of any kind (SteamAPI_SteamInput_v002 is
    //     the same already-used, side-effect-free accessor as every
    //     other Root-26 probe since chapter 64).
    //   - No function pointer is ever invoked - pure data read/compare.
    //   - Every dereference wrapped in try/catch; on exception this
    //     probe logs a warning and disables itself for the session
    //     (never destabilizes the mod).
    //   - No native detour/patch/injection/hook of any module.
    internal static class Root26StoredHandleCompareProbe
    {
        private const string Tag = "[NocturneModernController][Root26StoredHandleCompare]";
        private const int InnerObjectOffset = 0x8; // Chapter 87/88: CONFIRMED outer->inner delegation offset.
        private const int StoredHandleOffset = 0x109708; // Chapter 90.3: CONFIRMED via capstone - inputHandle-low32 gate field.

        // Chapter 78.2/79.1: cached-pointer-chain constants, identical to
        // Root26ManagedSelfTraceProbe/Root26AnalogDataSelfSwapProbe.
        private const long StaticImageBase = 0x180000000L;
        private const long TargetVa = 0x182E4F3E0L;
        private const long TargetRva = TargetVa - StaticImageBase;
        private const int ChainOffset1 = 0xB8;
        private const int ChainOffset2 = 0xB0;

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

            SteamInputUtil util;
            SteamPad pad;
            try
            {
                util = SteamInputUtil.instance;
                if (util == null)
                {
                    MaybeLogWaiting(now, "SteamInputUtil.instance still null");
                    CheckTimeout(now);
                    return;
                }
                pad = util.steam_pad;
                if (pad == null)
                {
                    MaybeLogWaiting(now, "steam_pad still null");
                    CheckTimeout(now);
                    return;
                }
            }
            catch (Exception ex)
            {
                _done = true;
                MelonLogger.Warning($"{Tag} SteamInputUtil.instance/steam_pad access threw {ex.GetType().FullName}: {ex.Message} - probe disabled for this session.");
                return;
            }

            ulong firstNonZeroHandle = 0;
            try
            {
                foreach (var handleRaw in pad.Controller.Keys)
                {
                    if (handleRaw != 0)
                    {
                        firstNonZeroHandle = handleRaw;
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                _done = true;
                MelonLogger.Warning($"{Tag} pad.Controller.Keys iteration threw {ex.GetType().FullName}: {ex.Message} - probe disabled for this session.");
                return;
            }

            if (firstNonZeroHandle == 0)
            {
                MaybeLogWaiting(now, "pad.Controller has no non-zero handle yet");
                CheckTimeout(now);
                return;
            }

            IntPtr managedSelfCandidate = ResolveManagedSelfCandidate();
            if (managedSelfCandidate == IntPtr.Zero)
            {
                MaybeLogWaiting(now, "managedSelfCandidate chain not yet resolved");
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

            IntPtr managedInner;
            IntPtr flatInner;
            try
            {
                managedInner = Marshal.ReadIntPtr(IntPtr.Add(managedSelfCandidate, InnerObjectOffset));
                flatInner = Marshal.ReadIntPtr(IntPtr.Add(flatSelf, InnerObjectOffset));
            }
            catch (Exception ex)
            {
                _done = true;
                MelonLogger.Warning($"{Tag} reading inner pointers (outer+0x8) threw {ex.GetType().FullName}: {ex.Message} - probe disabled for this session.");
                return;
            }

            if (managedInner == IntPtr.Zero)
            {
                MaybeLogWaiting(now, "managedInner (managedSelf+0x8) still 0x0");
                CheckTimeout(now);
                return;
            }
            if (flatInner == IntPtr.Zero)
            {
                MaybeLogWaiting(now, "flatInner (flatSelf+0x8) still 0x0");
                CheckTimeout(now);
                return;
            }

            // All preconditions met - read the stored-handle field and
            // log the complete comparison exactly once, then mark done.
            uint managedStored;
            uint flatStored;
            try
            {
                managedStored = unchecked((uint)Marshal.ReadInt32(IntPtr.Add(managedInner, StoredHandleOffset)));
                flatStored = unchecked((uint)Marshal.ReadInt32(IntPtr.Add(flatInner, StoredHandleOffset)));
            }
            catch (Exception ex)
            {
                _done = true;
                MelonLogger.Warning($"{Tag} Marshal.ReadInt32(inner+0x{StoredHandleOffset:X}) threw {ex.GetType().FullName}: {ex.Message} - probe disabled for this session.");
                return;
            }

            _done = true;

            MelonLogger.Msg(
                $"{Tag} RESOLVED managedSelfCandidate=0x{managedSelfCandidate.ToInt64():X} flatSelf=0x{flatSelf.ToInt64():X} " +
                $"managedInner=0x{managedInner.ToInt64():X} flatInner=0x{flatInner.ToInt64():X} " +
                $"managedStored=0x{managedStored:X8} flatStored=0x{flatStored:X8} at {DateTimeOffset.Now:O}");

            try
            {
                foreach (var handleRaw in pad.Controller.Keys)
                {
                    if (handleRaw == 0)
                    {
                        continue;
                    }
                    uint low32 = unchecked((uint)(handleRaw & 0xFFFFFFFFUL));
                    bool managedMatches = managedStored == low32;
                    bool flatMatches = flatStored == low32;
                    MelonLogger.Msg(
                        $"{Tag} handle={handleRaw} low32=0x{low32:X8} managedStored==low32:{managedMatches} " +
                        $"flatStored==low32:{flatMatches} at {DateTimeOffset.Now:O}");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"{Tag} pad.Controller.Keys iteration (final comparison) threw {ex.GetType().FullName}: {ex.Message}");
            }

            MelonLogger.Msg($"{Tag} NOTE: both known controller handles in this investigation share low32=0x33E98564 - a match above does not by itself identify WHICH physical interface's inner instance was read. at {DateTimeOffset.Now:O}");
        }

        // Re-derived every call (cheap, read-only), same chain as
        // Root26ManagedSelfTraceProbe/Root26AnalogDataSelfSwapProbe.
        // Returns IntPtr.Zero if any stage is not yet available (never
        // throws for that reason).
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
                IntPtr p1 = Marshal.ReadIntPtr(IntPtr.Add(p0, ChainOffset1));
                if (p1 == IntPtr.Zero)
                {
                    return IntPtr.Zero;
                }
                return Marshal.ReadIntPtr(IntPtr.Add(p1, ChainOffset2));
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"{Tag} managedSelfCandidate pointer-chain read threw {ex.GetType().FullName}: {ex.Message}");
                return IntPtr.Zero;
            }
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
            MelonLogger.Warning($"{Tag} INCONCLUSIVE timeout: preconditions not met within {RetryTimeoutMs}ms - probe stopped at {DateTimeOffset.Now:O}");
        }

        // Read-only self-process module enumeration, identical mechanism
        // to Root26ManagedSelfTraceProbe/Root26AnalogDataSelfSwapProbe.
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
