using System;
using Il2CppInterop.Runtime;
using Il2CppSteamworks;
using MelonLoader;

namespace NocturneModernController
{
    // Root-26 Chapter 72.6 follow-up: read-only, one-shot query of the
    // ACTUAL native field offsets IL2CPP itself has compiled for
    // Il2CppSteamworks.InputAnalogActionData_t in THIS game's build,
    // using Il2CppInterop.Runtime's own thin wrappers around il2cpp's
    // metadata introspection functions:
    //   Il2CppClassPointerStore<InputAnalogActionData_t>.NativeClassPtr
    //   IL2CPP.il2cpp_class_get_field_from_name(klass, fieldName)
    //   IL2CPP.il2cpp_field_get_offset(field)
    //
    // This is NOT a Steam API call of any kind - it is a pure IL2CPP
    // runtime metadata query against tables GameAssembly.dll's il2cpp
    // runtime already built at process startup, independent of any Steam
    // Input activity. It has no side effects (no field is read or
    // written, only the field's compiled byte offset is queried) and
    // cannot change at runtime (offsets are fixed at compile time), so
    // this only needs to run and log ONCE per session.
    //
    // Chapter 72.3 self-correction: an earlier static analysis attempt
    // (capstone-disassembling the flat GetAnalogActionData thunk's byte-
    // copy pattern) was found to be evidence-insufficient to determine
    // WHICH field (x/y/eMode) lives at which offset - a byte-copy-size
    // pattern (8+4+1 bytes) is consistent with multiple possible field
    // orderings. This probe queries il2cpp's own authoritative offset
    // table instead of inferring order from copy-instruction shape.
    internal static class Root26AnalogDataFieldOffsetProbe
    {
        private const string Tag = "[NocturneModernController][Root26AnalogDataFieldOffset]";

        private static bool _done;

        internal static void Sample()
        {
            if (_done)
            {
                return;
            }
            _done = true;

            try
            {
                IntPtr klass = Il2CppClassPointerStore<InputAnalogActionData_t>.NativeClassPtr;
                if (klass == IntPtr.Zero)
                {
                    MelonLogger.Warning($"{Tag} Il2CppClassPointerStore<InputAnalogActionData_t>.NativeClassPtr is IntPtr.Zero - cannot query field offsets this session.");
                    return;
                }

                LogFieldOffset(klass, "eMode");
                LogFieldOffset(klass, "x");
                LogFieldOffset(klass, "y");
                LogFieldOffset(klass, "bActive");
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"{Tag} field offset query threw {ex.GetType().FullName}: {ex.Message}");
            }
        }

        private static void LogFieldOffset(IntPtr klass, string fieldName)
        {
            IntPtr field = IL2CPP.il2cpp_class_get_field_from_name(klass, fieldName);
            if (field == IntPtr.Zero)
            {
                MelonLogger.Warning($"{Tag} il2cpp_class_get_field_from_name(\"{fieldName}\") returned IntPtr.Zero.");
                return;
            }
            uint offset = IL2CPP.il2cpp_field_get_offset(field);
            MelonLogger.Msg($"{Tag} FIELD-OFFSET name=\"{fieldName}\" offset={offset} at {DateTimeOffset.Now:O}");
        }
    }
}
