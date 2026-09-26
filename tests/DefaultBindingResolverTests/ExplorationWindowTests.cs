using System;
using NocturneModernController;

// shared/ExplorationWindow.cs: the 100 ms rule ExplorationState applies to the
// last observed field update.
internal static class ExplorationWindowTests
{
    internal static void Run()
    {
        Check(ExplorationWindow.IsActive(1000, 1000), "active right after a field update");
        Check(ExplorationWindow.IsActive(1000, 1100), "active at exactly 100 ms");
        Check(!ExplorationWindow.IsActive(1000, 1101), "inactive after 100 ms");
        Check(!ExplorationWindow.IsActive(1000, 60000), "inactive long after the last update");
        Check(ExplorationWindow.IsActive(int.MaxValue - 10, int.MinValue + 50),
            "Environment.TickCount wrap-around keeps the 100 ms window");
        Check(!ExplorationWindow.IsActive(int.MaxValue - 10, int.MinValue + 200),
            "window still expires across the wrap-around");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
