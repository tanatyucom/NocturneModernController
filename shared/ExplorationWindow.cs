namespace NocturneModernController
{
    // Timing rule behind ExplorationState: field exploration counts as active
    // while the last observed field update (fldPlayer.fldPlayerCalc) is at
    // most 100 ms old. Environment.TickCount wraps, so compare unchecked.
    internal static class ExplorationWindow
    {
        internal const int ActiveWindowMilliseconds = 100;

        internal static bool IsActive(int lastFieldTick, int nowTick) =>
            unchecked(nowTick - lastFieldTick) <= ActiveWindowMilliseconds;
    }
}
