# Changelog

All notable changes to Chart will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added
- **Cavern maps for roofed dimensions.** A dimension's owner mod can declare where the map's surface scan starts with the `chart:scanTopY` metadata (an integer Y, set through Manifold's `WithMetadata`). Chart steps through the ceiling the scan starts in and draws the floor under it, so a roofed dimension shows its caverns instead of a uniform roof; a column solid all the way down is drawn as a wall. The hint needs Manifold 0.6.0 or later on the server, which is when metadata started reaching clients. `chartScanTopY`, the key Rift Traveler already ships, is read as well.

### Fixed
- **Solid custom dimensions were mapped from the overworld's height map.** Map chunks are not per dimension, so the height Chart read first was the overworld's surface at the same X/Z. In a void or skyblock dimension that height is air and the scan took over, which hid the problem; in a dimension filled with rock it landed on an arbitrary block inside the terrain or on its roof, and that block was drawn as the surface. Chart no longer reads the height map outside the overworld: custom dimensions are always scanned.

### Changed
- The scenarios run against the Manifold 0.6.0 release, with a roofed fixture dimension that Chart's own scan is run over. Chart still works with Manifold 0.4.1 or later.

## [0.3.0] - 2026-09-27

### Added
- **Waypoints are now filtered by dimension** (issue #1). The vanilla waypoint layer renders every pin in every dimension by absolute X/Z, so overworld pins bled into Skyblock, Void or instanced dungeon dimensions. Chart now registers a dimension-aware replacement under the vanilla `"waypoints"` layer code: pins only show in the dimension they were created in, matching the per-dimension terrain map Chart already draws. A waypoint's dimension is decoded from its stored position (`InternalY / DimensionBoundary`), so pre-existing waypoints are filtered correctly with no migration or re-save. The swap is client-side only - the server keeps the vanilla layer, so `/waypoint` commands, persistence and network sync are untouched. Editing or removing a pin through the map dialog still targets the right waypoint (original indices are preserved through the filter), temporary pins are still rendered, and the visible set re-filters immediately when the player travels between dimensions.
- **Real-engine E2E scenario suite** (`tests/Chart.Scenarios`, built on [Atlas](https://github.com/Pixnop/Atlas)) - boots a headless Vintage Story server with the published Manifold release zip and a fixture mod, and pins the engine and Manifold contracts Chart's client renderer is built on: the `DimensionBoundary` constant, the empty `RainHeightMap` in custom dimensions (the reason for the renderer's bounded fallback scan), the `cy + dim * 1024` chunk-slice encoding, dimension codes containing a colon, and the `Destroyed` event that drives tile-cache cleanup. Chart itself is client-only and cannot load in a headless server; these scenarios make a game or Manifold update that breaks an assumption fail CI instead of silently corrupting the map in game.
- **Waypoint filter scenarios.** Two Atlas scenarios have a real player create pins with `/waypoint add` in the overworld and in a Manifold dimension, capture the waypoint list the server sends that player, and run Chart's `WaypointDimension` over it: each pin shows only in its own dimension, and removing the pin visible on a dimension map by the index Chart keeps deletes that pin, not the overworld one. The client-side layer swap and the rendering stay untested (Atlas has no client).
- **CI** (GitHub Actions): build and unit tests with coverage and a SonarCloud quality gate on every push and pull request, plus a dedicated job running the Atlas scenarios against a real server install.
- **`PackMod` build target** producing the Mod DB release zip at `release/Chart-<version>.zip` (mod dll, `modinfo.json` and `modicon.png` only; the game and Manifold dlls are provided at runtime).

### Changed
- **Chart now lives in its own repository**, [Pixnop/Chart](https://github.com/Pixnop/Chart), extracted from the Manifold monorepo with history preserved. Manifold is consumed as the published `Pixnop.Manifold` NuGet package (`ExcludeAssets=runtime`) instead of a project reference, exercising the public API surface the same way any third-party companion would.
- Atlas test harness bumped from 0.4.0 to 0.15.0, dropping a local folder-mod staging workaround that Atlas now handles natively; the scenarios run against the Manifold 0.5.0 release. Chart still works with Manifold 0.4.1 or later.

## [0.2.0] - 2026-06-12

### Added
- **Automatic tile-cache cleanup for ephemeral dimensions**: Chart purges a dimension's cached tiles when Manifold destroys it, and a scan at world load removes orphan cache files left behind by crashes.

### Fixed
- **Major performance fix inside custom dimensions**: the map's dirty queue looped forever between adjacent chunks, and each column was re-rendered once per vertical chunk slice, pegging the client CPU. Both feedback loops are fixed; custom dimensions are now smooth.

### Changed
- Internal renderer refactor (same pixels, more maintainable pipeline).
- Requires Manifold 0.4.1 or later.

## [0.1.0] - 2026-05-25

### Added
- First release. Dimension-aware world map for [Manifold](https://github.com/Pixnop/Manifold) custom dimensions, alpha visual quality. Per-dimension tile storage on disk (`ModData/Chart/<savegame>/<dim>.bin`, deflate-compressed), hot map swap on dimension transit, vanilla render pipeline ported (13-color palette, snow and water edges, 3-neighbor hillshade, blurred shadow map). Known limitations: chunk-edge seams, softened hillshade, unfiltered cross-dimension waypoints, no ephemeral cache cleanup. Requires Manifold 0.3.1 or later.
