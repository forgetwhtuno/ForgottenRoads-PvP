# PvP 0.5.33 live acceptance — equipment presentation fidelity

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
