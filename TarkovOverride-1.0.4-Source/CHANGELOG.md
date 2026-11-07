# Changelog

## 1.0.4 — Rage-only ADS movement stability

- Added default-on No ADS movement sway in Rage Aim / Weapon Stability.
- Filters the local held weapon's motion/turn sway and normal/overweight walking-bob amplitudes during ADS while Rage is enabled.
- Native processing and animation clocks continue; no stored animation/template values are changed.
- Normal amplitude returns outside ADS/Rage and for other players. RCS, breathing/injury effects and firing remain separate.

## 1.0.3 — Rage tracking rework and optional ballistics

- Strict Head now overrides all saved target-point choices, not only Head-with-fallback.
- Cache one rendered model-head transform per enemy instead of alternating bone/hitbox centres.
- Calculate Rage instant rotation from native player LookDirection rather than rendered camera direction.
- Add independent, default-off movement lead and gravity-drop estimates, with strength and maximum flight-time controls.
- Keep the target marker anatomical and label active lead/drop in status. No firing restrictions or automatic trigger actions added.

## 1.0.2 — Live head coordinates

- Prefer the original animated head transform over cached/imitation bone coordinates and physics bounds.
- Accept transformed head-collider centres only when they are attached under the live head and within a small local distance.
- Skip invalid/missing head data rather than estimate standing head height.
- Identify live head source in aim status; retain strict selection and optional explicit body fallback.
- Project overlays through normalized viewport coordinates to avoid render-resolution offsets.

## 1.0.1 — Head targeting correction

- Added default-on Strict head targeting to Rage Aim, preventing Head from silently becoming chest/pelvis when fallback is enabled.
- Preserved optional non-strict fallback, explicit body-point targeting and nearest-to-crosshair selection.
- Head aiming prefers the enabled head hitbox centre, with the existing bone-position fallback when unavailable.
- Visibility recognizes native registered player colliders and body-part ownership as well as transform parenting.
- Aim status now names the actual point and marks body fallback.
- Preserved the author header and removal of the F12 settings button/key-picker explanation.

## 1.0.0 — Tarkov Override release package

- Renamed the displayed mod and compact window to Tarkov Override.
- Added Author: StealthOTR beneath the persistent window title, outside all tabs.
- Updated assembly title, product, author/company credit and version metadata.
- Preserved plugin identity, installed DLL path and config locations for seamless upgrades.
- Prepared installation/removal instructions, feature guides, credits, known limitations and separate source package.
- Gameplay logic is unchanged from 0.8.3.

## 0.8.3

Fixed no-clip compatibility with SimpleCharacterController and improved controller-state restoration.

## 0.8.2

Added the native airdrop request button under Misc, gated by current-map support and a 60-second cooldown.

## 0.8.1

Removed grenade trajectory preview and its simulation work. Legacy JSON shortcut entries are accepted but ignored.

## 0.8.0

Added gear repair, infinite medical supplies, no weight penalties, interaction range, adjustable fire rate, nearby alerts and extraction overrides.

## Earlier development

Introduced ESP/loot tools, configurable aim profiles, the compact menu/tooltips, RCS, player practice tools, vision/no-clip, door/extraction actions and item spawning. See docs/VALIDATION.md for historical technical checks and their limits.




