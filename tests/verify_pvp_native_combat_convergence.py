from pathlib import Path
import re

ROOT = Path(__file__).resolve().parents[1]
SRC = ROOT / 'src'
def read(name): return (SRC / name).read_text(encoding='utf-8')

factory = read('PvpTemporaryCloneFactory.cs')
contain = read('PvpCombatContainment.cs')
controller = read('PvpController.cs')
world = read('PvpWorldCombatPolicy.cs')
target = read('PvpSpellTargetPolicy.cs')
sem = read('PvpSpellSemantics.cs')
effects = read('PvpNativeSpellEffectBridge.cs')
bridge = read('PvpNativeSpellExecutionBridge.cs')
plugin = read('ErenshorPvPPlugin.cs')
loop_policy = read('PvpNativeCombatLoopPolicy.cs')
readme = (ROOT / 'README.md').read_text(encoding='utf-8')
changelog = (ROOT / 'CHANGELOG.md').read_text(encoding='utf-8')
csproj = (ROOT / 'ErenshorPvP.csproj').read_text(encoding='utf-8')
handoff = (ROOT / 'HANDOFF.md').read_text(encoding='utf-8')
forensics = (ROOT / 'docs' / 'PVP_COMBAT_LOOP_RECOVERY_FORENSICS.md').read_text(encoding='utf-8')
live_qa = (ROOT / 'docs' / 'PVP_COMBAT_LOOP_RECOVERY_LIVE_QA.md').read_text(encoding='utf-8')
checks=[]
def check(cond,label):
    if not cond: raise AssertionError(label)
    checks.append(label)

# Duel-parity player target semantics.
check('CanAdaptOpponentHealToSelf' in target, 'narrow Duel-parity self-heal adapter policy')
check('directCastEntry' in contain and 'ref Stats targetStats' in contain, 'StartSpell target can be adapted by ref')
check('targetStats = selfStats' in contain, 'only Stats argument rewritten for proven self-heal shape')
check('current_target_preserved=true' in contain, 'CurrentTarget preservation explicit')
check('GameData.PlayerControl.CurrentTarget =' not in contain, 'self adaptation does not mutate persistent CurrentTarget')
for token in ('SelfOnly','ApplyToCaster','InflictOnSelf'):
    check(token in sem + target, f'{token} semantics present')
check('if (!adaptedToSelf && semantic.DeclaresSelf && source != null) policyTarget = source' in contain,
      'declared self spell authorized against caster semantics')
check('beneficial ? PvpInteractionDecision.Block : PvpInteractionDecision.AllowMatch' in world,
      'cross-team beneficial is blocked while harmful remains legal')
check('sourceAttacker && targetAttacker' in world and 'sourceDefender && targetDefender' in world,
      'same-team spell edge left to native semantics')

# Beneficial != healing and temporary class toolkit.
for token in ('DirectHpHeal','HealOverTime','BeneficialBuff','SelfUtility','HarmfulDamage','OffensiveDebuff','CrowdControl','Area'):
    check(token in sem, f'semantic category {token}')
check('semantic.DirectHpHeal || semantic.HealOverTime' in factory, 'heal counters/selectors require actual HP-heal or HoT semantics')
check('beneficial utilities remain buffs' in factory, 'generic beneficial spells not classified as heals')
check('UnsafeWorldShape = pet || charm' in sem or 'result.UnsafeWorldShape = pet || charm' in sem, 'pet/charm unsafe automation excluded')
check('PvpSpellSemantics.IsSafeTemporaryCombatAbility(source)' in factory, 'loadout admission uses semantic safety gate')

# SpellVessel bridge is temporary-proxy-only and restoration-safe.
check('[HarmonyPatch(typeof(SpellVessel), "FixedUpdate")]' in effects, 'current SpellVessel.FixedUpdate patched narrowly')
check('PvpTemporaryCloneFactory.IsTemporaryActor(caster)' in effects, 'bridge scopes exact temporary proxy caster')
check('PvpCombatContainment.LethalFightActive' in effects, 'bridge requires active PvP')
check('interruptable = false' in effects, 'only proven interruptable distinction temporarily changed')
check('state.OriginalInterruptable = interruptable' in effects, 'original interruptable state captured')
check('[HarmonyFinalizer]' in effects and 'RestoreVesselFixedUpdate' in effects, 'bridge restoration runs via Finalizer')
check('interruptable = state.OriginalInterruptable' in effects, 'original field restored')
for forbidden in ('SimPlayer = true','ThisSim =','HealMe(','CurrentHP +=','CurrentHP ='):
    check(forbidden not in effects, f'effect bridge avoids forbidden mutation {forbidden}')
check('casterAlive' in effects and 'targetAlive' in effects and 'targetInvulnerable' in effects,
      'dead/invalid/invulnerable target conditions fail bridge closed')

# Native effect observability.
check('[HarmonyPatch(typeof(SpellVessel), "CreateSpellChargeEffect")]' in effects, 'vessel creation observed')
check('[HarmonyPatch(typeof(SpellVessel), "ResolveSpell")]' in effects, 'native ResolveSpell entry observed')
check('spell_vessel_created' in factory and 'spell_vessel_resolve' in factory, 'vessel pipeline diagnostics')
check('healme_entered' in factory and 'heal_effect_applied' in factory, 'HealMe entry/effective result diagnostics')
check('hp_before=' in factory and 'hp_after=' in factory and 'effective=' in factory, 'actual HP before/after diagnostic')
check('if (applied <= 0) return; // accepted/full-health/overheal casts do not inflate FIGHT healing.' in contain,
      'FIGHT healing counts actual effective HP only')
check('SnapshotStatusEffectState' in contain, 'status state snapshotted')
check('slot.Effect != spell' in contain, 'status matching uses actual current slot effect')
check('ClassifyStatusApplication' in target and 'application_proven' in target and 'refresh_proven' in target and 'no_observable_state_change' in target,
      'status result distinguishes application, refresh, and no observable change')
check('proven_applied=' in factory and 'outcome=' in factory, 'status effect proof is observable')
check(contain.count('[HarmonyFinalizer] private static Exception Finalizer(Exception __exception, PvpCombatContainment.StatusEffectTelemetryState __state)') == 4,
      'status hooks use one finalizer completion path per overload')
check('PvpStatusEffect3Patch' in contain and '[HarmonyPostfix] private static void Postfix(PvpCombatContainment.StatusEffectTelemetryState __state)' not in contain,
      'status telemetry does not double-finish through postfix plus finalizer')
check(contain.count('FinishHpHealing(__instance, __state); return __exception;') == 2,
      'HealMe telemetry finalizers close nested scope on native exceptions')

# Existing one-time startup cooldown preserved; atkSpellDelay drains naturally.
check('TryClearSyntheticStartupCooldown(npc, true' in factory, 'one-time NPCSpellCooldown startup repair retained')
check(bridge.count('NpcSpellCooldownField.SetValue') == 1, 'NPCSpellCooldown is written only by one-time compatibility repair')
check('AttackSpellDelayField.SetValue' not in bridge + factory, 'atkSpellDelay is never forced')
check('StartupAttackDelayReady' in target and 'AttackStartupMaintenanceReady' in factory,
      'atkSpellDelay remains diagnostic without becoming a deferred-Start countdown gate')
check('attackSpellDelay <= 0f' in target, 'native atkSpellDelay units drain to readiness')

# Pre-GO lifecycle/readiness architecture.
check('PrepareNativeStartProbe(npc)' in factory, 'native Start probe prepared before countdown')
check('natural_start=deferred_to_go' in factory, 'Unity owns deferred natural Start lifecycle')
check('.Start()' not in re.sub(r'//.*','',factory), 'factory never manually invokes Start method')
for flag in ('NativeStartCompleted','RuntimeInvariantPassed','StatsReady','CharacterReady','CasterReady','SpellsReady',
             'NavAgentReady','OnNavMesh','VisualShellReady','RewardSuppressionReady','StartupNpcSpellCooldownNormalized',
             'AttackStartupMaintenanceReady','InitialTargetCanBeResolved'):
    check(flag in factory, f'readiness flag {flag}')
check('npc.NeverAggro = true' in factory and 'npc.CurrentAggroTarget = null' in factory, 'preparation holds aggression/target inert')
check('nav.isStopped = true' in factory, 'preparation holds NavMesh inert')
check('PreparationTimeoutSeconds' in controller and 'proxy_preparation_timeout' in controller, 'bounded preparation timeout')
check('Preparing opponents...' in controller, 'pre-countdown preparation UX')
check('AreAllProxiesReady' in controller and 'countdown_ready_barrier' in controller, 'visible countdown waits for all ready')
check('natural_start=enabled_after_go' in contain, 'GO enables the natural Start lifecycle')
check('NotifyGoReleased' in contain and 'go_release_timestamp' in factory, 'GO is explicit release timestamp')
check('AreAllProxiesReadyForGo' in factory and 'EvaluateAllProxiesReady(false, out reason)' in factory, 'GO readiness cannot recover missing loop infrastructure')
check('PvpTemporaryCloneFactory.AreAllProxiesReadyForGo(out readiness)' in controller, 'controller GO path is validation-only')
check('npc.NeverAggro = false' in contain, 'GO releases native aggression')
check('ObserveFirstActionLatency' in factory, 'bounded first-action latency telemetry')
for stage in ('first_target_acquired','first_movement','first_melee_decision','first_attack_spell_candidate','first_native_spell_request'):
    check(stage in factory, f'first action stage {stage}')

# 0.5.15 missing-loop recovery and ownership.
for token in ('ShouldStartMissingLoop','ownedStartCount == 0','InfrastructureReady','PreGoBoundaryReady','CanStopOwnedLoop'):
    check(token in loop_policy, f'combat-loop policy {token}')
for token in ('ModOwnedNavLoops','ModOwnedBehaviorLoops','ModOwnedNavLoopStarts','ModOwnedBehaviorLoopStarts',
              'TryStartOwnedNativeCombatLoop','StopOwnedNativeCombatLoops','pre_go_behavior_hold','pre_go_nav_hold',
              'behavior_section_reached','combat_loop_recovery'):
    check(token in factory, f'combat-loop runtime {token}')
check(factory.count('StartCoroutine(loop)') == 1, 'owned coroutine startup is centralized')
check(factory.count('StopCoroutine(owned)') == 2, 'cleanup stops only two exact owned loop kinds')
check('object.ReferenceEquals(current, loop)' in factory, 'failed startup clears only exact candidate field')
check('object.ReferenceEquals(current, owned)' in factory, 'cleanup clears native field only for exact owned iterator')
check('StopAllCoroutines(' not in factory, 'broad coroutine cleanup forbidden')
check('PvpNativeCombatLoopPolicy.RunSelfTests()' in controller, '/epvp selftest includes combat-loop ownership policy')

# Attacker ally bridge remains native effect path.
check('TryFindInjuredAttackerAlly' in contain, 'attacker ally selection remains bounded')
check('foreach (Character candidate in EnemyActors)' in contain, 'ally bridge only scans active attacker team')
check('candidate == healer' in contain, 'self-heal remains native rather than bridge')
check('npc.MemmedHealSpells' in factory, 'native memmed heal list used')
check('caster.StartSpell(selected, ally.MyStats)' in factory, 'ally bridge uses native StartSpell')
check('PvpSpellSemantics.Inspect(spell)' in factory, 'ally bridge filters actual heal semantics')
check('CurrentHP +=' not in factory + contain, 'no manual HP addition')

# Version/revision surfaces.
check('LunarisPlugin("forgetwhtuno.erenshor.pvp", "0.5.34"' in plugin, 'plugin version is 0.5.34')
check('revision=pvp-0.5.34-preop-world-context-r1' in plugin, 'runtime revision marker is pre-opportunity world context r1')
check('<Version>0.5.34</Version>' in csproj and '<AssemblyVersion>0.5.34.0</AssemblyVersion>' in csproj and '<FileVersion>0.5.34.0</FileVersion>' in csproj, 'csproj version metadata is 0.5.34')

# World combat/Nemesis/UI regressions.
check('AllowWorld' in world, 'world-combat participation retained')
check('third_party_aggro' not in contain, 'isolated-arena cancellation not restored')
check('LiveCombatPresentationSummary' in contain, 'FIGHT aggregate telemetry retained')
for token in ('ErenshorPvpApi','ErenshorPvpEvents','RequestNemesisAmbush','RecentResults'):
    check(token in ''.join(read(n) for n in ['PvpControlApi.cs','PvpEventContract.cs','PvpController.cs']), f'public integration {token} retained')


# 0.5.14 pre-GO structural navigation readiness: preparation may be stopped and need not
# observe UpdateNav until GO. Post-GO operational nav health remains unchanged.
pre_nav = read('PvpPreparationNavReadinessPolicy.cs')
pre_nav_runtime = read('PvpPreparationNavRuntime.cs')
heal_probe = read('PvpNativeHealThresholdProbe.cs')
check('IsStructurallyReady' in pre_nav and 'heldStopped' in pre_nav and '!hasPath' in pre_nav, 'structural pre-GO nav policy represented')
check('ResetPath()' in pre_nav_runtime and 'nav.isStopped = true' in pre_nav_runtime, 'pre-GO stale path cleared while held')
check('SetDestination(' not in pre_nav_runtime and 'Warp(' not in pre_nav_runtime, 'pre-GO probe cannot visibly move/teleport proxy')
check('PvpPreparationNavRuntime.EvaluateAgent' in factory, 'pre-GO readiness uses structural runtime adapter')
check('state.NavAgentReady = nav != null && nav.enabled && probe != null && probe.FirstUpdateNavCompleted' not in factory, 'circular UpdateNav pre-GO gate removed')
check('PvpNativeNavHealthPolicy.IsHealthy' in factory, 'post-GO operational native nav health retained')
check('GetILAsByteArray' in heal_probe and 'CheckHeals' in heal_probe and 'OpCodes.Ldc_R4' in heal_probe and 'OperandSize' in heal_probe, 'opcode-aware current-native heal threshold IL proof present')
check('PvpNativeHealThresholdProbe.RunSelfTest()' in controller, '/epvp selftest includes current-native heal proof')
print(f'verify_pvp_native_combat_convergence: PASS ({len(checks)} checks)')
