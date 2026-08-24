# PvP 0.5.19 reward-state forensics

The current `Character.Start` hydrates `factionMods` after the clone's initial
suppression and before the intentionally deferred `NPC.Start`. This explains a
successful initial proof followed by an unsafe countdown hold without changing
the deferred-to-GO NPC lifecycle.

The bounded final pre-countdown action is the `Character.Start` postfix: it
reapplies the reward boundary once, then readiness and every countdown/GO
consumer read the same current-state verifier. One privacy-safe snapshot is
logged per proxy for `post_apply`, `ready`, and `countdown_hold`.

Current Assembly-CSharp death-chain findings:

- `xp` and `NeverTrack`: MUST SUPPRESS and MUST VERIFY. `DoDeath` can add a
  level-adjusted minimum XP reward even with zero raw XP unless `NeverTrack`
  gates the player-reward branch.
- `QuestCompleteOnDeath`, `factionMods`, loot gold/drops/enabled state, and
  `SetAchievementOnDefeat`: MUST SUPPRESS and MUST VERIFY. The native death
  flow can complete quests, submit faction modifications, create corpse data,
  or grant defeat credit from these surfaces.
- `BossXp`: defensively zeroed, but not a standalone safety predicate. It is a
  multiplier inside the branch already gated by `NeverTrack` and native code
  normalizes zero to one in that branch.
- `BonusRangeXP`: defensively zeroed and IRRELEVANT TO PVP REWARD SAFETY in the
  inspected current death flow; it is not a required verifier predicate.

The separate `equipment_visual_failed=NullReferenceException` remains out of
scope: the inspected visual path does not write reward state.
