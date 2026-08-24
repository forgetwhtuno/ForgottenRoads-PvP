namespace ErenshorPvP
{
    // Pure scheduling/ownership policy for the temporary proxy's two native NPC AI loops.
    // Current Assembly-CSharp starts both loops from NPC.Start only when NeverAggro is false.
    // PvP intentionally runs natural Start while NeverAggro is true before countdown, so a
    // temporary proxy must recover only the missing loops after Start and own only what it starts.
    internal static class PvpNativeCombatLoopPolicy
    {
        internal static bool ShouldStartMissingLoop(bool temporaryProxy, bool nativeStartCompleted,
            bool existingHandlePresent, bool alreadyModOwned, int ownedStartCount)
        {
            return temporaryProxy && nativeStartCompleted && !existingHandlePresent && !alreadyModOwned &&
                ownedStartCount == 0;
        }

        internal static bool InfrastructureReady(bool nativeStartCompleted, bool navHandlePresent,
            bool behaviorHandlePresent, int navStartsOwned, int behaviorStartsOwned)
        {
            return nativeStartCompleted && navHandlePresent && behaviorHandlePresent &&
                navStartsOwned <= 1 && behaviorStartsOwned <= 1;
        }

        internal static bool PreGoBoundaryReady(bool neverAggro, bool targetCleared, bool navHeld,
            bool behaviorAdmissionBlocked, bool navStepBlocked)
        {
            return neverAggro && targetCleared && navHeld && behaviorAdmissionBlocked && navStepBlocked;
        }

        internal static bool CanStopOwnedLoop(bool temporaryProxy, bool ownedByMod)
        {
            return temporaryProxy && ownedByMod;
        }

        internal static string RunSelfTests()
        {
            if (!ShouldStartMissingLoop(true, true, false, false, 0)) return "FAIL missing temporary loop not recovered";
            if (ShouldStartMissingLoop(false, true, false, false, 0)) return "FAIL ordinary NPC loop touched";
            if (ShouldStartMissingLoop(true, false, false, false, 0)) return "FAIL loop started before native Start";
            if (ShouldStartMissingLoop(true, true, true, false, 0)) return "FAIL duplicate resident loop";
            if (ShouldStartMissingLoop(true, true, false, true, 0)) return "FAIL duplicate mod-owned loop";
            if (ShouldStartMissingLoop(true, true, false, false, 1)) return "FAIL second owned startup admitted";
            if (!InfrastructureReady(true, true, true, 1, 1)) return "FAIL healthy loop infrastructure";
            if (InfrastructureReady(true, false, true, 0, 1)) return "FAIL missing nav loop accepted";
            if (InfrastructureReady(true, true, false, 1, 0)) return "FAIL missing behavior loop accepted";
            if (InfrastructureReady(true, true, true, 2, 1)) return "FAIL duplicate nav ownership accepted";
            if (!PreGoBoundaryReady(true, true, true, true, true)) return "FAIL inert pre-GO boundary";
            if (PreGoBoundaryReady(false, true, true, true, true)) return "FAIL pre-GO aggression release";
            if (PreGoBoundaryReady(true, false, true, true, true)) return "FAIL pre-GO target retained";
            if (!CanStopOwnedLoop(true, true)) return "FAIL owned cleanup rejected";
            if (CanStopOwnedLoop(true, false)) return "FAIL native loop cleanup claimed";
            if (CanStopOwnedLoop(false, true)) return "FAIL ordinary NPC loop cleanup claimed";
            return "PASS pvp native combat-loop ownership policy";
        }
    }
}
