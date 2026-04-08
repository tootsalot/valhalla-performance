using System.Collections.Generic;
using UnityEngine;

namespace ValhallaPerformance
{
    internal static class StaggerScheduler
    {
        private static readonly Dictionary<int, float> NextRun = new Dictionary<int, float>();

        public static bool ShouldRun(string key, float interval, float intervalMultiplier = 1f)
        {
            if (interval <= 0f)
                return true;

            float now = Time.unscaledTime;
            float scaledInterval = Mathf.Max(0.01f, interval * Mathf.Max(0.01f, intervalMultiplier));
            int hash = key.GetHashCode();

            if (!NextRun.TryGetValue(hash, out float next))
            {
                float startupOffset = 0f;
                if (Cfg.EnableStaggerScheduler.Value)
                    startupOffset = scaledInterval * Mathf.Clamp01(Hash01(hash, 17)) * Mathf.Clamp(Cfg.StaggerJitter.Value, 0f, 0.5f);
                NextRun[hash] = now + startupOffset;
                return false;
            }

            if (now < next)
                return false;

            float jitter = 0f;
            if (Cfg.EnableStaggerScheduler.Value)
            {
                float jitterRange = Mathf.Clamp(Cfg.StaggerJitter.Value, 0f, 0.5f);
                jitter = (Hash01(hash, Mathf.FloorToInt(now * 10f)) * 2f - 1f) * (scaledInterval * jitterRange);
            }

            NextRun[hash] = now + Mathf.Max(0.01f, scaledInterval + jitter);
            return true;
        }

        public static void Clear()
        {
            NextRun.Clear();
        }

        private static float Hash01(int hash, int salt)
        {
            unchecked
            {
                uint x = (uint)(hash ^ (salt * 374761393));
                x = (x ^ (x >> 13)) * 1274126177u;
                x ^= x >> 16;
                return (x & 0x00FFFFFF) / 16777215f;
            }
        }
    }
}
