using System;
using System.Runtime.InteropServices;
using HarmonyLib;
using Il2Cpp;
using MelonLoader;

namespace NocturneModernController
{
    // Root-26 experimental PoC: explicitly calls the public, Valve-
    // documented ISteamInput::RunFrame() (flat export
    // SteamAPI_ISteamInput_RunFrame) once per frame, from a Harmony
    // Prefix on SteamInputUtil.UpdateInput() - i.e. immediately BEFORE
    // the game's own per-frame GetAnalogActionData reads that same
    // frame.
    //
    // Hypothesis under test (STRONG HYPOTHESIS, not yet CONFIRMED):
    // this session's static analysis found that SMT3HD's GameAssembly.dll
    // never calls ISteamInput::RunFrame() itself (CONFIRMED via
    // exhaustive string search across the entire 400MB+ binary -
    // "ISteamInput_RunFrame" occurs 0 times, and dynamic
    // GetProcAddress-style resolution requires that exact name string to
    // exist somewhere to be callable at all). SMT3HD's general Steam
    // callback pump was also fully traced this session
    // (SteamManager.Update() -> SteamAPI_ManualDispatch_RunFrame() ->
    // SteamAPI_ManualDispatch_GetNextCallback() drain loop, all CONFIRMED
    // correct and running every frame), but multiple real-machine
    // sessions (Root26EntryTimelineProbe logs) showed the native
    // entry.x/y / self+0x109708 / field116780 state staying flat for
    // long stretches between Guide-triggered bursts, which is NOT the
    // pattern you would expect if that general pump already drove
    // ISteamInput's own per-frame action-data refresh. RAIDOU
    // Remastered (same Unity/IL2CPP toolchain, confirmed via its
    // global-metadata.dat to bind ISteamInput_RunFrame) shows RSTICK
    // LIVE from startup with no Guide press. This PoC tests whether
    // explicitly driving RunFrame() ourselves changes SMT3HD's behavior.
    //
    // Self pointer: reused from the SAME SteamAPI_SteamInput_v002()
    // accessor already used by Root26ActionSetAndOriginsProbe (Chapter
    // 64/65 CONFIRMED side-effect-free public accessor for the
    // already-initialized ISteamInput* singleton) - no new Steam API
    // initialization is performed here.
    //
    // Scope / safety (per explicit user instruction):
    //  - Default DISABLED (Enabled = false below). Flip to true and
    //    rebuild ONLY for the deliberate "B" (RunFrame-enabled) test
    //    run; rebuild back to false for the "A" (baseline) run. This is
    //    a compile-time toggle, not a live keybind (F9/F10 remain
    //    off-limits per the standing safety constraints), so it cannot
    //    be triggered by an accidental key press.
    //  - The ONLY Steam Input API called from this file is
    //    SteamAPI_ISteamInput_RunFrame (plus the read-only
    //    SteamAPI_SteamInput_v002 self-pointer accessor). No
    //    ActivateActionSet/ActivateActionSetLayer/ResetController/
    //    SteamControllerReStart/Shutdown/Init/UpdateConnectedControllers
    //    call of any kind.
    //  - No native hook/injection/patch of steamclient64.dll,
    //    GameOverlayRenderer64.dll, or steam_api64.dll itself - this is
    //    a plain P/Invoke against an already-loaded, already-exported,
    //    publicly documented function, called the same way any
    //    legitimate Steamworks-linked application would call it.
    //  - No per-frame logging: only a one-time "PoC enabled" message,
    //    a one-time "first call succeeded" message, and (if any)
    //    exceptions - each logged at most once per session.
    [HarmonyPatch(typeof(SteamInputUtil), nameof(SteamInputUtil.UpdateInput))]
    internal static class Root26SteamInputRunFramePoc
    {
        // Flip to true and rebuild for the "B" (RunFrame-enabled) test
        // run. Leave false for the "A" (baseline) run and for any
        // non-experiment build. Deliberately "static readonly" rather
        // than "const" so the compiler does not flag the rest of
        // Prefix() as unreachable dead code when this is false.
        private static readonly bool Enabled = false;

        private const string Tag = "[NocturneModernController][Root26RunFramePoc]";

        [DllImport("steam_api64.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr SteamAPI_SteamInput_v002();

        [DllImport("steam_api64.dll", CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        private static extern bool SteamAPI_ISteamInput_RunFrame(
            IntPtr self,
            [MarshalAs(UnmanagedType.I1)] bool bReservedValue);

        private static bool _announcedEnabled;
        private static bool _selfPointerResolved;
        private static IntPtr _selfPointer = IntPtr.Zero;
        private static bool _selfDisabledLogged;
        private static bool _firstCallLogged;
        private static bool _errorLogged;

        private static void Prefix()
        {
            if (!Enabled)
            {
                return;
            }

            if (!_announcedEnabled)
            {
                _announcedEnabled = true;
                MelonLogger.Msg($"{Tag} PoC ENABLED - will call SteamAPI_ISteamInput_RunFrame() every frame, immediately before SteamInputUtil.UpdateInput().");
            }

            if (!ResolveSelfPointer())
            {
                return;
            }

            try
            {
                SteamAPI_ISteamInput_RunFrame(_selfPointer, false);
                if (!_firstCallLogged)
                {
                    _firstCallLogged = true;
                    MelonLogger.Msg($"{Tag} SteamAPI_ISteamInput_RunFrame() first call succeeded (self=0x{_selfPointer.ToInt64():X}).");
                }
            }
            catch (Exception ex)
            {
                if (!_errorLogged)
                {
                    _errorLogged = true;
                    MelonLogger.Warning($"{Tag} SteamAPI_ISteamInput_RunFrame() threw {ex.GetType().FullName}: {ex.Message} - PoC disabled for rest of session.");
                }
            }
        }

        // Resolves SteamAPI_SteamInput_v002() exactly once per session
        // (same pattern as Root26ActionSetAndOriginsProbe.ResolveSelfPointer).
        private static bool ResolveSelfPointer()
        {
            if (_selfPointerResolved)
            {
                return _selfPointer != IntPtr.Zero;
            }
            _selfPointerResolved = true;

            try
            {
                _selfPointer = SteamAPI_SteamInput_v002();
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"{Tag} SteamAPI_SteamInput_v002() threw {ex.GetType().FullName}: {ex.Message} - PoC disabled for this session.");
                _selfPointer = IntPtr.Zero;
                return false;
            }

            if (_selfPointer == IntPtr.Zero)
            {
                if (!_selfDisabledLogged)
                {
                    _selfDisabledLogged = true;
                    MelonLogger.Warning($"{Tag} SteamAPI_SteamInput_v002() returned IntPtr.Zero - PoC disabled for this session.");
                }
                return false;
            }

            return true;
        }
    }
}
