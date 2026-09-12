using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Il2Cpp;
using Il2CppInterop.Runtime;
using MelonLoader;

namespace NocturneModernController
{
    // Root-26 Chapter 115: read-only runtime identity check for the
    // "LastInputIndex recovery path" virtual call statically confirmed in
    // Chapter 114 (docs/research/RIGHT_STICK_VIEW_AND_DASH_INVESTIGATION.md
    // section 114.8), via GameAssembly.dll disassembly of
    // SteamInputUtil.UpdateInput() -> RVA 0x2602F30 -> RVA 0x1519140:
    //
    //   rcxGate       = *(*(SteamInputUtil.instance.Pointer + 0x80) + 0x10)
    //   globalPtr     = *(GameAssembly.dll base + RVA 0x2E88170)
    //   chainA        = *(globalPtr + 0x18)
    //   chainB        = *(chainA    + 0xc0)
    //   interfaceObj  = *(chainB    + 0x88)
    //   vtableSlot0   = *(interfaceObj + 0x0)   <- this is the address
    //                                              "call qword ptr [rax]"
    //                                              would jump to.
    //
    // This probe NEVER calls vtableSlot0 (or any other function pointer).
    // It only reads already-resident process memory (Marshal.ReadIntPtr),
    // exactly like every other Root-26 field-offset/self-pointer probe
    // (Root26FieldOffsetMapProbe, Root26AnalogDataSelfSwapProbe,
    // Root26EntryTimelineProbe), and resolves which loaded module (if any)
    // that address falls inside via Process.Modules - a read-only
    // enumeration, not code execution. No ResetController()/
    // SteamControllerReStart()/Shutdown()/Init()/
    // UpdateConnectedControllers()/ActivateActionSet*/SendInput/Guide
    // spoofing/Steam binary patch-hook-injection of any kind.
    //
    // Purpose: determine whether vtableSlot0 resolves into steamclient64.dll
    // (which would directly connect the GameAssembly-side LastInputIndex
    // recovery path to the same steamclient ISteamInput lineage traced in
    // chapters 78-101), into some other Steam-related module, or into
    // GameAssembly.dll itself (a same-process wrapper needing one more hop).
    // Logs on STATE-CHANGE only (any of the chain values, or
    // SteamInputUtil.LastInputIndex, differing from the previous sample),
    // so the resulting log can be time-correlated against the existing
    // Root26SteamStateProbe (LastInputIndex) / Root26SteamPadDeepStateProbe
    // (GetCurrentControlDevice()) STATE-CHANGE lines using the same
    // DateTimeOffset.Now:O timestamp format.
    internal static class Root26LastInputIndexVtableTargetProbe
    {
        private const string Tag = "[NocturneModernController][Root26LiiVtableTarget]";
        private const long HeartbeatIntervalMs = 5000;

        // Chapter 114.8 offsets, confirmed via pefile+capstone disassembly
        // of the deployed GameAssembly.dll (read-only static analysis).
        // Chapter 125 (a) correction: raw disassembly of the actual call
        // site (SteamInputUtil.UpdateInput(), VA 0x1825F9E10, at
        // 0x1825f9fa0/0x1825f9f30: "MOV RCX/RBP, qword ptr [RSI+0x50]"
        // where RSI=RCX=this at function entry, i.e. utilPtr) confirms the
        // first hop is +0x50, NOT +0x80 as originally recorded in Chapter
        // 114.8/115 (that mistaken offset is why rcxGate always read back
        // as 0x0 in every real-machine test so far - it was reading a
        // different, coincidentally-zero field of SteamInputUtil). RCX/RDX
        // are also confirmed UNCHANGED from this point all the way through
        // 0x182602F30 -> 0x181519140's final "call qword ptr [rax]".
        private const int SteamInputUtilField0x50Offset = 0x50;
        private const int Field0x10Offset = 0x10;
        private const long GlobalStorageRva = 0x2E88170L;
        private const int ChainOffsetA = 0x18;
        private const int ChainOffsetB = 0xc0;
        private const int InterfaceObjOffset = 0x88;
        private const int VtableSlot0Offset = 0x0;

        // Chapter 125.3 offsets, confirmed via Ghidra decompile of
        // GameAssembly.dll+0x1633C70 (the vtableSlot0 target itself - the
        // function "call qword ptr [rax]" jumps to). That function is a
        // generic bucket/entry table (Dictionary-like) search, keyed off
        // its FIRST argument (param_1 = RCX = rcxGate, per the calling
        // convention correction above - NOT interfaceObj, which ends up in
        // R8/RAX instead as the vtable-holding object itself):
        //   rcxGate+0x10 = buckets array pointer
        //   rcxGate+0x18 = entries array pointer
        //   rcxGate+0x30 = a single delegate/predicate object, shared
        //                  across all entries, passed to the matched
        //                  entry's invoke call alongside that entry's own
        //                  "value" field
        //   [array+0x18]      = array Length (standard IL2CPP array header,
        //                       used for both the buckets and entries
        //                       arrays in the decompiled bounds checks)
        //   [array+0x20 + i*0x28] = entry i's data start (entry stride
        //                       confirmed as 0x28 bytes from the
        //                       decompiled index arithmetic)
        //   entry+0x0 = hashCode (int)
        //   entry+0x4 = next (int)
        //   entry+0x8 = value (8-byte pointer, passed to the invoke call)
        // We deliberately do NOT try to recompute which entry would match
        // a given controller (that requires calling the native hash
        // function, func_0x180001fd0 - not read-only). Instead every
        // entries[] slot up to a small cap is dumped, so the whole table's
        // state is visible without replicating the hash algorithm.
        private const int DictBucketsPtrOffset = 0x10;
        private const int DictEntriesPtrOffset = 0x18;
        private const int DictDelegateFieldOffset = 0x30;
        private const int ArrayLengthOffset = 0x18;
        private const int ArrayDataOffset = 0x20;
        private const int EntryStride = 0x28;
        private const int EntryHashOffset = 0x0;
        private const int EntryNextOffset = 0x4;
        private const int EntryValueOffset = 0x8;
        private const int MaxEntriesToDump = 16;

        // Standard IL2CPP managed-object header convention: offset 0 of
        // any object holds its Il2CppClass* ("klass"). Used only to
        // identify delegateField's concrete type - never dereferenced
        // further by this probe.
        private const int DelegateKlassOffset = 0x0;

        private static bool _errorLogged;
        private static long _lastHeartbeatTickMs = -1;

        private static string? _lastSummary;

        // Chapter 115.7/115.9 post-mortem: the original build enumerated
        // Process.Modules on every single Sample() call (once inside
        // ResolveGameAssemblyBase's fallback path, and unconditionally
        // inside ResolveModuleAndRva regardless of whether anything
        // changed), which caused a severe in-game slowdown. Fixed per the
        // 115.9 design instruction: the module list is snapshotted exactly
        // once per session (success or failure - never retried), and the
        // vtableSlot0 -> module/RVA description is only recomputed when
        // vtableSlot0Target itself differs from the previous sample.
        private static bool _moduleSnapshotTaken;
        private static (string Name, long Base, long Size)[] _moduleSnapshot =
            Array.Empty<(string, long, long)>();

        private static bool _haveLastTarget;
        private static IntPtr _lastTarget = IntPtr.Zero;
        private static string _lastTargetModule = "NULL";

        // Chapter 127.6: delegateKlass identity resolution via
        // Il2CppInterop.Runtime.IL2CPP's existing thin wrappers around
        // GameAssembly.dll's il2cpp_class_get_name/il2cpp_class_get_namespace
        // exports (same API family already used read-only elsewhere in this
        // codebase, e.g. Root26AnalogDataFieldOffsetProbe's
        // il2cpp_class_get_field_from_name/il2cpp_field_get_offset). No new
        // DllImport, no klass-internal raw dump, no other IL2CPP API. Only
        // re-resolved when delegateKlass itself changes (it was CONFIRMED
        // constant across DEAD/LIVE in Chapter127.1, so this should resolve
        // once per session in practice); otherwise the cached name/namespace
        // strings are reused to avoid redundant native calls every sample.
        private static bool _haveResolvedKlass;
        private static IntPtr _lastResolvedKlass = IntPtr.Zero;
        private static string _cachedDelegateNamespace = "?";
        private static string _cachedDelegateClass = "?";

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

            IntPtr utilPtr;
            int lastInputIndex;
            try
            {
                utilPtr = util.Pointer;
                lastInputIndex = util.LastInputIndex;
            }
            catch
            {
                return;
            }
            if (utilPtr == IntPtr.Zero)
            {
                return;
            }

            IntPtr gameAssemblyBase = ResolveGameAssemblyBase();
            if (gameAssemblyBase == IntPtr.Zero)
            {
                return;
            }

            try
            {
                // rcxGate: the field null-checked as the first argument to
                // RVA 0x1519140 (Chapter 114.8). Chapter 125.3 correction:
                // this value is NOT just diagnostic context - x64 calling
                // convention means it stays in RCX, untouched, all the way
                // through to being param_1 of the final "call qword ptr
                // [rax]" (interfaceObj/vtableSlot0Target end up as R8/RAX
                // instead - see the class remarks near DictBucketsPtrOffset
                // for the corrected register mapping). So rcxGate, not
                // interfaceObj, is the base for the Dictionary-like table
                // offsets (+0x10/+0x18/+0x30).
                IntPtr fieldA = Marshal.ReadIntPtr(utilPtr, SteamInputUtilField0x50Offset);
                IntPtr rcxGate = fieldA == IntPtr.Zero ? IntPtr.Zero : SafeReadIntPtr(fieldA, Field0x10Offset);
                string rcxGateStr = fieldA == IntPtr.Zero ? "fieldA-NULL" : Describe(rcxGate);

                IntPtr globalStorageVa = IntPtr.Add(gameAssemblyBase, (int)GlobalStorageRva);
                IntPtr globalPtr = Marshal.ReadIntPtr(globalStorageVa);
                if (globalPtr == IntPtr.Zero)
                {
                    LogIfChanged(
                        $"rcxGate={rcxGateStr} globalPtr=NULL(not-yet-initialized) " +
                        $"lastInputIndex={lastInputIndex}");
                    return;
                }

                IntPtr chainA = SafeReadIntPtr(globalPtr, ChainOffsetA);
                if (chainA == IntPtr.Zero)
                {
                    LogIfChanged(
                        $"rcxGate={rcxGateStr} globalPtr=0x{globalPtr.ToInt64():X} chainA=NULL " +
                        $"lastInputIndex={lastInputIndex}");
                    return;
                }

                IntPtr chainB = SafeReadIntPtr(chainA, ChainOffsetB);
                if (chainB == IntPtr.Zero)
                {
                    LogIfChanged(
                        $"rcxGate={rcxGateStr} globalPtr=0x{globalPtr.ToInt64():X} " +
                        $"chainA=0x{chainA.ToInt64():X} chainB=NULL lastInputIndex={lastInputIndex}");
                    return;
                }

                IntPtr interfaceObj = SafeReadIntPtr(chainB, InterfaceObjOffset);
                if (interfaceObj == IntPtr.Zero)
                {
                    LogIfChanged(
                        $"rcxGate={rcxGateStr} globalPtr=0x{globalPtr.ToInt64():X} " +
                        $"chainA=0x{chainA.ToInt64():X} chainB=0x{chainB.ToInt64():X} " +
                        $"interfaceObj=NULL lastInputIndex={lastInputIndex}");
                    return;
                }

                IntPtr vtableSlot0Target = SafeReadIntPtr(interfaceObj, VtableSlot0Offset);
                string targetModule;
                if (_haveLastTarget && vtableSlot0Target == _lastTarget)
                {
                    targetModule = _lastTargetModule;
                }
                else
                {
                    targetModule = ResolveModuleAndRva(vtableSlot0Target);
                    _lastTarget = vtableSlot0Target;
                    _lastTargetModule = targetModule;
                    _haveLastTarget = true;
                }

                string dictDump = DumpDictTable(rcxGate);

                string summary =
                    $"rcxGate={rcxGateStr} globalPtr=0x{globalPtr.ToInt64():X} " +
                    $"chainA=0x{chainA.ToInt64():X} chainB=0x{chainB.ToInt64():X} " +
                    $"interfaceObj=0x{interfaceObj.ToInt64():X} " +
                    $"vtableSlot0=0x{vtableSlot0Target.ToInt64():X} target=[{targetModule}] " +
                    $"lastInputIndex={lastInputIndex} {dictDump}";

                LogIfChanged(summary);

                long now = Environment.TickCount64;
                if (_lastHeartbeatTickMs < 0 || now - _lastHeartbeatTickMs >= HeartbeatIntervalMs)
                {
                    _lastHeartbeatTickMs = now;
                    MelonLogger.Msg($"{Tag} HEARTBEAT {summary} at {DateTimeOffset.Now:O}");
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

        // Chapter 125 A/B probe: dumps interfaceObj's Dictionary-like
        // buckets/entries table (see offset comments above the constants).
        // Read-only: Marshal.ReadIntPtr/ReadInt32 only, never writes, never
        // calls any function pointer (the hash function itself is not
        // invoked - see class remarks).
        private static string DumpDictTable(IntPtr rcxGate)
        {
            try
            {
                IntPtr bucketsPtr = SafeReadIntPtr(rcxGate, DictBucketsPtrOffset);
                IntPtr entriesPtr = SafeReadIntPtr(rcxGate, DictEntriesPtrOffset);
                IntPtr delegateField = SafeReadIntPtr(rcxGate, DictDelegateFieldOffset);

                // Chapter 127 next step: a single, minimal read of
                // delegateField's own klass pointer - the standard IL2CPP
                // object header convention (every managed object's first
                // 8 bytes point to its Il2CppClass). Read-only: this
                // pointer is logged as a raw value only, never
                // dereferenced further, and no klass-internal fields are
                // read. Goal is identity (which concrete type answers the
                // DEAD/LIVE bool), not a DEAD/LIVE diff - delegateField
                // itself was already CONFIRMED constant across transitions
                // (Chapter127), so this klass pointer is expected to be
                // constant too; it is still read every sample (cheap,
                // single Marshal.ReadIntPtr) and logged via the same
                // change-gated summary as everything else here.
                IntPtr delegateKlass = SafeReadIntPtr(delegateField, DelegateKlassOffset);
                ResolveDelegateKlassNameIfChanged(delegateKlass);

                int bucketsLen = bucketsPtr == IntPtr.Zero ? -1 : Marshal.ReadInt32(bucketsPtr, ArrayLengthOffset);
                int entriesLen = entriesPtr == IntPtr.Zero ? -1 : Marshal.ReadInt32(entriesPtr, ArrayLengthOffset);

                var sb = new System.Text.StringBuilder();
                sb.Append($"bucketsPtr=0x{bucketsPtr.ToInt64():X} bucketsLen={bucketsLen} ");
                sb.Append($"entriesPtr=0x{entriesPtr.ToInt64():X} entriesLen={entriesLen} ");
                sb.Append($"delegateField=0x{delegateField.ToInt64():X} delegateKlass=0x{delegateKlass.ToInt64():X} ");
                sb.Append($"delegateNamespace={_cachedDelegateNamespace} delegateClass={_cachedDelegateClass} entries=[");

                if (entriesPtr != IntPtr.Zero && entriesLen > 0)
                {
                    int dumpCount = Math.Min(entriesLen, MaxEntriesToDump);
                    long dataStart = entriesPtr.ToInt64() + ArrayDataOffset;
                    for (int i = 0; i < dumpCount; i++)
                    {
                        long entryBase = dataStart + (long)i * EntryStride;
                        int hash = Marshal.ReadInt32(new IntPtr(entryBase + EntryHashOffset));
                        int next = Marshal.ReadInt32(new IntPtr(entryBase + EntryNextOffset));
                        IntPtr value = Marshal.ReadIntPtr(new IntPtr(entryBase + EntryValueOffset));
                        if (i > 0)
                        {
                            sb.Append(',');
                        }
                        sb.Append($"{i}:h={hash},n={next},v=0x{value.ToInt64():X}");
                    }
                }
                sb.Append(']');
                return sb.ToString();
            }
            catch (Exception ex)
            {
                return $"dictDump-ERROR({ex.GetType().Name})";
            }
        }

        // Chapter 127.6: resolves delegateKlass's namespace/class name only
        // when delegateKlass itself differs from the last-resolved value
        // (identity check, not a per-frame call) - avoids calling the
        // native il2cpp_class_get_name/il2cpp_class_get_namespace exports
        // every sample. Uses ONLY Il2CppInterop.Runtime.IL2CPP's existing
        // wrappers (no new DllImport, no other IL2CPP API, no klass-internal
        // raw dump, no dereference of entry.value). Read-only metadata
        // query: does not initialize, instantiate, or invoke anything.
        private static void ResolveDelegateKlassNameIfChanged(IntPtr delegateKlass)
        {
            if (_haveResolvedKlass && delegateKlass == _lastResolvedKlass)
            {
                return;
            }
            _haveResolvedKlass = true;
            _lastResolvedKlass = delegateKlass;

            if (delegateKlass == IntPtr.Zero)
            {
                _cachedDelegateNamespace = "";
                _cachedDelegateClass = "NULL";
                return;
            }

            try
            {
                IntPtr namePtr = IL2CPP.il2cpp_class_get_name(delegateKlass);
                IntPtr namespacePtr = IL2CPP.il2cpp_class_get_namespace(delegateKlass);
                _cachedDelegateClass = namePtr == IntPtr.Zero
                    ? "NULL"
                    : (Marshal.PtrToStringAnsi(namePtr) ?? "?");
                _cachedDelegateNamespace = namespacePtr == IntPtr.Zero
                    ? ""
                    : (Marshal.PtrToStringAnsi(namespacePtr) ?? "");
            }
            catch (Exception ex)
            {
                _cachedDelegateNamespace = "";
                _cachedDelegateClass = $"ERROR({ex.GetType().Name})";
            }
        }

        // Guards a single Marshal.ReadIntPtr against a null base pointer -
        // read-only, never writes, never calls anything.
        private static IntPtr SafeReadIntPtr(IntPtr basePtr, int offset)
        {
            if (basePtr == IntPtr.Zero)
            {
                return IntPtr.Zero;
            }
            return Marshal.ReadIntPtr(basePtr, offset);
        }

        private static string Describe(IntPtr value) => $"0x{value.ToInt64():X}";

        // Snapshots Process.Modules exactly once per session (success or
        // failure - _moduleSnapshotTaken is set either way, so a failed
        // attempt is never retried on a later frame). All subsequent
        // lookups (GameAssembly.dll base, vtableSlot0's owning module) read
        // this small in-memory array instead of re-enumerating modules.
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

        // Looks up which module (from the cached snapshot) contains the
        // given address. No Process.Modules enumeration here - just a scan
        // over the small cached array.
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

        private static IntPtr ResolveGameAssemblyBase()
        {
            EnsureModuleSnapshot();
            foreach (var module in _moduleSnapshot)
            {
                if (string.Equals(module.Name, "GameAssembly.dll", StringComparison.OrdinalIgnoreCase))
                {
                    return new IntPtr(module.Base);
                }
            }
            return IntPtr.Zero;
        }

        private static void LogIfChanged(string summary)
        {
            if (_lastSummary == summary)
            {
                return;
            }
            MelonLogger.Msg($"{Tag} STATE-CHANGE {summary} at {DateTimeOffset.Now:O}");
            _lastSummary = summary;
        }
    }
}
