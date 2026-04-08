using HarmonyLib;
using System;
using System.Reflection;
using UnityEngine;

namespace ValhallaPerformance
{
    public class RenderSystem : ISystem
    {
        private bool _tweaksApplied;
        private readonly float[] _frameTimes = new float[120];
        private int _frameIndex;
        private float _savedMaxDelta;
        private bool _guardActive;
        private float _adaptiveScale = 1f;

        public void Init(Harmony harmony)
        {
            harmony.PatchAll(typeof(MinimapPatches));
            harmony.PatchAll(typeof(ClutterPatch));
            Plugin.Log.LogInfo("[Render] Active");
        }

        public void Tick()
        {
            if (!_tweaksApplied)
            {
                ApplyEngineTweaks();
                _tweaksApplied = true;
            }

            if (StaggerScheduler.ShouldRun("render.terrain", 10f))
                ApplyTerrainDetailTweaks(_adaptiveScale);

            UpdateFrameMetrics();
        }

        public void Cleanup()
        {
            if (_guardActive && _savedMaxDelta > 0f)
                Time.maximumDeltaTime = _savedMaxDelta;
            RuntimeTuning.SetAdaptiveVisualScale(1f);
            QualitySettings.globalTextureMipmapLimit = 0;
        }

        private void ApplyEngineTweaks()
        {
            QualitySettings.lodBias = Cfg.LODBias.Value;
            if (Cfg.DisableSoftParticles.Value)
                QualitySettings.softParticles = false;
            if (Cfg.DisableSoftVeg.Value)
                QualitySettings.softVegetation = false;
            if (Cfg.DisableRealtimeReflections.Value)
                QualitySettings.realtimeReflectionProbes = false;

            ApplyAdaptiveQuality();
            ApplyTerrainDetailTweaks(_adaptiveScale);

            Plugin.Log.LogInfo(
                $"[Render] LOD={Cfg.LODBias.Value:F1} softP={QualitySettings.softParticles} softV={QualitySettings.softVegetation} " +
                $"pRay={Cfg.ParticleRayBudget.Value} shadow={Cfg.ShadowDist.Value:F0}m grass={Cfg.GrassDensity.Value:F2} " +
                $"detail={Cfg.DetailDist.Value:F0}m minimap={Cfg.MinimapOptimize.Value}/{Cfg.MinimapUpdateInterval.Value:F2}s adaptive={Cfg.AdaptiveFrameGovernor.Value} " +
                $"rtReflect={!Cfg.DisableRealtimeReflections.Value} grassPatch={Cfg.GrassPatchSize.Value:F0} texLimit={Cfg.BaseTextureLimit.Value}");
        }

        private void ApplyAdaptiveQuality()
        {
            if (!Cfg.AdaptiveFrameGovernor.Value)
                _adaptiveScale = 1f;

            float scaledShadow = Mathf.Max(20f, Cfg.ShadowDist.Value * _adaptiveScale);
            int scaledParticleBudget = Mathf.Max(128, Mathf.RoundToInt(Cfg.ParticleRayBudget.Value * _adaptiveScale));

            QualitySettings.shadowDistance = scaledShadow;
            QualitySettings.particleRaycastBudget = scaledParticleBudget;
            RuntimeTuning.SetAdaptiveVisualScale(_adaptiveScale);

            if (Cfg.AdaptiveTextureScaling.Value)
            {
                int texLimit = Cfg.BaseTextureLimit.Value;
                if (_adaptiveScale < 0.65f) texLimit = Mathf.Max(texLimit, 2);
                else if (_adaptiveScale < 0.8f) texLimit = Mathf.Max(texLimit, 1);
                QualitySettings.globalTextureMipmapLimit = texLimit;
            }
            else
            {
                QualitySettings.globalTextureMipmapLimit = Cfg.BaseTextureLimit.Value;
            }
        }

        private static void ApplyTerrainDetailTweaks(float scale)
        {
            float density = Mathf.Clamp01(Cfg.GrassDensity.Value * Mathf.Clamp(scale, 0.5f, 1f));
            float detailDistance = Mathf.Max(20f, Cfg.DetailDist.Value * Mathf.Lerp(0.7f, 1f, Mathf.Clamp01(scale)));

            Terrain[] terrains = Terrain.activeTerrains;
            if (terrains == null || terrains.Length == 0)
                return;

            foreach (Terrain terrain in terrains)
            {
                if (terrain == null)
                    continue;
                terrain.detailObjectDensity = density;
                terrain.detailObjectDistance = detailDistance;
            }
        }

        private void UpdateFrameMetrics()
        {
            _frameTimes[_frameIndex] = Time.unscaledDeltaTime * 1000f;
            _frameIndex = (_frameIndex + 1) % _frameTimes.Length;
            if (_frameIndex % 60 != 0)
                return;

            float worstAvg = CalculateWorstPercentileMs();

            if (Cfg.FrameBudgetGuard.Value)
                UpdateFrameBudgetGuard(worstAvg);
            else if (_guardActive)
            {
                Time.maximumDeltaTime = _savedMaxDelta;
                _guardActive = false;
            }

            if (Cfg.AdaptiveFrameGovernor.Value)
                UpdateAdaptiveGovernor(worstAvg);
            else if (Mathf.Abs(_adaptiveScale - 1f) > 0.001f)
            {
                _adaptiveScale = 1f;
                ApplyAdaptiveQuality();
                ApplyTerrainDetailTweaks(_adaptiveScale);
            }
        }

        private float CalculateWorstPercentileMs()
        {
            float[] sorted = new float[_frameTimes.Length];
            Array.Copy(_frameTimes, sorted, _frameTimes.Length);
            Array.Sort(sorted);

            int worstCount = Mathf.Max(1, sorted.Length / 100);
            float worstAvg = 0f;
            for (int i = sorted.Length - worstCount; i < sorted.Length; i++)
                worstAvg += sorted[i];
            return worstAvg / worstCount;
        }

        private void UpdateFrameBudgetGuard(float worstAvg)
        {
            float threshold = Cfg.FrameBudgetThresholdMs.Value;
            if (worstAvg > threshold && !_guardActive)
            {
                _savedMaxDelta = Time.maximumDeltaTime;
                Time.maximumDeltaTime = 0.04f;
                _guardActive = true;
            }
            else if (worstAvg < threshold * 0.7f && _guardActive)
            {
                Time.maximumDeltaTime = _savedMaxDelta;
                _guardActive = false;
            }
        }

        private void UpdateAdaptiveGovernor(float worstAvg)
        {
            float badFrame = Cfg.AdaptiveBadFrameMs.Value;
            float minScale = Mathf.Clamp(Cfg.AdaptiveMinScale.Value, 0.5f, 1f);
            float nextScale = _adaptiveScale;

            if (worstAvg > badFrame)
                nextScale = Mathf.Max(minScale, _adaptiveScale - Mathf.Clamp(Cfg.AdaptiveDownStep.Value, 0.005f, 0.25f));
            else if (worstAvg < badFrame * 0.75f)
                nextScale = Mathf.Min(1f, _adaptiveScale + Mathf.Clamp(Cfg.AdaptiveUpStep.Value, 0.002f, 0.2f));

            if (Mathf.Abs(nextScale - _adaptiveScale) < 0.001f)
                return;

            _adaptiveScale = nextScale;
            ApplyAdaptiveQuality();
            ApplyTerrainDetailTweaks(_adaptiveScale);
        }

        [HarmonyPatch(typeof(ClutterSystem), "Awake")]
        private static class ClutterPatch
        {
            private static void Postfix(ClutterSystem __instance)
            {
                __instance.m_grassPatchSize = Cfg.GrassPatchSize.Value;
                Plugin.Log.LogInfo($"[Render] ClutterSystem grassPatchSize set to {Cfg.GrassPatchSize.Value:F0}");
            }
        }

        [HarmonyPatch]
        private static class MinimapPatches
        {
            private static float _nextAllowedUpdate;
            private static readonly MethodInfo IsOpenMethod = AccessTools.Method(typeof(Minimap), "IsOpen");
            private static readonly FieldInfo ModeField = AccessTools.Field(typeof(Minimap), "m_mode");
            private static readonly FieldInfo LargeRootField = AccessTools.Field(typeof(Minimap), "m_largeRoot");

            [HarmonyPrefix]
            [HarmonyPatch(typeof(Minimap), "Update")]
            private static bool Prefix_Update(Minimap __instance)
            {
                if (!Cfg.MinimapOptimize.Value)
                    return true;

                if (__instance == null || IsMapTogglePressed() || IsInventoryTogglePressed() || IsMapInteractive(__instance))
                    return true;

                float interval = Mathf.Max(0.02f, Cfg.MinimapUpdateInterval.Value);
                float now = Time.unscaledTime;
                if (now < _nextAllowedUpdate)
                    return false;

                _nextAllowedUpdate = now + interval;
                return true;
            }

            private static bool IsMapTogglePressed()
            {
                try
                {
                    return ZInput.GetButtonDown("Map") || ZInput.GetButtonDown("JoyMap");
                }
                catch
                {
                    return false;
                }
            }

            private static bool IsInventoryTogglePressed()
            {
                try
                {
                    return ZInput.GetButtonDown("Inventory") ||
                           ZInput.GetButtonDown("JoyInventory") ||
                           ZInput.GetButtonDown("Tab");
                }
                catch
                {
                    return false;
                }
            }

            private static bool IsMapInteractive(Minimap minimap)
            {
                try
                {
                    if (IsOpenMethod != null && IsOpenMethod.Invoke(minimap, null) is bool isOpen && isOpen)
                        return true;
                }
                catch { }

                try
                {
                    if (LargeRootField != null && LargeRootField.GetValue(minimap) is GameObject go && go.activeSelf)
                        return true;
                }
                catch { }

                try
                {
                    if (ModeField != null)
                    {
                        object modeObj = ModeField.GetValue(minimap);
                        if (modeObj != null && Convert.ToInt32(modeObj) > 0)
                            return true;
                    }
                }
                catch { }

                return false;
            }
        }
    }
}
