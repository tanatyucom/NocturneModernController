using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using Il2Cpp;
using MelonLoader;

namespace NocturneModernController
{
    internal static class SettingsGuiController
    {
        private const int LongPressMilliseconds = 800;
        private static bool _wasHeld;
        private static bool _openedForCurrentHold;
        private static int _holdStartTick;
        private static Process? _process;

        internal static bool IsOpen => _process != null && !_process.HasExited;
        internal static bool IsAvailable => File.Exists(GetExecutablePath());

        internal static void Sample()
        {
            if (_process != null && _process.HasExited)
            {
                _process.Dispose();
                _process = null;
                ControllerSettings.Load();
                ModernControllerApi.ReloadBindings();
                ModernControllerApi.ApplyFeatureToggleRequests();
                MelonLogger.Msg("[NocturneModernController] Settings GUI closed; settings reloaded.");
            }

            bool held;
            try
            {
                held = ModernControllerApi.IsHeld(BuiltInControllerActions.OpenSettings, ControllerContext.Field) ||
                    ModernControllerApi.IsHeld(BuiltInControllerActions.OpenSettings, ControllerContext.Battle) ||
                    ModernControllerApi.IsHeld(BuiltInControllerActions.OpenSettings, ControllerContext.Puzzle) ||
                    ModernControllerApi.IsHeld(BuiltInControllerActions.OpenSettings, ControllerContext.WorldMap) ||
                    ModernControllerApi.IsHeld(BuiltInControllerActions.OpenSettings, ControllerContext.Menu);
            }
            catch (Exception)
            {
                return;
            }

            if (held && !_wasHeld)
            {
                _holdStartTick = Environment.TickCount;
                _openedForCurrentHold = false;
            }
            else if (!held)
            {
                _openedForCurrentHold = false;
            }

            if (held && !_openedForCurrentHold && !IsOpen &&
                unchecked(Environment.TickCount - _holdStartTick) >= LongPressMilliseconds)
            {
                _openedForCurrentHold = true;
                Open();
            }

            _wasHeld = held;
        }

        private static void Open()
        {
            string directory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? string.Empty;
            string executable = GetExecutablePath();
            if (!File.Exists(executable))
            {
                MelonLogger.Warning("[NocturneModernController] Settings GUI is missing: " + executable);
                return;
            }

            ControllerSettings.Save();
            // External providers may publish after this MOD initializes.
            // Refresh immediately before launching the separate GUI process so
            // its snapshot always reflects the currently installed providers.
            ModernControllerApi.SaveFeatureSnapshot();
            string arguments =
                $"\"{ControllerSettings.SettingsPath}\" " +
                $"\"{ModernControllerApi.RegistryPath}\" " +
                $"\"{ModernControllerApi.BindingsPath}\" " +
                $"\"{ModernControllerApi.FeaturesPath}\" " +
                $"\"{ModernControllerApi.FeatureRequestsPath}\" " +
                Process.GetCurrentProcess().Id;
            _process = Process.Start(new ProcessStartInfo(executable, arguments)
            {
                UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(executable) ?? directory
            });
            MelonLogger.Msg("[NocturneModernController] Settings GUI opened; MOD actions suspended.");
        }

        private static string GetExecutablePath()
        {
            string directory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? string.Empty;
            return Path.Combine(
                directory,
                "NocturneModernController.Helper",
                "NocturneModernController.Settings.exe");
        }

        internal static void Shutdown()
        {
            try
            {
                if (_process != null && !_process.HasExited)
                {
                    _process.CloseMainWindow();
                }
            }
            catch (Exception)
            {
            }
        }
    }
}
