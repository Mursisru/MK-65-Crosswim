# MK-65 Crosswim

![version](https://img.shields.io/badge/version-0.0.0-blue)
![bepinex](https://img.shields.io/badge/BepInEx-5-informational)
![license](https://img.shields.io/badge/license-MIT-green)

Standalone BepInEx plugin: MK-65 Crosswim anti-torpedo interceptor for Nuclear Option.

> [!IMPORTANT]
> **Requires BepInEx 5 and Blueprinter.** Do not install next to a shared MissilePack bundle.

> [!NOTE]
> Aircraft: add-only on hardpoints that already list AShM-300. Ships: Dynamo and Argus VLS clone (add-only).

## Install

Copy `BepInEx/plugins/MK-65-Crosswim/` (`MK65Crosswim.dll`, `MK65Crosswim.nobp`, `PreviewCrosswim.png`, `Textures/` including `GlossyMetal_Color.png`).

## Flight

- Air: drop, shed docking port, ballistic, water, solid motor + Opening 120f, intercept swim (550 km/h).
- Ship: no docking port, VLSB loft ~220 m, then the same ballistic→swim chain.
- Warhead 100 kg HE. Homing prefers hostile underwater missiles, then ships.

## Bake

Unity 2022.3 batchmode: `-executeMethod BatchBuild.Build` in `UnityBake/`.
