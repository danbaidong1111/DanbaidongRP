# ReSTIR Global Illumination

This implementation intentionally follows the older DanbaidongRP prototype supplied with the project. The first integration target is behavioral familiarity; visibility-aware and unbiased variants can be layered on later without changing the render-pipeline integration.

## Pass mapping

1. `ReSTIRGlobalIllumination.raytrace` (`SingleRayGen`) generates cosine-weighted one-bounce candidates and performs initial reservoir sampling. Ray Marching uses `ReSTIRGIInitialScreenSpace` instead, without a ray-tracing acceleration structure.
2. `ReSTIRGlobalIllumination.compute` reprojects the previous reservoir, validates depth and normals, applies the optional temporal Jacobian, and performs biased spatial reuse. Spatial also resolves GI. In temporal-only mode the history copy and resolve share one dispatch.
3. `ReSTIRGlobalIlluminationDenoiser.compute` performs history reprojection with neighborhood clamping and luminance moments, followed by two edge-aware A-trous iterations (steps 1 and 2).
4. `ReSTIRGlobalIlluminationPass.cs` allocates double-buffered camera histories and records the complete RenderGraph sequence before deferred lighting.

## Resolution and spatial settings

The GI Volume exposes `Resolution: Full / Half` immediately below Tracing for both tracing paths. Changing this selection resets sampling, reservoir reuse and denoising parameters to their mode defaults; the values below remain editable. The Full performance preset uses 1 candidate, 1 neighbor, radius 16 and temporal denoising only. The Half preset uses 2 candidates, 2 neighbors, radius 32 and both denoisers. Both radii are expressed in source pixels. Scene-dependent ray length, clamp, intensity and layer settings are preserved. The reset is an inspector operation, not a per-frame runtime override. Existing profiles are not automatically overwritten. Candidate count is capped at 8 in both the Volume and shader dispatch path.

Half uses half-width, half-height reservoir and denoiser textures: a fixed receiver at source `(2*x, 2*y)` represents each 2x2 cell. Current and previous frame history therefore use the same receiver grid. Pixel-center mapping uses the source texture size explicitly, including odd viewport dimensions, and the final output uses a depth/normal-aware upsample.

Alternating checkerboard sampling is disabled. RTXDI's FullSample pairs its packed checkerboard inputs with NRD's checkerboard mode. Feeding those inputs directly through this pipeline's ordinary packed temporal denoiser repeatedly interpolates history between left/right receivers, spreading energy horizontally even with zero motion. Fixed receivers avoid that additional resampling without adding full-resolution denoiser histories.

## Reservoir estimator

Initial directions use the precomputed cosine STBN vector masks (`pdf = cos(theta) / PI`). Candidates use distinct slices, with stride 17 and at most 8 candidates. After each 64-frame cycle the complete mask is translated in texture space so a stationary receiver does not endlessly reuse the exact same 64 vectors. This preserves each slice's spatial distribution instead of scrambling directions independently per pixel. Direction sampling is separate from reservoir selection. The Hammersley experiment was removed in favor of the low-spp STBN path.

The target is `Luminance(radiance * max(cos(theta), 0.001))` on the positive hemisphere. The final diffuse contribution uses the actual cosine and divides by PI; receiver albedo is applied by deferred shading, not stored in the GI texture. The stored `avgWeight` is the normalized reservoir weight:

`avgWeight = (weightSum / M) / target(selectedSample)`

Black candidates still increment `M`, including all-black reservoirs with zero weight. They cannot replace a positive-weight selected sample. Do not discard their sample counts when merging reservoirs.

Reservoir selection uses independent PCG streams for initial, temporal and spatial resampling. Floats are constructed in `[0,1)` with 23 random mantissa bits: neither an 8-bit UNorm endpoint of 1 nor `frac(uintAsFloat / 65536)` is appropriate here. This follows the [RTXDI SDK's random-sampling guidance](https://github.com/NVIDIA-RTX/RTXDI-Library/blob/main/Include/Rtxdi/Utils/RandomSamplerState.hlsli) to separate random streams per pass and avoid low-entropy table-only reservoir selection; it does not copy the SDK's RNG implementation.

The Volume history length is in frame-equivalent samples. Its history `M` cap is `restirMaxHistoryLength * sampleCount`, so increasing initial candidates does not shorten the effective temporal window. Sample age remains separately limited. Changing candidate count invalidates both reservoir and denoiser history. Invalid history is not fetched before rejection.

Temporal and spatial reuse transport the selected secondary point to a new receiver. Temporal Jacobian remains optional; spatial Jacobian remains commented out. Neither visibility rays nor bias correction have been added by these optimizations.

## Bandwidth and scheduling

- RayGen writes each reservoir once, including empty background pixels.
- Temporal denoise loads a 10x10 tile for an 8x8 output group and 3x3 statistics, instead of a 16x16 tile. All lanes still participate in synchronization.
- Boiling reduction uses two barriers with supported wave intrinsics, or a portable row/group reduction with three barriers instead of seven. Filter thresholds are unchanged.
- Spatial reads only the hit/light reservoir records and also produces resolved GI. Its packed streaming implementation does not decode/re-encode selected hit normals or radiance, reuses the selected target/cosine, and avoids loading hit records for zero-weight reservoirs while still counting their M. Receiver depth/normal rejection is retained.
- A required history copy preserves packed bits; temporal-only copy also resolves GI to avoid reading the reservoir again in a separate dispatch.
- Full resolution publishes the final producer texture directly. Only Half dispatches depth/normal-aware upsampling.
- Fully overwritten transient textures are not redundantly cleared. Background/invalid output paths must continue writing explicit values.

## Validation

`Tests/Editor/ReSTIRGIRegression.compute` exercises the production RNG, candidate sequence and reservoir functions on the GPU. `ReSTIRGIRegressionTests.cs` supplies the EditMode test runner when package tests are enabled. The cases cover random range / stream separation, constant-radiance energy, alternating black candidates, all-black inputs, and variance reduction with more candidates. These analytic tests do not substitute for testing scene motion, disocclusion and colored indirect lighting. Initial RIS still retains one selected sample, so increasing candidates is not equivalent to retaining and averaging N independent RGB estimates after spatial transport.

For scene comparisons, keep exposure, resolution, camera and elapsed accumulation frames fixed. Compare 1/2/4/8 initial candidates first with reuse and denoising disabled, then enable each stage. GPU timings must identify the GPU, viewport size, candidate count, neighbor count and enabled passes. Increasing candidate count still traces and shades every candidate; the intended real-time configuration is a small initial ray budget with reuse/denoising. `TestSpatialPacked` also compares the production packed merge against the original reservoir estimator, including black candidates and samples below the receiver hemisphere.

## Screen-space path

Select `Tracing = Ray Marching`. If hardware ray tracing is unavailable/disabled, requested ray-traced GI also falls back to screen space. Switching the effective backend invalidates reservoir and denoiser history. Mixed still uses the ray-traced backend when available; a per-ray hybrid fallback is not implemented.

The screen-space implementation follows HDRP's `RayMarching.hlsl` and `ScreenSpaceGlobalIllumination.compute`: packed hierarchical-depth traversal with thickness, cosine directions, motion-vector reprojection of the hit point, previous-depth validation, and mip-1 previous scene-color sampling. Tracing and radiance fetch are fused into initial reservoir generation; the existing reuse, denoising and edge-aware upsample are shared. It does not allocate another GBuffer or normal history. A frame without valid color/depth history writes empty reservoirs instead of sampling uninitialized history.

The input color pyramid is scene-linear, captured before post-processing. Like HDRP SSGI, it contains view-dependent lighting and previous indirect illumination, so this is not ground-truth world-space one-bounce GI. Missing/off-screen geometry cannot occlude the fallback. Sky-enabled misses can brighten enclosed rooms; use `Ray Miss = None` for a conservative screen-space-only estimate. Local reflection-probe and APV fallback are not implemented. The fallback setting is not changed automatically.

## Ray payload

The indirect payload stores `t`, radiance, recursion/sample bookkeeping, pixel coordinates, and a two-component octahedral hit normal. It does not store a ray cone or hit position. The ray-generation shader reconstructs the position from `Origin + Direction * t`.

Temporal reuse, spatial reuse, and resolve are compute passes and therefore do not add a visibility payload to the indirect DXR pipeline. This avoids the D3D12 pipeline-state error caused by mixing the 24-byte visibility payload with the former 72-byte indirect payload in one ray-tracing shader.

## Current limits

- Deferred rendering is required. Hardware ray tracing is only required by the ray-traced backend.
- The ray-traced backend generates one indirect bounce. Screen-space history may contain previous indirect light.
- Full-resolution and fixed 2x2 half-resolution modes are available.
- Spatial reuse is the biased form used by the old prototype.
- Reused samples are not re-tested with a visibility ray during resolve.
- Environment misses use the sky fallback from the old ray-generation shader.
- Reservoir history uses three `R32G32B32A32_UInt` textures.

These are deliberate compatibility limits for the first port and are the natural extension points for later work.
