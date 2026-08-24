using System;
using ErenshorPvP;

internal static class PvpUiPolicyTests
{
    private static int Main()
    {
        try
        {
            Assert(PvpUiGeometry.RunSelfTests().StartsWith("PASS", StringComparison.Ordinal), "normalized retained position policy");
            Assert(SuiteLauncherPolicy.RunSelfTests().StartsWith("PASS", StringComparison.Ordinal), "mandatory launcher fallback policy");
            Assert(PvpHubPresentation.Build(true, false) == "Enabled | Idle", "idle hub status exact");
            Assert(PvpHubPresentation.Build(true, true) == "Enabled | Match active", "active hub status exact");
            Assert(PvpHubPresentation.Build(false, false).Length < 240, "hub status remains bounded");
            Assert(PvpUiPresentation.ToggleLabel("PvP Enabled", true) == "PvP Enabled [ON]", "explicit PvP ON label");
            Assert(PvpUiPresentation.ToggleLabel("Arranged Challenges", false) == "Arranged Challenges [OFF]", "explicit arranged OFF label");
            Assert(PvpUiPresentation.ToggleLabel("Wild Ambushes", true) == "Wild Ambushes [ON]", "explicit ambush ON label");
            Assert(PvpUiPresentation.RunSelfTests().StartsWith("PASS", StringComparison.Ordinal), "explicit toggle presentation policy");
            Assert(PvpWindowChromePolicy.RunSelfTests().StartsWith("PASS", StringComparison.Ordinal), "Forgotten Roads PvP header collapse policy");
            TestWildAmbushZonePolicy();
            TestPreOpportunityPolicy();
            Assert(PvpWindowChromePolicy.ChevronPointsUp(false), "expanded PvP header points up to collapse");
            Assert(!PvpWindowChromePolicy.ChevronPointsUp(true), "collapsed PvP header points down to expand");
            Assert(Math.Abs(PvpWindowChromePolicy.PreserveTopBottomY(100f, 520f, 34f) - 586f) < .001f, "collapse preserves visual top edge");
            string uiState = PvpUiStatePolicy.Build("pvp", true, 520, 4.25d);
            Assert(Field(uiState, "module") == "pvp" && Field(uiState, "open") == "true" &&
                Field(uiState, "closeable") == "true", "PvP ui.state advertises visual close contract");
            Assert(Field(uiState, "sortOrder") == "520" && Field(uiState, "activated") == "4.25",
                "PvP ui.state reports deterministic stacking metadata");
            string boundedState = PvpUiStatePolicy.Build("pvp", true, 50000, double.NaN);
            Assert(Field(boundedState, "sortOrder") == "10000" && Field(boundedState, "activated") == "0",
                "PvP ui.state bounds malformed ordering values");
            Assert(PvpProxyStartupPolicy.InvariantPasses(true, true, true, true, true, true, true, true, false), "proxy startup invariant accepts complete synthetic graph");
            Assert(!PvpProxyStartupPolicy.InvariantPasses(true, true, true, true, false, true, true, true, false), "proxy startup invariant rejects broken Character-to-NPC link");
            Assert(!PvpProxyStartupPolicy.InvariantPasses(true, true, true, true, true, true, true, true, true), "proxy startup invariant rejects persistent Sim identity");
            Assert(PvpProxyStartupPolicy.ShouldRunNativeNpcStart(true, true), "live-source clone receives its own native NPC.Start lifecycle");
            Assert(PvpProxyStartupPolicy.ShouldRunNativeNpcStart(false, true), "ordinary native NPC.Start remains untouched");
            Assert(PvpProxyStartupPolicy.ShouldRunNativeNpcStart(true, false), "resource-prefab proxy receives native Start lifecycle");
            Assert(!PvpNativeNavHealthPolicy.LaunchAloneIsHealthy(true), "nav coroutine handle alone is never health proof");
            Assert(!PvpNativeNavHealthPolicy.IsHealthy(true, true, true, false, false), "UpdateNav entry without completed first step is unhealthy");
            Assert(!PvpNativeNavHealthPolicy.IsHealthy(true, true, true, true, true), "first MoveNext/UpdateNav fault is unhealthy");
            Assert(PvpNativeNavHealthPolicy.IsHealthy(true, true, true, true, false), "native Start plus successful UpdateNav progression is healthy");
            Assert(PvpNativeNavHealthPolicy.CompleteNavFailure(5, 5), "all proxy nav faults are a complete nav failure");
            Assert(!PvpNativeNavHealthPolicy.CompleteNavFailure(5, 1), "partial nav fault is not complete team failure");
            Assert(PvpNativeNavHealthPolicy.NeedsPursuit(true, 12f, 3f), "out-of-range melee profile requires pursuit");
            Assert(!PvpNativeNavHealthPolicy.NeedsPursuit(true, 8f, 10f), "ranged profile already in range does not require artificial movement");
            Assert(!PvpNativeNavHealthPolicy.PursuitSatisfied(true, true, false), "destination request alone is not movement proof");
            Assert(PvpNativeNavHealthPolicy.PursuitSatisfied(true, true, true), "observed displacement satisfies pursuit progression");
            Assert(PvpNativeCombatLoopPolicy.ShouldStartMissingLoop(true, true, false, false, 0), "missing temporary native combat loop is recovered after Start");
            Assert(!PvpNativeCombatLoopPolicy.ShouldStartMissingLoop(false, true, false, false, 0), "ordinary NPC combat loops are never claimed");
            Assert(!PvpNativeCombatLoopPolicy.ShouldStartMissingLoop(true, false, false, false, 0), "combat loops are not started before natural NPC.Start completes");
            Assert(!PvpNativeCombatLoopPolicy.ShouldStartMissingLoop(true, true, true, false, 0), "resident native loop is never duplicated");
            Assert(!PvpNativeCombatLoopPolicy.ShouldStartMissingLoop(true, true, false, true, 0), "existing PvP-owned loop is never duplicated");
            Assert(!PvpNativeCombatLoopPolicy.ShouldStartMissingLoop(true, true, false, false, 1), "per-proxy start count prevents a second owned startup");
            Assert(PvpNativeCombatLoopPolicy.InfrastructureReady(true, true, true, 1, 1), "readiness accepts one nav and one behavior loop");
            Assert(!PvpNativeCombatLoopPolicy.InfrastructureReady(true, true, true, 2, 1), "readiness rejects duplicate nav ownership");
            Assert(PvpNativeCombatLoopPolicy.PreGoBoundaryReady(true, true, true, true, true), "pre-GO loop infrastructure stays inert behind all gates");
            Assert(!PvpNativeCombatLoopPolicy.PreGoBoundaryReady(false, true, true, true, true), "pre-GO readiness rejects early NeverAggro release");
            Assert(PvpNativeCombatLoopPolicy.CanStopOwnedLoop(true, true), "cleanup may stop a temporary loop PvP owns");
            Assert(!PvpNativeCombatLoopPolicy.CanStopOwnedLoop(true, false), "cleanup may not stop a native-owned loop");
            Assert(PvpPreparationNavReadinessPolicy.IsStructurallyReady(true, true, true, true, false, false, 3.5f, 8f, 120f, 0.5f, 2f, 0f), "stopped on-mesh preparation agent is structurally ready without movement");
            Assert(!PvpPreparationNavReadinessPolicy.IsStructurallyReady(true, false, true, true, false, false, 3.5f, 8f, 120f, 0.5f, 2f, 0f), "disabled preparation agent is not ready");
            Assert(!PvpPreparationNavReadinessPolicy.IsStructurallyReady(true, true, false, true, false, false, 3.5f, 8f, 120f, 0.5f, 2f, 0f), "off-mesh preparation agent is not ready");
            Assert(!PvpPreparationNavReadinessPolicy.IsStructurallyReady(true, true, true, false, false, false, 3.5f, 8f, 120f, 0.5f, 2f, 0f), "pre-GO preparation agent must remain stopped");
            Assert(!PvpPreparationNavReadinessPolicy.IsStructurallyReady(true, true, true, true, true, false, 3.5f, 8f, 120f, 0.5f, 2f, 0f), "stale pre-GO path blocks readiness");
            Assert(!PvpCombatExecutionPolicy.PursuitProven(true, true, true, true, false, true, true, 0f, 0f), "complete path without displacement/velocity is not execution proof");
            Assert(PvpCombatExecutionPolicy.PursuitProven(true, true, true, true, false, true, true, .25f, 0f), "root displacement proves native pursuit execution");
            Assert(PvpCombatExecutionPolicy.PursuitAssessment(true, true, true, true, false, true, true, 0f, 0f) == "path_but_no_motion", "stalled path classified explicitly");
            Assert(PvpCombatExecutionPolicy.ShellFollowProven(1f, .95f, .01f), "render shell follows moving root");
            Assert(!PvpCombatExecutionPolicy.ShellFollowProven(1f, 0f, .5f), "render shell/root divergence detected");
            Assert(PvpCombatExecutionPolicy.HealingRuntimeConnected(3, 2), "configured healer with memmed heals is connected");
            Assert(!PvpCombatExecutionPolicy.HealingRuntimeConnected(3, 0), "configured healer without memmed heals is disconnected");
            Assert(PvpCombatExecutionPolicy.CastAssessment(4, 0, 0, 0) == "native_rejected", "spell request is not native cast admission");
            Assert(PvpCombatExecutionPolicy.CastAssessment(4, 3, 0, 0) == "accepted_not_finished", "accepted cast without finish remains incomplete");
            Assert(PvpCombatExecutionPolicy.CastAssessment(4, 3, 3, 0) == "finished_no_effect", "finished cast without effect remains ineffective");
            Assert(PvpCombatExecutionPolicy.CastAssessment(4, 3, 3, 1) == "effect_observed", "cast effect is distinct from start/finish telemetry");
            Assert(PvpSpellExecutionPolicy.ShouldClearSyntheticNpcSpellCooldown(true, false, 120f), "temporary non-Sim proxy clears proven native NPC spell gate");
            Assert(!PvpSpellExecutionPolicy.ShouldClearSyntheticNpcSpellCooldown(false, false, 120f), "ordinary native NPC spell gate remains game-owned");
            Assert(!PvpSpellExecutionPolicy.ShouldClearSyntheticNpcSpellCooldown(true, true, 120f), "native Sim spell gate remains game-owned");
            Assert(PvpSpellExecutionPolicy.AttackEntryAssessment(true, false, false, 3, 0f, 0f, 100, 10) == "native_selection_pending", "attack entry reaches native spell selection only when broad prerequisites pass");
            Assert(PvpSpellExecutionPolicy.AttackEntryAssessment(false, false, false, 3, 0f, 0f, 100, 10) == "no_target", "attack spell entry requires target");
            Assert(PvpSpellExecutionPolicy.AttackEntryAssessment(true, false, false, 3, 0f, 20f, 100, 10) == "attack_spell_delay", "native attack spell delay remains authoritative");
            Assert(PvpSpellExecutionPolicy.AttackEntryAssessment(true, false, false, 3, 0f, 0f, 10, 10) == "resources", "native strict mana threshold is represented");
            Assert(PvpSpellExecutionPolicy.CastPipelineAssessment(4, 0, 0, 0, 0, "native_selection_pending") == "native_no_request_after_eligible_entry", "eligible AI entry without request is classified distinctly");
            Assert(PvpSpellExecutionPolicy.CastPipelineAssessment(4, 2, 0, 0, 0, null) == "native_request_rejected", "native request rejection is distinct");
            Assert(PvpSpellExecutionPolicy.CastPipelineAssessment(4, 2, 2, 1, 0, null) == "cast_completed_no_effect", "completed ineffective attack cast is distinct");
            Assert(PvpSpellExecutionPolicy.CastPipelineAssessment(4, 2, 2, 2, 1, null) == "effect_observed", "effective native attack cast completes full pipeline");
            Assert(PvpSpellExecutionPolicy.NeedsHeal(65, 100), "native NPC heal threshold admits below 66 percent");
            Assert(!PvpSpellExecutionPolicy.NeedsHeal(67, 100), "native NPC heal threshold does not trigger clearly above 66 percent");
            Assert(PvpNativeHealThresholdPolicy.BoundarySemantics(PvpNativeHealThresholdPolicy.ExpectedThreshold), "native heal threshold pure semantics avoid brittle equality boundary");
            Assert(PvpSpellExecutionPolicy.LegalProxyAllyHealTarget(true, true, false, 30, 100), "injured living attacker ally is a legal synthetic team heal target");
            Assert(!PvpSpellExecutionPolicy.LegalProxyAllyHealTarget(true, true, true, 30, 100), "enemy defender is never a proxy ally heal target");
            Assert(!PvpSpellExecutionPolicy.LegalProxyAllyHealTarget(false, true, false, 30, 100), "unrelated world actor is never selected by proxy ally-heal bridge");
            Assert(PvpSpellExecutionPolicy.HealAssessment(3, 1, 1, 1, 1, 1, null) == "heal_effect_observed", "heal pipeline requires actual effect");
            Assert(PvpSpellExecutionPolicy.HealAssessment(3, 0, 1, 1, 1, 1, "effect_observed") == "heal_effect_observed", "native self-heal effect does not require synthetic ally-target evidence");
            Assert(PvpSpellExecutionPolicy.HealAssessment(3, 0, 1, 1, 1, 0, "native_accepted") == "heal_accepted_no_effect", "accepted native self-heal without HP restoration stays distinct");
            Assert(!PvpCombatExecutionPolicy.AnyExecutionEvidence(false, false, false, false, false), "decision-only state is not execution evidence");
            Assert(PvpCombatExecutionPolicy.AnyExecutionEvidence(true, false, false, false, false), "root displacement is execution evidence");
            Assert(PvpCombatExecutionPolicy.AnyExecutionEvidence(false, false, true, false, false), "completed native cast is execution evidence");
            Assert(PvpProxyStartupPolicy.MaintenanceStatePasses(true, true, true, true, true, true, true, true, true), "proxy maintenance invariant accepts NPC-side runtime state");
            Assert(!PvpProxyStartupPolicy.MaintenanceStatePasses(true, true, false, true, true, true, true, true, true), "proxy maintenance invariant rejects missing NPC.MyStats");
            Assert(!PvpProxyStartupPolicy.MaintenanceStatePasses(true, true, true, true, true, false, true, true, true), "proxy maintenance invariant rejects missing NameFlash");
            // Regression for the 5v5 that logged nameFlash=True / requiredRuntimeState=PASS and still threw
            // ~1,130 NPC.HandleNameTag NREs: NameFlash is not the field HandleNameTag dereferences, so a
            // proxy that satisfies NameFlash but lacks NamePlateTxt/NamePlateObject must NOT pass.
            Assert(!PvpProxyStartupPolicy.MaintenanceStatePasses(true, true, true, true, true, true, true, false, true), "proxy maintenance invariant rejects missing NamePlateTxt even when NameFlash is bound");
            Assert(!PvpProxyStartupPolicy.MaintenanceStatePasses(true, true, true, true, true, true, true, true, false), "proxy maintenance invariant rejects missing NamePlateObject even when NameFlash is bound");
            Assert(!PvpProxyStartupPolicy.MaintenanceStatePasses(false, true, true, true, true, true, true, true, true), "proxy maintenance invariant only applies to registered temporary proxies");
            Assert(PvpProxyStartupPolicy.ShouldInterceptMaintenance(true, false), "invalid temporary proxy is intercepted for terminal cleanup");
            Assert(!PvpProxyStartupPolicy.ShouldInterceptMaintenance(false, false), "vanilla NPC is never intercepted by PvP failsafe");
            Assert(!PvpProxyStartupPolicy.ShouldInterceptMaintenance(true, true), "valid temporary proxy keeps native maintenance");
            Assert(PvpProxyStartupPolicy.RewardBoundaryPasses(true, true, true, true, true, true), "reward boundary accepts fully suppressed proxy");
            Assert(!PvpProxyStartupPolicy.RewardBoundaryPasses(false, true, true, true, true, true), "reward boundary rejects unreadable/nonzero borrowed XP");
            Assert(!PvpProxyStartupPolicy.RewardBoundaryPasses(true, false, true, true, true, true), "reward boundary rejects XP eligibility when NeverTrack is absent");
            Assert(!PvpProxyStartupPolicy.RewardBoundaryPasses(true, true, true, true, false, true), "reward boundary rejects native loot gold");
            Assert(PvpProxyStartupPolicy.RewardSuppressionProofPasses(true, true, true, true), "authoritative reward proof accepts current boundary plus same actor/loot identities");
            Assert(PvpProxyStartupPolicy.CountdownHoldAccepts(true, true, true, true, true), "unchanged ready proxy remains accepted by countdown hold before GO");
            Assert(!PvpProxyStartupPolicy.CountdownHoldAccepts(true, false, true, true, true), "genuine current reward-boundary loss fails countdown hold");
            Assert(!PvpProxyStartupPolicy.CountdownHoldAccepts(true, true, true, false, true), "actor identity replacement fails countdown hold");
            Assert(!PvpProxyStartupPolicy.CountdownHoldAccepts(true, true, true, true, false), "loot identity replacement fails countdown hold");
            PvpMatchLifecyclePolicy countdownFailure = new PvpMatchLifecyclePolicy(true);
            Assert(countdownFailure.Queue("hold-recovery") && countdownFailure.BeginSpawn("hold-recovery") && countdownFailure.SpawnSucceeded(), "countdown failure fixture reaches Countdown");
            Assert(countdownFailure.State == PvpMatchLifecycleState.Countdown && countdownFailure.HoldAttackers, "countdown retains pre-GO holds before safety decision");
            countdownFailure.BeginCleanup(); countdownFailure.CompleteCleanup(true);
            Assert(countdownFailure.State == PvpMatchLifecycleState.Ready && !countdownFailure.HoldAttackers, "failed countdown returns lifecycle to ready and releases hold ownership");
            Assert(countdownFailure.Queue("second-attempt") && countdownFailure.BeginSpawn("second-attempt"), "technical cancellation permits a second match attempt");
            Assert(PvpProxyStartupPolicy.ZeroHealingAssessment(0, 0, 0, 0) == "expected_no_heal_loadout", "zero healing is expected with no heal-capable attackers");
            Assert(PvpProxyStartupPolicy.ZeroHealingAssessment(1, 0, 0, 0) == "heal_ai_not_evaluated", "heal-capable roster without heal checks is diagnostic");
            Assert(PvpProxyStartupPolicy.ZeroHealingAssessment(1, 3, 0, 0) == "heal_capable_but_no_cast_started", "heal checks without casts remain diagnostic");
            Assert(PvpProxyStartupPolicy.ZeroHealingAssessment(1, 3, 2, 0) == "heal_capable_casting_observed_no_effective_heal", "casting without healing remains diagnostic");

            // 0.5.11 per-proxy ability-use observability. A zero-spell proxy (pure-melee loadout) is
            // a real, expected outcome and must not be reported as any kind of failure.
            Assert(PvpProxyStartupPolicy.ProxyAbilityUseAssessment(0, 0, 0, 0, 0, 0, 0, 0) == "no_class_abilities_loaded", "zero-spell proxy is reported accurately, not as a failure");
            // Spells loaded but the AI never even evaluated them (no decisions, no heal checks).
            Assert(PvpProxyStartupPolicy.ProxyAbilityUseAssessment(3, 0, 0, 0, 0, 0, 0, 0) == "ability_ai_not_evaluated", "loaded-but-unevaluated is distinguished from never-loaded");
            // AI evaluated (decisions occurred) but no StartSpell-family cast was ever observed:
            // distinguishes "spells loaded" from "spells actually started casting".
            Assert(PvpProxyStartupPolicy.ProxyAbilityUseAssessment(3, 0, 0, 2, 1, 0, 0, 0) == "ability_evaluated_no_cast_started", "evaluated-but-no-cast is distinguished from merely having spells loaded");
            // Cast started but neither damage nor healing landed.
            Assert(PvpProxyStartupPolicy.ProxyAbilityUseAssessment(3, 0, 0, 2, 1, 1, 0, 0) == "cast_started_no_effective_outcome", "cast without outcome remains diagnostic, not a pass/fail verdict");
            // Confirmed use via damage, and separately via healing.
            Assert(PvpProxyStartupPolicy.ProxyAbilityUseAssessment(3, 0, 0, 2, 1, 1, 40, 0) == "ability_use_confirmed", "effective damage confirms ability use");
            Assert(PvpProxyStartupPolicy.ProxyAbilityUseAssessment(0, 2, 4, 0, 0, 1, 0, 25) == "ability_use_confirmed", "effective healing confirms ability use");
            // A heal-capable proxy with heal checks but zero heals is distinguished from one that
            // never evaluated healing at all - this reuses ZeroHealingAssessment unchanged (already
            // covered above), and this proves the reuse composes correctly at the per-proxy call site.
            Assert(PvpProxyStartupPolicy.ZeroHealingAssessment(1, 0, 0, 0) == "heal_ai_not_evaluated" &&
                PvpProxyStartupPolicy.ZeroHealingAssessment(1, 5, 2, 0) == "heal_capable_casting_observed_no_effective_heal",
                "heal-checked-but-zero-heals stays distinguishable from never-evaluated at the per-proxy call site");

            Assert(PvpWorldCombatPolicy.RunSelfTests().StartsWith("PASS", StringComparison.Ordinal), "MMO-style world-combat expansion policy");
            Assert(!PvpWorldCombatPolicy.IsProtectedNonCombat(true, false, true, true, true, true, true), "local/world Sim identity outranks neutral NPC heuristics");
            Assert(!PvpWorldCombatPolicy.IsProtectedNonCombat(false, true, true, true, true, true, true), "owned/summoned pet identity outranks neutral NPC heuristics");
            Assert(PvpWorldCombatPolicy.IsProtectedNonCombat(false, false, true, false, false, false, false), "vendor is protected noncombat world actor");
            Assert(!PvpWorldCombatPolicy.IsProtectedNonCombat(false, false, false, false, false, false, true), "friendly faction alone remains a world combatant");
            Assert(!PvpWorldCombatPolicy.IsProtectedNonCombat(false, false, false, true, false, false, false), "invulnerability alone does not imply neutral/noncombat");
            Assert(PvpWorldCombatPolicy.IsProtectedNonCombat(false, false, false, true, false, false, true), "invulnerable friendly non-Sim remains protected");
            Assert(PvpWorldCombatPolicy.DecideAggro(true, false, false, false, false, false) == PvpInteractionDecision.AllowWorld, "proxy may join native world combat");
            Assert(PvpWorldCombatPolicy.DecideAggro(false, false, true, false, false, false) == PvpInteractionDecision.AllowWorld, "outside world actor may aggro PvP attacker");
            Assert(PvpWorldCombatPolicy.DecideDamage(true, false, false, false, false, false, false) == PvpInteractionDecision.AllowWorld, "outside world damage may hit defender");
            Assert(PvpWorldCombatPolicy.DecideDamage(false, true, false, false, false, false, false) == PvpInteractionDecision.AllowWorld, "outside world damage may hit attacker");
            Assert(PvpWorldCombatPolicy.DecideDamage(true, false, false, false, true, false, false) == PvpInteractionDecision.Block, "unattributed player projectile cannot friendly-fire defender side");
            Assert(PvpWorldCombatPolicy.DecideSpellStart(false, true, false, false, true, false, false, false) == PvpInteractionDecision.AllowMatch, "attacker untargeted AoE is admitted without proximity veto");
            Assert(PvpWorldCombatPolicy.DecideSpellStart(true, false, false, false, true, false, false, false) == PvpInteractionDecision.AllowMatch, "defender untargeted AoE is admitted without proximity veto");
            Assert(PvpWorldCombatPolicy.DecideDamage(false, false, true, false, false, false, true) == PvpInteractionDecision.Block, "participant damage to proven protected neutral is rejected narrowly");

            Assert(PvpSpellTargetPolicy.DeclaresSelfApplication(true, false, false), "SelfOnly is self application");
            Assert(PvpSpellTargetPolicy.DeclaresSelfApplication(false, true, false), "ApplyToCaster is self application");
            Assert(PvpSpellTargetPolicy.DeclaresSelfApplication(false, false, true), "InflictOnSelf is self application");
            Assert(PvpSpellTargetPolicy.IsSelfCast(false, true, true), "declared self spell remains self even with opponent target argument");
            Assert(PvpSpellTargetPolicy.CanAdaptOpponentHealToSelf(true, true, true, true, true, 0, 0, false, false, false, false, false, false), "ordinary direct heal may adapt selected opponent target to caster stats");
            Assert(!PvpSpellTargetPolicy.CanAdaptOpponentHealToSelf(true, false, true, true, true, 0, 0, false, false, false, false, false, false), "proc/noanim path is not rewritten by direct-cast adapter");
            Assert(!PvpSpellTargetPolicy.CanAdaptOpponentHealToSelf(true, true, true, true, true, 0, 5, false, false, false, false, false, false), "damaging heal-shaped spell is not rewritten");
            Assert(!PvpSpellTargetPolicy.CanAdaptOpponentHealToSelf(true, true, true, true, true, 0, 0, true, false, false, false, false, false), "group heal is not rewritten as single-target self cast");
            Assert(PvpSpellTargetPolicy.IsDirectHpHeal(true, 25, 0, 0, true, false, false, false, false), "native heal payload classified as direct HP heal");
            Assert(!PvpSpellTargetPolicy.IsDirectHpHeal(false, 0, 0, 0, true, false, false, false, false), "generic beneficial payload is not direct healing");
            Assert(!PvpSpellTargetPolicy.StartupAttackDelayReady(19.06f), "atkSpellDelay units are allowed to drain natively before countdown");
            Assert(PvpSpellTargetPolicy.StartupAttackDelayReady(0f), "drained attack startup maintenance passes readiness");
            Assert(PvpSpellTargetPolicy.ShouldBridgeProxyVesselInterrupt(true, true, true, true, true, false, true), "active temporary proxy vessel can receive narrow Sim-parity interruption bridge");
            Assert(!PvpSpellTargetPolicy.ShouldBridgeProxyVesselInterrupt(true, false, true, true, true, false, true), "native Sim/NPC vessel is never bridged");
            Assert(!PvpSpellTargetPolicy.ShouldBridgeProxyVesselInterrupt(true, true, false, true, true, false, true), "dead temporary caster does not receive vessel bridge");
            Assert(!PvpSpellTargetPolicy.ShouldBridgeProxyVesselInterrupt(true, true, true, true, false, false, true), "dead target does not receive vessel bridge");
            Assert(!PvpSpellTargetPolicy.ShouldBridgeProxyVesselInterrupt(true, true, true, true, true, true, true), "invulnerable target does not receive vessel bridge");
            Assert(PvpSpellTargetPolicy.ReadinessComplete(true, true, true, true, true, true, true, true, true, true, true, true, true), "complete pre-GO readiness barrier passes");
            Assert(!PvpSpellTargetPolicy.ReadinessComplete(true, true, true, true, true, true, true, true, true, true, true, false, true), "undrained attack startup maintenance blocks visible countdown");
            Assert(PvpSpellTargetPolicy.ClassifyStatusApplication(0, 1, false, 0f, 5f) == "application_proven", "new matching status proves application");
            Assert(PvpSpellTargetPolicy.ClassifyStatusApplication(1, 1, false, 5f, 8f) == "refresh_proven", "duration increase proves status refresh");
            Assert(PvpSpellTargetPolicy.ClassifyStatusApplication(1, 1, false, 5f, 5f) == "no_observable_state_change", "AddStatusEffect entry without state mutation is not success");

            Assert(PvpPluginIdentityPolicy.ExactlyOneExpectedIdentity(new[] { "Lunaris", "ErenshorPvP", "OtherMod" }), "exactly one PvP plugin identity expected");
            Assert(!PvpPluginIdentityPolicy.ExactlyOneExpectedIdentity(new[] { "ErenshorPvP", "ErenshorPvP" }), "duplicate PvP plugin identity rejected");
            Assert(!PvpPluginIdentityPolicy.ExactlyOneExpectedIdentity(new[] { "Lunaris", "OtherMod" }), "missing PvP plugin identity rejected");

            Assert(!PvpCombatStartupPolicy.HasCombatEvidence(true, false, false, false, false, false, false, false), "native Update alone is not combat evidence");
            Assert(!PvpCombatStartupPolicy.HasCombatEvidence(false, true, false, false, false, false, false, false), "forced target alone is not combat evidence");
            Assert(PvpCombatStartupPolicy.HasCombatEvidence(true, true, false, false, false, false, false, false), "native target acquisition path counts only after Update");
            Assert(PvpCombatStartupPolicy.HasCombatEvidence(false, false, true, false, false, false, false, false), "native pursuit counts as combat active");
            Assert(PvpCombatStartupPolicy.HasCombatEvidence(false, false, false, true, false, false, false, false), "melee-only/combat decision counts as active");
            Assert(PvpCombatStartupPolicy.HasCombatEvidence(false, false, false, false, false, true, false, false), "spell attacker counts as active");
            Assert(PvpCombatStartupPolicy.HasCombatEvidence(false, false, false, false, true, false, false, false), "healer/support attacker counts as active");
            Assert(!PvpCombatStartupPolicy.ShouldFailInactive(true, false, 5.9f, 6f), "startup watchdog waits bounded window");
            Assert(PvpCombatStartupPolicy.ShouldFailInactive(true, false, 6f, 6f), "completely inert active team fails technically");
            Assert(!PvpCombatStartupPolicy.ShouldFailInactive(true, true, 99f, 6f), "engaged team never fails inert watchdog");
            Assert(PvpCombatStartupPolicy.IsTechnicalFailure("technical_failure_ai_inactive"), "technical failure token exact");
            Assert(!PvpCombatStartupPolicy.ShouldRecordCompetitiveResult("technical_failure_ai_inactive"), "technical failure receives no match/history credit");
            Assert(!PvpCombatStartupPolicy.ShouldRecordCompetitiveResult("fight_state_failed"), "fight-state exception receives no competitive result credit");
            Assert(!PvpCombatStartupPolicy.ShouldRecordCompetitiveResult("runtime_invalid"), "runtime invariant failure receives no competitive result credit");
            Assert(!PvpCombatStartupPolicy.ShouldRecordCompetitiveResult("scene_transition"), "scene teardown is not a competitive result");
            Assert(PvpCombatStartupPolicy.ShouldRecordCompetitiveResult("player_death"), "legitimate defeat remains competitive");
            Assert(PvpCombatStartupPolicy.ShouldRecordCompetitiveResult("player_fled"), "legitimate flee remains competitive");
            Assert(PvpCombatStartupPolicy.ShouldRecordCompetitiveResult("retreat"), "legitimate enemy retreat remains competitive");
            Assert(!PvpCombatStartupPolicy.CanGrantVictoryReward("technical_failure_ai_inactive", true), "technical failure grants zero reward");
            Assert(PvpCombatStartupPolicy.CanGrantVictoryReward("proxy_death", true), "legitimate victory remains reward eligible exactly once downstream");

            PvpPointerOwnershipState pointer = new PvpPointerOwnershipState();
            Assert(pointer.PointerDown() && pointer.OwnsPointer && !pointer.IsDragging, "PvP drag owns input at pointer-down before threshold");
            Assert(!pointer.PointerDown(), "repeated pointer-down does not double-acquire");
            Assert(!pointer.BeginDrag() && pointer.IsDragging, "begin-drag reuses existing pointer ownership");
            Assert(pointer.Release() && !pointer.OwnsPointer && !pointer.IsDragging, "pointer release clears ownership and drag state");
            Assert(!pointer.Release(), "repeated release is idempotent");
            PvpPointerOwnershipState recovered = new PvpPointerOwnershipState();
            Assert(recovered.BeginDrag() && recovered.OwnsPointer && recovered.IsDragging, "begin-drag can recover a missed pointer-down callback");
            Assert(recovered.Release(), "recovered gesture releases cleanly");
            for (int i = 0; i < 20; i++)
            {
                PvpPointerOwnershipState cycle = new PvpPointerOwnershipState();
                Assert(cycle.PointerDown(), "cycle acquires input");
                cycle.BeginDrag();
                Assert(cycle.Release() && !cycle.OwnsPointer && !cycle.IsDragging, "repeated open/drag/close cycle leaves no stuck ownership");
            }
            PvpMatchLifecyclePolicy lifecycle = new PvpMatchLifecyclePolicy(false);
            Assert(lifecycle.State == PvpMatchLifecycleState.Disabled, "disabled lifecycle starts inert");
            lifecycle.SetEnabled(true);
            Assert(lifecycle.State == PvpMatchLifecycleState.Ready, "enable makes a fresh match ready");
            Assert(lifecycle.Queue("match-a") && lifecycle.State == PvpMatchLifecycleState.PendingChallenge, "challenge setup owns one pending match");
            Assert(lifecycle.BeginSpawn("match-a") && lifecycle.State == PvpMatchLifecycleState.Preparing, "accept advances pending match to preparation");
            Assert(lifecycle.HoldAttackers && !lifecycle.DefenderMayAttackProxy && !lifecycle.CombatReleased, "preparation holds both attack permissions");
            Assert(lifecycle.SpawnSucceeded() && lifecycle.State == PvpMatchLifecycleState.Countdown, "runtime-ready attackers advance to countdown");
            Assert(lifecycle.HoldAttackers && !lifecycle.DefenderMayAttackProxy && !lifecycle.CombatReleased, "countdown holds both sides before GO");
            Assert(lifecycle.Go() && lifecycle.State == PvpMatchLifecycleState.Active, "GO alone transitions match active");
            Assert(lifecycle.GoTransitions == 1 && lifecycle.CombatReleased && !lifecycle.HoldAttackers && lifecycle.DefenderMayAttackProxy, "GO releases both sides exactly once");
            Assert(!lifecycle.Go() && lifecycle.GoTransitions == 1, "GO cannot run twice");
            Assert(!lifecycle.BeginSpawn("match-c"), "active match rejects duplicate attacker group");
            lifecycle.BeginCleanup(); lifecycle.CompleteCleanup(true);
            Assert(lifecycle.State == PvpMatchLifecycleState.Ready && lifecycle.MatchId == string.Empty, "terminal cleanup returns fresh ready state");
            Assert(lifecycle.BeginSpawn("match-b") && lifecycle.State == PvpMatchLifecycleState.Preparing, "second match starts without restart");
            Assert(lifecycle.SpawnSucceeded() && lifecycle.State == PvpMatchLifecycleState.Countdown, "second match reaches countdown");
            Assert(lifecycle.Go() && lifecycle.State == PvpMatchLifecycleState.Active && lifecycle.GoTransitions == 1, "second match gets one fresh GO");
            lifecycle.BeginCleanup();
            PvpMatchLifecycleState cleanupState = lifecycle.State;
            lifecycle.BeginCleanup();
            Assert(lifecycle.State == cleanupState, "cleanup begins idempotently");
            lifecycle.CompleteCleanup(false);
            Assert(lifecycle.State == PvpMatchLifecycleState.Disabled && lifecycle.MatchId == string.Empty, "zone/disable cleanup releases active ownership without restart");
            Console.WriteLine("PvpUiPolicyTests: PASS"); return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine("PvpUiPolicyTests: FAIL " + ex.Message); return 1; }
    }
    private static string Field(string line, string key)
    {
        string[] pairs = (line ?? string.Empty).Split('&');
        for (int i = 0; i < pairs.Length; i++)
        {
            int eq = pairs[i].IndexOf('=');
            if (eq <= 0) continue;
            if (pairs[i].Substring(0, eq) == key) return pairs[i].Substring(eq + 1);
        }
        return string.Empty;
    }

    private static void TestWildAmbushZonePolicy()
    {
        Func<string, bool, bool, bool, bool, bool, string, string, string, PvpWildAmbushZoneDecision> decide =
            (scene, ready, zoning, pvp, ambush, coop, protectedCsv, disabledCsv, enabledCsv) =>
                PvpWildAmbushZonePolicy.Evaluate(scene, ready, zoning, pvp, ambush, coop, protectedCsv, disabledCsv, enabledCsv);

        PvpWildAmbushZoneDecision hidden = decide("Hidden", true, false, true, true, false, "", "", "");
        Assert(hidden.Playable && !hidden.Protected && hidden.Allowed, "Hidden is ordinary eligible adventure gameplay");
        Assert(hidden.Source == "ordinary_adventure_zone", "Hidden explains default adventure admission");
        Assert(decide("Hidden Hills", true, false, true, true, false, "", "", "").Allowed, "Hidden Hills remains eligible");
        Assert(decide("FernallaField", true, false, true, true, false, "", "", "").Allowed, "non-legacy FernallaField adventure scene is eligible");
        Assert(decide("Loomingwood", true, false, true, true, false, "", "", "").Allowed, "second non-legacy adventure scene is eligible");
        Assert(decide("FutureAdventure01", true, false, true, true, false, "", "", "").Allowed, "future ready adventure scene needs no allowlist entry");
        Assert(decide("FutureAdventure01", true, false, true, true, false, "", "", "").Source == "ordinary_adventure_zone", "future scene uses default policy source");

        PvpWildAmbushZoneDecision azure = decide("Azure", true, false, true, true, false, "", "", "");
        Assert(azure.Playable && azure.Protected && !azure.Allowed, "Port Azure internal scene is a playable protected hub");
        Assert(azure.Source == "protected_zone", "Port Azure denial explains protected authority");
        Assert(!decide("Port Azure", true, false, true, true, false, "", "", "").Allowed, "Port Azure display alias is protected");
        Assert(!decide("Stowaway", true, false, true, true, false, "", "", "").Allowed, "starting island internal scene is protected");
        Assert(!decide("Stowaway's Step", true, false, true, true, false, "", "", "").Allowed, "starting area display alias is protected");
        Assert(!decide("Tutorial", true, false, true, true, false, "", "", "").Allowed, "tutorial scene is protected");
        Assert(!decide("Island Tomb", true, false, true, true, false, "", "", "").Allowed, "tutorial display alias is protected");
        Assert(!decide("Custom Sanctuary", true, false, true, true, false, "Custom Sanctuary", "", "").Allowed, "configured protected scene is denied");
        Assert(decide("IslandOfDanger", true, false, true, true, false, "", "", "").Allowed, "broad island substring no longer misclassifies adventure scenes");
        Assert(decide("DangerCityRuins", true, false, true, true, false, "", "", "").Allowed, "broad city substring no longer invents protection");

        Assert(!decide("Menu", true, false, true, true, false, "", "", "").Allowed, "Menu fails closed");
        Assert(!decide("CharacterSelect", true, false, true, true, false, "", "", "").Allowed, "CharacterSelect fails closed");
        Assert(!decide("LoadScene", true, false, true, true, false, "", "", "").Allowed, "LoadScene fails closed");
        Assert(!decide("Hidden", false, false, true, true, false, "", "", "").Allowed, "non-ready scene state fails closed");
        Assert(decide("Hidden", false, false, true, true, false, "", "", "").Source == "non_gameplay_scene", "non-ready state explains non-gameplay denial");
        Assert(!decide("Hidden", true, true, true, true, false, "", "", "").Allowed, "zoning state fails closed");
        Assert(!decide("", true, false, true, true, false, "", "", "").Allowed, "empty scene fails closed");
        Assert(!decide(null, true, false, true, true, false, "", "", "").Allowed, "null scene fails closed");

        Assert(!decide("Hidden", true, false, false, true, false, "", "", "").Allowed, "master PvP toggle still gates ambushes");
        Assert(decide("Hidden", true, false, false, true, false, "", "", "").Source == "pvp_disabled", "master toggle denial is diagnostic");
        Assert(!decide("Hidden", true, false, true, false, false, "", "", "").Allowed, "wild-ambush toggle still gates ambushes");
        Assert(decide("Hidden", true, false, true, false, false, "", "", "").Source == "ambush_disabled", "ambush toggle denial is diagnostic");
        Assert(!decide("Hidden", true, false, true, true, true, "", "", "").Allowed, "COOP still suppresses eligible adventure zones");
        Assert(decide("Hidden", true, false, true, true, true, "", "", "").Source == "coop_blocked", "COOP denial is diagnostic");
        Assert(decide("Hidden", true, false, true, true, false, "", "", "").Allowed, "single-player preserves normal eligibility");

        Assert(!decide("Hidden", true, false, true, true, false, "", "Hidden", "").Allowed, "explicit per-zone OFF is preserved");
        Assert(decide("Hidden", true, false, true, true, false, "", "Hidden", "").Source == "explicit_zone_disabled", "explicit OFF has diagnostic source");
        Assert(decide("Hidden", true, false, true, true, false, "", "", "Hidden").Allowed, "legacy explicit ON remains compatible");
        Assert(decide("Hidden", true, false, true, true, false, "", "", "Hidden").Source == "explicit_zone_enabled", "legacy ON has diagnostic source");
        Assert(!decide("Menu", true, false, true, true, false, "", "", "Menu").Allowed, "override cannot enable Menu");
        Assert(!decide("Azure", true, false, true, true, false, "", "", "Azure").Allowed, "override cannot enable protected hub");
        Assert(!decide("Hidden", false, false, true, true, false, "", "", "Hidden").Allowed, "override cannot enable non-ready gameplay");

        Assert(PvpWildAmbushZonePolicy.WithEntry("Hidden, Brake", "Hidden", false) == "Brake", "ambushhere off removes legacy explicit ON");
        Assert(PvpWildAmbushZonePolicy.WithEntry("Hidden", "FernallaField", true) == "Hidden, FernallaField", "ambushhere mutation adds exact scene once");
        Assert(PvpWildAmbushZonePolicy.WithEntry("Hidden, hidden", "Hidden", true) == "Hidden", "override mutation de-duplicates normalized scene identity");
        Assert(PvpWildAmbushZonePolicy.Contains("Port Azure, Hidden Hills", "portazure"), "configured lists normalize spaces and punctuation");
    }

    private static void TestPreOpportunityPolicy()
    {
        PvpPreOpportunityInput input = new PvpPreOpportunityInput
        {
            CharacterReady = true, PvpEnabled = true, PartyValid = true,
            PlayerLevel = PvpPreOpportunityPolicy.MinimumPlayerLevel
        };
        Assert(PvpPreOpportunityPolicy.Evaluate(input) == PvpPreOpportunityDecision.Allowed,
            "pre-opportunity policy permits ready clear context");
        input.PlayerLevel--;
        Assert(PvpPreOpportunityPolicy.Evaluate(input) == PvpPreOpportunityDecision.BelowMinimumLevel,
            "absolute player floor blocks arranged, wild, and Nemesis before selection");
        input.PlayerLevel++; input.NativeCombatActive = true;
        Assert(PvpPreOpportunityPolicy.Evaluate(input) == PvpPreOpportunityDecision.NativeCombatActive,
            "native combat blocks pre-opportunity context");
        input.NativeCombatActive = false; input.RecentNativeCombat = true;
        Assert(PvpPreOpportunityPolicy.Evaluate(input) == PvpPreOpportunityDecision.RecentNativeCombat,
            "recent native-combat grace blocks immediate post-pull opportunity");
        input.RecentNativeCombat = false; input.NearbyWorldActor = true;
        Assert(PvpPreOpportunityPolicy.Evaluate(input) == PvpPreOpportunityDecision.NearbyWorldActor,
            "nearby hostile neutral or protected external actor blocks opportunity");
        input.NearbyWorldActor = false; input.PartyValid = false;
        Assert(PvpPreOpportunityPolicy.Evaluate(input) == PvpPreOpportunityDecision.PartyInvalid,
            "invalid party blocks opportunity without changing matchmaking policy");
        input.PartyValid = true; input.Zoning = true;
        Assert(PvpPreOpportunityPolicy.Evaluate(input) == PvpPreOpportunityDecision.Zoning,
            "zoning blocks opportunity before any presentation");
        input.Zoning = false; input.RestrictedZone = true;
        Assert(PvpPreOpportunityPolicy.Evaluate(input) == PvpPreOpportunityDecision.RestrictedZone,
            "restricted zones remain an authoritative pre-opportunity block");
        Assert(PvpMatchmakingPolicy.Evaluate(new PvpMatchInput { DefenderPartySize = 2, AttackerPartySize = 2,
            DefenderAverageLevel = 8, AttackerAverageLevel = 8, LevelRange = 3 }) == PvpMatchDecision.Eligible,
            "party-average matchmaking remains unchanged after the absolute player floor");
        Assert(PvpPreOpportunityPolicy.RunSelfTests().StartsWith("PASS", StringComparison.Ordinal),
            "pre-opportunity deterministic self-tests");
    }

    private static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
