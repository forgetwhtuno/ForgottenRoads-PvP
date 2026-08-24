# PvP 0.5.18 countdown-hold consistency

## Root cause

0.5.17 readiness used the live `ValidateProxyRewardBoundary` result. Countdown hold reran the
same preparation evaluation, but its reward path still required both deferred native Start signals
and a post-Start cache. Before GO those signals are intentionally absent, so an unchanged safe
proxy could be reported ready and then immediately reported `reward_suppression_pending`.

## Repair

`VerifyRewardSuppression` is now the single authoritative proof for readiness, countdown hold,
GO validation, and post-Start reassertion. It verifies the live reward boundary and the stored
actor/loot instance IDs without using pre-GO Start completion as a safety condition. A current
boundary or identity change remains fail-closed.

Countdown hold emits one bounded `reward_hold_check` identity line per proxy, and repeats it if
the proof fails. A failed hold now gives a clear technical cancellation message before normal
despawn/containment cleanup restores lifecycle readiness.

Native NPC Start remains deferred until GO.
