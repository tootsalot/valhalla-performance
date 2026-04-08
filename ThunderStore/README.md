# ValhallaPerformance

All-in-one performance optimizer for Valheim with twelve modular systems for smoke, lights, culling, pieces, memory, networking, rendering, streaming, VFX, tar pit cleanup, boot optimization, and log filtering.

**This mod is in beta. I am actively testing features and fixing issues. Please email feedback to:** rare-relic-keg@duck.com

**Inspired by these mods:**
- ValheimPerformanceOverhaul (Skarif)
- Smoke_Collision (JereKuusela)
- VBNetTweaks (VitByr)

Works on both client and dedicated server setups (`valheim.exe` and `valheim_server.exe`). Every system can be toggled individually.

## Performance Profiles

ValhallaPerformance includes five client-side profile presets:
- `Potato`
- `Low`
- `Medium`
- `High`
- `Max`

Profile behavior:
- Profiles are selected in config under 1. Profile.
- Profiles apply client-side visual/performance baselines only.
- Network tuning is profile-independent. Profiles never override network settings.
- Zstd package compression is enabled by baseline for all profiles.
- Potato is intentionally extreme and meant as a last-resort FPS mode.
- Max keeps smoke and light distances close to vanilla with minimal smoke/light culling.
- Advanced users can still edit all individual config entries.
- Profile values apply during config init when the selected profile changes, not every frame.

There is currently no separate in-game profile menu and no plans to add one. Use the BepInEx config file or r2modman config editor.

## Systems

### Boot Optimizer
Skips logo intro flow with safe one-time bypass hooks and pre-compiles critical methods using incremental, time-sliced warmup to reduce first-use JIT hitching without one-frame bursts.

### Smoke System *(replaces Smoke_Collision + VPO smoke)*
Uses lightweight upward drift physics with roof collision spherecasts, per-source caps, and distance culling. Keeps smoke behavior while reducing CPU cost.

### Light System
Uses distance-based light culling with hysteresis to prevent cull-edge on/off flicker (including aggressive presets like Potato). Shadow casting is budgeted separately by proximity, and optional flicker freeze reduces per-frame shadow map churn in large bases.

### Culling System
Includes AI throttling plus distant animation throttling. Also supports far skinned-mesh reduction and optional far ragdoll sleep to reduce CPU overhead in dense scenes.

### Piece Optimizer
Maintains support caching and adds dirty-region processing so support work is focused near changed pieces instead of broad checks.

### Memory System
Performs GC on natural pauses, monitors heap pressure, and runs safe asset cleanup intervals.

### Network System *(replaces VBNetTweaks)*
Adds adaptive network control (ping/loss aware sync cadence), smart ZDO prioritization (players/ships/mobs/important objects), optional zone-owner rebalance by ping+distance, and transport tuning for send cadence/queue buffers. Includes optional package-level Zstd compression paths and keeps network behavior profile-independent from client visual presets.

### Render System
Applies LOD, shadow, particle, vegetation, terrain detail, minimap, and frame-budget guard settings. Includes adaptive frame governor scaling, configurable grass patch sizing (`ClutterSystem.m_grassPatchSize`), optional realtime reflection probe disable, and texture mip limit control via `QualitySettings.globalTextureMipmapLimit`.

### Streaming System
Adds ahead-of-player terrain/zone prefetching with travel-safe behavior (especially useful for high-speed travel and sailing). Travel-safe mode uses speed hysteresis plus enter/exit hold delays to avoid rapid toggling when movement hovers around threshold.

### VFX System
Adds a non-critical VFX budget/culling layer for short-lived one-shot effects to reduce burst allocation and rendering spikes.

### Tar Pit Fix
Periodically scans for orphaned tar pit VFX and removes leaked effects. Also applies a tar-specific `LiquidVolume` lifecycle fix that hardens thread/native-buffer cleanup on destroy to prevent long-session tarpit leaks.

### Log Filter
Deduplicates repeated logs, suppresses known noise, and keeps startup/runtime logs readable in heavy modpacks while preserving warnings/errors by default.

### Staggered Scheduler (Shared)
Periodic scan-based subsystem work is staggered to reduce frame-time spikes from multiple systems running heavy maintenance on the same frame.

---

## Key Settings

| Section | Setting | Default |
|---|---|---|
| Profile | Performance Profile | Medium |
| Boot | JIT Warmup | true |
| Smoke | Max Per Source | 10 |
| Lights | Max Active Lights | 18 |
| Culling | AI Throttle Distance / Interval | 60m / 4s |
| Culling | Animation Throttle Distance | 60m |
| Pieces | Dirty Region Updates | true |
| Memory | Heap Ceiling | 2048 MB |
| Render | Minimap Update Interval | 0.16s |
| Render | Adaptive Frame Governor | true |
| Render | Frame Budget Threshold | 28ms |
| Render | Grass Patch Size | 16 |
| Render | Adaptive Texture Scaling | false |
| Streaming | Zones Ahead | 1 |
| Streaming | Extra Zones While Fast | 2 (Medium baseline) |
| Streaming | Travel Speed Hysteresis | 1.2 |
| Streaming | Travel Enter / Exit Delay | 1.8s / 2.6s |
| VFX | Max Active Non-Critical VFX | 60 |
| Network | Near / Far Range | 64m / 128m |
| Network | Far Sync Interval | 4s |
| Network | Adaptive Control | true |
| Network | Adaptive Budget (min/max) | 100 / 260 |
| Network | Zone Owner Management | true |
| Network | Traffic Compression | true |
| Network | Zstd Compression | true |
| Network | Send FPS | 20 |
| Tar Pit | LiquidVolume Leak Fix | true |
| Scheduler | Enable Stagger Scheduler | true |

---

## Compatibility

**These are likely to conflict with ValhallaPerformance:**
- `Skarif-ValheimPerformanceOverhaul` (overlapping performance patches)
- `JereKuusela-Smoke_Collision` (smoke collision already included)
- `VitByr-VBNetTweaks` (network optimization already included)

---

## Contact

rare-relic-keg@duck.com

---

## Credits

Built by **Tootsalot** for [Tootsalot's Valhalla Overhaul](https://thunderstore.io/c/valheim/p/tootsalot/Tootsalots_Valhalla_Overhaul/).
