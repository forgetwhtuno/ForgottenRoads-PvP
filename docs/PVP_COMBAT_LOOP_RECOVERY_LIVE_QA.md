# Forgotten Roads: PvP — 0.5.15 Combat Loop Recovery Live QA

**Candidate:** 0.5.15  
**Revision:** `pvp-0.5.15-combat-loop-recovery-r1`  
**Purpose:** focused in-game acceptance after static/current-assembly validation.  
**Important:** this document is a test procedure. This workstream did **not** run Erenshor and does
not claim these runtime checks passed.

## 1. Startup identity

Launch with the 0.5.15 candidate only after normal local installation procedure is authorized.
Confirm one effective PvP plugin identity and the startup marker:

`revision=pvp-0.5.15-combat-loop-recovery-r1`

Run `/epvp selftest`. It must include:

`PASS pvp native combat-loop ownership policy`

No duplicate ErenshorPvP plugin instance should be present.

## 2. Preparation — no combat before GO

Start an arranged match in a normal navigable area.

Before the visible countdown, require every temporary proxy to reach:

- `proxy_native_start ... completion=completed` or a valid isolated Start-tail recovery;
- `combat_loop_recovery ... nav=True/.../starts<=1,behavior=True/.../starts<=1`;
- `proxy_ready ... nativeNavLoopReady=True,nativeBehaviorLoopReady=True`;
- `combatLoopInfrastructureReady=True`;
- `preGoLoopBoundaryReady=True`;
- structural NavMesh ready/on-mesh/stopped/no stale path;
- `native_start_pending=0` at `countdown_ready_barrier`.

While preparation/countdown is active:

- proxy target must remain clear;
- `NeverAggro` must remain true;
- no proxy attack, heal, buff, stance action, pursuit, or spell may affect combat;
- recovered behavior/nav loop attempts may log the bounded `pre_go_behavior_hold` /
  `pre_go_nav_hold`, but there must be no visible proxy movement or damage before GO.

A missing loop, duplicate start count, Start failure that cannot be recovered, or broken NavMesh
boundary must block countdown rather than releasing a partial team.

## 3. GO — native combat scheduling

At GO require:

- exactly one `go_release` / GO transition;
- target seeded and accepted;
- `native_update_reached`;
- `legal_target_acquired`;
- `behavior_section_reached` shortly after GO;
- `combat_section_reached`;
- when movement is required, `pursuit_probe_start` followed by native UpdateNav/path/displacement
  evidence;
- for melee-capable profiles, melee decision/attempt and real native damage when in range;
- no `technical_failure_ai_inactive` while real proxy-owned AI evidence exists.

The previous failure signature — legal target with `combat_sections=0`, `nav_pursuit=0`,
`heal_checks=0`, `attack_skill_decisions=0`, `attack_spell_ai_entries=0` — is a failure.

If Combat is not reached, capture the single bounded `combat_branch_trace` for the affected proxy.
It should identify the first remaining divergence instead of requiring frame-spam logs.

## 4. Healthy native Sim comparison

Have at least one ordinary native Sim actively fighting in the scene during a PvP match if practical.
Capture:

- `proxy_capability_snapshot`;
- `native_sim_capability_snapshot`.

Confirm the temporary proxy's intentional `SimPlayer=false` / `ThisSim=false` identity difference,
but verify its Character/Stats/caster/nav/Animator bindings and both native loop handles are present.
The snapshot should show loop ownership (`pvp` only for loops recovered by PvP; `native` for any
already-resident loop).

## 5. Offensive spell chain

Use at least one attack-spell-capable proxy. Require a real end-to-end chain:

1. `attack_spell_ai_entry` / attack decision;
2. `spell_native_request`;
3. `spell_native_result ... accepted=true`;
4. cast completion;
5. `spell_vessel_created`;
6. `spell_vessel_resolve`;
7. real damage or a proven status application/refresh.

Decision, animation, native bool acceptance, or `cast_finished` alone are not effect proof.
Do not accept fake/manual damage.

## 6. Player self-heal with opponent selected

Select a PvP opponent and keep it selected. Damage the player enough for a legitimate direct heal.
Cast an ordinary single-target self-heal shape that the Duel-derived adapter recognizes.

Require:

- player target remains the PvP opponent throughout;
- native StartSpell request is accepted;
- mana/cooldown remain native;
- actual player HP rises through native healing telemetry;
- no direct manual HP write occurs.

Also exercise a declared `SelfOnly` / `ApplyToCaster` / `InflictOnSelf` beneficial ability when a class
has one available.

## 7. Proxy self-heal

Use a heal-capable temporary proxy and lower it clearly below the native self-heal threshold.
Require:

- `heal_check` is reached;
- a legitimate native heal spell is selected;
- native StartSpell request/acceptance occurs;
- SpellVessel/ResolveSpell/HealMe path is observed as applicable;
- actual temporary proxy HP increases.

Do not count a full-health/overheal accepted cast as effective healing.

## 8. Temporary teammate healing

Run at least two attackers with a heal-capable proxy and injure a different temporary teammate.
Require:

- legal injured ally selection;
- native `StartSpell(selected, ally.MyStats)` path;
- actual teammate HP increase;
- no manual healing;
- no cross-team beneficial heal.

## 9. World-combat expansion

During an active PvP match, exercise as available:

- current party Sim joining combat;
- owned pet/summon joining;
- ordinary hostile mob/existing enemy joining;
- an outside local combat-capable actor interacting;
- legal AoE;
- legal heal/buff behavior.

None of those ordinary native participants may cancel/despawn the encounter merely for being a third
party. Separately verify a positively proven protected neutral/noncombat/network-owned actor is still
filtered rather than mutated by local PvP.

## 10. Lifecycle/cleanup

Exercise:

- normal attacker death;
- player death if safe/intentional;
- flee;
- enemy retreat if encountered;
- zone/scene transition;
- plugin disable/unload if testing Lunaris hot lifecycle;
- a second arranged match without restarting Erenshor.

After each terminal path confirm:

- no orphan temporary GameObject;
- no stale target left by PvP ownership;
- no PvP-owned behavior/nav coroutine remains alive;
- native-owned ordinary NPC/Sim loops are untouched;
- second match starts with fresh loop start counts and exactly one GO;
- technical failures produce no competitive history/reward;
- legitimate result/reward remains exact-once;
- Nemesis/public result correlation remains unchanged.

## 11. Pass criterion

0.5.15 is live-accepted only after at minimum one complete match proves native behavior/combat
sections, real movement or correct in-range behavior, melee and/or class ability evaluation, and an
actual offensive or healing effect, followed by a clean second match. Spell-capable and heal-capable
classes should be tested separately if one match cannot cover both.
