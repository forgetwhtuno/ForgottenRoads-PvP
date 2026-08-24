# PvP 0.5.17 startup-lifecycle recovery

The proxy clone is now made inactive immediately after construction, configured while inactive,
and released at GO. The native `NPC` component remains disabled through countdown preparation and
is enabled only after GO; no manual `NPC.Start()` call is used.

Readiness before GO proves the safe containment boundary (disabled NPC, `NeverAggro`, no target,
and held navigation). It deliberately does not require a native Start callback that cannot occur
while the component is correctly held disabled. The existing reward boundary remains fail-closed.

Live QA: start a duel and confirm readiness proceeds to countdown/GO, with native attack behavior
starting after GO and no post-yield manual autoattack cleanup needed.
