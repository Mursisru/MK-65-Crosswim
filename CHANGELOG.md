# Changelog

## 0.0.0

### Added

- Standalone MK-65 Crosswim BepInEx plugin (`com.mursisru.mk65crosswim`)
- AShM hardpoint add-only inject, Dynamo/Argus VLS ship launcher
- Anti-torpedo intercept swim profile, CrosswimVisual nobp stamp, hangar rest pose
- UCUPaint Color bake pipeline: `GlossyMetal_Color.png` from Bake All Channels

### Fixed

- Wrong albedo sources (UCUPaint masks and AO used as base color)
- Green chrome gloss on painted body (`GlossyMetal` metallic=0, matte roughness)
- Per-slot materials: body Color bake, Metal2/LightMaterial solid tints on fins

### Notes

- Requires **BepInEx 5** and **Blueprinter**
- Deploy folder: `BepInEx/plugins/MK-65-Crosswim/` (dll, nobp, preview, `Textures/`)
