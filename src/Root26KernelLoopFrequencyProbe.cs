using System;
using HarmonyLib;
using MelonLoader;

namespace NocturneModernController
{
    // Root-26: read-only call-count observation of the actual game tick
    // driver (Il2Cpp.dds3KernelMain.m_dds3KernelMainLoop()) and the
    // third-party "Nocturne Framerate Mod"'s OnFixedUpdate() (which
    // sometimes calls m_dds3KernelMainLoop() twice in a single tick - see
    // docs/research/RIGHT_STICK_VIEW_AND_DASH_INVESTIGATION.md section 43).
    //
    // Context: Phase 4 (section 42) found that a single manual
    // Il2CppSteamworks.SteamInput.Shutdown()+Init() call (via the now-
    // retired F10 PoC) was followed by a persistent ~2x increase in
    // UpdateInput/SteamPadSet/UpdateControl/UpdateConnectedControllers/
    // GetConnectedControllers call frequency for the rest of that session.
    // Static IL analysis of the third-party Framerate Mod (a normal, non-
    // IL2CPP .NET assembly - real IL, no AOT-stub limitation) found the
    // exact mechanism: OnFixedUpdate() calls m_dds3KernelMainLoop() once
    // unconditionally, then a SECOND time if a static flag
    // (NocturneGraphicsConfigurator.ForceExtraLoopCall) is true at that
    // point in the SAME call - a flag that is reset to false at the very
    // start of every OnFixedUpdate() and set to true by CallAhead()
    // (called from a Harmony Postfix, OutOfPlaceFix.Postfix(), hooked onto
    // several game-internal methods discovered via reflection: fixed names
    // fldAutoMapSeqEnd/fldAutoMapSeqStart/fldTitleMiniStart2, plus
    // reflection-matched methods whose names contain "Cut"+"End" (not
    // "Init"), "_Set", "_Init", "PopPosition"/"PopRotate", "_Init" again on
    // a different type). This structurally explains a PERSISTENT doubling
    // if any of those tracked methods reliably fires at least once per
    // m_dds3KernelMainLoop() tick going forward - this probe exists to
    // verify that call-count picture directly at runtime, WITHOUT re-using
    // F10 (permanently retired after the Phase 4 side effect - see section
    // 42.4). It is passive: it only counts calls, added for potential
    // future testing via some other natural trigger, not to be exercised
    // via a new manual PoC in this session.
    //
    // Read-only Harmony Prefix counters only; never touches __result or
    // any argument, never calls anything itself. If the third-party mod
    // DLL is absent/renamed, registration is skipped gracefully (see
    // ModMain.OnInitializeMelon()).
    internal static class Root26KernelLoopFrequencyProbe
    {
        private const string Tag = "[NocturneModernController][Root26KernelLoop]";
        private const long HeartbeatIntervalMs = 2000;

        internal static long KernelMainLoopCalls;
        internal static long OnFixedUpdateCalls;

        private static long _lastHeartbeatMs = -1;

        internal static void Sample()
        {
            long now = Environment.TickCount64;
            if (_lastHeartbeatMs >= 0 && now - _lastHeartbeatMs < HeartbeatIntervalMs)
            {
                return;
            }
            _lastHeartbeatMs = now;
            MelonLogger.Msg(
                $"{Tag} HEARTBEAT m_dds3KernelMainLoopCalls={KernelMainLoopCalls} " +
                $"FramerateModOnFixedUpdateCalls={OnFixedUpdateCalls} at {DateTimeOffset.Now:O}");
        }
    }

    [HarmonyPatch(typeof(Il2Cpp.dds3KernelMain), nameof(Il2Cpp.dds3KernelMain.m_dds3KernelMainLoop))]
    internal static class Root26KernelMainLoopCallProbe
    {
        private static void Prefix()
        {
            System.Threading.Interlocked.Increment(ref Root26KernelLoopFrequencyProbe.KernelMainLoopCalls);
        }
    }

    [HarmonyPatch(
        typeof(Nocturne_Graphics_Configurator.NocturneGraphicsConfigurator),
        nameof(Nocturne_Graphics_Configurator.NocturneGraphicsConfigurator.OnFixedUpdate))]
    internal static class Root26FramerateModOnFixedUpdateCallProbe
    {
        private static void Prefix()
        {
            System.Threading.Interlocked.Increment(ref Root26KernelLoopFrequencyProbe.OnFixedUpdateCalls);
        }
    }
}
