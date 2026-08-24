# PvP 0.5.12 — Exact Live Test Matrix

Run with the fresh 0.5.12 DLL built against the current installed Erenshor/Lunaris assemblies. Keep detailed validation on only for this test session.

## Setup

1. Close Erenshor before replacing the DLL.
2. Build/test/install with `BUILD_AND_INSTALL.ps1`.
3. Launch Erenshor and confirm the log startup line says `Erenshor PvP 0.5.12` and `revision=pvp-0.5.12-spell-execution-r1`.
4. Enter a normal combat-capable zone with room for the proxy formation.
5. Run `/epvp validation on`.
6. Run `/epvp on`.
7. Run `/epvp selftest`. Every returned component should begin `PASS`.
8. Run `/epvp diagnose`; confirm runtime hooks/spawn surface are healthy before forcing a match.

## 1. 1v1 caster — attack request boundary

1. Run `/epvp force arranged 1`.
2. Inspect the offered opponent. Accept with the panel or `/epvp accept`.
3. If the selected profile has no admitted offensive spell in the bounded runtime diagnostic, finish/refuse/despawn it and repeat; do not call a pure-melee profile a failed caster test.
4. After GO, keep enough distance/time for a spell-capable proxy to exercise its native spell AI.
5. Expected startup evidence: `proxy_spell_startup_gate ... result=cleared_synthetic_startup_npc_spell_cooldown` when the inherited initial gate was positive.
6. Expected attack chain for at least one appropriate proxy:
   - `spell_ai_entry`
   - `spell_native_request` with a concrete `spell` + `spell_id` + target/range/distance/mana
   - `spell_native_result ... accepted=true`
   - `cast_finished`
   - native damage/effect evidence (`attack_spell_effects>0` / `spell_effect_events>0`) when the spell actually lands.
7. If it stops, classify the exact boundary from the log instead of repeating the old `attack_spell_decision` counter.

## 2. Healthy native Sim comparison

During a PvP match where a normal loaded SimPlayer casts successfully, look for the one-time line:

`healthy_native_sim_cast accepted=true`

Compare its caster/spell/target/mana/cooldown/animator/weapon fields with the proxy's `spell_native_request`/result. Absence merely means no loaded real Sim completed a cast during that match; it is not a PvP failure.

## 3. Self-heal

1. Force a 1v1 with a profile whose runtime summary reports `heal_spells>0` and `memmed_heals>0`.
2. Damage that opponent below about 66% HP but do not kill it.
3. Allow it to act without continuous burst damage.
4. Expected native chain: heal check → beneficial `spell_native_request` → accepted result → cast finish → actual HP increase.
5. Confirm `healing_done` / `heal_effects` becomes non-zero when the heal lands.

## 4. Allied heal — explicit bridge test

1. Run `/epvp force arranged 3`, then `/epvp accept`.
2. Confirm at least one attacker is heal-capable from the runtime/terminal diagnostics.
3. Damage a **different attacker** below roughly 66% HP while leaving the healer alive.
4. Avoid killing the injured ally immediately.
5. Expected bridge evidence:
   - `heal_spell_selected ... source=pvp_team_bridge`
   - target is another `attacker:` actor, never a defender/world-neutral
   - `spell_native_request ... beneficial=True`
   - `spell_native_result ... accepted=true`
   - `cast_finished ... effect_observed=true`
   - the injured attacker's visible/current HP rises.
6. The terminal `proxy_ability_summary` should show `heal_legal_allies`, `heal_bridge_requests`, `heal_bridge_accepted`, `heal_native_accepted`, and `heal_effects` coherently.

## 5. 3v3 full regression

1. `/epvp force arranged 3`
2. `/epvp accept`
3. Confirm all surviving proxies complete/qualify through the existing native Start and runtime checks.
4. Confirm root movement, shell-follow and melee still occur.
5. Confirm spell-capable profiles now progress past AI entry into concrete native requests.
6. Confirm healer-capable profiles can self-heal or heal an injured attacker when conditions exist.
7. Finish the match and inspect `balance_summary` plus each `proxy_ability_summary`.

## 6. 5v5

Repeat using `/epvp force arranged 5`. Verify per-proxy fault isolation, movement, melee, spell/heal counters, cleanup, and bounded log volume.

## 7. Melee regression

In any forced match, deliberately close to melee range. Confirm `melee_attempt`, `melee_damage>0` or equivalent existing damage evidence still appears and the proxy physically moves as before.

## 8. World mob joins

1. Start a PvP encounter near an ordinary combat-capable hostile without putting a protected vendor/quest actor in danger.
2. Let the hostile acquire or retain native combat naturally.
3. Confirm the PvP match is **not** cancelled merely because the mob/Sims/pets participate.
4. Confirm protected actors remain narrowly filtered if positively identified.

## 9. Second match without restart

After the first match cleans up, run another `/epvp force arranged 1` (or 3), accept, and complete it without restarting the game. Confirm fresh startup-gate evidence, fresh counters, one GO, and no stale proxy/cast state.

## 10. Flee

During an active match run `/epvp flee`. Confirm escape classification, no victory reward, one cleanup, and no lingering cast/proxy state.

## 11. Zone cleanup

Start a match, then cross a normal zone boundary when the game allows it. Confirm the encounter is cleaned rather than resumed in the new scene and no stale proxy, cast probe, or heal-team state survives.

## 12. Nemesis-triggered PvP (only when current Nemesis is installed/configured)

1. Confirm `/enemesis status` has a current rival.
2. Enable PvP and enter an eligible wild ambush zone.
3. When out of combat and in a clear area, run `/enemesis ambush`.
4. If blocked, use `/enemesis diagnose` and `/epvp diagnose`; do not bypass either mod's eligibility rules.
5. On a successful request, confirm normal PvP `MatchId`/event correlation, spell/heal behavior, terminal result, and `/enemesis history` update.

## 13. Fight-panel damage feedback

During 1v1 and team combat open the PvP FIGHT tab. Confirm it updates without a giant parser window and shows:

- player HP;
- combined opponent HP/living count;
- damage to attackers;
- damage to defenders;
- healing to attackers;
- healing to defenders.

Because world combat is open, these are team aggregate labels by design.

## 14. End session

Run `/epvp validation off` after collecting the acceptance log.
