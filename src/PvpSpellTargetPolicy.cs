namespace ErenshorPvP
{
    // Pure, game-object-free spell-target/readiness policy. Runtime code feeds it facts proven from
    // the current Spell object/current match. The player-side target adaptation deliberately mirrors
    // the current Practice Duel implementation without importing Duel or changing CurrentTarget.
    internal static class PvpSpellTargetPolicy
    {
        internal static bool DeclaresSelfApplication(bool selfOnly, bool applyToCaster, bool inflictOnSelf)
        {
            return selfOnly || applyToCaster || inflictOnSelf;
        }

        internal static bool IsSelfCast(bool targetArgumentIsCaster, bool declaresSelfApplication, bool spellDamagesTarget)
        {
            if (declaresSelfApplication) return true;
            return targetArgumentIsCaster && !spellDamagesTarget;
        }

        // Current Hotkeys/StartSpell flow can hand an ordinary single-target heal the selected enemy
        // Stats even though the player's intent is a self heal. Adapt only the normal StartSpell
        // overloads and only the narrow healing shape proven by current Practice Duel.
        internal static bool CanAdaptOpponentHealToSelf(bool combatActive, bool directCastEntry,
            bool casterIsParticipant, bool passedTargetIsOpponent, bool healType, int targetHealing,
            int targetDamage, bool groupEffect, bool isAe, bool isPbae, bool petSummon,
            bool charmTarget, bool hasProc)
        {
            if (!combatActive || !directCastEntry || !casterIsParticipant || !passedTargetIsOpponent) return false;
            if (!(healType || targetHealing > 0) || targetDamage > 0) return false;
            if (groupEffect || isAe || isPbae || petSummon || charmTarget || hasProc) return false;
            return true;
        }

        // SpellVessel.ResolveSpell's current direct-heal branch reaches Stats.HealMe for a positive HP
        // payload with the native healing damage type. TargetHealing remains accepted because current
        // ordinary Heal assets can carry it in addition to HP.
        internal static bool IsDirectHpHeal(bool healType, int hp, int targetHealing, int targetDamage,
            bool damageTypeIsHealing, bool isAe, bool isPbae, bool petSummon, bool charmTarget)
        {
            if (petSummon || charmTarget || isAe || isPbae) return false;
            if (!damageTypeIsHealing || targetDamage > 0) return false;
            if (!(healType || targetHealing > 0 || hp > 0)) return false;
            return hp > 0 || targetHealing > 0;
        }

        internal static bool StartupAttackDelayReady(float attackSpellDelay)
        {
            // Current NPC maintenance decrements atkSpellDelay by 60 * deltaTime. It is a native
            // counter (constructor baseline ~120), not seconds and not a field PvP should zero.
            return attackSpellDelay <= 0f;
        }

        // The compatibility bridge is allowed only for a live, valid active temporary proxy vessel.
        // It never makes the NPC a persistent SimPlayer. Runtime also verifies the vessel's caster
        // object is exactly the active temporary actor before this pure decision is consulted.
        internal static bool ShouldBridgeProxyVesselInterrupt(bool matchActive, bool temporaryProxy,
            bool casterAlive, bool targetPresent, bool targetAlive, bool targetInvulnerable,
            bool interruptable)
        {
            return matchActive && temporaryProxy && casterAlive && targetPresent && targetAlive &&
                !targetInvulnerable && interruptable;
        }

        internal static bool ReadinessComplete(bool nativeStartCompleted, bool runtimeInvariantPassed,
            bool statsReady, bool characterReady, bool casterReady, bool spellsReady,
            bool navAgentReady, bool onNavMesh, bool visualShellReady, bool rewardSuppressionReady,
            bool startupNpcSpellCooldownNormalized, bool attackStartupMaintenanceReady,
            bool initialTargetCanBeResolved)
        {
            return nativeStartCompleted && runtimeInvariantPassed && statsReady && characterReady &&
                casterReady && spellsReady && navAgentReady && onNavMesh && visualShellReady &&
                rewardSuppressionReady && startupNpcSpellCooldownNormalized &&
                attackStartupMaintenanceReady && initialTargetCanBeResolved;
        }

        // Pre-GO preparation proves only the inert proxy surface. NPC.Start and the native
        // behavior/navigation loops are Unity-owned post-GO lifecycle work, so they cannot be a
        // countdown barrier when the NPC component is intentionally disabled.
        internal static bool PreGoReadinessComplete(bool runtimeInvariantPassed, bool statsReady,
            bool characterReady, bool casterReady, bool spellsReady, bool navAgentReady,
            bool onNavMesh, bool visualShellReady, bool rewardSuppressionReady,
            bool initialTargetCanBeResolved)
        {
            return runtimeInvariantPassed && statsReady && characterReady && casterReady && spellsReady &&
                navAgentReady && onNavMesh && visualShellReady && rewardSuppressionReady &&
                initialTargetCanBeResolved;
        }

        internal static string ClassifyStatusApplication(int beforeMatching, int afterMatching, bool newOrReplacedSlot,
            float beforeMaxDuration, float afterMaxDuration)
        {
            if (afterMatching > beforeMatching || newOrReplacedSlot) return "application_proven";
            if (afterMatching > 0 && afterMaxDuration > beforeMaxDuration + 0.001f) return "refresh_proven";
            return "no_observable_state_change";
        }

        internal static string RunSelfTests()
        {
            if (!DeclaresSelfApplication(true, false, false)) return "FAIL self-only semantics";
            if (!DeclaresSelfApplication(false, true, false)) return "FAIL apply-to-caster semantics";
            if (!DeclaresSelfApplication(false, false, true)) return "FAIL inflict-on-self semantics";
            if (DeclaresSelfApplication(false, false, false)) return "FAIL plain target semantics";
            if (!IsSelfCast(false, true, true)) return "FAIL declared self application";
            if (IsSelfCast(true, false, true)) return "FAIL offensive caster-arg self inference";
            if (!CanAdaptOpponentHealToSelf(true, true, true, true, true, 10, 0, false, false, false, false, false, false))
                return "FAIL opponent-target heal adaptation";
            if (CanAdaptOpponentHealToSelf(true, false, true, true, true, 10, 0, false, false, false, false, false, false))
                return "FAIL proc/noanim heal adaptation";
            if (CanAdaptOpponentHealToSelf(true, true, true, true, false, 0, 0, false, false, false, false, false, false))
                return "FAIL generic beneficial retarget";
            if (CanAdaptOpponentHealToSelf(true, true, true, true, true, 10, 1, false, false, false, false, false, false))
                return "FAIL damaging heal retarget";
            if (CanAdaptOpponentHealToSelf(true, true, true, true, true, 10, 0, true, false, false, false, false, false))
                return "FAIL group heal retarget";
            if (!IsDirectHpHeal(true, 25, 0, 0, true, false, false, false, false)) return "FAIL direct HP heal";
            if (IsDirectHpHeal(false, 0, 0, 0, true, false, false, false, false)) return "FAIL generic beneficial as heal";
            if (IsDirectHpHeal(true, 25, 0, 0, false, false, false, false, false)) return "FAIL damage-type heal gate";
            if (!StartupAttackDelayReady(0f) || StartupAttackDelayReady(19.06f)) return "FAIL native attack-delay units";
            if (!ShouldBridgeProxyVesselInterrupt(true, true, true, true, true, false, true)) return "FAIL proxy vessel bridge";
            if (ShouldBridgeProxyVesselInterrupt(true, true, false, true, true, false, true)) return "FAIL dead caster vessel bridge";
            if (ShouldBridgeProxyVesselInterrupt(true, false, true, true, true, false, true)) return "FAIL native vessel bridge";
            if (!ReadinessComplete(true, true, true, true, true, true, true, true, true, true, true, true, true))
                return "FAIL readiness complete";
            if (ReadinessComplete(true, true, true, true, true, true, true, true, true, true, true, false, true))
                return "FAIL readiness attack maintenance gate";
            if (!PreGoReadinessComplete(true, true, true, true, true, true, true, true, true, true))
                return "FAIL pre-GO structural readiness";
            if (PreGoReadinessComplete(false, true, true, true, true, true, true, true, true, true))
                return "FAIL pre-GO invariant gate";
            if (ClassifyStatusApplication(0, 1, false, 0f, 5f) != "application_proven") return "FAIL status application proof";
            if (ClassifyStatusApplication(1, 1, false, 5f, 8f) != "refresh_proven") return "FAIL status refresh proof";
            if (ClassifyStatusApplication(1, 1, false, 5f, 5f) != "no_observable_state_change") return "FAIL status no-change proof";
            return "PASS pvp spell target/native cadence/readiness policy";
        }
    }
}
