# PvP 0.5.13 Native-Combat Convergence — Live QA

Use only a freshly built `0.5.13` DLL whose startup log contains:

`revision=pvp-0.5.13-native-combat-convergence-r1`

Do not use the stale incoming `build-output/ErenshorPvP.dll` as this candidate.

## Setup

1. Close Erenshor before installing a fresh candidate.
2. Build on the Windows workspace with current Erenshor/Lunaris references:
   `powershell -ExecutionPolicy Bypass -File .\BUILD_AND_INSTALL.ps1 -GameDir "<Erenshor>" -LunarisLibDir "<Lunaris refs>" -BuildOnly`
3. Record the candidate SHA-256, then rerun without `-BuildOnly` while the game is closed.
4. Launch, confirm version/revision, then run:
   - `/epvp on`
   - `/epvp selftest`
   - `/epvp validation on`
   - `/epvp diagnose`

## Ordered acceptance pass

1. **Preparation/countdown** — start arranged 1v1. Verify `Preparing opponents...` appears before 3/2/1. Every attacker must log `proxy_ready` and the barrier must say `native_start_pending=0` before countdown.
2. **Immediate GO** — at GO, verify release/target seed happens immediately. Record `first_target_acquired`, `first_movement`, `first_melee_decision`, `first_attack_spell_candidate`, and `first_native_spell_request` where applicable. No normal flow should begin `proxy_native_start action=native_pending` after GO.
3. **Player self-heal with enemy selected** — keep the attacker selected, damage the player, cast an ordinary direct self-heal. Confirm the selected opponent remains selected, target adaptation reports `current_target_preserved=true` when the narrow adaptation is used, and FIGHT `Healing to defenders` rises only if HP actually rises.
4. **Player self-buff** — with the enemy still selected, cast a `SelfOnly`, `ApplyToCaster`, or `InflictOnSelf` class buff. Confirm native cast succeeds and the effect appears on the player without changing selection.
5. **Player offensive spell** — cast a normal harmful spell at the attacker. Confirm it still resolves against the enemy and actual damage appears.
6. **Proxy offensive spell** — use a caster-capable opponent. Confirm `spell_native_request -> spell_native_result accepted=true -> spell_vessel_created -> spell_vessel_resolve -> effect` or actual damage/status state.
7. **Proxy self-heal** — reduce a heal-capable proxy below the native threshold (~66%). Confirm native self-heal reaches `HealMe` and real HP increases. An accepted cast alone is not success.
8. **Attacker teammate heal** — in 3v3, injure one attacker while another heal-capable attacker survives. Confirm `source=pvp_team_bridge`, native `StartSpell`, vessel resolve, `HealMe`, and actual teammate HP increase. Defender/unrelated actors must never be selected by the bridge.
9. **HoT/status** — exercise one HoT and one status/buff/debuff/CC where available. Require `status_effect_result` to report `application_proven` or `refresh_proven`; `no_observable_state_change` is not a successful effect.
10. **3v3** — verify mixed melee/caster/healer activity, player party Sims continue normal combat, cleanup/reward/result remain correct.
11. **5v5** — repeat with the largest team. Watch bounded diagnostics, movement/shell follow, native spell toolkit, and no preparation deadlock.
12. **Party Sims** — confirm player-party Sims can naturally attack/heal during PvP and are not converted to PvP proxies.
13. **Hostile world actor** — allow a normal hostile mob to enter combat. The match must remain active and world combat must continue normally.
14. **Second match without restart** — immediately start another encounter. Readiness, startup cooldown markers, vessel probes, target state, and first-action telemetry must all be fresh.
15. **Flee** — `/epvp flee`; confirm safe cleanup and no victory reward.
16. **Zone cleanup** — zone during or immediately after an encounter. Confirm no proxy, nav hold, vessel bridge state, readiness state, or stale target survives.
17. **FIGHT panel** — confirm HP/damage numbers move from actual native HP deltas. Healing must rise only from actual effective HP restoration, not accepted beneficial casts.
18. **Nemesis** — if installed and an ambush is eligible, use the current Nemesis flow and confirm PvP `MatchId`/result/event correlation and cleanup still work.
19. Finish with `/epvp validation off`.

## Evidence to save for reconciliation

For any failure, capture the bounded PvP log section containing the match ID plus:

- `proxy_prepare_begin`
- `proxy_native_start ... completion=completed`
- `proxy_ready`
- `countdown_ready_barrier`
- `match_go` / `go_release`
- first-action latency line
- relevant `spell_native_request/result`
- `spell_vessel_created`
- `spell_vessel_resolve`
- `healme_entered` / `heal_effect_applied`
- `status_effect_result`
- terminal `proxy_ability_summary` / balance result
