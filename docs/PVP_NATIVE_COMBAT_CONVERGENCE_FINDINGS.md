# PvP 0.5.13 Native-Combat Convergence Findings

## Scope and evidence

This document records the 0.5.13 convergence pass for Forgotten Roads PvP. It is based on the current PvP source, the current read-only Practice Duel source, and the supplied current managed game references.

- PvP version: `0.5.13`
- Runtime revision: `pvp-0.5.13-native-combat-convergence-r1`
- `Assembly-CSharp.dll` SHA-256: `B840CB8076ED0553F7DC3BEB4042ABA653917882F763181EC0D2C13C26C17847`
- `Lunaris.dll` SHA-256: `5A70F3D1FD9441CEAE6D8E1F80CAFCE86FF2A47245FBCFA36BFCF8E88FD20B29`

Relevant current native surfaces verified by the portable metadata validator include `NPC.Start`, `NPC.HandleMaintenaceAndCounters`, `NPC.DoAttackSpell`, `NPC.CheckHeals`, the six bool-returning `CastSpell.StartSpell`-family overloads, `SpellVessel.FixedUpdate`, `SpellVessel.CreateSpellChargeEffect`, `SpellVessel.ResolveSpell`, both `Stats.HealMe` overloads, the current `Stats.AddStatusEffect` overloads, `Stats.AddStatusEffectNoChecks`, and the current `Stats.StatusEffects` backing field.

## Native / Practice Duel / PvP comparison

| Behavior | Native / Practice Duel | PvP 0.5.12 | PvP 0.5.13 |
|---|---|---|---|
| player self-heal with enemy selected | Current Duel proves the `Stats` argument is not final target truth. A narrow ordinary single-target Heal may adapt opponent `Stats` to caster `Stats`; native `StartSpell` still owns the cast. | Beneficial cast was judged against the selected attacker and could be rejected as cross-team healing. | Uses the same narrow direct-StartSpell adaptation. `PlayerControl.CurrentTarget` is not changed. |
| self-buff | `SelfOnly`, `ApplyToCaster`, and `InflictOnSelf` are spell-declared caster semantics even when another actor is selected. | Passed `Stats` could incorrectly drive PvP team legality. | Declared self semantics authorize against the caster without rewriting persistent target state. |
| offensive spell | Native `StartSpell` owns mana/range/cooldown/casting/effects. | 0.5.12 restored the request boundary and bool-result diagnostics. | Preserved; harmful target remains the selected attacker/world target subject to native/PvP safety edges. |
| ally heal | Native Sims use native group/Sim membership. | Temporary non-Sim proxies had a bounded attacker-team target bridge because they intentionally do not join persistent Sim groups. | Bridge retained but only chooses actual HP-heal/HoT spells from native `MemmedHealSpells` and living current attacker allies. |
| beneficial target legality | Self and legal allies are allowed; enemy healing is not ordinary combat behavior. | Broad passed-target check could reject legitimate self semantics. | Self/ally semantics are admitted; cross-team beneficial edges remain blocked. |
| harmful target legality | Native friendly-fire/per-target rules remain authoritative. | Some same-team protection was broader than necessary. | Same-team harmful starts are admitted to native rules; positively known protected neutral actors remain blocked. |
| neutral protection | Native world combat is not a sealed duel. | 0.5.12 already preserved MMO world expansion. | Preserved. Vendors, `NeverAggro`, resource objects, and positively proven neutral/noncombat actors are guarded; ordinary Sims/pets/hostiles/world actors remain eligible. |
| `NPC.Start` lifecycle | Unity invokes Start naturally. | GO could occur while proxy Start/runtime preparation was still pending. | Unity Start runs during inert preparation before countdown. No manual `NPC.Start()` call exists. |
| proxy preparation | Native actors initialize before normal combat. | Expensive/native readiness work could begin after GO. | Explicit per-proxy readiness barrier covers native Start, runtime links, stats, caster/spells, NavMesh, shell, reward suppression, startup cooldown normalization, attack-startup maintenance, and target resolvability. |
| visible countdown | Should represent already-prepared combatants. | Countdown could precede actual proxy readiness. | Arranged countdown starts only after every admitted proxy is ready. |
| GO semantics | Combat is live when aggression/targets are released. | GO could be followed by `native_start_pending`. | GO is release-only: recheck readiness, release `NeverAggro`/NavMesh hold, seed legal targets, and run native combat. |
| `NPCSpellCooldown` | Current non-Sim `NPC.Start` creates a startup gate; later values are legitimate native cadence. | 0.5.12 cleared the synthetic startup value once. | One-time startup normalization preserved; later native cooldown values are untouched. |
| `atkSpellDelay` | Native maintenance counter; current code drains it at roughly `60 * deltaTime`. | A live value such as `19.06` was easy to misread as seconds. | Never forced to zero. Preparation waits for the startup counter to drain naturally. |
| `SpellVessel` interruption | Current `FixedUpdate` has a Sim-vs-non-Sim interrupt distinction. | Temporary proxies are intentionally `SimPlayer=false`; an accepted cast could end before native resolution. | A proxy-only compatibility bridge temporarily changes only `interruptable` for a live valid current proxy vessel and restores it in a Harmony Finalizer. |
| `ResolveSpell` | Native effect resolution. | Accepted/finished cast did not prove effect resolution. | Observed directly; never replaced. |
| `HealMe` | Native HP mutation. | Accepted beneficial/heal cast could still show zero effective healing. | Entry and real HP before/after are observed. Effective healing is `max(0, HP_after - HP_before)`. No direct HP write exists in the bridge. |
| HoT/status application | Native `Stats.StatusEffects` is the resulting state. | Effect-entry telemetry could not prove application. | Snapshot-before/after proves new/replaced application, refresh by increased duration, or no observable state change. |
| world participation | Ordinary native world combat can expand. | World-combat policy already repaired in 0.5.11/0.5.12. | Preserved; Practice Duel is semantic reference only, not arena policy. |
| cleanup | Temporary state only. | Existing match/reward/Nemesis cleanup was working. | Readiness, startup-normalization, vessel/effect probes, and first-action state are cleared with existing temporary match state. |

## Player self-target root cause and repair

Current Practice Duel and current Erenshor behavior establish that the `Stats` argument entering `CastSpell.StartSpell` is not necessarily the actor that the spell ultimately affects. Player hotbar flow can hand the selected opponent into `StartSpell` while the `Spell` asset itself declares caster application.

PvP 0.5.12 evaluated `beneficial + target=attacker` too early and could reject a legitimate player self-benefit before native self semantics resolved it.

0.5.13 uses two narrow rules:

1. `SelfOnly || ApplyToCaster || InflictOnSelf` means the legality check treats the caster as the semantic self target while preserving the original native argument.
2. For the exact ordinary single-target healing shape proven in current Practice Duel, only the direct `StartSpell` argument is rewritten from the selected PvP opponent's `Stats` to the caster's own `Stats`. Group/AE/PBAE/pet/charm/proc/mixed-damage shapes are not adapted.

`PlayerControl.CurrentTarget` is never assigned by this repair.

## Spell semantic policy

PvP no longer equates `Beneficial` with healing. The current semantic model tracks:

- `DirectHpHeal`
- `HealOverTime`
- `BeneficialBuff`
- `SelfUtility`
- `HarmfulDamage`
- `OffensiveDebuff`
- `CrowdControl`
- `Area` (plus an independent area facet for damaging/healing categories)

Temporary proxy loadout admission still requires the native `SimUsable` flag, class eligibility, level eligibility, and the existing profile acquisition rules. The convergence layer excludes proven persistent/world-ownership escape shapes: pet/summon creation and charm. It keeps normal Sim-usable combat damage, DoT/debuff/CC, direct heal/HoT, buff/self-buff, and structurally ordinary area combat abilities. This is a normal-combat-toolkit policy, not literal admission of every `Spell` object.

## Accepted-heal / no-effect repair

Current `SpellVessel.ResolveSpell` remains the authoritative effect path and current `Stats.HealMe` remains the authoritative HP path.

The relevant current-assembly divergence is in `SpellVessel.FixedUpdate`: native Sim casters and ordinary non-Sim NPC casters do not take the same interrupt branch. PvP proxies must remain non-Sim and non-persistent, so an accepted temporary-proxy vessel can otherwise end on the non-Sim interrupt distinction before `ResolveSpell`.

The bridge is deliberately narrow:

- requires an active PvP lethal encounter;
- requires the vessel's `CastSpell.MyChar` to be in the current temporary proxy set;
- requires living caster and target and a non-invulnerable target;
- changes only the current vessel's `interruptable` field from true to false for that `FixedUpdate` call;
- captures the original value per Harmony invocation;
- restores it through a Harmony Finalizer, including exception flow;
- never sets `NPC.SimPlayer`, assigns `ThisSim`, changes HP, changes spell magnitude, calls `ResolveSpell`, calls `HealMe`, or bypasses native StartSpell validation/resources/cooldowns/range.

Because Harmony `__state` is invocation-local and no static restoration value exists, nested/reentrant calls cannot overwrite another invocation's saved value. A nested call that sees `interruptable=false` does not acquire a second bridge state; the outer invocation restores its own original value.

## Native heal and status observability

The native heal observation chain is now:

`StartSpell request -> bool accepted -> vessel created -> ResolveSpell entered -> HealMe entered -> HP before/after`

FIGHT healing increases only for actual HP delta. A full-health/overheal call records zero effective healing rather than an accepted-cast success.

Status telemetry snapshots matching entries in current `Stats.StatusEffects` before native processing and compares state afterward. A result is classified as:

- `application_proven` — matching count increased or a new/replaced matching status object exists;
- `refresh_proven` — the matching status remains but native duration increases;
- `no_observable_state_change` — entering `AddStatusEffect` produced no proven state mutation.

Normal and exception paths use one Finalizer completion path per status overload. Heal telemetry also finishes through exception-safe Finalizers so nested `HealMe` scope cannot remain stuck if native code throws.

## `atkSpellDelay` and startup cadence

Current assembly/source investigation found that `atkSpellDelay` is a native maintenance counter, not a number of seconds. The baseline is approximately 120 units and native maintenance removes approximately `60 * deltaTime`. A diagnostic value of `19.06` therefore represents roughly 0.32 seconds remaining, not nineteen seconds.

PvP never writes this field. If a proxy has admitted combat spells, readiness waits until native maintenance has drained the startup value. Post-GO cadence remains native.

`NPCSpellCooldown` is different: the 0.5.12 current-assembly finding remains valid, so only the synthetic startup value generated for a disposable non-Sim proxy is normalized once after native Start/recoverable Start completion. The compatibility bridge contains the only intentional write to that field; later native values are preserved.

## Pre-GO readiness architecture

Final arranged-match state machine:

1. Spawn temporary profiles/proxies.
2. Enable the required native components.
3. Hold the proxy inert with `NeverAggro=true`, no hostile target, and stopped NavMesh.
4. Let Unity invoke `NPC.Start` naturally.
5. Reassert only PvP-owned identity/reward/profile constraints after native Start.
6. Verify runtime invariant, stats/character/caster/spells, native nav first-step/on-mesh state, shell, reward suppression, startup cooldown normalization, native attack-startup maintenance, and initial target resolvability.
7. Once every admitted proxy is ready, enter the visible 3/2/1 countdown while the hold remains active.
8. Recheck readiness and COOP authority at GO.
9. Release `NeverAggro`/NavMesh hold, seed legal targets, and let native combat proceed.

Preparation is bounded to 12 seconds. Timeout/final-readiness failure despawns the temporary team and returns through existing cleanup without a competitive result/reward. Readiness, vessel/effect probes, cooldown-normalization markers, and first-action telemetry are cleared before a second match.

First-action diagnostics measure GO-to-first target, movement, melee decision, attack-spell candidate, and native StartSpell request. The goal is to prove the previous multi-second artificial post-GO dead period is gone, not to impose zero-latency AI.

## World-combat policy

PvP remains MMO-style world combat. Practice Duel was used only for proven spell-target semantics. Active encounters continue to allow legal party Sims, pets, ordinary local Sims, hostile mobs, existing enemies, outside combat actors, native AoE, and native legal heals. Third-party participation does not cancel the match.

Protection remains narrow and positive. Network-owned COOP actors are an authority boundary; positively known vendor/never-aggro/resource/neutral actors are protected. Unknown ordinary combat-capable actors are left to native behavior rather than being treated as arena interference.

## Portable validation status

Final counts are recorded in `HANDOFF.md`. The C# deterministic suite is intentionally reported separately because this Linux environment has no C#/.NET/PowerShell toolchain and therefore cannot execute it.

## Remaining live-only questions

Static/current-assembly evidence cannot prove the final runtime presentation. Live QA must still establish:

- player self-heal and self-buff while the opponent remains selected;
- representative proxy offensive cast reaches native effect;
- representative proxy self-heal and teammate heal reach `HealMe` and produce real HP increase;
- representative HoT/buff/debuff/CC updates `StatusEffects` as expected;
- all real proxy templates reach the readiness barrier inside the timeout;
- practical GO-to-first-action latency for melee/caster/healer profiles;
- continued party-Sim/world-hostile/Nemesis behavior in a full game session.
