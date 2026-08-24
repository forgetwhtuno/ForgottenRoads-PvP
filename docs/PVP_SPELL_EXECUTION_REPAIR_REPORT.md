# Forgotten Roads PvP 0.5.12 — Spell / Healing Execution Repair

## Scope

This workstream modifies only `mods/Erenshor-PvP`. It starts from the supplied 0.5.11 ChatSource snapshot and preserves the working 0.5.11 native startup, NavMesh movement, shell-follow, melee, MMO world-combat, match lifecycle, rewards, cleanup, and Nemesis-facing public contracts.

## Root cause repaired

The latest live 3v3 proved that proxy AI reached `NPC.DoAttackSpell` repeatedly but never invoked `CastSpell.StartSpell`. Current-assembly inspection found that temporary non-Sim proxies inherit a random `NPCSpellCooldown` of 20–360 from `NPC.Start`, while `DoAttackSpell` returns immediately for non-Sims until that value reaches zero. A real SimPlayer bypasses this gate.

Native `SetAttackRanges` does decrement the field, so the correct repair is not to erase it forever. 0.5.12 clears the synthetic **initial** value once after a temporary proxy's native `NPC.Start` (or its existing recoverable Start-fault path). Later cooldowns created by real native casts are untouched.

## Attack spell execution changes

The old 0.5.11 telemetry observed entry into `DoAttackSpell` and a StartSpell-family Prefix. 0.5.12 separates the full path:

| Stage | Evidence |
|---|---|
| AI evaluated attack spell path | `spell_ai_entry` / `attack_spell_ai_entries` |
| broad prerequisite blocked | `assessment=<reason>` |
| concrete native request reached | `spell_native_request` |
| PvP containment rejected it | `spell_request_blocked_by_pvp_boundary` |
| native method returned false | `spell_native_result ... accepted=false` |
| native method returned true | `spell_native_result ... accepted=true` |
| cast ended | `cast_finished` / `spell_casts_finished` |
| native HP/heal effect observed | `spell_effect_events`, split attack/heal effect counters |

The actual bool returned by every current `StartSpell`-family overload is now threaded into the Postfix. `attack_spell_ai_entry` is deliberately not called a cast.

Diagnostics include the concrete spell ID/name, target, range, current distance, mana/cost, native cooldown state, and chosen request method. The current `DoAttackSpell` path has no explicit local LOS gate, so diagnostics state that instead of inventing one.

Each stage has its own bounded logging budget; repeated AI entries cannot exhaust the budget for later request/result/completion evidence.

## Healing execution changes

Native self-healing is preserved. `npc.NoSelfHeal` remains false and `NPC.CheckHeals` keeps ownership of self-heal behavior, including the current below-66% threshold.

The missing behavior was teammate healing: disposable proxies intentionally never join Erenshor's persistent `InGroup` / Sim tracking structures, while native ally-heal branches depend on those structures.

0.5.12 adds a narrow semantic bridge:

1. native `CheckHeals` runs first;
2. if it already requested a spell, PvP does nothing;
3. otherwise PvP looks only among the existing attacker-team `EnemyActors`;
4. self is excluded so native self-heal remains owner;
5. only a living ally below the current native 66% threshold is eligible;
6. lowest health-ratio ally is chosen;
7. spell must come from `MemmedHealSpells` and be beneficial;
8. current mana and range must pass;
9. PvP invokes native `CastSpell.StartSpell(selected, ally.MyStats)`;
10. native bool acceptance, cast completion, and actual HP restoration are observed normally.

No spell damage or healing is synthesized.

## World-combat policy preserved

The repair does not restore arena isolation. Existing policy remains:

- local Sims may naturally participate;
- party Sims may participate;
- pets/summons may participate;
- ordinary hostile mobs/existing enemies may join;
- legal AoE/heals continue;
- third-party activity does not itself cancel the encounter;
- only positively identified protected/noncombat/network-owned actors are filtered;
- cleanup owns only temporary-match state.

The new ally-heal bridge is intentionally narrower than native world healing: it exists only to model the temporary proxy team that cannot be represented through persistent Sim grouping. It never reaches outward to unrelated actors.

## Movement / melee regression boundary

No movement, NavMesh, shell-follow, native `NPC.Start`, melee execution, or melee damage algorithm was rewritten. Existing per-proxy native Start fault isolation remains intact.

## Combat presentation

The existing retained FIGHT panel now adds a small live block:

- `Your HP`
- combined `Opponents` HP + living attacker count
- `Damage dealt to attackers`
- `Damage taken by defenders`
- `Healing to attackers`
- `Healing to defenders`

These are intentionally truthful team/world-combat aggregate labels. Existing telemetry cannot always prove that every defender-side point came specifically from the human player because party Sims, pets, and world actors are allowed to participate.

## Nemesis/public API compatibility

No changes were made to the shapes of:

- `ErenshorPvpApi`
- `ErenshorPvpEvents`
- `RequestNemesisAmbush`
- `RecentResults`
- `MatchId` correlation/result classification

The existing Nemesis integration remains optional and runtime-bound.

## Version

`0.5.11` → `0.5.12`

Startup marker:

`revision=pvp-0.5.12-spell-execution-r1`

## Release status

This is a **live-QA candidate**, not a release-ready claim. Static/current-assembly evidence can prove the target surfaces and intended ownership boundaries, but only live Erenshor can prove visible native animation/effects and class-specific spell behavior for actual proxy profiles.
