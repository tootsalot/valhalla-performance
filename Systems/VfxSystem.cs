using HarmonyLib;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace ValhallaPerformance
{
    public class VfxSystem : ISystem
    {
        // Keep matching conservative: burst-style non-critical FX only.
        private static readonly string[] NonCriticalKeywords =
        {
            "splash", "debris", "impact", "hit", "spark", "shatter", "break"
        };

        private static readonly string[] ExcludedKeywords =
        {
            "smoke", "tar", "portal", "boss", "spawn", "teleport", "death",
            "rain", "snow", "mist", "fog", "ambient", "aura", "status", "breath", "wisp", "mote"
        };

        private static readonly Dictionary<int, bool> OriginalEmissionEnabled = new Dictionary<int, bool>();
        private static readonly HashSet<int> CulledBySystem = new HashSet<int>();
        private static readonly List<(ParticleSystem ps, float distSq)> CandidateBuffer = new List<(ParticleSystem, float)>(256);
        private static readonly List<(ParticleSystem ps, float distSq)> KeptBuffer = new List<(ParticleSystem, float)>(128);
        private static readonly HashSet<int> KeepIds = new HashSet<int>();
        private static readonly HashSet<int> LiveIds = new HashSet<int>();
        private static readonly List<int> StaleIds = new List<int>();

        public void Init(Harmony harmony)
        {
            Plugin.Log.LogInfo("[VFX] Active");
        }

        public void Tick()
        {
            if (!StaggerScheduler.ShouldRun("vfx.scan", Cfg.VfxScanInterval.Value))
                return;

            Player player = Player.m_localPlayer;
            if (player == null)
                return;

            Camera cam = Camera.main;
            Vector3 playerPos = player.transform.position;
            float cullDist = Mathf.Max(20f, Cfg.VfxCullDist.Value * RuntimeTuning.CullingDistanceMultiplier);
            float cullSq = cullDist * cullDist;
            int budget = Mathf.Max(8, Mathf.RoundToInt(Cfg.VfxMaxActive.Value * RuntimeTuning.VfxBudgetScale));

            ParticleSystem[] systems = UnityEngine.Object.FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None);
            CandidateBuffer.Clear();
            KeptBuffer.Clear();
            KeepIds.Clear();

            foreach (ParticleSystem ps in systems)
            {
                if (ps == null || !ps.gameObject.activeInHierarchy)
                    continue;

                if (!IsNonCritical(ps.gameObject.name) || !IsBurstLike(ps))
                    continue;

                if (IsPlayerOrCameraAttached(ps, player, cam))
                    continue;

                int id = ps.GetInstanceID();
                if (!OriginalEmissionEnabled.ContainsKey(id))
                    OriginalEmissionEnabled[id] = ps.emission.enabled;

                float distSq = (ps.transform.position - playerPos).sqrMagnitude;
                if (distSq > cullSq)
                {
                    SetVfxState(ps, false);
                    continue;
                }

                CandidateBuffer.Add((ps, distSq));
                KeepNearestWithinBudget(ps, distSq, budget);
            }

            for (int i = 0; i < KeptBuffer.Count; i++)
            {
                ParticleSystem ps = KeptBuffer[i].ps;
                if (ps != null)
                    KeepIds.Add(ps.GetInstanceID());
            }

            for (int i = 0; i < CandidateBuffer.Count; i++)
            {
                ParticleSystem ps = CandidateBuffer[i].ps;
                if (ps == null)
                    continue;

                bool keep = KeepIds.Contains(ps.GetInstanceID());
                SetVfxState(ps, keep);
            }

            if (StaggerScheduler.ShouldRun("vfx.cache_cleanup", 30f))
                CleanupStaleCaches(systems);
        }

        public void Cleanup()
        {
            OriginalEmissionEnabled.Clear();
            CulledBySystem.Clear();
            CandidateBuffer.Clear();
            KeptBuffer.Clear();
            KeepIds.Clear();
            LiveIds.Clear();
            StaleIds.Clear();
        }

        private static void KeepNearestWithinBudget(ParticleSystem ps, float distSq, int budget)
        {
            if (ps == null || budget <= 0)
                return;

            if (KeptBuffer.Count < budget)
            {
                KeptBuffer.Add((ps, distSq));
                return;
            }

            int farthestIndex = -1;
            float farthestDistSq = float.MinValue;
            for (int i = 0; i < KeptBuffer.Count; i++)
            {
                float candidateDistSq = KeptBuffer[i].distSq;
                if (candidateDistSq > farthestDistSq)
                {
                    farthestDistSq = candidateDistSq;
                    farthestIndex = i;
                }
            }

            if (farthestIndex < 0)
                return;

            if (distSq >= farthestDistSq)
                return;

            KeptBuffer[farthestIndex] = (ps, distSq);
        }

        private static bool IsNonCritical(string name)
        {
            if (string.IsNullOrEmpty(name))
                return false;

            for (int i = 0; i < ExcludedKeywords.Length; i++)
            {
                if (name.IndexOf(ExcludedKeywords[i], StringComparison.OrdinalIgnoreCase) >= 0)
                    return false;
            }

            for (int i = 0; i < NonCriticalKeywords.Length; i++)
            {
                if (name.IndexOf(NonCriticalKeywords[i], StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }

            return false;
        }

        private static bool IsBurstLike(ParticleSystem ps)
        {
            ParticleSystem.MainModule main = ps.main;
            if (main.loop)
                return false;

            if (main.duration > 12f)
                return false;

            ParticleSystem.EmissionModule emission = ps.emission;
            if (emission.rateOverTime.constantMax > 5f)
                return false;

            return true;
        }

        private static bool IsPlayerOrCameraAttached(ParticleSystem ps, Player player, Camera cam)
        {
            Transform t = ps.transform;
            if (player != null)
            {
                Transform p = player.transform;
                if (t == p || t.IsChildOf(p) || ps.GetComponentInParent<Player>() != null || ps.GetComponentInParent<Character>() != null)
                    return true;
            }

            if (cam != null)
            {
                Transform c = cam.transform;
                if (t == c || t.IsChildOf(c))
                    return true;
            }

            // Never touch UI hierarchy particle systems.
            if (ps.GetComponentInParent<InventoryGui>() != null || ps.GetComponentInParent<Hud>() != null)
                return true;

            return false;
        }

        private static void SetVfxState(ParticleSystem ps, bool keep)
        {
            if (ps == null)
                return;

            int id = ps.GetInstanceID();
            bool originalEnabled = OriginalEmissionEnabled.TryGetValue(id, out bool original) ? original : ps.emission.enabled;
            var emission = ps.emission;

            if (keep)
            {
                if (CulledBySystem.Contains(id))
                {
                    emission.enabled = originalEnabled;
                    CulledBySystem.Remove(id);

                    if (originalEnabled && !ps.isPlaying && ps.gameObject.activeInHierarchy)
                        ps.Play();
                }
                else if (!originalEnabled && emission.enabled)
                {
                    // Do not force-on systems that were originally disabled.
                    emission.enabled = false;
                }

                return;
            }

            // Do not alter systems that were originally disabled.
            if (!originalEnabled)
                return;

            if (!CulledBySystem.Contains(id))
            {
                emission.enabled = false;
                CulledBySystem.Add(id);

                if (Cfg.VfxHardCull.Value)
                    ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            }
        }

        private static void CleanupStaleCaches(ParticleSystem[] systems)
        {
            LiveIds.Clear();
            foreach (ParticleSystem ps in systems)
            {
                if (ps != null)
                    LiveIds.Add(ps.GetInstanceID());
            }

            StaleIds.Clear();
            foreach (var kvp in OriginalEmissionEnabled)
            {
                if (!LiveIds.Contains(kvp.Key))
                    StaleIds.Add(kvp.Key);
            }

            foreach (int id in StaleIds)
            {
                OriginalEmissionEnabled.Remove(id);
                CulledBySystem.Remove(id);
            }
        }
    }
}
