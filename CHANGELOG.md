# Changelog

## 3.0.0 - 2026-09-27

Gameplay features are now separate mods. The Controller is the controller,
binding, and settings core; Force Encounter, Quick Heal, Dash / Dash Keep, and
Smart Auto Battle ship as standalone mods that also work without it. Updating
only the Controller from v2.0.3 removes those features: install the matching
mods to keep them. See "Upgrading from v2.0.3" in the README.

### Changed

- Force Encounter, Quick Heal, Dash / Dash Keep, and Smart Auto Battle moved from the Controller to the standalone mods NocturneForceEncounter 0.2.0, NocturneQuickHeal 0.1.0, NocturneModernDash 0.1.0, and NocturneSmartAutoBattle 0.1.0.
- The Controller now covers controller input, the right-stick camera and PUZZLE turning, bindings, native GAME binding editing, the settings window, and the integration of the gameplay mods.
- Smart Auto Battle: disabling it now fully disables it. The built-in version kept replacing commands in Skill Priority mode and learning affinities even when disabled; now a disabled Smart Auto changes no speed, commands, or targets and learns nothing, and the game's own Auto runs unchanged.
- Smart Auto Battle mode and speed are set in the MOD Features tab (Mode and Speed selectors) instead of a dedicated tab.
- The GAME Bindings tab is no longer read-only. Actions bound to an unsupported button stay visible as "Unknown (raw=N)".

### Added

- Editable native GAME controller bindings in the Settings "GAME Bindings" tab: 15 verified actions and 12 buttons (A, B, X, Y, LB, LT, RB, RT, L3, R3, SELECT, START), written through the game's native key config save path and kept across restarts. Safety checks: one change per Apply, native duplicate rejection, stale-value detection, readiness checks, and automatic rollback on failure.
- Feature value support for mods: besides on/off, a mod can offer selectable values (with display labels) in the MOD Features tab.
- Release packages for the four gameplay mods, built reproducibly together with the Controller.

### Removed

- Built-in Force Encounter, Quick Heal, Dash / Dash Keep, and Smart Auto Battle, including their feature cards and default bindings in the Controller.
- The Auto Battle settings tab.

### Fixed

- GAME binding changes are applied once field exploration resumes after Settings closes, instead of being rejected while the game window is still restoring.
- A value change in the MOD Features tab could have been handled as turning that feature off.

### Compatibility

- The old settings (`ForceEncounterEnabled`, `QuickHealEnabled`, `DashEnabled`, `SmartAutoEnabled`, `AutoBattleMode`, `AutoBattleSpeed`) stay in `NocturneModernController.settings.json`; each gameplay mod takes its values from there on its first run. Smart Auto Battle also copies the old learned affinities.
- Saved bindings for Force Encounter, Quick Heal, Dash, and Dash Keep are kept and used again when the mod is installed.
- Do not use the new gameplay mods with Controller v2.0.3 or earlier: the old Controller contains the same features, so both would run.
- The Settings "MOD Features" integration keeps accepting mods built for earlier Controller versions.

## 2.0.3 - 2026-09-16

- Reduced Field Dash speed to 1.5x.
- Fixed an issue where excessive dash speed could allow the player to enter areas that are normally inaccessible.
- Right-stick and controller behavior are otherwise unchanged.

## 2.0.2 - 2026-09-12

- Added support for multi-value external feature settings.
- Added Chance selection support for compatible external gameplay features.
- Added 0% / Native / 100% Chance selector display (0% / 通常 / 100% in Japanese).
- Fixed an issue where the Chance selector could be hidden underneath its feature title.
- Prevented the mouse wheel from unintentionally changing dropdown values while scrolling the Settings window.
- Removed unused legacy synthetic mouse movement code.
- Aligned MOD and assembly version metadata with the release version.

Right-stick startup architecture is unchanged from v2.0.1: Explorer-based automatic helper startup, no additional setup, and no Guide button press required.

## 2.0.1 - 2026-08-23

- Added automatic Japanese/English settings UI selection based on the Windows UI language.
- Added a saved Auto/Japanese/English language selector to the settings window.
- Localized built-in action names, binding dialogs, controller diagram text, and feature descriptions.
- Added an English README for international distribution.

## 2.0.0 - 2026-08-23

First public release under the Nocturne Modern Controller name. The major
version distinguishes this package from the repository's earlier v1.0.0 tag.

- Added SDL3-based generic controller input helper.
- Added native right-stick horizontal turn and vertical camera control.
- Added sensitivity, dead-zone, invert-X, invert-Y, and right-stick mode settings.
- Added context-aware bindings with up to three-button combinations.
- Added Dash, Dash Keep, Quick Heal, Force Encounter, and Smart Auto Battle.
- Added the integrated settings GUI and controller binding diagram.
- Added metadata-driven feature cards and external provider integration.
- Preserved battle RB Pass while suppressing legacy field shoulder turning.

Known limitations:

- PUZZLE right-stick rotation still needs final real-play verification.
- Xbox Elite Series 2 is verified; PlayStation, Switch, and generic SDL controllers are supported by design but not exhaustively tested.
- Smart Auto Battle uses heuristics and may make suboptimal choices in unusual encounters.
- The external Feature Provider format is currently an integration interface, not a stable SDK compatibility promise.
