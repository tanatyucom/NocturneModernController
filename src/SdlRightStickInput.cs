using System;
using MelonLoader;

namespace NocturneModernController
{
    internal static class SdlRightStickInput
    {
        private const int EngageThreshold = 10000;
        private const int ReleaseThreshold = 6000;
        private static TurnState _state;
        private static VerticalState _verticalState;
        private static int _rawX;
        private static int _rawY;
        private static bool _hasLiveInput;

        internal static bool HasLiveInput => _hasLiveInput;

        internal static void Initialize(MelonLogger.Instance logger)
        {
            ExternalInputBridge.Start(logger);
        }

        internal static void Sample()
        {
            if (SettingsGuiController.IsOpen || !ControllerSettings.Current.RightStickEnabled)
            {
                VanillaTurnInvocationPoc.SetHeldState(false, false, false, false);
                return;
            }
            if (!ExternalInputBridge.TryRead(out int x, out int y))
            {
                _hasLiveInput = false;
                _rawX = 0;
                _rawY = 0;
                VanillaTurnInvocationPoc.SetHeldState(
                    left: false, right: false, up: false, down: false);
                return;
            }

            _hasLiveInput = true;
            _rawX = x;
            _rawY = y;

            TurnState next = _state switch
            {
                TurnState.Left when x < -ReleaseThreshold => TurnState.Left,
                TurnState.Right when x > ReleaseThreshold => TurnState.Right,
                _ when x < -EngageThreshold => TurnState.Left,
                _ when x > EngageThreshold => TurnState.Right,
                _ => TurnState.Neutral
            };
            VerticalState nextVertical = _verticalState switch
            {
                VerticalState.Up when y < -ReleaseThreshold => VerticalState.Up,
                VerticalState.Down when y > ReleaseThreshold => VerticalState.Down,
                _ when y < -EngageThreshold => VerticalState.Up,
                _ when y > EngageThreshold => VerticalState.Down,
                _ => VerticalState.Neutral
            };
            VanillaTurnInvocationPoc.SetHeldState(
                left: next == TurnState.Left,
                right: next == TurnState.Right,
                up: nextVertical == VerticalState.Up,
                down: nextVertical == VerticalState.Down);

            if (next != _state)
            {
                MelonLogger.Msg($"[NocturneModernController] Q4 right stick {next} (x={x}, engage={EngageThreshold}, release={ReleaseThreshold}).");
                _state = next;
            }
            if (nextVertical != _verticalState)
            {
                MelonLogger.Msg($"[NocturneModernController] Q4 right stick vertical {nextVertical} (y={y}).");
                _verticalState = nextVertical;
            }
        }

        internal static void Shutdown()
        {
            _hasLiveInput = false;
            _rawX = 0;
            _rawY = 0;
            VanillaTurnInvocationPoc.SetHeldState(
                left: false, right: false, up: false, down: false);
            ExternalInputBridge.Stop();
        }

        internal static byte GetNativeVerticalAxis()
        {
            ControllerSettings settings = ControllerSettings.Current;
            if (!settings.RightStickEnabled || !_hasLiveInput || settings.RightStickMode == RightStickMode.HorizontalTurn)
            {
                return 128;
            }

            float normalized = NormalizeAxis(_rawY);
            if (settings.InvertY)
            {
                normalized = -normalized;
            }

            normalized = ApplyDeadZoneAndSensitivity(
                normalized,
                settings.DeadZone,
                settings.SensitivityY);

            // SDL Y is negative upward; SMT3 expects Up=255, Down=0.
            float mapped = normalized < 0.0f
                ? 128.0f + (-normalized * 127.0f)
                : 128.0f - (normalized * 128.0f);
            return (byte)Math.Clamp((int)Math.Round(mapped), 0, 255);
        }

        internal static byte GetNativeHorizontalAxis()
        {
            ControllerSettings settings = ControllerSettings.Current;
            if (!settings.RightStickEnabled || !_hasLiveInput || settings.RightStickMode == RightStickMode.HorizontalTurn)
            {
                return 128;
            }

            float normalized = NormalizeAxis(_rawX);
            if (settings.InvertX)
            {
                normalized = -normalized;
            }

            normalized = ApplyDeadZoneAndSensitivity(
                normalized,
                settings.DeadZone,
                settings.SensitivityX);

            // SDL and SMT3 both use negative/low for left and positive/high for right.
            float mapped = normalized < 0.0f
                ? 128.0f + (normalized * 128.0f)
                : 128.0f + (normalized * 127.0f);
            return (byte)Math.Clamp((int)Math.Round(mapped), 0, 255);
        }

        private static float NormalizeAxis(int raw)
        {
            return raw < 0
                ? Math.Max(-1.0f, raw / 32768.0f)
                : Math.Min(1.0f, raw / 32767.0f);
        }

        private static float ApplyDeadZoneAndSensitivity(
            float value,
            float deadZone,
            float sensitivity)
        {
            deadZone = Math.Clamp(deadZone, 0.0f, 0.95f);
            sensitivity = Math.Max(0.0f, sensitivity);
            float magnitude = Math.Abs(value);
            if (magnitude <= deadZone)
            {
                return 0.0f;
            }

            float scaled = ((magnitude - deadZone) / (1.0f - deadZone)) * sensitivity;
            return Math.Sign(value) * Math.Min(1.0f, scaled);
        }

        private enum TurnState
        {
            Neutral,
            Left,
            Right
        }

        private enum VerticalState
        {
            Neutral,
            Up,
            Down
        }
    }
}
