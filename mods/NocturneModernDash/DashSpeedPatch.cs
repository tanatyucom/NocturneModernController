using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using HarmonyLib;
using Il2Cpp;
using Il2Cpplibsdf_H;
using MelonLoader;

namespace NocturneModernDash
{
    // The speed change, moved unchanged from Controller's built-in
    // FieldDashPatch: during the field update only (Prefix writes, Postfix and
    // Finalizer restore), the native movement-step constants 29/20 (dungeon)
    // and 16 (world map) are multiplied by 1.5. Because it only acts inside
    // fldPlayerCalc, no separate exploration tracking is needed.
    [HarmonyPatch(typeof(fldPlayer), nameof(fldPlayer.fldPlayerCalc))]
    internal static class DashSpeedPatch
    {
        private const string LogPrefix = "[NocturneModernDash] ";
        private const int VirtualKeyDash = 0x50; // P
        // Dungeon doors use thin event/collision volumes.  At x1.60 the
        // player can cross one between field ticks, so keep dungeon movement
        // below that tunnelling threshold.  The world map has no such doors
        // and uses the same conservative x1.50 multiplier.
        private const float DungeonMultiplier = 1.50f;
        private const float WorldMapMultiplier = 1.50f;
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

        internal static DashState State { get; } = new();

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int virtualKey);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool VirtualProtect(
            IntPtr address,
            UIntPtr size,
            uint newProtection,
            out uint oldProtection);

        private static bool UpdateDashState()
        {
            ModernControllerIntegration? integration = DashMod.Integration;
            bool dashHeld = DashInput.ReadHeld(
                integration != null,
                () => integration!.IsHeld(ModernControllerIntegration.DashActionId),
                StandaloneInput.IsDashHeld);
            bool comboHeld = DashInput.ReadHeld(
                integration != null,
                () => integration!.IsHeld(ModernControllerIntegration.DashKeepActionId),
                StandaloneInput.IsKeepComboHeld);
            bool keyboardHeld = (GetAsyncKeyState(VirtualKeyDash) & 0x8000) != 0;

            bool active = State.Update(dashHeld, comboHeld, keyboardHeld, out bool keepToggled);
            if (keepToggled)
            {
                MelonLogger.Msg(LogPrefix + $"Dash keep {(State.IsKeepOn ? "ON" : "OFF")} (LT+RT)");
            }
            return active;
        }

        private static void Prefix()
        {
            RestoreSpeeds();
            if (!DashGate.Allows(DashSettings.Current.Enabled, DashMod.IsSettingsOpen))
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
                    LogPrefix + "Dash ON " +
                    "(P/LT/RT, dungeon x1.50 / world map x1.50)");
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
                        LogPrefix + "Unsupported game constants; dash disabled " +
                        $"(normal={normal}, alternate={alternate}, worldMap={worldMap}).");
                }
                return false;
            }

            _addressesValidated = true;
            MelonLogger.Msg(LogPrefix + "Native movement speed constants validated (29/20/16).");
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
            MelonLogger.Msg(LogPrefix + "Dash OFF");
        }
    }

    // Standalone input: the game's logical LT/RT (the same pad maps Controller
    // uses for ControllerButton.LT/RT), so any pad the game supports works.
    // LT and RT have no field/dungeon action in the game's default key config.
    internal static class StandaloneInput
    {
        internal static bool IsDashHeld() =>
            IsHeld(SDF_PADMAP.SDF_PADMAP_L2) || IsHeld(SDF_PADMAP.SDF_PADMAP_R2);

        internal static bool IsKeepComboHeld() =>
            IsHeld(SDF_PADMAP.SDF_PADMAP_L2) && IsHeld(SDF_PADMAP.SDF_PADMAP_R2);

        private static bool IsHeld(SDF_PADMAP map)
        {
            try
            {
                return dds3PadManager.DDS3_PADCHECK_PRESS(map, 0);
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
