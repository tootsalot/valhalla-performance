using HarmonyLib;
using System.Collections.Generic;
using UnityEngine;

namespace ValhallaPerformance
{
    public class LightSystem : ISystem
    {
        private static Vector3 _playerPos;
        private static readonly Dictionary<int, LightShadows> OriginalShadows = new Dictionary<int, LightShadows>();
        private static readonly HashSet<int> CulledBySystem = new HashSet<int>();
        private static readonly List<(Light light, float distSq)> ActiveLightsBuffer = new List<(Light, float)>(128);
        private static readonly HashSet<int> LiveLightIds = new HashSet<int>();
        private static readonly List<int> StaleLightIds = new List<int>();

        public void Init(Harmony harmony)
        {
            if (Cfg.FreezeFlicker.Value)
                harmony.PatchAll(typeof(FlickerPatch));
            Plugin.Log.LogInfo("[Lights] Active");
        }

        public void Tick()
        {
            float interval = Mathf.Clamp(Cfg.LightScanInterval.Value, 0.2f, 3f);
            if (!StaggerScheduler.ShouldRun("lights.manage", interval))
                return;

            if (Player.m_localPlayer == null)
                return;

            _playerPos = Player.m_localPlayer.transform.position;
            ManageLights();
        }

        public void Cleanup()
        {
            OriginalShadows.Clear();
            CulledBySystem.Clear();
            ActiveLightsBuffer.Clear();
            LiveLightIds.Clear();
            StaleLightIds.Clear();
        }

        private static void ManageLights()
        {
            float cullMultiplier = RuntimeTuning.CullingDistanceMultiplier;
            float cullDist = Cfg.LightCullDist.Value * cullMultiplier;
            float shadowDist = Cfg.ShadowCullDist.Value * cullMultiplier;
            int maxLights = Mathf.Max(1, Mathf.RoundToInt(Cfg.MaxLights.Value * RuntimeTuning.LightBudgetScale));

            float lightHysteresis = Mathf.Clamp(cullDist * 0.08f, 1.5f, 6f);
            float lightEnableDist = Mathf.Max(0f, cullDist - lightHysteresis);
            float lightDisableDist = cullDist + lightHysteresis;

            float shadowHysteresis = Mathf.Clamp(shadowDist * 0.10f, 2f, 10f);
            float shadowEnableDist = Mathf.Max(0f, shadowDist - shadowHysteresis);
            float shadowDisableDist = shadowDist + shadowHysteresis;

            float lightEnableSq = lightEnableDist * lightEnableDist;
            float lightDisableSq = lightDisableDist * lightDisableDist;
            float shadowEnableSq = shadowEnableDist * shadowEnableDist;
            float shadowDisableSq = shadowDisableDist * shadowDisableDist;

            Light[] lights = UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None);
            ActiveLightsBuffer.Clear();
            foreach (Light light in lights)
            {
                if (light == null || light.type == LightType.Directional)
                    continue;

                int id = light.GetInstanceID();
                if (light.enabled && !OriginalShadows.ContainsKey(id))
                    OriginalShadows[id] = light.shadows;

                float distSq = (light.transform.position - _playerPos).sqrMagnitude;

                // Full light cull: only re-enable lights this system disabled.
                if (light.enabled)
                {
                    if (distSq > lightDisableSq)
                    {
                        light.enabled = false;
                        CulledBySystem.Add(id);
                        continue;
                    }
                }
                else
                {
                    if (!CulledBySystem.Contains(id) || distSq > lightEnableSq)
                        continue;

                    light.enabled = true;
                    CulledBySystem.Remove(id);
                    if (!OriginalShadows.ContainsKey(id))
                        OriginalShadows[id] = light.shadows;
                }

                if (!OriginalShadows.TryGetValue(id, out LightShadows original) || original == LightShadows.None)
                    continue;

                // Shadow cull with hysteresis only (no rotating budget swaps).
                if (distSq > shadowDisableSq)
                    light.shadows = LightShadows.None;
                else if (distSq < shadowEnableSq)
                    light.shadows = original;

                if (!light.enabled)
                    continue;

                KeepNearestLightWithinBudget(light, distSq, maxLights);
            }

            if (StaggerScheduler.ShouldRun("lights.cache_cleanup", 30f))
            {
                LiveLightIds.Clear();
                foreach (Light light in lights)
                {
                    if (light != null)
                        LiveLightIds.Add(light.GetInstanceID());
                }

                StaleLightIds.Clear();
                foreach (var kvp in OriginalShadows)
                {
                    if (!LiveLightIds.Contains(kvp.Key))
                        StaleLightIds.Add(kvp.Key);
                }

                foreach (int id in StaleLightIds)
                {
                    OriginalShadows.Remove(id);
                    CulledBySystem.Remove(id);
                }
            }
        }

        private static void KeepNearestLightWithinBudget(Light light, float distSq, int maxLights)
        {
            if (light == null)
                return;

            if (ActiveLightsBuffer.Count < maxLights)
            {
                ActiveLightsBuffer.Add((light, distSq));
                return;
            }

            int farthestIndex = -1;
            float farthestDistSq = float.MinValue;
            for (int i = 0; i < ActiveLightsBuffer.Count; i++)
            {
                float candidateDistSq = ActiveLightsBuffer[i].distSq;
                if (candidateDistSq > farthestDistSq)
                {
                    farthestDistSq = candidateDistSq;
                    farthestIndex = i;
                }
            }

            if (farthestIndex < 0)
                return;

            if (distSq >= farthestDistSq)
            {
                light.enabled = false;
                CulledBySystem.Add(light.GetInstanceID());
                return;
            }

            Light previous = ActiveLightsBuffer[farthestIndex].light;
            if (previous != null)
            {
                previous.enabled = false;
                CulledBySystem.Add(previous.GetInstanceID());
            }

            ActiveLightsBuffer[farthestIndex] = (light, distSq);
        }

        [HarmonyPatch]
        private static class FlickerPatch
        {
            [HarmonyPrefix]
            [HarmonyPatch(typeof(LightFlicker), "CustomUpdate")]
            private static bool Prefix(LightFlicker __instance)
            {
                var trav = Traverse.Create(__instance);
                var light = trav.Field("m_light").GetValue<Light>();
                float baseIntensity = trav.Field("m_baseIntensity").GetValue<float>();
                if (light != null)
                    light.intensity = baseIntensity;
                return false;
            }
        }
    }
}
