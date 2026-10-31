# PvP 0.5.34 Beta live acceptance

Before evaluating the equipment visuals below, run the current pre-opportunity and combat gate:

1. Verify offers/ambushes do not start in restricted, native-combat/recent-combat, unsafe-party, low-level, or nearby-world-actor contexts.
2. Run an arranged 5v5 on the installed candidate. Confirm one canonical PvP plugin identity, the 3/2/1/GO countdown, no pre-GO damage, attackers moving and engaging, damage in both directions, native attacks/spells, and legitimate-outcome-only rewards.
3. r2 grace: kill one quick mob (under 12 seconds), then immediately run `/epvp force ambush 1`. It must
   be refused with `recent_native_combat`; after 20 calm seconds the same command may proceed.
4. r2 health floor: drop below half health out of combat, wait 20 seconds, then run
   `/epvp force ambush 1`. It must be refused with `low_health`. `/epvp force arranged 1` must still
   offer a challenge with the Accept prompt. A refused force command now says why in chat.
5. Confirm one failed proxy does not destroy an otherwise viable match. Run a second 5v5 without restarting, then restart the game and run a smoke match.

Equipment presentation fidelity in a valid match:

1. Start a normal arranged or wild PvP match.
2. Confirm reward suppression remains valid through countdown and GO, then confirm combat/damage and
   normal cleanup still work.
3. For a proxy carrying a leg item, verify `pvp_visual_gender_state` reports matching profile,
   modular, and presentation-inventory gender. Verify `pvp_leg_native_result` identifies the selected
   gender branch, active node, enabled renderer, and mesh.
4. For a proxy carrying a two-handed item, verify `weapon_presentation_normalized` reports the native
   `ThisWeaponType`, `main=primary`, and `off=empty`; then inspect `weapon_visual_result` for the
   visible native attachment.
5. Repeat once if practical to ensure no startup, reward, navigation, spell/heal, or world-participation
   regression.

The build is ready for live verification; this source/build pass does not itself constitute live visual
acceptance.
