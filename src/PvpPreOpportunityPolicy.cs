using System;

namespace ErenshorPvP
{
    // Pure admission policy for a new PvP opportunity. Runtime acquisition of native actor state
    // remains in PvpController so this policy stays deterministic and directly testable.
    internal enum PvpPreOpportunityDecision
    {
        Allowed,
        CharacterNotReady,
        Zoning,
        PvpInactive,
        PartyInvalid,
        BelowMinimumLevel,
        NativeCombatActive,
        RecentNativeCombat,
        NearbyWorldActor,
        RestrictedZone
    }

    internal struct PvpPreOpportunityInput
    {
        internal bool CharacterReady;
        internal bool Zoning;
        internal bool PvpEnabled;
        internal bool PartyValid;
        internal bool NativeCombatActive;
        internal bool RecentNativeCombat;
        internal bool NearbyWorldActor;
        internal bool RestrictedZone;
        internal int PlayerLevel;
    }

    internal static class PvpPreOpportunityPolicy
    {
        // DESIGN-SELECTED: current PvP had no historic absolute floor. Level 3 is the smallest
        // conservative early-game protection while preserving party-average matchmaking above it.
        internal const int MinimumPlayerLevel = 3;
        // A 20-second hold spans the controller's 12-second opportunity scan without making a
        // just-finished pull immediately eligible on the next scan.
        internal const float RecentNativeCombatGraceSeconds = 20f;
        // Slightly beyond the 11m spawn formation, but not an empty-zone requirement.
        internal const float NearbyWorldActorRadius = 12f;

        internal static PvpPreOpportunityDecision Evaluate(PvpPreOpportunityInput input)
        {
            if (!input.CharacterReady) return PvpPreOpportunityDecision.CharacterNotReady;
            if (input.Zoning) return PvpPreOpportunityDecision.Zoning;
            if (!input.PvpEnabled) return PvpPreOpportunityDecision.PvpInactive;
            if (input.RestrictedZone) return PvpPreOpportunityDecision.RestrictedZone;
            if (!input.PartyValid) return PvpPreOpportunityDecision.PartyInvalid;
            if (input.PlayerLevel < MinimumPlayerLevel) return PvpPreOpportunityDecision.BelowMinimumLevel;
            if (input.NativeCombatActive) return PvpPreOpportunityDecision.NativeCombatActive;
            if (input.RecentNativeCombat) return PvpPreOpportunityDecision.RecentNativeCombat;
            if (input.NearbyWorldActor) return PvpPreOpportunityDecision.NearbyWorldActor;
            return PvpPreOpportunityDecision.Allowed;
        }

        internal static string Token(PvpPreOpportunityDecision value)
        {
            switch (value)
            {
                case PvpPreOpportunityDecision.CharacterNotReady: return "character_not_ready";
                case PvpPreOpportunityDecision.Zoning: return "zoning";
                case PvpPreOpportunityDecision.PvpInactive: return "pvp_inactive";
                case PvpPreOpportunityDecision.PartyInvalid: return "party_invalid";
                case PvpPreOpportunityDecision.BelowMinimumLevel: return "below_minimum_level";
                case PvpPreOpportunityDecision.NativeCombatActive: return "native_combat";
                case PvpPreOpportunityDecision.RecentNativeCombat: return "recent_native_combat";
                case PvpPreOpportunityDecision.NearbyWorldActor: return "nearby_world_actor";
                case PvpPreOpportunityDecision.RestrictedZone: return "restricted_zone";
                default: return "allowed";
            }
        }

        internal static string RunSelfTests()
        {
            PvpPreOpportunityInput good = new PvpPreOpportunityInput { CharacterReady = true, PvpEnabled = true,
                PartyValid = true, PlayerLevel = MinimumPlayerLevel };
            if (Evaluate(good) != PvpPreOpportunityDecision.Allowed) return "FAIL preop allowed";
            good.PlayerLevel = MinimumPlayerLevel - 1;
            if (Evaluate(good) != PvpPreOpportunityDecision.BelowMinimumLevel) return "FAIL preop level";
            good.PlayerLevel = MinimumPlayerLevel; good.NativeCombatActive = true;
            if (Evaluate(good) != PvpPreOpportunityDecision.NativeCombatActive) return "FAIL preop combat";
            good.NativeCombatActive = false; good.RecentNativeCombat = true;
            if (Evaluate(good) != PvpPreOpportunityDecision.RecentNativeCombat) return "FAIL preop grace";
            good.RecentNativeCombat = false; good.NearbyWorldActor = true;
            if (Evaluate(good) != PvpPreOpportunityDecision.NearbyWorldActor) return "FAIL preop nearby";
            good.NearbyWorldActor = false; good.PartyValid = false;
            if (Evaluate(good) != PvpPreOpportunityDecision.PartyInvalid) return "FAIL preop party";
            return "PASS preop policy";
        }
    }
}
