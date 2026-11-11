## [Unreleased]

## [0.2.4] - 2026-11-10

## [0.2.3] - 2026-11-10

### Added
- Custom model mesh generation
- Torch material

### Changed
- Chests now use custom model
- Furnaces now use custom model
- Pestles now use custom model
- A Voxel's Metadata now holds a set of objects

### Removed
- Ember material

## [0.2.2] - 2026-11-08
### Added
- Goat animal with jumping
- Unified editor for biome placement
- Cliff biome

### Changed
- Surface Biomes use steepness now not influence offset
- Changed sizes of certain biomes
- Entities will try to jump when path is obstructed instead of immediately giving up.

### Fixed
- NaN position causing unrecoverable entity errors
- Missing null checks for player loading for multiplayer clients

## [0.2.1] - 2026-11-03

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