using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using HarmonyLib;
using Il2Cpp;
using MelonLoader;

namespace NocturneModernController
{
    [HarmonyPatch(typeof(fldPlayer), nameof(fldPlayer.fldPlayerCalc))]
    internal static class FieldDashPatch
    {
        private const int VirtualKeyDash = 0x50; // P
        // Dungeon doors use thin event/collision volumes.  At x1.60 the
        // player can cross one between field ticks, so keep dungeon movement
        // below that tunnelling threshold.  The world map has no such doors
        // and retains the established x1.60 feel.
        private const float DungeonMultiplier = 1.50f;
        private const float WorldMapMultiplier = 1.60f;
        private const int NormalSpeedRva = 0x02AF1FF8;
        private const int AlternateSpeedRva = 0x028C7528;
        private const int WorldMapSpeedRva = 0x028C9E30;
        private const float ExpectedNormalSpeed = 29f;
        private const float ExpectedAlternateSpeed = 20f;
        private const float ExpectedWorldMapSpeed = 16f;
        private const uint PageExecuteReadWrite = 0x40;

        private static IntPtr _normalSpeedAddress;
        private static IntPtr _alternateSpeedAddress;
        private static IntPtr _worldMapSpeedAddress;
        private static bool _addressesValidated;
        private static bool _patchActive;
        private static bool _loggedHeld;
        private static bool _loggedUnsupported;
        private static bool _dashLatched;
        private static bool _comboWasHeld;
        private static int _lastExplorationTick;

        internal static bool IsExplorationActive =>
            unchecked(Environment.TickCount - _lastExplorationTick) <= 100;

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int virtualKey);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool VirtualProtect(
            IntPtr address,
            UIntPtr size,
            uint newProtection,
            out uint oldProtection);

        private static bool IsPadMapHeld(Il2Cpplibsdf_H.SDF_PADMAP map)
        {
            try
            {
                return dds3PadManager.DDS3_PADCHECK_PRESS(map, 0);
            }
            catch (Exception)
            {
                // Keep the verified keyboard/reWASD fallback available even if
                // the game's logical pad manager is unavailable during startup.
                return false;
            }
        }

        private static bool UpdateDashState()
        {
            bool dashHeld = ModernControllerApi.IsHeld(
                BuiltInControllerActions.Dash,
                ControllerContext.Field);
            bool comboHeld = ModernControllerApi.IsHeld(
                BuiltInControllerActions.DashKeep,
                ControllerContext.Field);
            if (comboHeld && !_comboWasHeld)
            {
                _dashLatched = !_dashLatched;
                MelonLogger.Msg(
                    $"[NocturneModernController] Dash keep {(_dashLatched ? "ON" : "OFF")} (LT+RT)");
            }
            _comboWasHeld = comboHeld;

            bool keyboardHeld = (GetAsyncKeyState(VirtualKeyDash) & 0x8000) != 0;
            return keyboardHeld || dashHeld || _dashLatched;
        }

        private static void Prefix()
        {
            _lastExplorationTick = Environment.TickCount;
            RestoreSpeeds();
            if (SettingsGuiController.IsOpen || !ControllerSettings.Current.DashEnabled)
            {
                return;
            }
            if (!UpdateDashState())
            {
                LogRelease();
                return;
            }

            if (!ValidateAddresses())
            {
                return;
            }

            // Wm2 consumes its own movement-step constant. It is harmless to
            // patch it around the dispatcher when another field mode is active.
            WriteFloat(_worldMapSpeedAddress, ExpectedWorldMapSpeed * WorldMapMultiplier);
            if (IsSafeNormalMovement())
            {
                WriteFloat(_normalSpeedAddress, ExpectedNormalSpeed * DungeonMultiplier);
                WriteFloat(_alternateSpeedAddress, ExpectedAlternateSpeed * DungeonMultiplier);
            }
            _patchActive = true;
            if (!_loggedHeld)
            {
                _loggedHeld = true;
                MelonLogger.Msg(
                    "[NocturneModernController] Dash ON " +
                    "(P/LT/RT, dungeon x1.50 / world map x1.60)");
            }
        }

        private static void Postfix()
        {
            RestoreSpeeds();
        }

        private static Exception? Finalizer(Exception? __exception)
        {
            RestoreSpeeds();
            return __exception;
        }

        private static bool ValidateAddresses()
        {
            if (_addressesValidated)
            {
                return true;
            }

            IntPtr moduleBase = IntPtr.Zero;
            foreach (ProcessModule module in Process.GetCurrentProcess().Modules)
            {
                if (string.Equals(
                    Path.GetFileName(module.FileName),
                    "GameAssembly.dll",
                    StringComparison.OrdinalIgnoreCase))
                {
                    moduleBase = module.BaseAddress;
                    break;
                }
            }
            if (moduleBase == IntPtr.Zero)
            {
                return false;
            }

            _normalSpeedAddress = IntPtr.Add(moduleBase, NormalSpeedRva);
            _alternateSpeedAddress = IntPtr.Add(moduleBase, AlternateSpeedRva);
            _worldMapSpeedAddress = IntPtr.Add(moduleBase, WorldMapSpeedRva);
            float normal = ReadFloat(_normalSpeedAddress);
            float alternate = ReadFloat(_alternateSpeedAddress);
            float worldMap = ReadFloat(_worldMapSpeedAddress);
            if (Math.Abs(normal - ExpectedNormalSpeed) > 0.001f ||
                Math.Abs(alternate - ExpectedAlternateSpeed) > 0.001f ||
                Math.Abs(worldMap - ExpectedWorldMapSpeed) > 0.001f)
            {
                if (!_loggedUnsupported)
                {
                    _loggedUnsupported = true;
                    MelonLogger.Warning(
                        $"[NocturneModernController] Unsupported game constants; dash disabled " +
                        $"(normal={normal}, alternate={alternate}, worldMap={worldMap}).");
                }
                return false;
            }

            _addressesValidated = true;
            MelonLogger.Msg("[NocturneModernController] Native movement speed constants validated (29/20/16).");
            return true;
        }

        private static float ReadFloat(IntPtr address)
        {
            return BitConverter.Int32BitsToSingle(Marshal.ReadInt32(address));
        }

        private static void WriteFloat(IntPtr address, float value)
        {
            if (!VirtualProtect(address, (UIntPtr)4, PageExecuteReadWrite, out uint oldProtection))
            {
                throw new InvalidOperationException("VirtualProtect failed: " + Marshal.GetLastWin32Error());
            }

            Marshal.WriteInt32(address, BitConverter.SingleToInt32Bits(value));
            VirtualProtect(address, (UIntPtr)4, oldProtection, out _);
        }

        private static void RestoreSpeeds()
        {
            if (!_patchActive)
            {
                return;
            }

            WriteFloat(_normalSpeedAddress, ExpectedNormalSpeed);
            WriteFloat(_alternateSpeedAddress, ExpectedAlternateSpeed);
            WriteFloat(_worldMapSpeedAddress, ExpectedWorldMapSpeed);
            _patchActive = false;
        }

        private static bool IsSafeNormalMovement()
        {
            try
            {
                return fldPlayer.playerRun &&
                    fldPlayer.gfldPlayerHasiCnt == 0 &&
                    fldPlayer.gfldPlayerAnaCnt == 0 &&
                    fldPlayer.gfldPlayerDamegeCnt == 0 &&
                    !fldPlayer.bRestoreMode;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static void LogRelease()
        {
            if (!_loggedHeld)
            {
                return;
            }

            _loggedHeld = false;
            MelonLogger.Msg("[NocturneModernController] Dash OFF");
        }
    }
}
