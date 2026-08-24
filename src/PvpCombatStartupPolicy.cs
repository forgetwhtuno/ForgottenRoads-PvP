using System;

namespace ErenshorPvP
{
    // Pure policy used by the runtime watchdog and deterministic tests. A forced target by itself is
    // not enough: the old inert-proxy failure could hold a target forever while native AI never ran.
    // Target acquisition counts only once a post-GO native NPC.Update has also been observed.
    internal static class PvpCombatStartupPolicy
    {
        internal const float DefaultStartupWindowSeconds = 6f;
        internal const string TechnicalFailureAiInactive = "technical_failure_ai_inactive";

        internal static bool HasCombatEvidence(bool nativeUpdateReached, bool legalTargetAcquired,
            bool navPursuitRequested, bool attackDecision, bool healCheck, bool spellStart,
            bool damageToDefender, bool healingToAttacker)
        {
            return (nativeUpdateReached && legalTargetAcquired) || navPursuitRequested || attackDecision ||
                   healCheck || spellStart || damageToDefender || healingToAttacker;
        }

        internal static bool ShouldFailInactive(bool active, bool anyCombatEvidence, float secondsSinceGo, float startupWindowSeconds)
        {
            float window = Math.Max(1f, startupWindowSeconds);
            return active && !anyCombatEvidence && secondsSinceGo >= window;
        }

        internal static bool IsTechnicalFailure(string reason)
        {
            string value = (reason ?? string.Empty).Trim();
            return value.StartsWith("technical_failure_", StringComparison.Ordinal) ||
                string.Equals(value, "fight_state_failed", StringComparison.Ordinal) ||
                string.Equals(value, "runtime_invalid", StringComparison.Ordinal) ||
                string.Equals(value, "start_failed", StringComparison.Ordinal) ||
                string.Equals(value, "all_proxies_failed_start", StringComparison.Ordinal) ||
                string.Equals(value, "native_nav_failed", StringComparison.Ordinal);
        }

        internal static bool ShouldRecordCompetitiveResult(string reason)
        {
            string value = (reason ?? string.Empty).Trim();
            return string.Equals(value, "proxy_death", StringComparison.Ordinal) ||
                string.Equals(value, "player_death", StringComparison.Ordinal) ||
                string.Equals(value, "player_fled", StringComparison.Ordinal) ||
                string.Equals(value, "retreat", StringComparison.Ordinal);
        }

        internal static bool CanGrantVictoryReward(string reason, bool winnerPresent)
        {
            return winnerPresent && string.Equals(reason ?? string.Empty, "proxy_death", StringComparison.Ordinal);
        }
    }
}
