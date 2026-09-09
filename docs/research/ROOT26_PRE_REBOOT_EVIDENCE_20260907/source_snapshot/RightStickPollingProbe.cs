using System;
using HarmonyLib;
using Il2Cpp;
using MelonLoader;

namespace NocturneModernController
{
    // Root-26 diagnostic-only PoC: read-only observation of the vanilla
    // dds3PadManager.GetPadAnalog return value for the Right Stick X/Y
    // channels (padno=0, stick_lr=1, cip_no=1 - see
    // docs/research/RIGHT_STICK_VIEW_AND_DASH_INVESTIGATION.md section 4.1
    // for the confirmed native signature), independent of
    // SdlRightStickInput.HasLiveInput and without touching
    // NativeRightStickCameraPatch (AnalogCameraRouteProbe.cs) in any way.
    //
    // Purpose (updated for the "continuous rolling observation" experiment):
    // rather than a fixed 3-second one-shot window, continuously observe for
    // as long as FieldDashPatch.IsExplorationActive stays true (a "session"),
    // aggregating samples into ~250ms buckets per channel, and log only when
    // the debounced classification (DEAD = completely flat at 128, LIVE =
    // repeated non-128 values observed) changes. This is meant to
    // directly time-stamp the moment the native value stops being flat at
    // 128, no matter how far into the session it occurs.
    //
    // Purely observational: __result is read only, never modified. No
    // synthetic input, no foreground manipulation, no SetForegroundWindow/
    // AttachThreadInput, no SDL/MMF/Broker/NativeRightStickCameraPatch
    // changes.
    [HarmonyPatch(typeof(dds3PadManager), nameof(dds3PadManager.GetPadAnalog))]
    internal static class RightStickPollingProbe
    {
        private const int BucketDurationMilliseconds = 250;

        // A bucket is classified LIVE only if it contains at least this many
        // samples whose value is not exactly the centered/rest value (128).
        // This is a small threshold, not a strict "any deviation" trigger, so
        // that a single stray/noisy sample does not flip the classification.
        private const int NonCenterValueThreshold = 3;
        private const int DebounceBucketThreshold = 2;
        private const byte CenterValue = 128;

        private enum ChannelState
        {
            Unknown,
            Dead,
            Live
        }

        private sealed class ChannelStats
        {
            internal int Count;
            internal byte Min = byte.MaxValue;
            internal byte Max = byte.MinValue;
            internal byte Last;
            internal int NonCenterCount;

            internal void Reset()
            {
                Count = 0;
                Min = byte.MaxValue;
                Max = byte.MinValue;
                Last = 0;
                NonCenterCount = 0;
            }

            internal void Observe(byte value)
            {
                Count++;
                if (value < Min)
                {
                    Min = value;
                }
                if (value > Max)
                {
                    Max = value;
                }
                Last = value;
                if (value != CenterValue)
                {
                    NonCenterCount++;
                }
            }
        }

        // Per-bucket accumulation only; reset every BucketDurationMilliseconds.
        private static readonly ChannelStats BucketX = new();
        private static readonly ChannelStats BucketY = new();

        // Whole-session cumulative totals, used only for the final summary.
        private static readonly ChannelStats SessionX = new();
        private static readonly ChannelStats SessionY = new();

        private static bool _armed;
        private static bool _sessionActive;
        private static int _bucketStartTick;
        private static ChannelState _stateX = ChannelState.Unknown;
        private static ChannelState _stateY = ChannelState.Unknown;
        private static ChannelState _candidateX = ChannelState.Unknown;
        private static ChannelState _candidateY = ChannelState.Unknown;
        private static int _candidateBucketsX;
        private static int _candidateBucketsY;
        private static int _transitionCountX;
        private static int _transitionCountY;
        private static bool _finalSummaryEmitted;

        static RightStickPollingProbe()
        {
            // Best-effort "game end" flush. Registered here (rather than via
            // a ModMain.OnDeinitializeMelon hook) so this file alone owns its
            // full lifecycle. Never throws past this handler.
            try
            {
                AppDomain.CurrentDomain.ProcessExit += (_, _) => Shutdown();
            }
            catch (Exception ex)
            {
                MelonLogger.Warning(
                    "[NocturneModernController][Root26NativePoll] Failed to register ProcessExit handler: " +
                    ex.GetType().FullName + ": " + ex.Message);
            }
        }

        // No `ref` - this Postfix never modifies __result.
        private static void Postfix(int __0, int __1, int __2, int __3, byte __result)
        {
            if (__0 != 0 || __1 != 1 || __3 != 1 || (__2 != 0 && __2 != 1))
            {
                return; // not padno=0 / right stick / cip_no=1 / X or Y
            }

            if (!FieldDashPatch.IsExplorationActive)
            {
                if (_sessionActive)
                {
                    EndSession("exploration ended");
                }
                return;
            }

            if (!_armed)
            {
                // Arms once per exploration bout ("session"); a new session
                // starts again the next time exploration becomes active.
                _armed = true;
                StartSession();
            }

            int now = Environment.TickCount;
            if (unchecked(now - _bucketStartTick) >= BucketDurationMilliseconds)
            {
                FlushBucket();
                _bucketStartTick = now;
            }

            if (__2 == 0)
            {
                BucketX.Observe(__result);
                SessionX.Observe(__result);
            }
            else
            {
                BucketY.Observe(__result);
                SessionY.Observe(__result);
            }
        }

        // Called once per frame so exploration end and partially filled
        // buckets are observed even if GetPadAnalog stops being called.
        internal static void Sample()
        {
            if (!_sessionActive)
            {
                return;
            }

            if (!FieldDashPatch.IsExplorationActive)
            {
                EndSession("exploration ended");
                return;
            }

            int now = Environment.TickCount;
            if (unchecked(now - _bucketStartTick) >= BucketDurationMilliseconds)
            {
                FlushBucket();
                _bucketStartTick = now;
            }
        }

        internal static void Shutdown()
        {
            if (_sessionActive)
            {
                EndSession("mod shutdown / game exit");
            }
        }

        private static void StartSession()
        {
            _sessionActive = true;
            _finalSummaryEmitted = false;
            _bucketStartTick = Environment.TickCount;
            BucketX.Reset();
            BucketY.Reset();
            SessionX.Reset();
            SessionY.Reset();
            _stateX = ChannelState.Unknown;
            _stateY = ChannelState.Unknown;
            _candidateX = ChannelState.Unknown;
            _candidateY = ChannelState.Unknown;
            _candidateBucketsX = 0;
            _candidateBucketsY = 0;
            _transitionCountX = 0;
            _transitionCountY = 0;
            MelonLogger.Msg(
                "[NocturneModernController][Root26NativePoll] SESSION-START rolling bucket observation " +
                $"(bucket={BucketDurationMilliseconds}ms, debounce={DebounceBucketThreshold} buckets, " +
                "continues while exploration stays active; initial state UNKNOWN).");
        }

        private static void FlushBucket()
        {
            EvaluateBucket("X", BucketX, ref _stateX, ref _candidateX, ref _candidateBucketsX, ref _transitionCountX);
            EvaluateBucket("Y", BucketY, ref _stateY, ref _candidateY, ref _candidateBucketsY, ref _transitionCountY);
            BucketX.Reset();
            BucketY.Reset();
        }

        private static void EvaluateBucket(
            string label,
            ChannelStats bucket,
            ref ChannelState state,
            ref ChannelState candidate,
            ref int candidateBuckets,
            ref int transitionCount)
        {
            if (bucket.Count == 0)
            {
                return; // no samples this bucket - not enough information to judge, keep previous state
            }

            ChannelState observed;
            if (bucket.NonCenterCount >= NonCenterValueThreshold)
            {
                observed = ChannelState.Live;
            }
            else if (bucket.NonCenterCount == 0 && bucket.Min == CenterValue && bucket.Max == CenterValue)
            {
                observed = ChannelState.Dead;
            }
            else
            {
                // One or two off-centre samples are deliberately treated as
                // noise/indeterminate and cannot change the stable state.
                candidate = ChannelState.Unknown;
                candidateBuckets = 0;
                return;
            }

            if (observed == state)
            {
                candidate = ChannelState.Unknown;
                candidateBuckets = 0;
                return;
            }

            if (candidate != observed)
            {
                candidate = observed;
                candidateBuckets = 1;
                return;
            }

            candidateBuckets++;
            if (candidateBuckets < DebounceBucketThreshold)
            {
                return;
            }

            ChannelState previous = state;
            state = observed;
            candidate = ChannelState.Unknown;
            candidateBuckets = 0;
            if (previous != ChannelState.Unknown)
            {
                transitionCount++;
            }

            string transition = previous == ChannelState.Unknown
                ? $"INITIAL -> {StateLabel(observed)}"
                : $"{StateLabel(previous)} -> {StateLabel(observed)}";
            MelonLogger.Msg(
                $"[NocturneModernController][Root26NativePoll] STATE-TRANSITION channel={label} " +
                $"{transition} " +
                $"bucket(count={bucket.Count} min={bucket.Min} max={bucket.Max} last={bucket.Last} nonCenter={bucket.NonCenterCount}) " +
                $"at {DateTimeOffset.Now:O}");
        }

        private static string StateLabel(ChannelState state) => state switch
        {
            ChannelState.Dead => "DEAD(128-fixed)",
            ChannelState.Live => "LIVE(analog-active)",
            _ => "UNKNOWN"
        };

        private static void EndSession(string reason)
        {
            FlushBucket();
            EmitFinalSummaryIfNeeded(reason);
            _sessionActive = false;
            _armed = false; // allow re-arming if exploration becomes active again later this run
        }

        private static void EmitFinalSummaryIfNeeded(string reason)
        {
            if (_finalSummaryEmitted)
            {
                return;
            }
            _finalSummaryEmitted = true;

            MelonLogger.Msg(
                $"[NocturneModernController][Root26NativePoll] SESSION-SUMMARY ({reason}) " +
                $"channel=RightStickX(0,1,0,1) {FormatSummary(SessionX)} finalState={StateLabel(_stateX)} transitions={_transitionCountX}");
            MelonLogger.Msg(
                $"[NocturneModernController][Root26NativePoll] SESSION-SUMMARY ({reason}) " +
                $"channel=RightStickY(0,1,1,1) {FormatSummary(SessionY)} finalState={StateLabel(_stateY)} transitions={_transitionCountY}");
        }

        private static string FormatSummary(ChannelStats stats) =>
            stats.Count == 0
                ? "count=0 (no samples)"
                : $"count={stats.Count} min={stats.Min} max={stats.Max} last={stats.Last} nonCenter={stats.NonCenterCount}";
    }
}
