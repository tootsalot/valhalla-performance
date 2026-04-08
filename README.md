# ValhallaPerformance

Performance mod for Valheim that consolidates three existing mods I was running in my modpack into one plugin with a bunch of additions on top. If you were using [ValheimPerformanceOverhaul](https://thunderstore.io/c/valheim/p/Skarif/ValheimPerformanceOverhaul/), [Smoke_Collision](https://thunderstore.io/c/valheim/p/JereKuusela/Smoke_Collision/), or [VBNetTweaks](https://thunderstore.io/c/valheim/p/VitByr/VBNetTweaks/), this replaces all three.

**This is in beta.** I'm actively testing and fixing things. Feedback welcome at rare-relic-keg@duck.com.

Built for [Tootsalot's Valhalla Overhaul](https://thunderstore.io/c/valheim/p/tootsalot/Tootsalots_Valhalla_Overhaul/).

## What it does

Twelve systems, all individually toggleable, that work on both client and dedicated server (`valheim.exe` and `valheim_server.exe`). The plugin gates client-only systems at runtime so nothing breaks on a headless server.

**Smoke** replaces both VPO's smoke handling and Smoke_Collision. Lightweight upward drift with roof collision spherecasts, per-source caps, and distance culling. Keeps the visual behavior without tanking your CPU in a big base.

**Lights** does distance-based culling with hysteresis so you don't get that annoying flicker when you're right at the cull edge. Shadow casting gets its own proximity budget, and optional flicker freeze cuts down on shadow map churn.

**Culling** throttles AI updates and distant animations. Also has options for far skinned-mesh reduction and ragdoll sleep to help in dense scenes.

**Pieces** adds dirty-region processing on top of support caching so the game only recalculates support near pieces that actually changed, instead of broad sweeps.

**Memory** triggers GC during natural pauses, watches heap pressure, and runs safe asset cleanup on intervals.

**Network** replaces VBNetTweaks. Adaptive sync cadence based on ping and packet loss, smart ZDO prioritization (players > ships > mobs > everything else), optional zone-owner rebalancing by ping and distance, and transport tuning for send cadence and queue buffers. Also adds optional Zstd package compression. Network behavior is always independent of the client visual profiles.

**Render** handles LOD, shadows, particles, vegetation, terrain detail, minimap intervals, and frame budget guarding. Includes adaptive frame governor scaling, configurable grass patch sizing, optional realtime reflection probe disable, and texture mip limit control.

**Streaming** prefetches terrain and zones ahead of the player. Particularly useful when sailing or moving fast. Uses speed hysteresis with enter/exit hold delays so it doesn't rapid-toggle when you're hovering around the speed threshold.

**VFX** budgets and culls short-lived one-shot effects to reduce burst allocation spikes.

**Tar Pit Fix** scans for orphaned tar pit VFX on intervals and cleans up leaked effects. Also patches `LiquidVolume` lifecycle cleanup to prevent the long-session tarpit leak.

**Boot Optimizer** skips the logo intro and does incremental JIT warmup on critical methods to reduce first-use hitching.

**Log Filter** deduplicates repeated log entries and suppresses known noise while keeping warnings and errors intact. Makes the console actually readable in a heavy modpack.

A shared staggered scheduler spreads periodic scan work across frames so multiple systems aren't all doing heavy maintenance on the same tick.

## Performance Profiles

Five presets: `Potato`, `Low`, `Medium`, `High`, `Max`. These only affect client-side visual and performance baselines. Network settings are never touched by profiles. Potato is intentionally extreme (last-resort FPS mode), Max keeps smoke and light distances close to vanilla. Profiles apply once on config init when the selection changes, not every frame. No in-game menu for this, just use the BepInEx config file or r2modman's config editor.

## Building

Open `ValhallaPerformance.csproj` and set `ValheimDir` and `BepInExDir` to your local paths. Build the project. If `AutoDeployOnBuild` is true it'll copy the DLL straight to your plugins folder.

For manual install, drop `ValhallaPerformance.dll` into `BepInEx/plugins/ValhallaPerformance/`, launch once to generate config, then edit `BepInEx/config/valhalla.performance.cfg`.

## Project Layout

- `Plugin.cs` is the entry point and lifecycle management.
- `Cfg.cs` has all the config definitions.
- `Systems/` contains the individual optimization subsystems and Harmony patches.

## Conflicts

Don't run this alongside any of the three mods it replaces:
- `Skarif-ValheimPerformanceOverhaul`
- `JereKuusela-Smoke_Collision`
- `VitByr-VBNetTweaks`

## License

[MPL-2.0](LICENSE)
