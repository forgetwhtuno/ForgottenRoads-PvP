from pathlib import Path
import re

ROOT = Path(__file__).resolve().parents[1]
SRC = ROOT / "src"

def read(name):
    return (SRC / name).read_text(encoding="utf-8")

checks = 0
def require(condition, message):
    global checks
    if not condition:
        raise AssertionError(message)
    checks += 1

world = read("PvpWorldCombatPolicy.cs")
contain = read("PvpCombatContainment.cs")
compat = read("PvpCompatibility.cs")
controller = read("PvpController.cs")
factory = read("PvpTemporaryCloneFactory.cs")
startup = read("PvpCombatStartupPolicy.cs")
lifecycle = read("PvpMatchLifecyclePolicy.cs")
reward = read("PvpRewardService.cs")
events = read("PvpEventContract.cs")
plugin = read("ErenshorPvPPlugin.cs")

# World combat is open: normal third parties are admitted; protected actors are filtered narrowly.
for fn in ("DecideAggro", "DecideDamage", "DecideHeal"):
    require(re.search(rf"{fn}[\s\S]*AllowWorld", world), f"{fn} no longer admits world combat")
require("third_party_aggro" not in contain and "player_in_combat" not in controller,
        "isolation-era runtime abort returned")
require("sourceParticipant && noTarget" in world and "Do not proximity-block AE/PBAE starts" in world,
        "AE/PBAE world-combat admission missing")
require("simPlayer || ownedOrSummoned" in world,
        "local Sims/pets are being protected merely for NPC ownership")
require("invulnerable && knownFriendlyFaction" in world and "vendor || neverAggro || resourceObject" in world,
        "neutral classifier is broader than proven noncombat signals")
require("targetDefender && unknownPlayerProjectile" in world,
        "unattributed player projectile defender-side friendly-fire block missing")
require("targetDefender && sourceDefender" in world and "targetAttacker && sourceAttacker" in world,
        "known same-team harmful edges are not left to native friendly-fire semantics")

# Remote/network actors are an authority boundary, not ordinary local world Sims.
for token in ("ErenshorCoop.NetworkedPlayer", "ErenshorCoop.NetworkedSim", "IsNetworkOwnedActor"):
    require(token in compat, "COOP authority detection missing: " + token)
require("PvpCompatibility.IsNetworkOwnedActor(actor)" in contain,
        "network-owned actors are not protected in active world-combat filtering")
require(re.search(r"StartEncounter[\s\S]*?IsCoopSession\(\)", controller),
        "encounter start does not revalidate COOP after pending challenge")
require(re.search(r"ReleaseMatchAtGo[\s\S]*?IsCoopSession\(\)", controller),
        "GO does not revalidate COOP after countdown")
require("_nextAuthorityCheck" in controller and 'Despawn("coop_session_active")' in controller,
        "active encounter does not fail closed when network authority appears")
require("coop_session_active" in events and "ClassCancelled" in events,
        "COOP start rejection is not classified as cancellation")
require("ResolveNetworkTypes(true)" in compat and "ResolveNetworkTypes(false)" in compat,
        "COOP binding cache does not separate low-frequency refresh from hot interaction checks")

# Technical failure is not a competitive result. Only actual fight outcomes are allowed through.
record_method = re.search(r"ShouldRecordCompetitiveResult\(string reason\)[\s\S]*?\n        }", startup)
require(record_method is not None, "competitive-result policy missing")
body = record_method.group(0)
for token in ("proxy_death", "player_death", "player_fled", "retreat"):
    require(token in body, "competitive outcome missing from allow-list: " + token)
require("return !IsTechnicalFailure" not in startup,
        "negative technical-failure filter returned; unknown failures could become results")
require("history_credit=false" in factory and "winner = null" in factory,
        "technical terminal path can retain winner/history credit")
require("CanGrantVictoryReward(reason, winner != null)" in factory,
        "reward path is not guarded by explicit victory eligibility")
require("_lastClaimedMatchId" in reward and "already claimed" in reward,
        "exact-once reward claim barrier missing")

# Proxy lifecycle: native Start ownership retained, partial failure isolated, total failure fails closed.
require('[HarmonyPatch(typeof(NPC), "Start")]' in factory and "ObserveNativeNpcStartFailed" in factory,
        "native NPC.Start observation missing")
require("TryRecoverFaultedProxyStart" in factory and "RemoveAttacker(npc)" in factory and "RetireProxy(npc)" in factory,
        "single-proxy Start recovery/drop path missing")
require("if (remaining <= 0)" in factory and "_runtimeInvalidCleanupQueued = true" in factory,
        "complete proxy failure does not fail closed")
for forbidden in ("EnsureNativeBehaviorCoroutines", "InvokeCoroutineBody", "StopAllCoroutines("):
    require(forbidden not in factory, "obsolete/broad native behavior ownership returned: " + forbidden)
# 0.5.15 deliberately recovers only loops skipped by the current pre-GO NeverAggro Start path.
loop_policy = read("PvpNativeCombatLoopPolicy.cs")
require("ShouldStartMissingLoop" in loop_policy and "ownedStartCount == 0" in loop_policy,
        "0.5.15 missing-loop duplicate-prevention policy missing")
require(factory.count("StartCoroutine(loop)") == 1 and "TryStartOwnedNativeCombatLoop" in factory,
        "0.5.15 scoped owned-loop startup is not centralized")
require(factory.count("StopCoroutine(owned)") == 2 and "object.ReferenceEquals(current, owned)" in factory,
        "0.5.15 cleanup is not exact-iterator owned cleanup")
require("ModOwnedNavLoops" in factory and "ModOwnedBehaviorLoops" in factory and
        "ModOwnedNavLoopStarts" in factory and "ModOwnedBehaviorLoopStarts" in factory,
        "0.5.15 per-proxy loop ownership/start counters missing")
require("PvpNativeCombatLoopPolicy.RunSelfTests()" in controller,
        "0.5.15 loop ownership policy omitted from runtime selftest")
require("PrepareNativeStartProbe" in factory and "proxy_prepare_begin" in factory and "natural_start=deferred_to_go" in factory,
        "native Start is not prewarmed naturally before countdown")
require("AreAllProxiesReady" in factory and "PreGoReadinessComplete" in factory and "countdown_ready_barrier" in controller,
        "structural pre-GO readiness barrier missing")
require("AreAllProxiesReadyForGo" in factory and "EvaluateAllProxiesReady(false, out reason)" in factory and
        "PvpTemporaryCloneFactory.AreAllProxiesReadyForGo(out readiness)" in controller,
        "GO readiness is allowed to recover native loop infrastructure")

# Setup -> countdown -> GO -> repeat -> teardown lifecycle remains explicit and idempotent.
for token in ("PendingChallenge", "Preparing", "Countdown", "Active", "CleaningUp"):
    require(token in lifecycle, "lifecycle state missing: " + token)
require("GoTransitions" in lifecycle and "GoTransitions != 0) return false" in lifecycle,
        "GO exact-once transition guard missing")
require("EncounterCleaned" in controller and "TeamClones.Clear()" in factory and "TeamProfiles.Clear()" in factory,
        "terminal cleanup does not clear owned proxy state")
require("SceneTransition()" in controller and 'Despawn("scene_transition")' in controller,
        "scene/zoning teardown missing")
require("EnemyActors.Contains(current)" in contain and "pet.MyNPC.ForceAggroOn(null)" in contain,
        "defender-pet cleanup is not scoped to PvP-owned targets")
require("OnDestroy()" in plugin and "PvpController.Shutdown()" in plugin and "UnpatchSelf()" in plugin,
        "plugin-disable/hot-unload teardown missing")
require("PvpPanel.Dispose()" in controller and "PvpPanel.ReleaseDrag()" in controller,
        "retained UI ownership cleanup missing")

# No production IMGUI regression.
panel = read("PvpPanel.cs")
for token in ("OnGUI", "GUILayout", "GUI.Window"):
    require(token not in panel + plugin, "production retained-uGUI regression: " + token)

print(f"verify_pvp_stabilization_source: PASS ({checks} checks)")
