from pathlib import Path
import re

ROOT = Path(__file__).resolve().parents[1]
factory = (ROOT / "src" / "PvpTemporaryCloneFactory.cs").read_text(encoding="utf-8")
contain = (ROOT / "src" / "PvpCombatContainment.cs").read_text(encoding="utf-8")
nav = (ROOT / "src" / "PvpNativeNavHealthPolicy.cs").read_text(encoding="utf-8")
execution = (ROOT / "src" / "PvpCombatExecutionPolicy.cs").read_text(encoding="utf-8")
world = (ROOT / "src" / "PvpWorldCombatPolicy.cs").read_text(encoding="utf-8")

checks = 0
def require(value, message):
    global checks
    if not value:
        raise AssertionError(message)
    checks += 1

# Native Start remains authoritative, but Start-derived ability state may not be destroyed afterward.
post = re.search(r"internal static void ObserveNativeNpcStartCompleted[\s\S]*?PvpCombatContainment\.ObserveProxyNativeStartCompleted\(npc\);", factory)
require(post is not None, "native Start completion block missing")
require("ApplyProfileLoadout(npc, profile)" not in post.group(0), "post-Start spell/loadout rebuild returned")
require("LogPostStartAbilityRuntime" in post.group(0) and "MemmedHealSpells" in factory, "Start-derived heal runtime is not observed")

# Gameplay root weapon mode and render equipment are derived from the same effective profile equipment.
for token in ('ResolveEffectiveEquipment', 'TrySetField(npc, "MHBow"', 'TrySetField(npc, "MHWand"',
              'TrySetField(npc, "BowEquipped"', 'TrySetField(npc, "WandEquipped"', 'SetAttackRanges'):
    require(token in factory, "profile weapon runtime missing: " + token)
require(factory.count("ResolveEffectiveEquipment(profile, out fallback)") >= 2,
        "visual and root weapon paths are not sharing effective equipment resolution")

# Render shell is presentation-only and cannot own locomotion.
require("visualAnimator.applyRootMotion = false" in factory, "render shell root motion was not disabled")
require("combatActor.AssignAnim(visualAnimator)" in factory, "native Character animator redirect missing")
require("shell_follow=" in factory and "shell_local_delta=" in factory, "root-vs-shell movement evidence missing")

# A nav request is no longer treated as execution; bounded delayed displacement evidence exists.
require("return !needsPursuit || movementObserved" in nav, "destination request still counts as pursuit success")
for token in ("pursuit_probe_start", "pursuit_probe_result", "root_displacement=", "pathStatus=", "remaining=", "target_stable=", "animator_state="):
    require(token in factory, "bounded pursuit evidence missing: " + token)
require("path_but_no_motion" in execution and "PursuitProven" in execution, "execution-level nav policy missing")

# Startup/engagement evidence must use execution-level boundaries, not the counters from the failed 5v5.
evidence = re.search(r"internal static bool HasAnyNativeCombatEvidence\(\)[\s\S]*?\n        }", factory)
require(evidence is not None, "native combat evidence method missing")
for token in ("MeleeAttemptCounts", "SpellCompletedCounts", "MovementObserved", "DamageDealtCounts", "HealingDoneCounts"):
    require(token in evidence.group(0), "execution evidence missing: " + token)
for token in ("AttackDecisionCounts", "HealCheckCounts", "SpellStartCounts", "NavPursuitRequested"):
    require(token not in evidence.group(0), "decision/request counter still proves execution: " + token)

# StartSpell Prefix = request/admission only; Postfix checks native casting state, completion and effects separately.
require(factory.count("ObserveSpellStartResult(__instance, __0, __1, __result)") == 6, "all StartSpell variants need actual bool return observation")
for token in ("nativeAccepted", "caster.GetCurrentCast() == spell", "spell_native_accepted", "spell_casts_finished", "spell_effect_events", "cast_finished", "animator_state_changed="):
    require(token in factory, "cast execution boundary missing: " + token)
require("heal_targets_resolved" in factory and "heal_memmed=" in factory, "healing target/runtime evidence missing")
require("BeginAttackSpellEntry" in factory and "FinishAttackSpellEntry" in factory and "spell_no_request" in factory,
        "attack AI entry -> concrete request boundary is not instrumented")
bridge = (ROOT / "src" / "PvpNativeSpellExecutionBridge.cs").read_text(encoding="utf-8")
require("PvpNativeSpellExecutionBridge.TryClearSyntheticStartupCooldown" in factory and "cleared_synthetic_startup_npc_spell_cooldown" in bridge and
        "PvpNativeSpellExecutionBridge.TryReadAttackSpellState" in factory,
        "temporary non-Sim NPC startup spell cooldown compatibility repair missing")
require(bridge.count("NpcSpellCooldownField.SetValue") == 1,
        "post-cast native NPCSpellCooldown must not be cleared from the attack hot path")
require("TryAssistProxyAllyHeal" in factory and "TryFindInjuredAttackerAlly" in contain and "caster.StartSpell(selected, ally.MyStats)" in factory,
        "native allied-heal request bridge missing")

# Melee attempt and synchronous native damage are distinguishable.
require("FinishMeleeAttempt" in factory and "MeleeDamageCounts" in factory and "melee_damage=" in factory,
        "melee attempt-to-damage telemetry missing")

# Exactly one native-Sim comparison scan per match; it is read-only evidence.
require("native_sim_capability_snapshot" in factory and "proxy_capability_snapshot" in factory,
        "native Sim capability comparison missing")
require("exactly one bounded scan per match" in factory, "native comparison is not explicitly bounded")

# World-combat policy must stay open; no isolation-era cancellation may return.
require("AllowWorld" in world, "world-combat admission removed")
require("third_party_aggro" not in contain, "third-party isolation cancellation returned")

print(f"verify_pvp_live_execution_source: PASS ({checks} checks)")
