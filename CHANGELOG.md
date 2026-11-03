## [Unreleased]

## [0.5.0] - 2026-11-03

## [0.4.0] - 2026-11-03

## [0.3.0] - 2026-11-03

### Added
- Fishing rod model

### Fixed
- Armor not showing in multiplayer
- Monster appendages detached for clients in multiplayer
- Structures somtimes cut off

## [0.2.0] - 2026-11-01

### Added
- Support for multiplayer via P2P connections
- Usage of custom protocol and endpoint setup in ArterraResources
- Added Multi-sourced generation drivers 
- Added armor models that show on equip

### Fixed
- Used simplex gradients and warp jacobian to more accurately simulate erosion
- Greatly improved player animations
- Armor UI was not showing

## [0.1.1] - 2026-09-24
### Added
- In game volume controls
- Dimming of sun flare in sky if sun is obscured by atmospheric effects

### Changed

- UI and text font for procedural main menu options panel, and in-game pause menu

### Fixed

- Fix bug so armor scene so character is drawn bigger
- Fix missing names for certain player settings being substituted by placeholders
- Fix options panel list entries collapsing buttons into text area when UI refreshes

## [0.1.0] - 2026-09-22
- Reduced Graphics Memory usage by refining deferred allocation strategy
- Switched Generation Pipeline (Graphics) to be rendering-async if supported
- Added animal sounds to all flying animals

### Changed
- Change Steam Release CI to use TOTP for authentication

## [0.0.1] - 2026-08-31

### Added
- Added Steam Release CI command