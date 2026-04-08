using BepInEx.Configuration;
using BepInEx.Logging;

namespace ValhallaPerformance
{
    public enum PerformanceProfile
    {
        Potato,
        Low,
        Medium,
        High,
        Max
    }

    public static class Cfg
    {
        // Profile
        public static ConfigEntry<PerformanceProfile> Profile;
        private static ConfigEntry<PerformanceProfile> LastAppliedProfile;
        private static ConfigEntry<int> LastAppliedProfileVersion;
        private const int ProfileBaselineVersion = 14;

        // Toggles
        public static ConfigEntry<bool> EnableBoot, EnableSmoke, EnableLights;
        public static ConfigEntry<bool> EnableCulling, EnablePieces, EnableMemory;
        public static ConfigEntry<bool> EnableNetwork, EnableRender, EnableTarPit, EnableLog;
        public static ConfigEntry<bool> EnableStreaming, EnableVfx;

        // Boot
        public static ConfigEntry<bool> SkipIntro;
        public static ConfigEntry<bool> JITWarmup;

        // Smoke
        public static ConfigEntry<float> SmokeLift, SmokeLife, SmokeCullDist;
        public static ConfigEntry<int> SmokeMaxPerSource;
        public static ConfigEntry<bool> SmokeCollision;
        public static ConfigEntry<float> SmokeCollisionRadius;

        // Lights
        public static ConfigEntry<int> MaxLights;
        public static ConfigEntry<float> LightCullDist, ShadowCullDist;
        public static ConfigEntry<float> LightScanInterval;
        public static ConfigEntry<bool> FreezeFlicker;

        // Culling
        public static ConfigEntry<float> CreatureCullDist, PieceCullDist;
        public static ConfigEntry<float> AIThrottleDist, AIThrottleInterval;
        public static ConfigEntry<float> LOSCacheDuration;
        public static ConfigEntry<bool> TamedIdleLowPower;
        public static ConfigEntry<float> TamedIdleDistance, TamedIdleInterval;
        public static ConfigEntry<float> AnimThrottleDist, AnimScanInterval, AnimFarSpeed;
        public static ConfigEntry<bool> AnimAggressiveCull, SleepFarRagdolls;

        // Pieces
        public static ConfigEntry<float> SupportCacheTTL;
        public static ConfigEntry<bool> AsyncWearInit;
        public static ConfigEntry<int> AsyncWearBatchSize;
        public static ConfigEntry<bool> PieceDirtyRegion;
        public static ConfigEntry<float> PieceDirtyRadius, PieceDirtyInterval;
        public static ConfigEntry<int> PieceDirtyBatch;

        // Memory
        public static ConfigEntry<int> HeapCeilingMB;
        public static ConfigEntry<float> AssetSweepInterval;
        public static ConfigEntry<bool> GCOnSceneLoad, GCOnPause;
        public static ConfigEntry<bool> PoolItemDrops, PoolAudio;
        public static ConfigEntry<int> ItemPoolSize, AudioPoolSize;

        // Network (intentionally profile-independent)
        public static ConfigEntry<float> NearRange, FarRange, FarInterval;
        public static ConfigEntry<bool> PriorityPlayers, PrioritizeShips, PrioritizeMobs, PrioritizeImportant, SkipUnchanged;
        public static ConfigEntry<int> SendBufferSize;
        public static ConfigEntry<bool> AdaptiveNetworkControl;
        public static ConfigEntry<float> AdaptivePingGoodMs, AdaptivePingBadMs, AdaptiveLossBad;
        public static ConfigEntry<float> AdaptiveFarIntervalMin, AdaptiveFarIntervalMax;
        public static ConfigEntry<int> AdaptiveSyncBudgetMin, AdaptiveSyncBudgetMax;
        public static ConfigEntry<bool> ZoneOwnerManagement;
        public static ConfigEntry<float> ZoneOwnerUpdateInterval, ZoneOwnerMaxDistance, ZoneOwnerPingGainMs;
        public static ConfigEntry<bool> EnableTrafficCompression, CompressionPlayFabOnly;
        public static ConfigEntry<int> CompressionMinBytes;
        public static ConfigEntry<float> NetworkSendFPS;
        public static ConfigEntry<bool> SteamSocketTuning;
        public static ConfigEntry<bool> EnableZstdCompression;
        public static ConfigEntry<int> ZstdCompressionLevel;

        // Render
        public static ConfigEntry<float> LODBias, ShadowDist;
        public static ConfigEntry<bool> DisableSoftParticles, DisableSoftVeg;
        public static ConfigEntry<int> ParticleRayBudget;
        public static ConfigEntry<float> GrassDensity, DetailDist;
        public static ConfigEntry<bool> MinimapOptimize;
        public static ConfigEntry<float> MinimapUpdateInterval;
        public static ConfigEntry<bool> FrameBudgetGuard;
        public static ConfigEntry<float> FrameBudgetThresholdMs;
        public static ConfigEntry<bool> AdaptiveFrameGovernor;
        public static ConfigEntry<float> AdaptiveMinScale, AdaptiveDownStep, AdaptiveUpStep;
        public static ConfigEntry<float> AdaptiveBadFrameMs;
        public static ConfigEntry<float> GrassPatchSize;
        public static ConfigEntry<bool> DisableRealtimeReflections;
        public static ConfigEntry<bool> AdaptiveTextureScaling;
        public static ConfigEntry<int> BaseTextureLimit;

        // Tar Pit
        public static ConfigEntry<float> TarCleanupInterval;
        public static ConfigEntry<bool> TarLiquidLeakFix;

        // Log
        public static ConfigEntry<float> DedupeWindow;
        public static ConfigEntry<int> ReportInterval;
        public static ConfigEntry<bool> MuteHarmony, MuteShaders;
        public static ConfigEntry<LogLevel> MinLogLevel;

        // Streaming
        public static ConfigEntry<float> StreamingPrefetchInterval;
        public static ConfigEntry<int> StreamingZonesAhead, StreamingExtraZonesSailing;
        public static ConfigEntry<bool> StreamingSafeMode, StreamingRelaxCleanup;
        public static ConfigEntry<float> StreamingSpeedThreshold, StreamingSpeedHysteresis;
        public static ConfigEntry<float> StreamingEnterDelay, StreamingExitDelay;
        public static ConfigEntry<float> StreamingCullBoost, StreamingSweepMultiplier;

        // VFX
        public static ConfigEntry<int> VfxMaxActive;
        public static ConfigEntry<float> VfxCullDist, VfxScanInterval;
        public static ConfigEntry<bool> VfxHardCull;

        // Scheduler
        public static ConfigEntry<bool> EnableStaggerScheduler;
        public static ConfigEntry<float> StaggerJitter;

        public static void Init(ConfigFile c)
        {
            // 1. Profile
            Profile = c.Bind("1. Profile", "Performance Profile", PerformanceProfile.Medium,
                "Client-side visual/performance preset baseline (Potato, Low, Medium, High, Max).\n" +
                "Only changes client-side visuals/performance tuning. Networking behavior is unchanged.");

            // 2. Modules
            EnableBoot = c.Bind("1. Modules", "Boot Optimizer", true,
                "Skip intro logos and pre-compile hot methods on first spawn.");
            EnableSmoke = c.Bind("1. Modules", "Smoke System", true,
                "Lightweight smoke physics with simple collision. Replaces Smoke_Collision and VPO smoke.");
            EnableLights = c.Bind("1. Modules", "Light System", true,
                "Distance-based light culling, shadow stripping, flicker freeze.");
            EnableCulling = c.Bind("1. Modules", "Culling System", true,
                "AI throttling and distance-based culling optimizations.");
            EnablePieces = c.Bind("1. Modules", "Piece Optimizer", true,
                "WearNTear support caching plus dirty-region updates.");
            EnableMemory = c.Bind("1. Modules", "Memory System", true,
                "GC timing, asset sweeps, object pooling.");
            EnableNetwork = c.Bind("1. Modules", "Network System", true,
                "ZDO sync throttling, priority sending, data dedup. Replaces VBNetTweaks.");
            EnableRender = c.Bind("1. Modules", "Render System", true,
                "Engine quality tweaks, terrain detail, minimap throttling, adaptive frame governor.");
            EnableTarPit = c.Bind("1. Modules", "Tar Pit Fix", true,
                "Cleans orphaned tar pit VFX.");
            EnableLog = c.Bind("1. Modules", "Log Filter", true,
                "Deduplicates and suppresses noisy logs.");
            EnableStreaming = c.Bind("1. Modules", "Streaming System", true,
                "Ahead-of-player terrain/zone prefetching with travel-safe behavior.");
            EnableVfx = c.Bind("1. Modules", "VFX System", true,
                "Global budget/culling for non-critical one-shot VFX.");

            // 3. Boot
            SkipIntro = c.Bind("2. Boot", "Skip Intro", true,
                "Skip Iron Gate and Coffee Stain logo screens on launch.");
            JITWarmup = c.Bind("2. Boot", "JIT Warmup", true,
                "Pre-compile critical methods on first spawn to reduce first-use stutter.");

            // 4. Smoke
            SmokeLift = c.Bind("3. Smoke", "Lift Force", 0.5f,
                "Upward force on smoke particles.");
            SmokeLife = c.Bind("3. Smoke", "Lifetime", 12f,
                "Seconds before smoke fades.");
            SmokeMaxPerSource = c.Bind("3. Smoke", "Max Per Source", 12,
                "Cap smoke particles per local source area.");
            SmokeCullDist = c.Bind("3. Smoke", "Cull Distance", 40f,
                "Skip smoke physics beyond this distance.");
            SmokeCollision = c.Bind("3. Smoke", "Enable Collision", true,
                "Simple raycast collision so smoke does not clip through roofs.");
            SmokeCollisionRadius = c.Bind("3. Smoke", "Collision Check Radius", 0.3f,
                "Spherecast radius for smoke roof checks.");

            // 5. Lights
            MaxLights = c.Bind("4. Lights", "Max Active Lights", 20,
                "Maximum simultaneous dynamic lights.");
            LightCullDist = c.Bind("4. Lights", "Cull Distance", 55f,
                "Lights beyond this distance are disabled.");
            ShadowCullDist = c.Bind("4. Lights", "Shadow Cull Distance", 35f,
                "Shadows disabled beyond this distance.");
            LightScanInterval = c.Bind("4. Lights", "Scan Interval", 0.75f,
                "Seconds between dynamic light management passes.");
            FreezeFlicker = c.Bind("4. Lights", "Freeze Flicker", true,
                "Fix torch/fire intensity at base value to reduce shadow-map churn.");

            // 6. Culling
            CreatureCullDist = c.Bind("5. Culling", "Creature Sleep Distance", 80f,
                "Creatures beyond this distance pause selected update work.");
            PieceCullDist = c.Bind("5. Culling", "Piece Sleep Distance", 100f,
                "WearNTear updates beyond this distance are skipped.");
            AIThrottleDist = c.Bind("5. Culling", "AI Throttle Distance", 60f,
                "Monsters beyond this distance use throttled AI updates.");
            AIThrottleInterval = c.Bind("5. Culling", "AI Throttle Interval", 4f,
                "Seconds between distant AI updates.");
            LOSCacheDuration = c.Bind("5. Culling", "LOS Cache Duration", 0.5f,
                "Seconds to cache line-of-sight check results.");
            AnimThrottleDist = c.Bind("5. Culling", "Animation Throttle Distance", 70f,
                "Distance where animator and skinned-mesh throttling starts.");
            AnimScanInterval = c.Bind("5. Culling", "Animation Scan Interval", 0.5f,
                "Seconds between animation throttling scans.");
            AnimFarSpeed = c.Bind("5. Culling", "Far Animator Speed", 0.55f,
                "Animator speed multiplier beyond throttle distance.");
            AnimAggressiveCull = c.Bind("5. Culling", "Aggressive Animator Culling", true,
                "Use CullCompletely for far animators.");
            SleepFarRagdolls = c.Bind("5. Culling", "Sleep Far Ragdolls", true,
                "Force far ragdoll rigidbodies to sleep sooner.");
            TamedIdleLowPower = c.Bind("5. Culling", "Tamed Idle Low-Power", true,
                "Throttle tamed idle AnimalAI updates when they are calm, unfed-state is false, and far from the player.");
            TamedIdleDistance = c.Bind("5. Culling", "Tamed Idle Distance", 25f,
                "Tamed animals closer than this distance keep normal AI update rate.");
            TamedIdleInterval = c.Bind("5. Culling", "Tamed Idle Interval", 2.5f,
                "Seconds between low-power AI ticks for eligible tamed animals.");

            // 7. Pieces
            SupportCacheTTL = c.Bind("6. Pieces", "Support Cache TTL", 5f,
                "Seconds to cache WearNTear support values.");
            AsyncWearInit = c.Bind("6. Pieces", "Async WearNTear Init", true,
                "Spread WearNTear initialization across frames on scene load.");
            AsyncWearBatchSize = c.Bind("6. Pieces", "Async Batch Size", 20,
                "WearNTear components initialized per frame during async init.");
            PieceDirtyRegion = c.Bind("6. Pieces", "Dirty Region Updates", true,
                "When piece state changes, update nearby support graph region first.");
            PieceDirtyRadius = c.Bind("6. Pieces", "Dirty Region Radius", 14f,
                "Meters around a dirty piece to update support state.");
            PieceDirtyInterval = c.Bind("6. Pieces", "Dirty Region Interval", 0.25f,
                "Seconds between dirty-region processing steps.");
            PieceDirtyBatch = c.Bind("6. Pieces", "Dirty Region Batch", 32,
                "Max WearNTear components processed per dirty step.");

            // 8. Memory
            HeapCeilingMB = c.Bind("7. Memory", "Heap Ceiling MB", 2048,
                "Trigger cleanup when managed heap exceeds this value. 0 disables.");
            AssetSweepInterval = c.Bind("7. Memory", "Asset Sweep Interval", 300f,
                "Seconds between unused-asset sweeps. 0 disables.");
            GCOnSceneLoad = c.Bind("7. Memory", "GC On Scene Load", true, "");
            GCOnPause = c.Bind("7. Memory", "GC On Pause", true, "");
            PoolItemDrops = c.Bind("7. Memory", "Pool Item Drops", true,
                "Reuse item drop GameObjects.");
            PoolAudio = c.Bind("7. Memory", "Pool Audio Sources", true,
                "Reuse AudioSource components.");
            ItemPoolSize = c.Bind("7. Memory", "Item Pool Size", 64, "");
            AudioPoolSize = c.Bind("7. Memory", "Audio Pool Size", 32, "");

            // 9. Network
            NearRange = c.Bind("8. Network", "Near Range", 64f,
                "Full sync rate within this distance.");
            FarRange = c.Bind("8. Network", "Far Range", 128f,
                "Reduced sync rate beyond this distance.");
            FarInterval = c.Bind("8. Network", "Far Sync Interval", 4f,
                "Seconds between distant sync attempts.");
            PriorityPlayers = c.Bind("8. Network", "Prioritize Players", true,
                "Always sync player ZDOs at full rate.");
            PrioritizeShips = c.Bind("8. Network", "Prioritize Ships", true,
                "Give boats and ship-related ZDOs higher sync priority.");
            PrioritizeMobs = c.Bind("8. Network", "Prioritize Mobs", true,
                "Give creature/AI ZDOs higher sync priority.");
            PrioritizeImportant = c.Bind("8. Network", "Prioritize Important Objects", true,
                "Prefer portals, chests, crafting stations, and beds when under sync budget.");
            SkipUnchanged = c.Bind("8. Network", "Skip Unchanged", true,
                "Skip sending unchanged data revisions.");
            SendBufferSize = c.Bind("8. Network", "Send Buffer Size", 16384,
                "Network send buffer size in bytes.");
            AdaptiveNetworkControl = c.Bind("8. Network", "Adaptive Control", true,
                "Automatically adapts distant sync interval and send budget based on connection quality.");
            AdaptivePingGoodMs = c.Bind("8. Network", "Adaptive Ping Good (ms)", 70f,
                "Ping at or below this value is treated as good.");
            AdaptivePingBadMs = c.Bind("8. Network", "Adaptive Ping Bad (ms)", 220f,
                "Ping at or above this value is treated as poor.");
            AdaptiveLossBad = c.Bind("8. Network", "Adaptive Loss Bad", 0.12f,
                "Packet-loss quality threshold (0-1) where adaptive control becomes conservative.");
            AdaptiveFarIntervalMin = c.Bind("8. Network", "Adaptive Far Interval Min", 2f,
                "Minimum far sync interval used on healthy links.");
            AdaptiveFarIntervalMax = c.Bind("8. Network", "Adaptive Far Interval Max", 6f,
                "Maximum far sync interval used on degraded links.");
            AdaptiveSyncBudgetMin = c.Bind("8. Network", "Adaptive Sync Budget Min", 100,
                "Minimum ZDO sync entries sent per pass under poor conditions.");
            AdaptiveSyncBudgetMax = c.Bind("8. Network", "Adaptive Sync Budget Max", 260,
                "Maximum ZDO sync entries sent per pass under good conditions.");
            ZoneOwnerManagement = c.Bind("8. Network", "Zone Owner Management", true,
                "Periodically prefers a low-ping nearby owner for zone-control ZDOs.");
            ZoneOwnerUpdateInterval = c.Bind("8. Network", "Zone Owner Update Interval", 10f,
                "Seconds between zone owner rebalance passes.");
            ZoneOwnerMaxDistance = c.Bind("8. Network", "Zone Owner Max Distance", 380f,
                "Maximum distance from zone center for owner candidacy.");
            ZoneOwnerPingGainMs = c.Bind("8. Network", "Zone Owner Distance Penalty (ms/100m)", 8f,
                "Distance penalty added to candidate ping when selecting best zone owner.");
            EnableTrafficCompression = c.Bind("8. Network", "Enable Traffic Compression", true,
                "Enable transport-level compression where supported by socket backend.");
            CompressionPlayFabOnly = c.Bind("8. Network", "Compression PlayFab Only", true,
                "If true, compression is only forced on PlayFab sockets.");
            CompressionMinBytes = c.Bind("8. Network", "Compression Min Bytes", 512,
                "Minimum payload threshold used by optional compression paths.");
            NetworkSendFPS = c.Bind("8. Network", "Send FPS", 20f,
                "Target network send cadence used for ZDO send loop tuning.");
            SteamSocketTuning = c.Bind("8. Network", "Steam Socket Tuning", true,
                "Apply conservative socket queue and send-rate tuning for Steam/PlayFab peers.");
            EnableZstdCompression = c.Bind("8. Network", "Enable Zstd Compression", true,
                "Compress ZDO network payloads with Zstd. Both client and server need this mod.");
            ZstdCompressionLevel = c.Bind("8. Network", "Zstd Compression Level", 3,
                "Zstd compression level (1-19). Higher = better ratio but more CPU.");

            // 10. Render
            LODBias = c.Bind("9. Render", "LOD Bias", 1.2f,
                "LOD transition multiplier.");
            ShadowDist = c.Bind("9. Render", "Shadow Distance", 80f,
                "Maximum shadow render distance.");
            DisableSoftParticles = c.Bind("9. Render", "Disable Soft Particles", true,
                "Disable soft particles.");
            DisableSoftVeg = c.Bind("9. Render", "Disable Soft Vegetation", true,
                "Disable soft vegetation.");
            ParticleRayBudget = c.Bind("9. Render", "Particle Raycast Budget", 1024,
                "Particle collision raycasts per frame.");
            GrassDensity = c.Bind("9. Render", "Grass Density", 0.75f,
                "Terrain detail density multiplier.");
            DetailDist = c.Bind("9. Render", "Detail Distance", 80f,
                "Terrain detail object distance.");
            MinimapOptimize = c.Bind("9. Render", "Minimap Optimize", true,
                "Throttle minimap updates in normal gameplay.");
            MinimapUpdateInterval = c.Bind("9. Render", "Minimap Update Interval", 0.12f,
                "Seconds between throttled minimap updates.");
            FrameBudgetGuard = c.Bind("9. Render", "Frame Budget Guard", true,
                "Prevents physics death spiral during heavy spikes.");
            FrameBudgetThresholdMs = c.Bind("9. Render", "Frame Budget Threshold", 28f,
                "1% low frametime threshold in ms for frame guard.");
            AdaptiveFrameGovernor = c.Bind("9. Render", "Adaptive Frame Governor", true,
                "Automatically scales visual budgets down/up based on frametime.");
            AdaptiveMinScale = c.Bind("9. Render", "Adaptive Min Scale", 0.7f,
                "Lower clamp for adaptive visual scaling.");
            AdaptiveDownStep = c.Bind("9. Render", "Adaptive Down Step", 0.06f,
                "Scale reduction step when frametime is bad.");
            AdaptiveUpStep = c.Bind("9. Render", "Adaptive Up Step", 0.02f,
                "Scale restore step when frametime stabilizes.");
            AdaptiveBadFrameMs = c.Bind("9. Render", "Adaptive Bad Frame Ms", 28f,
                "Frametime threshold in ms that triggers adaptive downscale.");
            GrassPatchSize = c.Bind("9. Render", "Grass Patch Size", 16f,
                "Grass patch render size. Higher values reduce draw calls. Vanilla default is 8.");
            DisableRealtimeReflections = c.Bind("9. Render", "Disable Realtime Reflections", true,
                "Disable realtime reflection probes to reduce GPU load.");
            AdaptiveTextureScaling = c.Bind("9. Render", "Adaptive Texture Scaling", false,
                "Allow adaptive governor to reduce texture resolution under heavy load.");
            BaseTextureLimit = c.Bind("9. Render", "Base Texture Limit", 0,
                "Base texture quality level (0=full, 1=half, 2=quarter).");

            // 11. Tar Pit
            TarCleanupInterval = c.Bind("10. Tar Pit", "Cleanup Interval", 30f,
                "Seconds between orphan tar VFX scans.");
            TarLiquidLeakFix = c.Bind("10. Tar Pit", "LiquidVolume Leak Fix", true,
                "Apply tar-specific LiquidVolume cleanup to prevent thread/native-array leaks.");

            // 12. Log
            DedupeWindow = c.Bind("11. Log", "Dedupe Window", 5f, "");
            ReportInterval = c.Bind("11. Log", "Report Interval", 60, "");
            MuteHarmony = c.Bind("11. Log", "Mute Harmony", false,
                "Suppress Harmony startup noise.");
            MuteShaders = c.Bind("11. Log", "Mute Shader Warnings", false, "");
            MinLogLevel = c.Bind("11. Log", "Min Log Level", LogLevel.Info, "");

            // 13. Streaming
            StreamingPrefetchInterval = c.Bind("12. Streaming", "Prefetch Interval", 0.45f,
                "Seconds between ahead-of-player prefetch passes.");
            StreamingZonesAhead = c.Bind("12. Streaming", "Zones Ahead", 1,
                "Number of zones to prefetch ahead during normal travel.");
            StreamingExtraZonesSailing = c.Bind("12. Streaming", "Extra Zones While Fast", 2,
                "Additional ahead zones when moving quickly (sailing/cart/travel).");
            StreamingSafeMode = c.Bind("12. Streaming", "Streaming-Safe Mode", true,
                "Enable travel-mode behavior while moving quickly.");
            StreamingRelaxCleanup = c.Bind("12. Streaming", "Relax Cleanup During Travel", true,
                "Temporarily slow non-critical cleanup scans while streaming-safe mode is active.");
            StreamingSpeedThreshold = c.Bind("12. Streaming", "Fast Travel Speed Threshold", 9f,
                "Player speed that triggers travel-mode behavior.");
            StreamingSpeedHysteresis = c.Bind("12. Streaming", "Travel Speed Hysteresis", 1.2f,
                "Speed buffer around threshold to prevent rapid travel-mode toggling.");
            StreamingEnterDelay = c.Bind("12. Streaming", "Travel Mode Enter Delay", 1.8f,
                "Seconds speed must stay above threshold+hysteresis before travel mode enables.");
            StreamingExitDelay = c.Bind("12. Streaming", "Travel Mode Exit Delay", 2.6f,
                "Seconds speed must stay below threshold-hysteresis before travel mode disables.");
            StreamingCullBoost = c.Bind("12. Streaming", "Travel Culling Distance Boost", 1.12f,
                "Multiplier for selected culling distances while in travel mode.");
            StreamingSweepMultiplier = c.Bind("12. Streaming", "Travel Cleanup Multiplier", 1.5f,
                "Multiplier applied to cleanup intervals while in travel mode.");

            // 14. VFX
            VfxMaxActive = c.Bind("13. VFX", "Max Active Non-Critical VFX", 80,
                "Maximum active non-critical one-shot VFX near the player.");
            VfxCullDist = c.Bind("13. VFX", "VFX Cull Distance", 90f,
                "Distance where non-critical VFX are culled.");
            VfxScanInterval = c.Bind("13. VFX", "VFX Scan Interval", 0.5f,
                "Seconds between VFX budget scans.");
            VfxHardCull = c.Bind("13. VFX", "Hard Cull", false,
                "Stop and clear far VFX aggressively.");

            // 15. Scheduler
            EnableStaggerScheduler = c.Bind("14. Scheduler", "Enable Stagger Scheduler", true,
                "Staggers periodic subsystem work to avoid synchronized spikes.");
            StaggerJitter = c.Bind("14. Scheduler", "Jitter", 0.15f,
                "Scheduler jitter amount (0 to 0.5).");

            LastAppliedProfile = c.Bind("99. Internal", "Last Applied Profile", PerformanceProfile.Medium,
                "Internal state: tracks which client profile baseline was last applied.");
            LastAppliedProfileVersion = c.Bind("99. Internal", "Last Applied Profile Baseline Version", 0,
                "Internal state: tracks profile baseline schema version.");

            ApplyProfileIfNeeded(c);
        }

        private readonly struct ClientProfileValues
        {
            public readonly int SmokeMaxPerSource;
            public readonly float SmokeCullDist;
            public readonly bool SmokeCollision;
            public readonly float SmokeCollisionRadius;
            public readonly int MaxLights;
            public readonly float LightCullDist;
            public readonly float ShadowCullDist;
            public readonly float LightScanInterval;
            public readonly bool FreezeFlicker;
            public readonly float LODBias;
            public readonly float ShadowDist;
            public readonly bool DisableSoftParticles;
            public readonly bool DisableSoftVeg;
            public readonly int ParticleRayBudget;
            public readonly float GrassDensity;
            public readonly float DetailDist;
            public readonly bool MinimapOptimize;
            public readonly float MinimapUpdateInterval;
            public readonly bool AdaptiveFrameGovernor;
            public readonly float AdaptiveMinScale;
            public readonly float AdaptiveDownStep;
            public readonly float AdaptiveUpStep;
            public readonly float AdaptiveBadFrameMs;
            public readonly bool FrameBudgetGuard;
            public readonly float FrameBudgetThresholdMs;
            public readonly int StreamingZonesAhead;
            public readonly int StreamingExtraZonesSailing;
            public readonly float StreamingSpeedThreshold;
            public readonly float StreamingCullBoost;
            public readonly bool StreamingSafeMode;
            public readonly bool StreamingRelaxCleanup;
            public readonly float StreamingSweepMultiplier;
            public readonly int VfxMaxActive;
            public readonly float VfxCullDist;
            public readonly float VfxScanInterval;
            public readonly bool VfxHardCull;
            public readonly float AnimThrottleDist;
            public readonly float AnimScanInterval;
            public readonly float AnimFarSpeed;
            public readonly bool AnimAggressiveCull;
            public readonly bool SleepFarRagdolls;
            public readonly float CreatureCullDist;
            public readonly bool PieceDirtyRegion;
            public readonly float PieceDirtyRadius;
            public readonly int PieceDirtyBatch;
            public readonly float PieceDirtyInterval;
            public readonly float GrassPatchSize;
            public readonly bool DisableRealtimeReflections;
            public readonly bool AdaptiveTextureScaling;
            public readonly int BaseTextureLimit;

            public ClientProfileValues(
                int smokeMaxPerSource,
                float smokeCullDist,
                bool smokeCollision,
                float smokeCollisionRadius,
                int maxLights,
                float lightCullDist,
                float shadowCullDist,
                float lightScanInterval,
                bool freezeFlicker,
                float lodBias,
                float shadowDist,
                bool disableSoftParticles,
                bool disableSoftVeg,
                int particleRayBudget,
                float grassDensity,
                float detailDist,
                bool minimapOptimize,
                float minimapUpdateInterval,
                bool adaptiveFrameGovernor,
                float adaptiveMinScale,
                float adaptiveDownStep,
                float adaptiveUpStep,
                float adaptiveBadFrameMs,
                bool frameBudgetGuard,
                float frameBudgetThresholdMs,
                int streamingZonesAhead,
                int streamingExtraZonesSailing,
                float streamingSpeedThreshold,
                float streamingCullBoost,
                bool streamingSafeMode,
                bool streamingRelaxCleanup,
                float streamingSweepMultiplier,
                int vfxMaxActive,
                float vfxCullDist,
                float vfxScanInterval,
                bool vfxHardCull,
                float animThrottleDist,
                float animScanInterval,
                float animFarSpeed,
                bool animAggressiveCull,
                bool sleepFarRagdolls,
                float creatureCullDist,
                bool pieceDirtyRegion,
                float pieceDirtyRadius,
                int pieceDirtyBatch,
                float pieceDirtyInterval,
                float grassPatchSize,
                bool disableRealtimeReflections,
                bool adaptiveTextureScaling,
                int baseTextureLimit)
            {
                SmokeMaxPerSource = smokeMaxPerSource;
                SmokeCullDist = smokeCullDist;
                SmokeCollision = smokeCollision;
                SmokeCollisionRadius = smokeCollisionRadius;
                MaxLights = maxLights;
                LightCullDist = lightCullDist;
                ShadowCullDist = shadowCullDist;
                LightScanInterval = lightScanInterval;
                FreezeFlicker = freezeFlicker;
                LODBias = lodBias;
                ShadowDist = shadowDist;
                DisableSoftParticles = disableSoftParticles;
                DisableSoftVeg = disableSoftVeg;
                ParticleRayBudget = particleRayBudget;
                GrassDensity = grassDensity;
                DetailDist = detailDist;
                MinimapOptimize = minimapOptimize;
                MinimapUpdateInterval = minimapUpdateInterval;
                AdaptiveFrameGovernor = adaptiveFrameGovernor;
                AdaptiveMinScale = adaptiveMinScale;
                AdaptiveDownStep = adaptiveDownStep;
                AdaptiveUpStep = adaptiveUpStep;
                AdaptiveBadFrameMs = adaptiveBadFrameMs;
                FrameBudgetGuard = frameBudgetGuard;
                FrameBudgetThresholdMs = frameBudgetThresholdMs;
                StreamingZonesAhead = streamingZonesAhead;
                StreamingExtraZonesSailing = streamingExtraZonesSailing;
                StreamingSpeedThreshold = streamingSpeedThreshold;
                StreamingCullBoost = streamingCullBoost;
                StreamingSafeMode = streamingSafeMode;
                StreamingRelaxCleanup = streamingRelaxCleanup;
                StreamingSweepMultiplier = streamingSweepMultiplier;
                VfxMaxActive = vfxMaxActive;
                VfxCullDist = vfxCullDist;
                VfxScanInterval = vfxScanInterval;
                VfxHardCull = vfxHardCull;
                AnimThrottleDist = animThrottleDist;
                AnimScanInterval = animScanInterval;
                AnimFarSpeed = animFarSpeed;
                AnimAggressiveCull = animAggressiveCull;
                SleepFarRagdolls = sleepFarRagdolls;
                CreatureCullDist = creatureCullDist;
                PieceDirtyRegion = pieceDirtyRegion;
                PieceDirtyRadius = pieceDirtyRadius;
                PieceDirtyBatch = pieceDirtyBatch;
                PieceDirtyInterval = pieceDirtyInterval;
                GrassPatchSize = grassPatchSize;
                DisableRealtimeReflections = disableRealtimeReflections;
                AdaptiveTextureScaling = adaptiveTextureScaling;
                BaseTextureLimit = baseTextureLimit;
            }
        }

        private static void ApplyProfileIfNeeded(ConfigFile c)
        {
            if (Plugin.IsDedicatedProcess)
                return;

            bool profileChanged = LastAppliedProfile.Value != Profile.Value;
            bool baselineChanged = LastAppliedProfileVersion.Value != ProfileBaselineVersion;
            if (!profileChanged && !baselineChanged)
                return;

            ClientProfileValues values = GetProfileValues(Profile.Value);
            ApplyProfileValues(values);

            LastAppliedProfile.Value = Profile.Value;
            LastAppliedProfileVersion.Value = ProfileBaselineVersion;
            c.Save();
            Plugin.Log?.LogInfo($"[Profile] Applied {Profile.Value} client profile baseline");
        }

        private static ClientProfileValues GetProfileValues(PerformanceProfile profile)
        {
            return profile switch
            {
                PerformanceProfile.Potato => new ClientProfileValues(
                    smokeMaxPerSource: 2,
                    smokeCullDist: 20f,
                    smokeCollision: false,
                    smokeCollisionRadius: 0.25f,
                    maxLights: 6,
                    lightCullDist: 20f,
                    shadowCullDist: 12f,
                    lightScanInterval: 1.60f,
                    freezeFlicker: true,
                    lodBias: 0.70f,
                    shadowDist: 35f,
                    disableSoftParticles: true,
                    disableSoftVeg: true,
                    particleRayBudget: 128,
                    grassDensity: 0.12f,
                    detailDist: 22f,
                    minimapOptimize: true,
                    minimapUpdateInterval: 0.30f,
                    adaptiveFrameGovernor: true,
                    adaptiveMinScale: 0.50f,
                    adaptiveDownStep: 0.10f,
                    adaptiveUpStep: 0.02f,
                    adaptiveBadFrameMs: 22f,
                    frameBudgetGuard: true,
                    frameBudgetThresholdMs: 24f,
                    streamingZonesAhead: 1,
                    streamingExtraZonesSailing: 1,
                    streamingSpeedThreshold: 11f,
                    streamingCullBoost: 1.00f,
                    streamingSafeMode: true,
                    streamingRelaxCleanup: true,
                    streamingSweepMultiplier: 2.40f,
                    vfxMaxActive: 16,
                    vfxCullDist: 36f,
                    vfxScanInterval: 1.50f,
                    vfxHardCull: true,
                    animThrottleDist: 28f,
                    animScanInterval: 1.80f,
                    animFarSpeed: 0.15f,
                    animAggressiveCull: true,
                    sleepFarRagdolls: true,
                    creatureCullDist: 40f,
                    pieceDirtyRegion: true,
                    pieceDirtyRadius: 8f,
                    pieceDirtyBatch: 8,
                    pieceDirtyInterval: 1.00f,
                    grassPatchSize: 24f,
                    disableRealtimeReflections: true,
                    adaptiveTextureScaling: false,
                    baseTextureLimit: 1),
                PerformanceProfile.Low => new ClientProfileValues(
                    smokeMaxPerSource: 6,
                    smokeCullDist: 36f,
                    smokeCollision: false,
                    smokeCollisionRadius: 0.28f,
                    maxLights: 12,
                    lightCullDist: 36f,
                    shadowCullDist: 26f,
                    lightScanInterval: 1.35f,
                    freezeFlicker: true,
                    lodBias: 0.82f,
                    shadowDist: 50f,
                    disableSoftParticles: true,
                    disableSoftVeg: true,
                    particleRayBudget: 256,
                    grassDensity: 0.30f,
                    detailDist: 40f,
                    minimapOptimize: true,
                    minimapUpdateInterval: 0.24f,
                    adaptiveFrameGovernor: true,
                    adaptiveMinScale: 0.56f,
                    adaptiveDownStep: 0.085f,
                    adaptiveUpStep: 0.022f,
                    adaptiveBadFrameMs: 24f,
                    frameBudgetGuard: true,
                    frameBudgetThresholdMs: 26f,
                    streamingZonesAhead: 1,
                    streamingExtraZonesSailing: 1,
                    streamingSpeedThreshold: 11.5f,
                    streamingCullBoost: 1.00f,
                    streamingSafeMode: true,
                    streamingRelaxCleanup: true,
                    streamingSweepMultiplier: 2.00f,
                    vfxMaxActive: 36,
                    vfxCullDist: 58f,
                    vfxScanInterval: 1.20f,
                    vfxHardCull: true,
                    animThrottleDist: 44f,
                    animScanInterval: 1.35f,
                    animFarSpeed: 0.28f,
                    animAggressiveCull: true,
                    sleepFarRagdolls: true,
                    creatureCullDist: 60f,
                    pieceDirtyRegion: true,
                    pieceDirtyRadius: 10f,
                    pieceDirtyBatch: 12,
                    pieceDirtyInterval: 0.70f,
                    grassPatchSize: 20f,
                    disableRealtimeReflections: true,
                    adaptiveTextureScaling: false,
                    baseTextureLimit: 0),
                PerformanceProfile.Medium => new ClientProfileValues(
                    smokeMaxPerSource: 10,
                    smokeCullDist: 52f,
                    smokeCollision: true,
                    smokeCollisionRadius: 0.30f,
                    maxLights: 18,
                    lightCullDist: 50f,
                    shadowCullDist: 40f,
                    lightScanInterval: 0.75f,
                    freezeFlicker: true,
                    lodBias: 0.95f,
                    shadowDist: 72f,
                    disableSoftParticles: true,
                    disableSoftVeg: true,
                    particleRayBudget: 768,
                    grassDensity: 0.55f,
                    detailDist: 65f,
                    minimapOptimize: true,
                    minimapUpdateInterval: 0.16f,
                    adaptiveFrameGovernor: true,
                    adaptiveMinScale: 0.66f,
                    adaptiveDownStep: 0.07f,
                    adaptiveUpStep: 0.02f,
                    adaptiveBadFrameMs: 28f,
                    frameBudgetGuard: true,
                    frameBudgetThresholdMs: 28f,
                    streamingZonesAhead: 1,
                    streamingExtraZonesSailing: 2,
                    streamingSpeedThreshold: 12f,
                    streamingCullBoost: 1.00f,
                    streamingSafeMode: true,
                    streamingRelaxCleanup: true,
                    streamingSweepMultiplier: 1.50f,
                    vfxMaxActive: 60,
                    vfxCullDist: 80f,
                    vfxScanInterval: 0.90f,
                    vfxHardCull: true,
                    animThrottleDist: 60f,
                    animScanInterval: 0.90f,
                    animFarSpeed: 0.50f,
                    animAggressiveCull: true,
                    sleepFarRagdolls: true,
                    creatureCullDist: 80f,
                    pieceDirtyRegion: true,
                    pieceDirtyRadius: 14f,
                    pieceDirtyBatch: 26,
                    pieceDirtyInterval: 0.40f,
                    grassPatchSize: 16f,
                    disableRealtimeReflections: true,
                    adaptiveTextureScaling: false,
                    baseTextureLimit: 0),
                PerformanceProfile.High => new ClientProfileValues(
                    smokeMaxPerSource: 16,
                    smokeCullDist: 78f,
                    smokeCollision: true,
                    smokeCollisionRadius: 0.30f,
                    maxLights: 28,
                    lightCullDist: 72f,
                    shadowCullDist: 72f,
                    lightScanInterval: 0.65f,
                    freezeFlicker: true,
                    lodBias: 1.00f,
                    shadowDist: 100f,
                    disableSoftParticles: false,
                    disableSoftVeg: false,
                    particleRayBudget: 2048,
                    grassDensity: 0.85f,
                    detailDist: 95f,
                    minimapOptimize: true,
                    minimapUpdateInterval: 0.10f,
                    adaptiveFrameGovernor: true,
                    adaptiveMinScale: 0.80f,
                    adaptiveDownStep: 0.05f,
                    adaptiveUpStep: 0.018f,
                    adaptiveBadFrameMs: 31f,
                    frameBudgetGuard: true,
                    frameBudgetThresholdMs: 31f,
                    streamingZonesAhead: 2,
                    streamingExtraZonesSailing: 2,
                    streamingSpeedThreshold: 13f,
                    streamingCullBoost: 1.06f,
                    streamingSafeMode: true,
                    streamingRelaxCleanup: true,
                    streamingSweepMultiplier: 1.30f,
                    vfxMaxActive: 90,
                    vfxCullDist: 108f,
                    vfxScanInterval: 0.40f,
                    vfxHardCull: true,
                    animThrottleDist: 86f,
                    animScanInterval: 0.40f,
                    animFarSpeed: 0.72f,
                    animAggressiveCull: false,
                    sleepFarRagdolls: false,
                    creatureCullDist: 120f,
                    pieceDirtyRegion: true,
                    pieceDirtyRadius: 15f,
                    pieceDirtyBatch: 34,
                    pieceDirtyInterval: 0.23f,
                    grassPatchSize: 12f,
                    disableRealtimeReflections: true,
                    adaptiveTextureScaling: false,
                    baseTextureLimit: 0),
                _ => new ClientProfileValues(
                    smokeMaxPerSource: 24,
                    smokeCullDist: 130f,
                    smokeCollision: true,
                    smokeCollisionRadius: 0.30f,
                    maxLights: 48,
                    lightCullDist: 130f,
                    shadowCullDist: 130f,
                    lightScanInterval: 0.50f,
                    freezeFlicker: false,
                    lodBias: 1.00f,
                    shadowDist: 120f,
                    disableSoftParticles: false,
                    disableSoftVeg: false,
                    particleRayBudget: 4096,
                    grassDensity: 1.00f,
                    detailDist: 110f,
                    minimapOptimize: false,
                    minimapUpdateInterval: 0.05f,
                    adaptiveFrameGovernor: false,
                    adaptiveMinScale: 0.95f,
                    adaptiveDownStep: 0.02f,
                    adaptiveUpStep: 0.01f,
                    adaptiveBadFrameMs: 36f,
                    frameBudgetGuard: false,
                    frameBudgetThresholdMs: 36f,
                    streamingZonesAhead: 2,
                    streamingExtraZonesSailing: 2,
                    streamingSpeedThreshold: 14f,
                    streamingCullBoost: 1.02f,
                    streamingSafeMode: false,
                    streamingRelaxCleanup: false,
                    streamingSweepMultiplier: 1.00f,
                    vfxMaxActive: 150,
                    vfxCullDist: 150f,
                    vfxScanInterval: 0.35f,
                    vfxHardCull: false,
                    animThrottleDist: 120f,
                    animScanInterval: 0.40f,
                    animFarSpeed: 0.95f,
                    animAggressiveCull: false,
                    sleepFarRagdolls: false,
                    creatureCullDist: 200f,
                    pieceDirtyRegion: true,
                    pieceDirtyRadius: 16f,
                    pieceDirtyBatch: 42,
                    pieceDirtyInterval: 0.20f,
                    grassPatchSize: 8f,
                    disableRealtimeReflections: false,
                    adaptiveTextureScaling: false,
                    baseTextureLimit: 0),
            };
        }
        private static void ApplyProfileValues(ClientProfileValues values)
        {
            SmokeMaxPerSource.Value = values.SmokeMaxPerSource;
            SmokeCullDist.Value = values.SmokeCullDist;
            SmokeCollision.Value = values.SmokeCollision;
            SmokeCollisionRadius.Value = values.SmokeCollisionRadius;
            MaxLights.Value = values.MaxLights;
            LightCullDist.Value = values.LightCullDist;
            ShadowCullDist.Value = values.ShadowCullDist;
            LightScanInterval.Value = values.LightScanInterval;
            FreezeFlicker.Value = values.FreezeFlicker;
            LODBias.Value = values.LODBias;
            ShadowDist.Value = values.ShadowDist;
            DisableSoftParticles.Value = values.DisableSoftParticles;
            DisableSoftVeg.Value = values.DisableSoftVeg;
            ParticleRayBudget.Value = values.ParticleRayBudget;
            GrassDensity.Value = values.GrassDensity;
            DetailDist.Value = values.DetailDist;
            MinimapOptimize.Value = values.MinimapOptimize;
            MinimapUpdateInterval.Value = values.MinimapUpdateInterval;
            AdaptiveFrameGovernor.Value = values.AdaptiveFrameGovernor;
            AdaptiveMinScale.Value = values.AdaptiveMinScale;
            AdaptiveDownStep.Value = values.AdaptiveDownStep;
            AdaptiveUpStep.Value = values.AdaptiveUpStep;
            AdaptiveBadFrameMs.Value = values.AdaptiveBadFrameMs;
            FrameBudgetGuard.Value = values.FrameBudgetGuard;
            FrameBudgetThresholdMs.Value = values.FrameBudgetThresholdMs;
            StreamingZonesAhead.Value = values.StreamingZonesAhead;
            StreamingExtraZonesSailing.Value = values.StreamingExtraZonesSailing;
            StreamingSpeedThreshold.Value = values.StreamingSpeedThreshold;
            StreamingCullBoost.Value = values.StreamingCullBoost;
            StreamingSafeMode.Value = values.StreamingSafeMode;
            StreamingRelaxCleanup.Value = values.StreamingRelaxCleanup;
            StreamingSweepMultiplier.Value = values.StreamingSweepMultiplier;
            VfxMaxActive.Value = values.VfxMaxActive;
            VfxCullDist.Value = values.VfxCullDist;
            VfxScanInterval.Value = values.VfxScanInterval;
            VfxHardCull.Value = values.VfxHardCull;
            AnimThrottleDist.Value = values.AnimThrottleDist;
            AnimScanInterval.Value = values.AnimScanInterval;
            AnimFarSpeed.Value = values.AnimFarSpeed;
            AnimAggressiveCull.Value = values.AnimAggressiveCull;
            SleepFarRagdolls.Value = values.SleepFarRagdolls;
            CreatureCullDist.Value = values.CreatureCullDist;
            PieceDirtyRegion.Value = values.PieceDirtyRegion;
            PieceDirtyRadius.Value = values.PieceDirtyRadius;
            PieceDirtyBatch.Value = values.PieceDirtyBatch;
            PieceDirtyInterval.Value = values.PieceDirtyInterval;
            GrassPatchSize.Value = values.GrassPatchSize;
            DisableRealtimeReflections.Value = values.DisableRealtimeReflections;
            AdaptiveTextureScaling.Value = values.AdaptiveTextureScaling;
            BaseTextureLimit.Value = values.BaseTextureLimit;
            EnableZstdCompression.Value = true;

            // Re-enabled now that warmup is incremental/time-sliced.
            JITWarmup.Value = true;

            // Optimization-first guardrail for lower-end presets:
            // keep the core optimization stack fully enabled on Potato/Low.
            if (Profile.Value == PerformanceProfile.Potato || Profile.Value == PerformanceProfile.Low)
            {
                EnableBoot.Value = true;
                EnableSmoke.Value = true;
                EnableLights.Value = true;
                EnableCulling.Value = true;
                EnablePieces.Value = true;
                EnableMemory.Value = true;
                EnableRender.Value = true;
                EnableLog.Value = true;
                EnableStreaming.Value = true;
                EnableVfx.Value = true;
                EnableTarPit.Value = true;

                SkipIntro.Value = true;
                EnableStaggerScheduler.Value = true;
                if (StaggerJitter.Value < 0.30f)
                    StaggerJitter.Value = 0.30f;

                TamedIdleLowPower.Value = true;
                AsyncWearInit.Value = true;
                GCOnSceneLoad.Value = true;
                GCOnPause.Value = true;
                PoolItemDrops.Value = true;
                PoolAudio.Value = true;
                MinimapOptimize.Value = true;
                PieceDirtyRegion.Value = true;
                StreamingSafeMode.Value = true;
                StreamingRelaxCleanup.Value = true;
            }
        }
    }
}







