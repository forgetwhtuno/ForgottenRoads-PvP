# PvP 0.5.32 equipment presentation fidelity

## Native requirements

PvP render shells disable the cloned `SimPlayer` component. The normal Sim lifecycle
therefore does not rebuild `ModularParts`' gender-scoped transform cache after PvP applies
the profile gender. The shell now calls native `ModularParts.GetTransformNames()` after
setting `ModularParts.Gender` and the shell-local `Inventory.isMale`, immediately before
native `UpdateSimPlayerVisuals`.

Leg completion and visual-state checks are branch-scoped to the selected `Male_Parts` or
`Female_Parts` hierarchy and require an active renderer. This prevents an inactive duplicate
from being mistaken for the selected native leg presentation.

Weapon presentation uses current ItemDB `ThisWeaponType` metadata. `TwoHandMelee`,
`TwoHandStaff`, and `TwoHandBow` items are normalized into the native primary slot with an
explicit native Empty secondary slot. No mesh, armor, combat stat, inventory ownership, or
native renderer is fabricated or rewritten.

## Scope and preservation

The repair is limited to the render-shell equipment boundary. Countdown/GO, reward
suppression, native startup, navigation, combat, spells, healing, world participation,
profile gender propagation, and donor selection remain unchanged.

Live QA must still confirm a natural proxy with affected leg and two-handed equipment.
