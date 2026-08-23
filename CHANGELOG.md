# Changelog

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
