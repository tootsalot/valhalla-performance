using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using System;
using System.Diagnostics;
using UnityEngine;

namespace ValhallaPerformance
{
    [BepInPlugin(GUID, Name, Ver)]
    [BepInProcess("valheim.exe")]
    [BepInProcess("valheim_server.exe")]
    public class Plugin : BaseUnityPlugin
    {
        public const string GUID = "valhalla.performance";
        public const string Name = "ValhallaPerformance";
        public const string Ver = "1.3.0";

        internal static Plugin Instance;
        internal static ManualLogSource Log;
        internal static bool IsDedicatedProcess { get; private set; }
        private Harmony _harmony;

        // All systems implement ISystem for uniform lifecycle
        private ISystem[] _systems;

        private void Awake()
        {
            Instance = this;
            Log = Logger;
            IsDedicatedProcess = DetectDedicatedProcess();
            Cfg.Init(Config);

            _harmony = new Harmony(GUID);
            bool isClientProcess = !IsDedicatedProcess;
            Logger.LogInfo($"[{Name}] Runtime mode: {(isClientProcess ? "client" : "dedicated-server")}");

            _systems = new ISystem[]
            {
                // Boot-time systems first
                Cfg.EnableBoot.Value && isClientProcess ? new BootSystem() : null,
                Cfg.EnableLog.Value ? new LogSystem() : null,

                // Core rendering / presentation
                Cfg.EnableSmoke.Value && isClientProcess ? new SmokeSystem() : null,
                Cfg.EnableLights.Value && isClientProcess ? new LightSystem() : null,
                Cfg.EnableRender.Value && isClientProcess ? new RenderSystem() : null,
                Cfg.EnableStreaming.Value && isClientProcess ? new StreamingSystem() : null,
                Cfg.EnableVfx.Value && isClientProcess ? new VfxSystem() : null,

                // Simulation
                Cfg.EnableCulling.Value && isClientProcess ? new CullingSystem() : null,
                Cfg.EnablePieces.Value ? new PieceSystem() : null,
                Cfg.EnableTarPit.Value ? new TarPitSystem() : null,

                // Resources + network
                Cfg.EnableMemory.Value ? new MemorySystem() : null,
                Cfg.EnableNetwork.Value ? new NetworkSystem() : null,
            };

            int active = 0;
            foreach (ISystem sys in _systems)
            {
                if (sys == null)
                    continue;
                sys.Init(_harmony);
                active++;
            }

            Logger.LogInfo($"{Name} v{Ver} - {active} systems active");
        }

        private static bool DetectDedicatedProcess()
        {
            try
            {
                string processName = Process.GetCurrentProcess().ProcessName;
                if (processName.IndexOf("valheim_server", StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
            catch { }

            // Unity batch mode is expected for dedicated/headless processes.
            return Application.isBatchMode;
        }

        private void Update()
        {
            foreach (ISystem sys in _systems)
                sys?.Tick();
        }

        private void OnDestroy()
        {
            _harmony?.UnpatchSelf();
            foreach (ISystem sys in _systems)
                sys?.Cleanup();

            RuntimeTuning.Reset();
            StaggerScheduler.Clear();
        }
    }

    /// <summary>
    /// Common interface for all optimization systems.
    /// </summary>
    public interface ISystem
    {
        void Init(Harmony harmony);
        void Tick();
        void Cleanup();
    }
}
