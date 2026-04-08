## v1.3.0 (Beta)
- Synced package/runtime metadata to 1.3.0 and updated Thunderstore docs for the current codebase
- Expanded config/profile baseline system with additional render and network profile-managed settings
- Added new render config entries: `GrassPatchSize`, `DisableRealtimeReflections`, `AdaptiveTextureScaling`, `BaseTextureLimit`
- Added new network config entries: `EnableZstdCompression`, `ZstdCompressionLevel`
- Expanded `ClientProfileValues` and updated all five profile tiers with the new fields and revised tuning values
- `ApplyProfileValues()` now applies the new profile-managed entries and enforces Zstd compression as enabled
- Profile baseline version advanced to 14 to ensure new baselines re-apply automatically
- Added dedicated process support for `valheim_server.exe` and runtime dedicated/client detection
- Gated client-only systems off on dedicated servers for safe headless operation
- Render System: added `ClutterSystem.Awake` patch to apply `m_grassPatchSize` from config
- Render System: added realtime reflection probe toggle
- Render System: adaptive texture limit control now uses `QualitySettings.globalTextureMipmapLimit` and restores on cleanup
- Replaced obsolete `QualitySettings.masterTextureLimit` usage with `globalTextureMipmapLimit`
- Network System: implemented per-peer adaptive far interval and sync budget based on each peer's connection quality
- Network System: `CreateSyncList` now uses per-peer interval/budget instead of only global adaptive state
- Network System: added Zstd package compression/decompression hooks for `ZRpc.SendPackage` and `ZRpc.HandlePackage`
- Network System: uses thread-static Zstd compressor/decompressor instances and magic-byte payload tagging
- Fixed Harmony patch parameter binding for ZRpc package patches to avoid signature/name mismatch errors
- Hardened socket tuning compatibility with cached reflection lookups and transport fallback behavior
- Reduced Harmony warning noise by using compatibility-safe runtime method/field resolution in changed game signatures
- Log System policy updated to keep warnings/errors visible; Harmony and shader warning muting defaults remain disabled
- Boot System: changed JIT warmup from one-shot burst to incremental time-sliced warmup to reduce hitch spikes
- Culling/Light/VFX hot paths optimized to reduce scan clumping, allocations, and sort-heavy work
- Retuned lower profile tiers after regression testing and disabled adaptive texture scaling baselines due confirmed regression risk

## v1.2.1 (Beta)
- Fixed Harmony prefix patches in SmokeSystem, LightSystem, and CullingSystem that were void instead of returning bool - original methods were always executing, making throttle/freeze/skip logic a no-op
- Fixed SmokeSystem Prefix_CustomUpdate: double m_time advancement and double position updates no longer occur
- Fixed LightSystem FlickerPatch.Prefix: flicker freeze now actually freezes LightFlicker.CustomUpdate
- Fixed CullingSystem AI throttle prefixes (MonsterAI, AnimalAI, WearNTear): all now correctly skip originals when throttled
- Wired MaxLights config to enforcement in LightSystem.ManageLights() - farthest non-directional lights are disabled when over budget after distance culling
- Wired CreatureCullDist config to CullingSystem - creatures beyond this distance are fully skipped (unless alerted); creates three-tier system: full rate / throttled / culled
- Added CreatureCullDist to all five profile presets: Potato=45, Low=60, Medium=80, High=120, Max=200
- Bumped profile baseline version to 7

## v1.2.0 (Beta)
- Added adaptive network control that scales far-sync interval and sync budget from live ping/loss quality
- Added smart ZDO prioritization for players, ships, mobs, and high-value world objects
- Added optional zone-owner management that reassigns zone-control ownership by distance+ping score
- Added transport tuning controls (send FPS, socket queue/buffer tuning, backend compression toggles)
- Added tar-specific LiquidVolume lifecycle hardening to prevent thread/native-array leaks in vanilla tarpits
- Added a real profile-driven client tuning baseline system with five presets: `Potato`, `Low`, `Medium`, `High`, `Max`
- Added two new client modules:
  - `Streaming System`: ahead-of-player terrain/zone prefetch with travel-safe mode
  - `VFX System`: global budget/culling for non-critical one-shot effects
- Added distant animation throttling (Animator + SkinnedMeshRenderer) and optional far-ragdoll sleeping
- Added adaptive frame governor for dynamic client visual scaling under frametime pressure
- Added piece dirty-region processing to focus support updates around changed pieces
- Added central stagger scheduler to spread periodic subsystem work across frames
- Hardened intro/logo skip flow with multi-hook fallback behavior
- Improved minimap throttling safety so map-interaction updates are not starved
- Expanded profile-managed settings to include new render/streaming/vfx/animation/piece tuning knobs
- Networking remains profile-independent from client visual presets
- Synced runtime and package version metadata to 1.2.0
- Boot skip path stabilized to avoid scene reload loops on startup
- Light culling updated with hysteresis to prevent cull-edge flicker (notably on Potato/Low)
- Streaming reflection probes softened to avoid startup warning spam when optional methods are missing
- Low/Potato profile baselines no longer force soft particles off (prevents square particle artifacts on affected clients)
- Added travel-safe speed hysteresis plus enter/exit delays to prevent rapid mode flapping while moving near threshold
- Corrected profile LOD progression so lower presets now use lower detail bias instead of inadvertently increasing detail
- Hardened WearNTear patch compatibility with runtime method resolution to reduce missing-method warnings across Valheim updates
- Activated support-cache read path through compatibility-safe support method hooks, with structural-change invalidation to prevent stale support values

## v1.0.1 (Beta)
- Updated README

## v1.0.0 (Beta)
- Initial release - 10 independent optimization systems
- Replaces ValheimPerformanceOverhaul, Smoke_Collision, and VBNetTweaks
- Boot: skip intro logos, JIT warmup for critical methods
- Smoke: lightweight drift physics + roof collision via spherecast, per-source particle cap, distance culling
- Lights: distance-based light culling, shadow stripping, flicker freeze
- Culling: AI throttle for distant creatures, WearNTear sleep, LOS caching
- Pieces: GetSupport result caching, async WearNTear initialization
- Memory: strategic GC timing, heap monitoring, asset sweeps, audio pooling
- Network: distance-tiered ZDO sync, data revision skip, player priority
- Render: LOD bias, soft particle/vegetation toggle, frame budget guard
- Tar Pit: orphaned VFX leak scanner and cleanup
- Log: message deduplication, noise suppression, Harmony startup muting
