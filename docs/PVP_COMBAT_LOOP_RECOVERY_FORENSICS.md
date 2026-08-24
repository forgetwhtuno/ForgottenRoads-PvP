# Forgotten Roads: PvP — Combat Loop Recovery Forensics

**Workstream:** deepest PvP combat-regression archaeology / native AI recovery / self-heal / class combat  
**Candidate:** 0.5.15  
**Revision:** `pvp-0.5.15-combat-loop-recovery-r1`  
**Date:** 2026-08-21  
**Scope:** current `mods/Erenshor-PvP`; Git/history and Practice Duel read-only; no Git writes; no install.

## Source and binary baseline

The supplied current working source began at **0.5.14**, runtime marker
`pvp-0.5.14-nav-readiness-r1`. The Git remote was independently verified as:

`forgetwhtuno/ForgottenRoads-PvP`

The current supplied game assembly used for native lifecycle proof is:

- `Assembly-CSharp.dll` SHA-256: `B840CB8076ED0553F7DC3BEB4042ABA653917882F763181EC0D2C13C26C17847`
- current Lunaris reference SHA-256: `5A70F3D1FD9441CEAE6D8E1F80CAFCE86FF2A47245FBCFA36BFCF8E88FD20B29`
- current Harmony reference SHA-256: `C349E1A3FD13FA5A9FACC9805A5E160161B14489F46F6BDD38202B8E124F78DF`

The public Git history available for this repository currently ends at the 0.5.11 source sync. The
0.5.12–0.5.14 revisions in the supplied working tree therefore do **not** have identifiable public Git
commit SHAs in the evidence available to this workstream. They are compared by the current local
CHANGELOG/source history rather than assigned invented SHAs.

## Exact historical points

### Last revision with explicit PvP-owned loop startup

Commit `9a8ed82a3d3ba3e2f2031e4701eff3db40c0e25f` (the reconstructed 0.5.5–0.5.8
checkpoint, with 0.5.8 recorded as the last live-confirmed state before the 0.5.9 lifecycle rework)
explicitly recovered the native iterators by:

- invoking `NPC.NavUpdate(0.3f)`;
- assigning `navDo`;
- `StartCoroutine(navDo)`;
- invoking `NPC.BehaviorUpdate(0.1f)`;
- assigning `behDo`;
- `StartCoroutine(behDo)`.

That is the exact last Git revision where PvP itself explicitly started both loops.

### First architectural divergence from explicit loop ownership

Commit `c43e4bda602bb940654a5a6e82027ed1a3b30f1e` (0.5.9) deliberately removed
manual `NavUpdate` / `BehaviorUpdate` construction and returned loop ownership to natural `NPC.Start`.
This was a meaningful architectural divergence, but **not the current regression by itself**.

At 0.5.9–0.5.11, natural `NPC.Start` still occurred as combat was released. The proxy had
`NeverAggro=false` at the point current `NPC.Start` considered the loop-launch branches. 0.5.11 live
results then proved that this architecture could run the native combat decision section: targets were
acquired, movement/pursuit occurred, melee attempts and real melee damage occurred, and one captured
case reached approximately `attack_spell_decisions=66`.

### Last live-working combat architecture

The current public 0.5.11 source sync is commit
`070fa36994e7257d71f9e9816af2c71e48c2817f` (merged by
`b902c0608a3e19e95288942287e0bfd85853f2b4`). Its associated live evidence proves that the
natural-Start-owned loop architecture was still executing combat decisions. 0.5.12 then repaired the
non-Sim startup `NPCSpellCooldown` gate and reached real native spell request, bool acceptance, and
`cast_finished` in live testing.

### Exact regression introduction

The first source revision that conflicts with the **current installed assembly lifecycle** is
**0.5.13**. Its local history explicitly changed startup so natural native `NPC.Start` completes while
opponents remain inert **before** the visible countdown. During that preparation, current PvP sets:

- `NeverAggro=true`;
- `CurrentAggroTarget=null`;
- NavMesh held/stopped;
- proxy NPC enabled so Unity executes natural `Start`.

The current assembly's `NPC.Start` gates **both** loop launches behind `!NeverAggro`. Therefore the
0.5.13 pre-GO Start can complete successfully with both `navDo` and `behDo` null. 0.5.14 preserved this
architecture and replaced a circular operational pre-GO nav requirement with correct structural
NavMesh readiness, but it did not add behavior/nav loop existence to the readiness barrier. This made
a structurally healthy but AI-loopless proxy more consistently reach countdown/GO.

There is no available 0.5.13 Git SHA in the supplied snapshot/public history; the exact introduction is
therefore identified as the **0.5.13 local source revision**, not an invented commit.

## Current native NPC lifecycle proof

`tests/verify_pvp_combat_loop_lifecycle.py` reads the current CLR metadata and method bodies directly.
Against the current `Assembly-CSharp.dll` it proves:

1. `NPC.Start` reads `NeverAggro` twice and uses a `brtrue` gate around each native loop launch.
2. If allowed, Start creates/stores `NavUpdate(0.3f)` in `navDo` and starts it.
3. If allowed, Start creates/stores `BehaviorUpdate(0.1f)` in `behDo` and starts it.
4. `NPC.Update` is a separate Unity callback and does **not** call `NavUpdate`, `BehaviorUpdate`, or
   `Combat`; reaching Update cannot replace the missing loops.
5. the generated `BehaviorUpdate` iterator reaches `DoStances`, `DoNonRaidBehavior`, and
   `DoRaidBehavior`;
6. `DoNonRaidBehavior` reaches `CheckHeals` and `Combat`;
7. `Combat` reaches native skill, spell, and melee decision/execution methods;
8. the generated `NavUpdate` iterator reaches both `HighPriorityNavUpdate` and `UpdateNav`.

A second important assembly finding is that `NeverAggro` is checked at the iterator's initial path but
is not a safe permanent admission gate for an iterator that PvP explicitly starts. An explicit
pre-GO action boundary is therefore required when recovering the loops before GO.

## Exact cause of `combat_sections=0`

The 0.5.14 live sequence is internally consistent:

`native_update_reached -> legal_target_acquired -> combat_sections=0`

It does **not** prove a hidden Combat early-return. It proves the wrong scheduler was being observed.
`NPC.Update` can run and target state can be valid while `BehaviorUpdate` never exists. With pre-GO
`NeverAggro=true`, current `NPC.Start` skipped both native coroutine launches; after GO, PvP cleared
`NeverAggro` and seeded a legal target, but Unity does not call `Start` a second time. No code then
created `behDo` or `navDo`. Consequently:

- no `DoNonRaidBehavior`;
- no `CheckHeals`;
- no `Combat`;
- no `DoAttackSkill`;
- no `DoAttackSpell`;
- no native pursuit loop.

That is the exact causal boundary for the observed zero counters.

## Healthy native Sim vs temporary proxy

The intentional proxy differences remain:

- `SimPlayer=false`;
- `ThisSim=null`;
- no persistent Sim roster/save identity;
- PvP-owned reward/loot suppression.

Those differences are not sufficient to explain the regression: 0.5.11/0.5.12 live results proved
that the same temporary non-Sim shape can move, melee, evaluate spells, and complete a native spell
cast.

The gating difference is loop scheduling. A healthy fighting native Sim whose Start ran while
aggression is allowed has resident `navDo`/`behDo`; a 0.5.14 temporary proxy whose natural Start ran
under `NeverAggro=true` did not.

0.5.15 adds one bounded live comparison per match. It selects a loaded **actively fighting** native Sim
when available and logs, side-by-side with one temporary proxy:

- NPC enabled / SimPlayer / ThisSim;
- NPC `Myself`, `MyStats`, `MySpells`, `MyNav` binding;
- Character / Stats / caster;
- NavMeshAgent enabled, on-mesh, stopped, path, status, destination, remaining distance;
- current target / NeverAggro / alive;
- nav/behavior loop presence and ownership;
- leash/off-nav/startup/behavior-delay state;
- melee/casting state;
- attack/heal/native spell cooldowns;
- stun/fear/root/charm state;
- attack range and movement speed;
- Animator/controller;
- admitted attack/heal spell counts.

No runtime healthy-Sim snapshot was captured in this offline workstream; the instrumentation is for the
focused live QA run.

## 0.5.15 repair

The repair ports only the missing scheduling behavior into current source. It does not restore the old
0.5.8 startup architecture wholesale.

After a temporary proxy's natural `NPC.Start` has completed, `EnsureNativeCombatLoops`:

- checks current `navDo` / `behDo` first;
- starts only a missing `NavUpdate(0.3f)` or `BehaviorUpdate(0.1f)`;
- verifies the native iterator field is still empty before startup;
- assigns the exact candidate iterator field and starts it transactionally; if startup throws, only that
  exact candidate field is cleared and no ownership record is retained;
- tracks the exact enumerator only if PvP successfully started it;
- refuses duplicate starts through both an ownership map and a per-proxy start count;
- makes both resident loops and single-start ownership part of pre-countdown readiness.

During pre-GO preparation, recovered iterators remain alive but PvP keeps them inert through all of the
existing symmetric combat boundary plus explicit temporary-proxy-only gates on:

- `NPC.DoStances`;
- `NPC.DoNonRaidBehavior`;
- `NPC.DoRaidBehavior`;
- `NPC.HighPriorityNavUpdate`;
- `NPC.UpdateNav`.

The agent remains structurally ready but stopped with no path; `NeverAggro=true` and no legal target
remain asserted. GO is still release-only: both countdown-end and combat-start readiness checks are
read-only and cannot recover missing loops. If loop infrastructure disappears during countdown, GO
fails closed rather than creating AI. GO does not replay `NPC.Start`; it releases `NeverAggro`, unstops
on-mesh navigation, and seeds the native target. The recovered native iterators are then already
scheduled and may proceed through normal class AI.

Cleanup stops only enumerators recorded in PvP's ownership maps. The native `navDo` / `behDo` field is
cleared only when it still references the exact owned enumerator being stopped. Native-owned loops and
ordinary NPCs/Sims are never stopped by this recovery path. Owned loop maps and start counters are
cleared on proxy retirement, normal/failure terminal despawn, scene teardown/hot unload, and
second-match reset. No `StopAllCoroutines` ownership shortcut is used.

The existing Stormcaller Start-tail recovery was also adjusted for the 0.5.13/0.5.14 preparation
architecture. A successfully recovered Start-tail exception no longer leaves the shared nav probe's
`Faulted` bit set; that exception is not an `UpdateNav` failure and must not poison 0.5.14's structural
NavMesh readiness forever.

## Bounded failure-boundary trace

For one post-GO trace per temporary proxy, if a legal target exists but `Combat` has still not been
reached after the bounded initial interval, 0.5.15 records `combat_branch_trace` with the first
observable divergence among:

- behavior loop missing;
- nav loop missing;
- NeverAggro still set;
- no target;
- dead;
- behavior coroutine not progressing;
- leashing;
- spawn cooldown;
- behavior delay;
- casting;
- behavior section reached but Combat not yet reached.

`behavior_section_reached` is logged separately on first `DoNonRaidBehavior` admission. This is bounded
telemetry, not per-frame spam.

## Spell, self-heal, team-heal, and world-combat state

No manual damage or healing was introduced.

The current 0.5.13/0.5.14 convergence work is retained:

- **player self-heal:** an ordinary legitimate single-target heal may adapt only the native
  `StartSpell` target-Stats argument to the player's Stats while the opponent remains selected;
  `PlayerControl.CurrentTarget` is not rewritten;
- **declared self utility:** `SelfOnly`, `ApplyToCaster`, and `InflictOnSelf` retain Duel-derived
  self semantics;
- **proxy self-heal:** `NoSelfHeal=false` remains required; native `CheckHeals`, native threshold,
  mana, cooldown, spell selection, `StartSpell`, and `HealMe` remain authoritative;
- **team heal:** the existing narrow non-Sim attacker ally selector still chooses only a legal injured
  temporary teammate and calls native `CastSpell.StartSpell`; it does not synthesize HP;
- **offensive spells:** AI entry -> native request -> actual bool result -> cast completion ->
  SpellVessel -> ResolveSpell -> real damage/status state remains the required proof chain;
- **world combat:** ordinary party Sims, local Sims where native rules permit, pets/summons, hostile
  mobs, existing enemies, outside combat-capable actors, legal AoE and legal heals remain admitted;
  `third_party_aggro` cancellation is not restored;
- only positively protected neutral/noncombat/network-owned actors are filtered narrowly.

## Validation status

Static/current-assembly validators pass in this worktree. A real game was not launched, so none of the
0.5.15 runtime behavior is claimed live-proven yet. See `PVP_COMBAT_LOOP_RECOVERY_LIVE_QA.md` for the
focused acceptance sequence.
