using System;

namespace ErenshorPvP
{
    // Pure policy for the native-cast execution boundary. This deliberately does not select or
    // execute game spells; it classifies observed native state and the one temporary-proxy
    // compatibility gate that is proven incompatible with the current non-Sim proxy model.
    internal static class PvpSpellExecutionPolicy
    {
        internal const float NativeHealThreshold = PvpNativeHealThresholdPolicy.ExpectedThreshold;

        internal static bool ShouldClearSyntheticNpcSpellCooldown(bool temporaryProxy, bool simPlayer, float npcSpellCooldown)
        {
            return temporaryProxy && !simPlayer && npcSpellCooldown > 0f;
        }

        internal static string AttackEntryAssessment(bool hasTarget, bool targetInvulnerable, bool casting,
            int attackSpellCount, float npcSpellCooldown, float attackSpellDelay, int currentMana, int minimumManaCost)
        {
            if (!hasTarget) return "no_target";
            if (targetInvulnerable) return "target_invulnerable";
            if (npcSpellCooldown > 0f) return "native_npc_spell_cooldown";
            if (casting) return "already_casting";
            if (attackSpellDelay > 0f) return "attack_spell_delay";
            if (attackSpellCount <= 0) return "no_attack_spells";
            if (minimumManaCost >= 0 && currentMana <= minimumManaCost) return "resources";
            return "native_selection_pending";
        }

        internal static string CastPipelineAssessment(int aiEntries, int requests, int accepted, int completed, int effects,
            string latestPreRequestAssessment)
        {
            if (aiEntries <= 0) return "ai_not_evaluated";
            if (requests <= 0)
            {
                if (!string.IsNullOrWhiteSpace(latestPreRequestAssessment) &&
                    !string.Equals(latestPreRequestAssessment, "native_selection_pending", StringComparison.Ordinal))
                    return "prerequisite_blocked:" + latestPreRequestAssessment;
                return "native_no_request_after_eligible_entry";
            }
            if (accepted <= 0) return "native_request_rejected";
            if (completed <= 0) return "native_accepted_not_completed";
            if (effects <= 0) return "cast_completed_no_effect";
            return "effect_observed";
        }

        internal static bool NeedsHeal(int currentHp, int maxHp)
        {
            return maxHp > 0 && currentHp >= 0 && currentHp < maxHp * NativeHealThreshold;
        }

        internal static bool LegalProxyAllyHealTarget(bool sameTemporaryTeam, bool alive, bool isEnemyDefender,
            int currentHp, int maxHp)
        {
            return sameTemporaryTeam && alive && !isEnemyDefender && NeedsHeal(currentHp, maxHp);
        }

        internal static string HealAssessment(int checks, int legalTargets, int spellSelections, int requests,
            int accepted, int effects, string latestBlocker)
        {
            if (checks <= 0) return "heal_ai_not_evaluated";
            // Native self-heals do not pass through the PvP ally-target bridge, so observed native
            // request/accept/effect evidence outranks the bridge-only legalTargets counter.
            if (effects > 0) return "heal_effect_observed";
            if (accepted > 0) return "heal_accepted_no_effect";
            if (requests > 0) return "heal_native_rejected";
            if (spellSelections > 0) return "heal_request_not_invoked";
            if (legalTargets <= 0) return string.IsNullOrWhiteSpace(latestBlocker)
                ? "no_injured_legal_ally" : "heal_spell_blocked:" + latestBlocker;
            return string.IsNullOrWhiteSpace(latestBlocker)
                ? "heal_spell_not_selected" : "heal_spell_blocked:" + latestBlocker;
        }

        internal static string RunSelfTests()
        {
            if (!ShouldClearSyntheticNpcSpellCooldown(true, false, 120f)) return "FAIL synthetic NPC spell gate";
            if (ShouldClearSyntheticNpcSpellCooldown(false, false, 120f)) return "FAIL vanilla NPC spell gate ownership";
            if (ShouldClearSyntheticNpcSpellCooldown(true, true, 120f)) return "FAIL Sim spell gate ownership";
            if (AttackEntryAssessment(true, false, false, 2, 0f, 0f, 100, 20) != "native_selection_pending") return "FAIL attack ready";
            if (AttackEntryAssessment(false, false, false, 2, 0f, 0f, 100, 20) != "no_target") return "FAIL attack target";
            if (CastPipelineAssessment(2, 0, 0, 0, 0, "native_selection_pending") != "native_no_request_after_eligible_entry") return "FAIL request boundary";
            if (CastPipelineAssessment(2, 1, 0, 0, 0, null) != "native_request_rejected") return "FAIL native rejection";
            if (CastPipelineAssessment(2, 1, 1, 1, 1, null) != "effect_observed") return "FAIL spell effect";
            if (!PvpNativeHealThresholdPolicy.BoundarySemantics(NativeHealThreshold) || !NeedsHeal(65, 100) || NeedsHeal(67, 100))
                return "FAIL native heal threshold semantics";
            if (!LegalProxyAllyHealTarget(true, true, false, 40, 100)) return "FAIL legal heal ally";
            if (LegalProxyAllyHealTarget(true, true, true, 40, 100)) return "FAIL enemy heal target";
            if (HealAssessment(1, 1, 1, 1, 1, 1, null) != "heal_effect_observed") return "FAIL heal effect";
            if (HealAssessment(1, 0, 1, 1, 1, 1, "effect_observed") != "heal_effect_observed") return "FAIL native self-heal effect";
            return "PASS pvp native spell execution policy";
        }
    }
}
