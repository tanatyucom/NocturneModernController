using System;
using Il2Cpp;
using MelonLoader;

namespace NocturneModernController
{
    // Field auto-map shown: the game's own fldAutoMap.fldAutoMapChkMode()
    // returns fldAutoMap.AutoMapSeq, which fldAutoMapSeqStart sets to 1 when
    // the map opens and fldAutoMapSeqEnd / fldAutoMapFree reset to 0
    // (GameAssembly disassembly: 0x1820194ED / 0x182018BD2). The field update
    // keeps running under the map, so ExplorationState stays active there.
    // A failing read counts as closed (Controller behaves as before).
    internal static class AutoMapState
    {
        private static bool _loggedPassThrough;

        internal static bool IsOpen()
        {
            try
            {
                return fldAutoMap.fldAutoMapChkMode() != 0;
            }
            catch (Exception)
            {
                return false;
            }
        }

        internal static void LogPassThrough(SIActionName action)
        {
            if (_loggedPassThrough)
            {
                return;
            }

            _loggedPassThrough = true;
            MelonLogger.Msg(
                $"[NocturneModernController] Q5 auto-map open: native shoulder route passed through ({action}).");
        }
    }
}
