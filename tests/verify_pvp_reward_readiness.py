#!/usr/bin/env python3
"""Validate PvP 0.5.19 reward-state forensics against current source + Assembly-CSharp.

This is deliberately standard-library-only. It proves the native Start/death fields that justify the
post-Start normalization, and source-level ownership/readiness constraints that must not degrade into
an unconditional readiness boolean.
"""
from __future__ import annotations

import argparse
import importlib.util
import re
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
LOOP_VALIDATOR = ROOT / "tests" / "verify_pvp_combat_loop_lifecycle.py"


def load_loop_validator():
    spec = importlib.util.spec_from_file_location("pvp_loop_validator", LOOP_VALIDATOR)
    mod = importlib.util.module_from_spec(spec)
    assert spec.loader is not None
    spec.loader.exec_module(mod)
    return mod


def read(name: str) -> str:
    return (ROOT / name).read_text(encoding="utf-8")


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--assembly", help="Path to current Assembly-CSharp.dll")
    args = parser.parse_args()
    loop = load_loop_validator()
    assembly = loop.locate_assembly(args.assembly)
    methods, fields, body_for = loop.parse_surface(assembly)

    factory = read("src/PvpTemporaryCloneFactory.cs")
    policy = read("src/PvpProxyStartupPolicy.cs")
    startup = read("src/PvpCombatStartupPolicy.cs")
    plugin = read("src/ErenshorPvPPlugin.cs")
    csproj = read("ErenshorPvP.csproj")
    readme = read("README.md")
    changelog = read("CHANGELOG.md")
    handoff = read("HANDOFF.md")
    live_test = read("LIVE_TEST.md") if (ROOT / "LIVE_TEST.md").is_file() else ""
    findings = read("docs/PVP_REWARD_READINESS_FINDINGS.md") if (ROOT / "docs/PVP_REWARD_READINESS_FINDINGS.md").is_file() else ""

    checks: list[str] = []

    def check(condition: bool, label: str) -> None:
        if not condition:
            raise AssertionError(label)
        checks.append(label)

    def method(type_name: str, name: str, params: int):
        value = methods.get((type_name, name, params))
        check(value is not None, f"current assembly exposes {type_name}.{name}/{params}")
        assert value is not None
        token, rva = value
        return token, body_for(rva)

    def field(type_name: str, name: str):
        value = fields.get((type_name, name))
        check(value is not None, f"current assembly exposes field {type_name}.{name}")
        assert value is not None
        return value

    # Version/revision surfaces.
    check('LunarisPlugin("forgetwhtuno.erenshor.pvp", "0.5.34"' in plugin, "plugin version is 0.5.34")
    check('revision=pvp-0.5.34-preop-world-context-r1' in plugin, "runtime revision marker is pre-opportunity world context r1")
    check('<Version>0.5.34</Version>' in csproj and '<AssemblyVersion>0.5.34.0</AssemblyVersion>' in csproj and '<FileVersion>0.5.34.0</FileVersion>' in csproj, "csproj version metadata is 0.5.34")

    # Application/proof must be real current state, not a boolean override.
    check('RewardSuppressionReady = true' not in factory, "readiness is never forced true")
    check('state.RewardSuppressionReady = characterStartCompleted && VerifyRewardSuppression(member, out reward)' in factory, "readiness requires Character.Start completion and authoritative current reward proof")
    check('bool verified = VerifyRewardSuppression(member, out rewardReason)' in factory, "countdown hold rechecks authoritative current reward proof")
    check('if (!VerifyRewardSuppression(member, out rewardInvariant))' in factory, "post-apply preparation uses authoritative current reward proof")
    for phase in ('"post_apply"', '"ready"', '"countdown_hold"'):
        check('LogRewardStateSnapshot' in factory and phase in factory, f"bounded snapshot records {phase} state")
    check('NativeCharacterStartCompleted' in factory and 'NativeStartCompleted' in factory, "both native Start lifecycles are tracked")
    check('[HarmonyPatch(typeof(Character), "Start")]' in factory and 'ObserveNativeCharacterStartCompleted(__instance)' in factory, "Character.Start completion is observed narrowly")
    check('[HarmonyPatch(typeof(NPC), "Start")]\n    internal static class PvpTemporaryNpcStartPatch' in factory, "NPC.Start patch is registered on its own patch class")
    check('FinalizePostStartRewardSuppression' in factory and 'if (!NativeCharacterStartCompleted.Contains(id)) return false;' in factory and 'VerifyRewardSuppression(go, out reason)' in factory, "Character.Start finalization reasserts and shares authoritative reward proof without deferred NPC.Start gating")
    check('RewardSuppressionProofs' in factory and 'ActorInstanceId' in factory and 'LootInstanceId' in factory, "reward proof stores current object identities")
    proof_class = re.search(r'private sealed class RewardSuppressionProof([\s\S]*?)\n        }', factory)
    check(proof_class is not None and 'Character ' not in proof_class.group(1) and 'LootTable ' not in proof_class.group(1), "reward proof caches IDs, not stale Unity references")
    check('proof.ActorInstanceId == actor.GetInstanceID()' in factory and 'proof.LootInstanceId == (loot == null ? 0 : loot.GetInstanceID())' in factory, "readiness rejects stale actor/loot identities")
    check('RewardSuppressionProofPasses' in policy, "pure reward-proof policy exists")
    check('boundarySuppressed && proofVerified' in policy and 'actorIdentityMatches && lootIdentityMatches' in policy and 'CountdownHoldAccepts' in policy, "proof requires actual current suppression, identity match, and shared countdown acceptance")
    check('reward_suppression_state proxy=' in factory and 'applied=' in factory and 'verified=' in factory and 'actor_id=' in factory and 'loot_id=' in factory, "bounded per-proxy reward diagnostic exists")
    check('RewardSuppressionStateLogged.Add(actor.gameObject.GetInstanceID())' in factory and 'RewardHoldCheckLogged.Add(id)' in factory, "reward/hold diagnostics are bounded per proxy")
    check('RewardSuppressionDetail' in factory and 'reward_suppression_pending:' in factory, "readiness carries bounded current reward-proof failure detail")
    check('PASS pvp spawn/reward readiness' in factory and 'RewardSuppressionProofPasses' in factory, "/epvp selftest retains reward proof lifecycle coverage")

    # Suppression covers the current native reward surface.
    for token in ('TrySetCharacterXp(actor, 0)', 'actor.NeverTrack = true', 'actor.BossXp = 0f', 'actor.QuestCompleteOnDeath = null', 'actor.factionMods = new ModifyFaction[0]'):
        check(token in factory, f"suppression applies {token}")
    check('TryReadCharacterXp' in factory and 'xpReadable ? xp.ToString() : "unreadable"' in factory, "XP proof distinguishes unreadable from an actual value")
    check('xpReadableAndZero && neverTrack' in policy, "XP reward safety requires readable zero XP and NeverTrack")
    for token in ('loot.MinGold = 0', 'loot.MaxGold = 0', 'loot.MyGold = 0', 'ClearBorrowedLootCollection(loot, "ActualDrops")', 'ClearBorrowedLootCollection(loot, "ActualDropsQual")', 'loot.enabled = false'):
        check(token in factory, f"suppression applies {token}")
    check('TrySetField(npc, "SetAchievementOnDefeat", string.Empty)' in factory, "borrowed achievement credit is cleared")
    check('lootDropsEmpty = BorrowedLootCollectionsEmpty(loot)' in factory and 'achievementCreditClear' in factory, "readiness verifies drops and achievement credit")
    check('[HarmonyPatch(typeof(Character), "DoDeath")]' in factory and 'SuppressBorrowedDeathRewards(__instance)' in factory, "death prefix reasserts suppression")
    check('[HarmonyPatch(typeof(Character), "DoWorldEventCredit")]' in factory and 'return !PvpTemporaryCloneFactory.IsTemporaryActor(__instance);' in factory, "world-event/guild credit is blocked only for temporary proxies")
    check(factory.count('return !PvpTemporaryCloneFactory.IsTemporaryActor(__instance);') == 1, "ordinary Character world-event credit remains native")

    # Technical failure/PvP reward contract remains separate from borrowed PvE rewards.
    check('winnerPresent && string.Equals(reason ?? string.Empty, "proxy_death", StringComparison.Ordinal)' in startup, "PvP victory reward remains exact proxy-death + winner")
    check('CanGrantVictoryReward(reason, winner != null)' in factory, "terminal path still gates PvP reward through startup policy")

    # Cleanup/second-match proof state.
    check('RewardStateSnapshotLogged.Clear();' in factory, "match cleanup resets bounded reward snapshots")
    check('RewardSuppressionProofs.Remove(member.GetInstanceID());' in factory and 'RewardSuppressionStateLogged.Remove(member.GetInstanceID());' in factory and 'RewardHoldCheckLogged.Remove(member.GetInstanceID());' in factory, "proxy retirement clears reward proof state")
    check('NativeCharacterStartCompleted.Clear();' not in factory[factory.index('internal static bool PrepareForCountdown'):factory.index('internal static bool MaintainPreparationHold')], "preparation re-entry does not erase completed Start proof")
    countdown = factory[factory.index('internal static bool MaintainCountdownHold'):factory.index('internal static bool AreAllProxiesReadyForGo')]
    check('SuppressBorrowedDeathRewards' not in countdown and 'VerifyRewardSuppression' in countdown, "countdown hold verifies reward state without per-frame suppression")
    probe_slice = factory[factory.index('internal static void PrepareNativeStartProbe'):factory.index('internal static void ObserveNativeNavEntered')]
    check('NativeCharacterStartCompleted.Remove(id);' in probe_slice and 'RewardSuppressionProofs.Remove(id);' in probe_slice, "fresh native Start probe clears only that proxy reward proof")

    # Current Assembly-CSharp lifecycle/death facts.
    _, char_start = method("Character", "Start", 0)
    _, npc_start = method("NPC", "Start", 0)
    _, death = method("Character", "DoDeath", 0)
    _, loot_start = method("LootTable", "Start", 0)
    _, loot_init = method("LootTable", "InitLootTable", 0)
    _, world_credit = method("Character", "DoWorldEventCredit", 1)

    xp = field("Character", "xp")
    boss_xp = field("Character", "BossXp")
    quest = field("Character", "QuestCompleteOnDeath")
    factions = field("Character", "factionMods")
    my_loot = field("Character", "MyLootTable")
    my_gold = field("LootTable", "MyGold")
    actual_drops = field("LootTable", "ActualDrops")
    achievement = field("NPC", "SetAchievementOnDefeat")

    check(loop.contains_field(char_start, 0x7D, xp), "Character.Start writes xp")
    check(loop.contains_field(char_start, 0x7D, factions), "Character.Start writes factionMods")
    check(loop.contains_field(char_start, 0x7D, my_loot), "Character.Start rewires MyLootTable")
    check(loop.contains_field(npc_start, 0x7D, boss_xp), "NPC.Start writes BossXp")
    for token, label in ((xp, "xp"), (boss_xp, "BossXp"), (quest, "QuestCompleteOnDeath"), (factions, "factionMods"), (my_gold, "LootTable.MyGold"), (actual_drops, "LootTable.ActualDrops"), (achievement, "NPC.SetAchievementOnDefeat")):
        check(loop.contains_field(death, 0x7B, token), f"Character.DoDeath reads {label}")

    add_xp, _ = method("GameData", "AddExperience", 2)
    finish_quest, _ = method("GameData", "FinishQuest", 1)
    world_credit_method, _ = method("Character", "DoWorldEventCredit", 1)
    check(loop.contains_call(death, add_xp), "Character.DoDeath reaches GameData.AddExperience")
    check(loop.contains_call(death, finish_quest), "Character.DoDeath reaches GameData.FinishQuest")
    check(loop.contains_call(death, world_credit_method), "Character.DoDeath reaches DoWorldEventCredit")
    loot_init_token, _ = method("LootTable", "InitLootTable", 0)
    check(loop.contains_call(loot_start, loot_init_token), "LootTable.Start reaches InitLootTable")
    check(loop.contains_field(loot_init, 0x7D, my_gold), "LootTable.InitLootTable can write MyGold")
    guild_single, _ = method("GuildManager", "CreditSingleForGodTarget", 1)
    guild_group, _ = method("GuildManager", "CreditForGodTarget", 1)
    check(loop.contains_call(world_credit, guild_single) and loop.contains_call(world_credit, guild_group), "DoWorldEventCredit reaches guild/world-event credit")

    print(f"PVP REWARD READINESS VALIDATOR PASS checks={len(checks)}")
    print(f"Assembly-CSharp SHA256={__import__('hashlib').sha256(assembly.read_bytes()).hexdigest().upper()}")


if __name__ == "__main__":
    main()
