using BepInEx.Logging;
using HarmonyLib;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace ValhallaPerformance
{
    public class LogSystem : ISystem
    {
        private static readonly Dictionary<int, (float time, int count)> _seen
            = new Dictionary<int, (float, int)>();
        private static int _suppressed;
        private static readonly object _sync = new object();

        private static readonly string[] Noise =
        {
            "shader is not supported", "Can't find shader",
            "Missing asset bundle", "material doesn't have",
            "The referenced script", "Setting quality level",
            "Shader wants texture", "%.teleport_log", "%.RPC_",
            "%.DamageText", "%.FlashColor", "%.AddNoise",
            "You should only patch implemented methods/constructors",
            "Only custom filters can be played",
            "Missing audio clip in music respawn",
            "Character ID for player",
            "Setting linear velocity of a kinematic body is not supported",
            "Setting angular velocity of a kinematic body is not supported",
            "Destroyed invalid prefab ZDO"
        };

        public void Init(Harmony harmony)
        {
            harmony.PatchAll(typeof(Patch));
            Plugin.Log.LogInfo("[Log] Filter active");
        }

        public void Tick()
        {
            int suppressedSnapshot = 0;
            lock (_sync)
                suppressedSnapshot = _suppressed;

            if (suppressedSnapshot > 0 && StaggerScheduler.ShouldRun("log.report", Cfg.ReportInterval.Value))
            {
                Plugin.Log.LogInfo($"[Log] Suppressed {suppressedSnapshot} noise messages");
                lock (_sync)
                    _suppressed = 0;
            }

            if (!StaggerScheduler.ShouldRun("log.purge", 30f, RuntimeTuning.CleanupIntervalMultiplier))
                return;

            float time = Time.unscaledTime;
            float ttl = Cfg.DedupeWindow.Value * 4f;
            var stale = new List<int>();
            lock (_sync)
            {
                foreach (var kvp in _seen)
                {
                    if (time - kvp.Value.time > ttl)
                        stale.Add(kvp.Key);
                }
                foreach (int key in stale)
                    _seen.Remove(key);
            }
        }

        public void Cleanup()
        {
            lock (_sync)
            {
                _seen.Clear();
                _suppressed = 0;
            }
        }

        internal static bool ShouldDrop(LogLevel level, object data)
        {
            // Never suppress warnings/errors/fatal logs: these are actionable signals.
            if ((level & (LogLevel.Warning | LogLevel.Error | LogLevel.Fatal)) != 0)
                return false;

            if (level < Cfg.MinLogLevel.Value)
            {
                lock (_sync)
                    _suppressed++;
                return true;
            }

            if (data == null)
                return false;

            string msg = data.ToString();
            if (string.IsNullOrEmpty(msg))
                return false;

            if (Cfg.MuteHarmony.Value &&
                (msg.StartsWith("Patching ", StringComparison.Ordinal)
                 || msg.Contains("Harmony id=")
                 || msg.Contains("[HarmonyLib")
                 || msg.Contains("HarmonyX")
                 || msg.IndexOf("You should only patch implemented methods/constructors", StringComparison.OrdinalIgnoreCase) >= 0))
            {
                lock (_sync)
                    _suppressed++;
                return true;
            }

            if (Cfg.MuteShaders.Value)
            {
                for (int i = 0; i < Noise.Length; i++)
                {
                    if (msg.IndexOf(Noise[i], StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        lock (_sync)
                            _suppressed++;
                        return true;
                    }
                }
            }

            int hash = msg.GetHashCode();
            float time = Time.unscaledTime;
            lock (_sync)
            {
                if (_seen.TryGetValue(hash, out var rec) && time - rec.time < Cfg.DedupeWindow.Value)
                {
                    _seen[hash] = (rec.time, rec.count + 1);
                    _suppressed++;
                    return true;
                }

                _seen[hash] = (time, 1);
            }
            return false;
        }

        [HarmonyPatch]
        private static class Patch
        {
            [HarmonyPrefix]
            [HarmonyPatch(typeof(ManualLogSource), nameof(ManualLogSource.Log))]
            private static bool Prefix(LogLevel level, object data, ManualLogSource __instance)
            {
                if (__instance.SourceName == Plugin.Name)
                    return true;
                return !ShouldDrop(level, data);
            }
        }
    }
}
