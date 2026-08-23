using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using HarmonyLib;
using Il2Cpp;
using MelonLoader;

namespace NocturneModernController
{
    [HarmonyPatch(typeof(dds3PadManager), nameof(dds3PadManager.GetPadAnalog))]
    internal static class AnalogChannelTelemetry
    {
        private sealed class ChannelStats
        {
            internal int Count;
            internal byte Min = byte.MaxValue;
            internal byte Max = byte.MinValue;
            internal byte Last;
        }

        private static readonly Dictionary<string, ChannelStats> Channels = new();
        private static readonly Stopwatch Clock = Stopwatch.StartNew();
        private static long _lastReportMilliseconds;

        private static void Postfix(int __0, int __1, int __2, int __3, byte __result)
        {
            if (!FieldDashPatch.IsExplorationActive)
            {
                return;
            }

            string key = $"{__0},{__1},{__2},{__3}";
            if (!Channels.TryGetValue(key, out ChannelStats? stats))
            {
                stats = new ChannelStats();
                Channels.Add(key, stats);
            }

            stats.Count++;
            stats.Min = Math.Min(stats.Min, __result);
            stats.Max = Math.Max(stats.Max, __result);
            stats.Last = __result;

            long now = Clock.ElapsedMilliseconds;
            if (now - _lastReportMilliseconds < 2000)
            {
                return;
            }

            _lastReportMilliseconds = now;
            StringBuilder report = new StringBuilder("[NocturneModernController] ANALOG-CHANNELS");
            foreach (KeyValuePair<string, ChannelStats> entry in Channels)
            {
                ChannelStats value = entry.Value;
                report.Append($" [{entry.Key}: n={value.Count} min={value.Min} max={value.Max} last={value.Last}]");
            }

            MelonLogger.Msg(report.ToString());
            Channels.Clear();
        }
    }
}
