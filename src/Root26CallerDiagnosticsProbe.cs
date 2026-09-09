using System;
using System.Collections.Generic;
using System.Diagnostics;
using Il2Cpp;
using MelonLoader;

namespace NocturneModernController
{
    // Root-26: read-only caller-identification helper for
    // SteamInputUtil.ResetController() / SteamPad.SteamControllerReStart().
    //
    // Context: an exhaustive static-analysis pass (four independent
    // searches, all read-only / no patching) found ZERO evidence of a
    // static call site for either method anywhere in GameAssembly.dll:
    //   1. IL-level call/callvirt/ldftn/ldvirtftn search across all 42
    //      IL2CPP interop assemblies (Mono.Cecil) - 0 hits. (Not
    //      conclusive by itself: IL2CPP AOT method bodies in the interop
    //      assembly are trivial stubs, so this alone proves little.)
    //   2. Native E8 (near CALL rel32) byte-scan of all ~31692 known
    //      managed method bodies (RVA+Length from Cecil AddressAttribute,
    //      across all 42 assemblies) - 0 hits.
    //   3. Native E8 byte-scan of the ENTIRE executable region of
    //      GameAssembly.dll (374MB, all executable memory blocks) - 0 hits.
    //   4. Raw 8-byte absolute-pointer byte-scan of the ENTIRE initialized
    //      image (387MB, every memory block including .rdata/.data1/
    //      .debug/.edata) - 0 hits.
    // See docs/research/RIGHT_STICK_VIEW_AND_DASH_INVESTIGATION.md section
    // 33 for full detail. Conclusion: the call is not a direct near-CALL
    // nor a literal stored function pointer anywhere in the static image -
    // it must go through IL2CPP's runtime method-pointer resolution
    // (delegate/UnityEvent/Steamworks-callback dispatch, or a
    // RIP-relative-LEA-populated table), which static analysis of the raw
    // binary cannot resolve any further. This probe captures the managed
    // call stack at the moment of the call instead (read-only: .NET's
    // System.Diagnostics.StackTrace is a standard reflection API with no
    // side effects - it does not call, write, or alter anything), plus a
    // snapshot of the same fields already tracked elsewhere
    // (SteamManager.Initialized / SteamInputUtil.instance / Controller.
    // Count / Controller handle / Current / ControllerType / Analog[0].
    // Handle / Analog[1].Handle) so the pre/post-Reset state can be
    // compared directly against the CALLER-CONTEXT log line's timestamp.
    internal static class Root26CallerDiagnostics
    {
        private const string Tag = "[NocturneModernController][Root26CallerDiag]";

        internal static void LogCallerContext(string methodLabel, int callCount)
        {
            string flatStack;
            try
            {
                flatStack = new StackTrace(true).ToString().Replace("\r\n", " | ").Replace("\n", " | ").Trim();
            }
            catch (Exception ex)
            {
                flatStack = "<StackTrace threw " + ex.GetType().Name + ": " + ex.Message + ">";
            }

            string snapshot;
            try
            {
                snapshot = BuildSnapshot();
            }
            catch (Exception ex)
            {
                snapshot = "ERROR(" + ex.GetType().Name + ": " + ex.Message + ")";
            }

            MelonLogger.Msg(
                $"{Tag} CALLER-CONTEXT {methodLabel} count={callCount} snapshot=({snapshot}) " +
                $"stack=[{flatStack}] at {DateTimeOffset.Now:O}");
        }

        private static string BuildSnapshot()
        {
            bool conditionA = SteamManager.Initialized;
            SteamInputUtil util = SteamInputUtil.instance;
            if (util == null)
            {
                return $"conditionA={conditionA} conditionC=False";
            }

            SteamPad pad;
            try
            {
                pad = util.steam_pad;
            }
            catch (Exception ex)
            {
                return $"conditionA={conditionA} conditionC=True steam_pad-access-threw={ex.GetType().Name}";
            }
            if (pad == null)
            {
                return $"conditionA={conditionA} conditionC=True steam_pad=null";
            }

            int controllerCount;
            try
            {
                controllerCount = pad.Controller.Count;
            }
            catch (Exception ex)
            {
                return $"conditionA={conditionA} conditionC=True Controller.Count-access-threw={ex.GetType().Name}";
            }

            var parts = new List<string>();
            try
            {
                foreach (var key in pad.Controller.Keys)
                {
                    SteamPad.InputInfo info = pad.Controller[key];
                    string a0 = Root26Phase1SteamPadSetCallProbe.ResolveAnalogHandle(info, 0);
                    string a1 = Root26Phase1SteamPadSetCallProbe.ResolveAnalogHandle(info, 1);
                    parts.Add(
                        $"[handle={key} Current={info.Current} ControllerType={info.ControllerType} " +
                        $"Analog0={a0} Analog1={a1}]");
                }
            }
            catch (Exception ex)
            {
                parts.Add("Controller-iteration-threw=" + ex.GetType().Name);
            }

            return $"conditionA={conditionA} conditionC=True Controller.Count={controllerCount} " +
                   (parts.Count == 0 ? "no-controllers" : string.Join(" ", parts));
        }
    }
}
