# Forgotten Roads PvP 0.5.11 deep stabilization audit

**Scope:** `forgetwhtuno/ForgottenRoads-PvP` only. This audit supersedes the old 0.4.0 arena/isolation-era completion language. It deliberately separates deterministic/static evidence from behavior that still requires Erenshor live QA.

## Starting state

- Source version: **0.5.11**.
- Starting local branch: `codex/forgotten-roads-consistency-20260815`.
- Starting HEAD: `3847b8f810170c0753cde4f9a28cf15069a999fa` (`PvP 0.5.10: isolate per-proxy native Start faults`).
- The supplied working tree already contained uncommitted 0.5.11 changes. This workstream preserved them and did not reset, commit, push, merge, create a PR, or publish a release.
- Current assembly evidence used by the portable verifier: `Assembly-CSharp.dll` SHA-256 `B840CB8076ED0553F7DC3BEB4042ABA653917882F763181EC0D2C13C26C17847`.

## World-combat contract

Active PvP is **ordinary Erenshor world combat**, not a sealed duel bubble.

Allowed to expand the live combat graph without ending PvP:

- local Sims and party Sims;
- player-/Sim-owned pets and summons when native ownership permits them to fight;
- hostile mobs, existing enemies, and unrelated combat-capable world actors;
- naturally acquired aggro;
- legitimate damage, healing, buffs/debuffs, and AoE/PBAE interactions.

PvP intervenes only for its own team legality/ownership or when current state positively proves an actor needs protection. The protected-native classifier is intentionally narrow: vendor, `NeverAggro`, and resource-object evidence qualifies; invulnerability qualifies only together with a known friendly non-Sim faction. Friendly faction or temporary invulnerability by itself is not treated as proof of noncombat.

Network-owned COOP actors are not classified as neutral NPCs; they are an **authority boundary**. Current `ErenshorCoop.NetworkedPlayer` / `ErenshorCoop.NetworkedSim` components are detected by full names and cached. Local PvP fails closed if network authority is active when the match starts, appears during countdown, or appears during an active fight.

## Root causes repaired in this pass

1. **Unattributed player projectile friendly fire.** `unknownPlayerProjectile` was used to let a projectile hit a PvP attacker, but the same source was not treated as defender-side when its target was another defender. The effect could therefore fall through to `AllowWorld`. `DecideDamage` now uses the same `playerSide` identity for both valid opponent damage and same-side rejection.
2. **Overbroad protected-actor classification.** Friendly faction or invulnerability alone could exclude actors that are still legitimate combat-capable world participants. The policy now protects only stronger noncombat evidence.
3. **Technical failure could become a competitive result.** The old policy effectively treated anything not recognized by a narrow technical-failure list as competitive. Competitive completion is now a positive exact allow-list: `proxy_death`, `player_death`, `player_fled`, `retreat`. Rewards remain exact `proxy_death` with a winner.
4. **Current COOP type names could be missed.** The prior unqualified type lookup did not reliably resolve current namespaced COOP types. Full names are now bound, with a constrained fallback scan inside ErenshorCoop assemblies. Hot damage/aggro checks reuse that binding instead of scanning assemblies per effect.
5. **Network authority could change after offer-time validation.** Authority is now rechecked at the mutation boundary, immediately before GO, and periodically while a team is active.
6. **Cleanup over-owned defender pets.** Ending PvP previously cleared pet aggro regardless of what the pet was currently fighting. Cleanup now clears only a target that is one of the temporary PvP attackers; a legitimate world-mob target is preserved.

## Proxy/native lifecycle finding

The 0.5.10 native-Start repair remains intact. PvP still lets native `NPC.Start` own initialization, NavMesh/behavior coroutine launch, and class AI. If one proxy faults in native Start, the fault handler reasserts only PvP-owned transient state and verifies native lifecycle evidence. A recoverable proxy remains; an unrecoverable proxy is removed from the attacker set and retired alone. The encounter becomes `runtime_invalid` only when no attacker remains.

Before GO, team construction/preparation still fails closed when the requested team cannot be constructed safely or an attacker cannot meet required startup/reward invariants. That is intentional: silently changing the accepted attacker roster before the fight begins is different from losing one already-created proxy to an isolated runtime Start failure.

## Current assembly surface verification

`tests/verify_current_assembly_surface.py` parses CLR metadata directly and verifies the method/overload counts the current Harmony surface depends on. The current supplied assembly passed 24 target groups, including:

- `NPC.Start`, `UpdateNav`, `HandleMaintenaceAndCounters`, `Update`, `Combat`, `PerformMeleeHit`, attack/heal decision methods, `AggroOn`, `ForceAggroOn`, `ManageAggro`;
- `Character.DamageMe`, `MagicDamageMe`, `BleedDamageMe`, `DoDeath`;
- `Stats.ReduceHP`, both `HealMe` overload shapes;
- `CastSpell.StartSpell` overload shapes, `StartSpellFromProc`, `StartSpellNoAnim`;
- `TypeText.CheckCommands` and `SimPlayer.LoadAllSimData`.

This verifies that the expected managed method surfaces exist in that exact assembly. It does **not** prove live semantic behavior.

## Deterministic/static evidence actually run in this workstream

- `python3 tests/verify_current_assembly_surface.py` — **PASS** against the assembly hash above; 24 target groups verified.
- `python3 tests/verify_pvp_stabilization_source.py` — **PASS**. Guards world-combat admission, narrow neutral protection, player-projectile friendly fire, network authority boundaries, result/reward allow-lists, per-proxy Start isolation, native coroutine/NavMesh ownership, repeated lifecycle cleanup, zoning, hot-unload, pet target cleanup, and retained-uGUI ownership.
- `python3 tests/verify_retained_ui_source.py` — **PASS**.
- `tests/RUN_UI_TESTS.ps1` — **NOT RUN in this sandbox** because PowerShell and a C# compiler are unavailable. Its C# policy executable therefore must not be reported as passing here.
- Full DLL build — **NOT RUN in this sandbox** because no `dotnet`, MSBuild, Mono/C# compiler, or PowerShell toolchain is installed. The supplied assemblies were inspected, but source compilation still needs the normal Windows/Erenshor build environment.

## Focused live QA matrix still required

1. Arranged 1v1 clean start: challenge -> accept -> 3/2/1 -> one GO; verify real native targeting/movement/damage/death.
2. Repeat at least three matches without zoning; verify no stale target, proxy, spell clone, lifecycle state, reward claim, or UI state leaks into the next match.
3. 2–5 attacker matches, including one proxy that exercises the known per-proxy native-Start recovery/drop path; surviving proxies must continue. Force/observe complete proxy failure separately and confirm invalid/no reward/no competitive history.
4. External hostile mob joins and attacks a defender; PvP must continue.
5. External hostile/world actor attacks a PvP attacker; PvP must continue.
6. Outside local Sim / party Sim joins combat; PvP must continue.
7. Player-/Sim-owned pet participates, switches from PvP attacker to a legitimate world mob, then PvP ends; cleanup must preserve the world-mob target.
8. Legitimate outside healer heals a defender and, where native game rules permit, another world actor; PvP must continue and no protected-target false positive should occur.
9. Offensive AoE/PBAE hits an ordinary combat-capable outside actor; native combat may expand and PvP must continue.
10. Same AoE reaches a proven protected vendor/`NeverAggro`/resource actor; only that target/effect is excluded and the match remains active.
11. Friendly combat-capable guard/healer and an invulnerable-but-hostile/phase actor: verify neither is excluded solely by friendly faction or invulnerability.
12. Player projectile with no resolvable source actor: opponent hit remains legal, same-side defender hit is rejected.
13. Normal player defeat: native death/respawn consequences occur; result is a loss, never a technical failure or reward.
14. Proxy-team defeat: result is a win only after all active attackers are defeated; reward is granted at most once.
15. Flee and low-health retreat: classified competitively as escape/retreat according to existing record rules, with no victory reward.
16. Inject/observe `fight_state_failed` / `runtime_invalid` / native-nav complete failure: no winner, XP, gold, win credit, or history credit.
17. Scene transition during pending offer, countdown, and active combat: all owned proxy/spell/UI/target state tears down without touching unrelated world combat ownership.
18. Hot-disable/reload through Lunaris during pending/countdown/active states: no stale Harmony state, event subscription, panel, coroutine-owned mod state, or temporary actor remains.
19. COOP remote human/networked Sim present before offer acceptance, connecting during countdown, and appearing during active combat: local PvP fails closed as noncompetitive and never mutates the network-owned actor.
20. Retained-uGUI challenge/fight/failure state and repeated open/close/reload behavior remain clear while all combat tests above run.

## Remaining uncertainty

- Static metadata proves method presence/overload shape, not that every Harmony prefix/finalizer sees the exact semantic event expected at runtime. Live capture remains mandatory after game updates.
- World-combat expansion deliberately allows outside actors to help kill PvP attackers. Current reward semantics are **match victory**, not a separate kill-credit/contribution system. The bounded balance/per-proxy diagnostics measure combat outcome, but do not yet assign a percentage of victory credit to each outside world actor. Do not silently add a new kill-credit rule without a product decision and live evidence.
- COOP remains intentionally unsupported for active local PvP. The pass improves exclusion/teardown; it does not implement networked PvP.
- Native Start/NavMesh/class-specific behavior is deliberately owned by Erenshor. The next meaningful confidence increase must come from focused live QA, not more speculative replacement AI logic.

## Assessment

The source is now internally consistent with the world-combat design and has materially stronger failure/result/authority/cleanup boundaries. On the evidence available here, the appropriate next step is **focused live QA**, not another broad architectural rewrite. This is not a release-readiness claim until the C# suite/build and the live matrix above are completed.
