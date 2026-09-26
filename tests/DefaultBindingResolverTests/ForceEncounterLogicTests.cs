using System;
using NocturneForceEncounter;

// mods/NocturneForceEncounter/ForceEncounterLogic.cs: request rules moved
// from Controller's built-in ForceEncounterRuntime.
internal static class ForceEncounterLogicTests
{
    internal static void Run()
    {
        Gates();
        RisingEdgeRequests();
        TimeoutAndAcceptance();
        Boost();
    }

    private static void Gates()
    {
        var logic = new ForceEncounterLogic();
        Check(logic.Sample(false, true, false, true, 0) == ForceEncounterEvent.None && !logic.IsRequestPending,
            "disabled: no request");
        Check(logic.Sample(true, false, false, true, 0) == ForceEncounterEvent.None && !logic.IsRequestPending,
            "not exploring: no request");
        Check(logic.Sample(true, true, true, true, 0) == ForceEncounterEvent.None && !logic.IsRequestPending,
            "Settings open: no request");
        Check(logic.Sample(true, true, false, false, 0) == ForceEncounterEvent.None && !logic.IsRequestPending,
            "no input: no request");
    }

    private static void RisingEdgeRequests()
    {
        var logic = new ForceEncounterLogic();
        Check(logic.Sample(true, true, false, true, 100) == ForceEncounterEvent.Requested && logic.IsRequestPending,
            "press requests an encounter");
        Check(logic.Sample(true, true, false, true, 200) == ForceEncounterEvent.None,
            "holding the button does not request again");
        logic.Sample(true, true, false, false, 300);
        Check(logic.Sample(true, true, false, true, 400) == ForceEncounterEvent.Requested,
            "release and press requests again");

        // Leaving exploration does not reset the button state (Controller did
        // not sample outside exploration either).
        var held = new ForceEncounterLogic();
        held.Sample(true, true, false, true, 0);
        held.ObserveNormalCheckResult(1);
        held.Sample(true, false, false, true, 50);
        Check(held.Sample(true, true, false, true, 100) == ForceEncounterEvent.None,
            "a button still held across a gate is not a new press");
    }

    private static void TimeoutAndAcceptance()
    {
        var logic = new ForceEncounterLogic();
        logic.Sample(true, true, false, true, 1000);
        Check(logic.Sample(true, true, false, true, 2999) == ForceEncounterEvent.None && logic.IsRequestPending,
            "still pending just before 2 s");
        Check(logic.Sample(true, true, false, true, 3000) == ForceEncounterEvent.TimedOut && !logic.IsRequestPending,
            "times out at 2 s");

        var accepted = new ForceEncounterLogic();
        Check(!accepted.ObserveNormalCheckResult(5), "a result without a request is ignored");
        accepted.Sample(true, true, false, true, 0);
        Check(!accepted.ObserveNormalCheckResult(0) && accepted.IsRequestPending, "result 0 keeps the request pending");
        Check(accepted.ObserveNormalCheckResult(262779) && !accepted.IsRequestPending, "a battle result completes the request");
        Check(!accepted.ObserveNormalCheckResult(262779), "a completed request is reported once");
    }

    private static void Boost()
    {
        var logic = new ForceEncounterLogic();
        float length = 12.5f;
        logic.BoostNextNormalCheck(ref length, true);
        Check(length == 12.5f, "no boost without a request");

        logic.Sample(true, true, false, true, 0);
        logic.BoostNextNormalCheck(ref length, false);
        Check(length == 12.5f, "no boost outside exploration");
        logic.BoostNextNormalCheck(ref length, true);
        Check(length == ForceEncounterLogic.ForcedTravelLength, "pending request boosts the travel length");

        float longer = 200000f;
        logic.BoostNextNormalCheck(ref longer, true);
        Check(longer == 200000f, "a longer native length is kept");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
