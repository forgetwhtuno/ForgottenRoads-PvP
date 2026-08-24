from pathlib import Path
import re

ROOT = Path(__file__).resolve().parents[1]
SRC = ROOT / "src"
factory = (SRC / "PvpTemporaryCloneFactory.cs").read_text(encoding="utf-8")
bridge = (SRC / "PvpNativeSpellExecutionBridge.cs").read_text(encoding="utf-8")
policy = (SRC / "PvpSpellExecutionPolicy.cs").read_text(encoding="utf-8")
contain = (SRC / "PvpCombatContainment.cs").read_text(encoding="utf-8")
panel = (SRC / "PvpPanel.cs").read_text(encoding="utf-8")
world = (SRC / "PvpWorldCombatPolicy.cs").read_text(encoding="utf-8")
controller = (SRC / "PvpController.cs").read_text(encoding="utf-8")

checks = []
def check(cond, label):
    if not cond:
        raise AssertionError(label)
    checks.append(label)

# Exact current incompatibility repair: cached reflection, temporary-only, non-Sim only.
check('private static readonly FieldInfo NpcSpellCooldownField' in bridge, 'reflection lookup cached')
check('typeof(NPC).GetField("NPCSpellCooldown", InstanceFlags)' in bridge, 'current NPCSpellCooldown bound')
check('typeof(NPC).GetField("atkSpellDelay", InstanceFlags)' in bridge, 'current atkSpellDelay bound')
check('typeof(NPC).GetField("healCD", InstanceFlags)' in bridge, 'current healCD bound')
check('ShouldClearSyntheticNpcSpellCooldown(true, npc.SimPlayer' in bridge, 'temporary non-Sim startup gate policy used')
check('TryClearSyntheticStartupCooldown' in bridge and 'NpcSpellCooldownField.SetValue(npc, 0f)' in bridge, 'synthetic startup spell gate cleared once after Start')
check('TryReadAttackSpellState' in bridge, 'combat hot path observes native cooldown state without clearing it')
check(factory.count('TryClearSyntheticStartupCooldown(npc, true') == 2, 'normal and recovered native Start paths clear startup gate')
check('proxy_spell_startup_gate' in factory, 'startup compatibility repair observable')
check('AttackSpellDelayField.SetValue' not in bridge, 'native attack spell delay not overridden')
check('forceSpellCD' in bridge and 'ForceSpellCooldownField.SetValue' not in bridge, 'force spell cooldown observed not overridden')
check(bridge.count('NpcSpellCooldownField.SetValue') == 1, 'post-cast NPC spell cooldown is never cleared by attack hot path')

# Attack pipeline: AI entry is not called a cast; actual request + bool return are distinct.
check('attack_spell_ai_entry' in factory, 'AI entry labeled accurately')
check('selected=native_pending' in factory, 'pre-selection state explicit')
check('spell_native_request' in factory, 'concrete native request logged')
check('spell_no_request' in factory, 'no-request boundary logged')
check('spell_id=' in factory and 'spell.Id' in factory, 'selected spell identity logged')
check('range=' in factory and 'distance=' in factory and 'mana_cost=' in factory, 'request prerequisites logged')
check(factory.count('bool __state, bool __result') == 6, 'all StartSpell-family bool results captured')
check(factory.count('ObserveSpellStartResult(__instance, __0, __1, __result)') == 6, 'actual native return threaded')
check('classification=native_request_rejected' in factory, 'native rejection classified')
check('classification=native_accepted' in factory, 'native acceptance classified')
check('AttackSpellCompletedCounts' in factory and 'AttackSpellEffectCounts' in factory, 'attack completion/effect split')
check('CastPipelineAssessment' in factory, 'terminal A-G-style attack classification')
check('AttackEntryDetailLogCounts' in factory and 'SpellRequestDetailLogCounts' in factory and 'SpellResultDetailLogCounts' in factory and 'CastFinishDetailLogCounts' in factory, 'cast stages have independent bounded log budgets')
check('healthy_native_sim_cast accepted=true' in factory, 'healthy native Sim accepted-cast comparison captured')
check('_healthyNativeCastLogged' in factory, 'healthy native Sim cast comparison bounded once per match')

# Healing: native self-heal stays enabled; bridge only fills synthetic ally-membership gap.
check('npc.NoSelfHeal = false' in factory, 'native self-heal preserved')
threshold_policy = (SRC / 'PvpNativeHealThresholdPolicy.cs').read_text(encoding='utf-8')
probe = (SRC / 'PvpNativeHealThresholdProbe.cs').read_text(encoding='utf-8')
check('ExpectedThreshold = 0.66f' in threshold_policy, 'current native 66 percent heal threshold represented')
check('CheckHeals' in probe and 'GetILAsByteArray' in probe and 'OpCodes.Ldc_R4' in probe and 'OperandSize' in probe, 'native heal threshold is proven by opcode-aware current CheckHeals IL walk')
check('TryFindInjuredAttackerAlly' in contain, 'synthetic attacker team heal target query')
check('candidate == healer' in contain, 'ally bridge leaves self-heal to native AI')
check('EnemyActors.Contains(healer)' in contain and 'foreach (Character candidate in EnemyActors)' in contain, 'heal candidates restricted to attacker team')
check('LegalProxyAllyHealTarget(true, true, false' in contain, 'heal target policy applied')
check('npc.MemmedHealSpells' in factory, 'native memmed heal list used')
check('currentStats.CurrentMana <= spell.ManaCost' in factory, 'native strict mana prerequisite represented')
check('spell.SpellRange <= distance' in factory, 'heal range prerequisite represented')
check('caster.StartSpell(selected, ally.MyStats)' in factory, 'native heal request invoked')
check('TrySetNativeHealCooldown' in factory and 'spell.Cooldown * 60f' in bridge, 'native ordinary-NPC heal cadence preserved')
check('HealNativeAcceptedCounts' in factory and 'HealEffectCounts' in factory, 'heal acceptance/effect split')
check('HealAssessment' in factory, 'terminal heal classification')

# Never fake spell damage/heal; native HP telemetry remains the effect proof.
check('directly subtract' not in factory.lower(), 'no documented manual spell damage path')
check('RecordDamageDealt' in factory and 'RecordHealingDone' in factory, 'effective native HP deltas remain evidence')
check('TargetHpBefore' in factory and 'probe.Target.CurrentHP > probe.TargetHpBefore' in factory, 'actual heal restoration observed')

# Regression-sensitive systems and world-combat semantics remain present.
for token, label in [
    ('[HarmonyPatch(typeof(NPC), "Start")]', 'NPC.Start observer preserved'),
    ('pursuit_probe_result', 'movement probe preserved'),
    ('PvpMeleeAttemptTelemetryPatch', 'melee patch preserved'),
    ('MeleeDamageCounts', 'melee damage telemetry preserved'),
    ('AllowWorld', 'world-combat expansion preserved'),
    ('ErenshorPvpApi', 'public PvP API preserved'),
    ('ErenshorPvpEvents', 'public PvP events preserved'),
    ('RequestNemesisAmbush', 'Nemesis request contract preserved'),
    ('RecentResults', 'recent-result contract preserved'),
]:
    haystack = factory + contain + world + controller + ''.join((SRC / n).read_text(encoding='utf-8') for n in ['PvpControlApi.cs','PvpEventContract.cs'])
    check(token in haystack, label)
check('third_party_aggro' not in contain, 'isolated-arena cancellation not restored')

# Restrained retained fight feedback uses existing truthful team aggregate counters.
check('LiveCombatPresentationSummary' in contain, 'bounded fight stats surface')
check('Damage dealt to attackers' in contain and 'Healing to defenders' in contain, 'truthful aggregate labels')
check('PvpCombatContainment.LiveCombatPresentationSummary()' in panel, 'fight panel presents live stats')

# Policy unit-test surface and in-game self-test integration.
check('RunSelfTests()' in policy, 'spell execution policy selftest')
check('PvpSpellExecutionPolicy.RunSelfTests()' in controller, '/epvp selftest includes spell policy')
check('PvpNativeHealThresholdProbe.RunSelfTest()' in controller, '/epvp selftest includes native heal-threshold proof')

print(f"verify_pvp_spell_execution_repair: PASS ({len(checks)} checks)")
