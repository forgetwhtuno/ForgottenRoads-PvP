# PvP 0.5.32 reward safety rehydration repair

## Authority and ordering

Current `Assembly-CSharp.dll` inspection establishes that `Character.Start` writes `xp` and
`factionMods`. The live sequence was therefore valid: an initial PvP suppression snapshot could
pass, native Character initialization could subsequently hydrate borrowed reward values, and the
countdown hold correctly refused unsafe combat.

`NPC.Start` is deliberately deferred until GO. It is not the final pre-GO writer for these fields
and cannot be a prerequisite for the countdown barrier.

## Repair

The temporary-proxy `Character.Start` postfix now records completion and performs the one allowed
post-hydration suppression reassertion. Pre-GO readiness requires that completion and the existing
current Character/LootTable identity proof. The countdown hold only verifies; it never reapplies
suppression every frame.

The source also registers the narrow Character and NPC Start Harmony patches on their correct
individual patch classes. No native lifecycle is skipped.

## Validation and release boundary

All source/deterministic PvP tests and all current-assembly validators pass. The built and
installed 0.5.32 assembly hash is
`BA4CBA1ECE5772625649EFB8CC982A72E304104AD0A6C8992D97EF849BCE379C`.

Live QA is required to prove the safe state persists through countdown and GO. This document does
not claim a pants fix; the existing visual observer is retained unchanged.
