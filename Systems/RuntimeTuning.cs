using UnityEngine;

namespace ValhallaPerformance
{
    internal static class RuntimeTuning
    {
        public static float CleanupIntervalMultiplier { get; private set; } = 1f;
        public static float CullingDistanceMultiplier { get; private set; } = 1f;
        public static float LightBudgetScale { get; private set; } = 1f;
        public static float VfxBudgetScale { get; private set; } = 1f;

        public static void Reset()
        {
            CleanupIntervalMultiplier = 1f;
            CullingDistanceMultiplier = 1f;
            LightBudgetScale = 1f;
            VfxBudgetScale = 1f;
        }

        public static void SetTravelMode(bool active, float cullBoost, float cleanupMultiplier)
        {
            if (!active)
            {
                CleanupIntervalMultiplier = 1f;
                CullingDistanceMultiplier = 1f;
                return;
            }

            CullingDistanceMultiplier = Mathf.Clamp(cullBoost, 1f, 2f);
            CleanupIntervalMultiplier = Mathf.Clamp(cleanupMultiplier, 1f, 5f);
        }

        public static void SetAdaptiveVisualScale(float scale)
        {
            float clamped = Mathf.Clamp(scale, 0.5f, 1f);
            LightBudgetScale = clamped;
            VfxBudgetScale = clamped;
        }
    }
}
