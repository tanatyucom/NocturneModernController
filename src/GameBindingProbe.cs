using System;
using Il2Cpp;
using MelonLoader;

namespace NocturneModernController
{
    // Read-only diagnostic PoC (temporary, not part of the public API surface).
    // Confirms whether dds3ConfigGamePadSteam.GetConfigGamePad(index) tracks the
    // player's current native Controller Key Config binding state. No writes
    // (ChangeKey and other write APIs are never called). No Harmony patches.
    // Logs the full 0..0x21 (34-slot) sweep once.
    internal static class GameBindingProbe
    {
        private const int SlotCount = 0x22;

        private static bool _logged;

        internal static void Sample()
        {
            if (_logged || !ExplorationState.IsExplorationActive)
            {
                return;
            }

            try
            {
                MelonLogger.Msg("[GameBindingProbe] BEGIN");

                for (int index = 0; index < SlotCount; index++)
                {
                    try
                    {
                        int raw = dds3ConfigGamePadSteam.GetConfigGamePad(index);
                        MelonLogger.Msg($"[GameBindingProbe] GetConfigGamePad index={index} raw={raw}");
                    }
                    catch (Exception ex)
                    {
                        MelonLogger.Msg(
                            $"[GameBindingProbe] GetConfigGamePad index={index} ERROR {ex.GetType().Name}: {ex.Message}");
                    }
                }

                MelonLogger.Msg("[GameBindingProbe] END");

                // Only latch once the full sweep has completed; a fatal error
                // below (native side mid-initialization) should retry next tick.
                _logged = true;
            }
            catch (Exception ex)
            {
                MelonLogger.Msg($"[GameBindingProbe] fatal error, will retry: {ex.GetType().Name}: {ex.Message}");
            }
        }
    }
}
