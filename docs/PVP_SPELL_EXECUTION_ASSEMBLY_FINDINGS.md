# PvP 0.5.12 — Current-Assembly Spell / Heal Findings

**Target:** `mods/Erenshor-PvP`  
**Assembly inspected:** supplied current `CURRENT_GAME_REFERENCES/Assembly-CSharp.dll`  
**SHA-256:** `B840CB8076ED0553F7DC3BEB4042ABA653917882F763181EC0D2C13C26C17847`

This document records facts established from the supplied current managed assembly. It does not claim these internals are a stable Erenshor API.

## Attack spell path

Current `NPC.DoAttackSpell()` does the following at its opening boundary:

1. updates `forceSpellCD`;
2. returns when `CurrentAggroTarget` is null;
3. when `SimPlayer == false`, returns while `NPCSpellCooldown > 0`;
4. checks native target/caster/class state;
5. returns while `CastSpell.isCasting()`;
6. later returns while `atkSpellDelay > 0`;
7. requires entries in `MyAttackSpells`;
8. selects a concrete native spell and validates target/range/mana/class-specific requirements;
9. stops native navigation/walking as appropriate;
10. invokes `CastSpell.StartSpell(selectedSpell, CurrentAggroTarget.MyStats)`.

There are three observed native attack branches ending in the same two-argument `StartSpell` request in the current IL.

### Proven short-match incompatibility

`NPC.Start()` assigns ordinary NPCs an initial `NPCSpellCooldown` using a random 20–360 value. Current `NPC.SetAttackRanges()` decrements positive `NPCSpellCooldown` by `60 * Time.deltaTime` while the NPC is eligible. Current `NPC.DoAttackSpell()` bypasses this gate for `SimPlayer == true`, but temporary PvP proxies deliberately remain:

- `SimPlayer = false`;
- `ThisSim = null`;
- outside persistent Sim/player manager structures.

Therefore the startup value can consume about 0.33–6 seconds before a non-Sim proxy is even eligible for attack-spell selection. That is directly compatible with the latest roughly five-second 3v3 showing many `DoAttackSpell` entries but zero `StartSpell` requests.

The repair clears **only this initial post-`NPC.Start` value once** for a temporary proxy. It does the same in the already-supported recoverable `NPC.Start` fault path. It does **not** clear the field in `DoAttackSpell`.

That distinction matters because `SpellVessel.EndSpell` / `EndSpellNoCD` can set `NPCSpellCooldown` after a real native cast. Those post-cast values are left intact and continue to be decremented by native code. `atkSpellDelay` and `forceSpellCD` are observed only and are never written by the repair.

## `CastSpell.StartSpell` surface

The current metadata verifier confirms:

- `StartSpell(Spell, Stats)` → bool
- `StartSpell(Spell, Stats, float)` → bool
- `StartSpell(Spell, Stats, float, bool)` → bool
- `StartSpell(Spell, Stats, float, bool, float)` → bool
- `StartSpellFromProc(Spell, Stats, float, bool, float)` → bool
- `StartSpellNoAnim(Spell, Stats, float)` → bool

PvP now observes the actual Harmony `__result` for all six. A Prefix/admission is no longer treated as native acceptance.

The current `DoAttackSpell()` IL contains no explicit line-of-sight test at its local request boundary. PvP diagnostics therefore report that fact instead of fabricating a separate LOS raycast result. Any additional rejection inside `CastSpell.StartSpell` remains native and is captured by the returned bool.

## Healing path

Current `NPC.CheckHeals()`:

- requires a usable `MySpells` caster and `MyHealSpells`/`MemmedHealSpells` state;
- skips harmful/damage-type entries in the heal list;
- uses the strict mana condition `CurrentMana > ManaCost`;
- permits native self-heal when `CurrentHP < CurrentMaxHP * 0.66` and `NoSelfHeal == false`;
- stops navigation/walking before the request;
- invokes `CastSpell.StartSpell(spell, MyStats)` for self-heal;
- applies native heal cadence afterward;
- has ordinary ally-heal branches that rely on `InGroup`, player/group structures, or persistent Sim tracking.

The 66% threshold is taken directly from the current IL (`0.660000026...`).

Temporary PvP proxies intentionally use `InGroup = false` and no `ThisSim`, so the persistent group/Sim branches are not a valid way to make proxy teammates visible to `CheckHeals`.

Phase 0.5.12 therefore leaves self-healing entirely native and adds a narrow PvP-owned **target-discovery bridge** only for another attacker on the same temporary team. The chosen spell still comes from the proxy's native `MemmedHealSpells`, and execution still goes through `CastSpell.StartSpell(Spell, Stats)`.

The bridge never selects:

- the enemy defender;
- unrelated local Sims;
- ordinary world NPCs;
- neutral/noncombat actors;
- the healer itself (native self-heal owns that case).

For the bridge cooldown, the current ordinary non-Druid path uses `spell.Cooldown * 60`. The bridge uses that conservative native cadence. The Druid self-heal path has separate native timing and remains native when it is a self-heal.

## Weapon / presentation finding

Current `NPC.DoAttackSpell()` does not require `MHWand`, `MHBow`, `WandEquipped`, or `BowEquipped` at the actual `StartSpell` request boundary. Weapon state is relevant elsewhere (including attack-range/presentation setup), but the current spell-request failure does not justify creating fake weapon objects.

The existing 0.5.11 profile weapon-runtime and visible-shell binding is therefore preserved unchanged.

## Healthy native Sim comparison

0.5.12 keeps the existing one-per-match proxy/native capability snapshot and adds another bounded observation: the first **successfully accepted** `StartSpell` from a loaded real `SimPlayer` during the match logs a read-only `healthy_native_sim_cast` record containing:

- caster ownership/binding;
- concrete spell ID/name;
- target and distance/range;
- current mana and spell cost;
- attack/heal/memmed-heal counts;
- native cooldown field values;
- animator availability;
- bow/wand flags.

Nothing about the native Sim is mutated by this diagnostic.
