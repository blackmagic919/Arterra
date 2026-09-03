# Atmosphere Sun-Ray Optical Depth Volume Design

## Goal
Replace per-sample sun-ray marching in the atmosphere bake with a reusable light-space volume that stores optical depth/transmittance to the sun. This improves performance and accuracy by integrating each light column once and reusing it across many camera samples.

## Why This Change
Current approach in `OpticalData.compute` computes `opticalDepth(inScatterPoint, _LightDirection, sunRayLength)` for every in-scatter point. That is expensive and undersamples long sun rays.

A precomputed sun-ray volume allows:
- More effective samples along light direction at fixed cost.
- Better temporal stability and quality.
- O(1) lookup during fog/in-scatter evaluation via trilinear sampling.

## Coordinate System
Use a matrix-based transform for world <-> frustum light space.

- World to light space: `LS = mul(worldToLight, float4(WS, 1)).xyz`
- Light to world space: `WS = mul(lightToWorld, float4(LS, 1)).xyz`

These matrices are already reflected in `FrustumLightSpaceHelper.hlsl`.

## Frustum Shadow Footprint Parameterization

### Key Idea
Parameterize XY over the frustum shadow footprint using barycentric coordinates, but store results in a rectangular texture domain so hardware trilinear filtering remains usable.

### Recommended Mapping (Square -> Triangle)
For a 2D texel index `(i, j)` in an `N x N` grid:

- `u = (i + 0.5) / N`
- `v = (j + 0.5) / N`
- `s = sqrt(u)`
- `b0 = 1 - s`
- `b1 = s * (1 - v)`
- `b2 = s * v`

Then the light-space XY point in projected triangle `(A, B, C)` is:

- `Pxy = b0 * A + b1 * B + b2 * C`

This gives area-uniform coverage of the triangle and avoids over-focusing near one corner.

## Depth Axis
For each XY sample, march along light-space Z (sun direction) with `Nz` slices:

- `t = (k + 0.5) / Nz`
- `z = lerp(zEntry(u, v), zExit(u, v), t)`

`zEntry/zExit` can start as global slab bounds and later be refined to per-XY entry/exit maps for tighter coverage.

## Storage Layout

### Preferred
Use a 3D texture (or 2D array equivalent) with dimensions:
- `N x N x Nz`

Store one of:
- Cumulative optical depth `tauRGB`
- Transmittance `TRGB = exp(-tauRGB)`

Trilinear sampling works directly in `(u, v, t)`.

### Optional Compact Triangle Packing
A compact triangular buffer saves memory but loses native trilinear interpolation and requires custom neighbor fetch/interpolation. Avoid for first implementation.

## Reverse Mapping for Runtime Lookup
Given a world point `WS`:

1. Convert to light space `LS` with `worldToLight`.
2. Compute barycentric `(b0, b1, b2)` of `LS.xy` against triangle `(A, B, C)`.
3. Invert to rectangular sample domain:
   - `s = b1 + b2`
   - `u = s * s`
   - `v = b2 / max(s, eps)`
4. Convert Z to depth coordinate:
   - `t = (LS.z - zEntry(u, v)) / max(zExit(u, v) - zEntry(u, v), eps)`
5. Sample volume at `(u, v, t)`.

## Prepass Pipeline

1. Build frustum shadow triangle `(A, B, C)` in light-space XY from camera frustum projection.
2. Build `worldToLight` and `lightToWorld` matrices.
3. Dispatch prepass kernel over `(N, N, Nz)`.
4. For each `(i, j)`:
   - Map to barycentric XY point with square->triangle transform.
5. For each `k` along Z:
   - Reconstruct `WS` sample point.
   - Sample density/extinction map.
   - Integrate cumulative optical depth.
6. Write cumulative value into volume texture.

## Runtime Integration

In atmosphere evaluation:
- Replace per-point sun-ray optical-depth marching with one volume lookup.
- Keep camera-segment integration path unchanged initially.

## Performance and Quality Notes
- Choose `N=128`, `Nz=64..128` as initial range.
- Snap frustum/light-space bounds to texel size to reduce shimmer.
- Use half precision for storage only after validating precision.
- Start with `tau` storage for easier debugging; convert to `T=exp(-tau)` at use time.

## Validation Plan

1. Compare old vs new transmittance on fixed camera/light snapshots.
2. Check edge cases where frustum exits atmosphere.
3. Profile GPU time of optical prepass and main fog pass.
4. Validate temporal stability while rotating camera and sun.

## Risks
- Footprint may not always be exactly triangular for all clipping cases.
- Entry/exit depth approximation can introduce bias near boundaries.
- Incorrect inverse mapping can create subtle lookup distortions.

## Mitigations
- Support split into two triangles when needed.
- Add occupancy mask for valid volume cells.
- Keep debug views for `(u, v, t)`, barycentrics, and sampled tau.

## Suggested Next Implementation Tasks

1. Add barycentric helper functions to `FrustumLightSpaceHelper.hlsl`:
   - Square->triangle mapping
   - Barycentric solve
   - Triangle->square inverse
2. Add new compute prepass for sun-ray optical depth volume generation.
3. Add volume resource binding and matrix/triangle uniform upload from C#.
4. Swap sun optical-depth lookup in atmosphere bake path behind a feature flag.
