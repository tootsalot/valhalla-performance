using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ValhallaPerformance
{
    public class MemorySystem : ISystem
    {
        private static bool _sceneLoadPending;

        private static readonly MethodInfo InCombatMethod =
            typeof(Player).GetMethod("InCombat", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        private static readonly Queue<AudioSource> PooledAudioSources = new Queue<AudioSource>();
        private static readonly List<AudioSource> ActivePooledSources = new List<AudioSource>();
        private static readonly Dictionary<int, float> AudioReleaseTimes = new Dictionary<int, float>();
        private static Transform _audioPoolRoot;

        private const float DropMergeRadius = 3f;

        public void Init(Harmony harmony)
        {
            harmony.PatchAll(typeof(Patches));
            SceneManager.sceneLoaded += OnSceneLoaded;
            Plugin.Log.LogInfo("[Memory] Active");
        }

        public void Tick()
        {
            if (_sceneLoadPending)
            {
                _sceneLoadPending = false;
                if (Cfg.GCOnSceneLoad.Value)
                    DoGC("scene load");
            }

            if (Cfg.PoolAudio.Value)
                UpdateAudioPool();

            if (StaggerScheduler.ShouldRun("memory.asset_sweep", Cfg.AssetSweepInterval.Value, RuntimeTuning.CleanupIntervalMultiplier))
            {
                if (IsSafe())
                    Resources.UnloadUnusedAssets();
            }

            if (StaggerScheduler.ShouldRun("memory.heap_check", 15f, RuntimeTuning.CleanupIntervalMultiplier))
            {
                int ceiling = Cfg.HeapCeilingMB.Value;
                if (ceiling > 0)
                {
                    long mb = GC.GetTotalMemory(false) / (1024L * 1024L);
                    if (mb > ceiling)
                        DoGC($"heap {mb}MB>{ceiling}MB");
                }
            }
        }

        public void Cleanup()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            ClearAudioPool();
        }

        private static void OnSceneLoaded(Scene _, LoadSceneMode __)
        {
            _sceneLoadPending = true;
        }

        private bool IsSafe()
        {
            Player player = Player.m_localPlayer;
            if (player == null)
                return true;

            bool inCombat = false;
            if (InCombatMethod != null)
            {
                try
                {
                    inCombat = (bool)InCombatMethod.Invoke(player, null);
                }
                catch { }
            }

            return !inCombat && !player.IsSwimming();
        }

        private static bool TryPlayClipAtPointPooled(AudioClip clip, Vector3 position, float volume)
        {
            if (clip == null)
                return false;

            EnsureAudioPoolRoot();
            AudioSource source = AcquireAudioSource();
            if (source == null)
                return false;

            source.gameObject.SetActive(true);
            source.transform.position = position;
            source.clip = clip;
            source.loop = false;
            source.volume = Mathf.Clamp01(volume);
            source.pitch = 1f;
            source.spatialBlend = 1f;
            source.rolloffMode = AudioRolloffMode.Linear;
            source.minDistance = 1f;
            source.maxDistance = 64f;
            source.dopplerLevel = 0f;
            source.priority = 128;
            source.Play();

            int id = source.GetInstanceID();
            float releaseAt = Time.unscaledTime + Mathf.Max(0.05f, clip.length + 0.05f);
            AudioReleaseTimes[id] = releaseAt;
            ActivePooledSources.Add(source);
            return true;
        }

        private static void UpdateAudioPool()
        {
            if (ActivePooledSources.Count == 0)
                return;

            float now = Time.unscaledTime;
            for (int i = ActivePooledSources.Count - 1; i >= 0; i--)
            {
                AudioSource source = ActivePooledSources[i];
                if (source == null)
                {
                    ActivePooledSources.RemoveAt(i);
                    continue;
                }

                int id = source.GetInstanceID();
                float releaseAt = AudioReleaseTimes.TryGetValue(id, out float t) ? t : 0f;
                if (source.isPlaying || now < releaseAt)
                    continue;

                ActivePooledSources.RemoveAt(i);
                AudioReleaseTimes.Remove(id);
                RecycleAudioSource(source);
            }
        }

        private static void EnsureAudioPoolRoot()
        {
            if (_audioPoolRoot != null)
                return;

            GameObject root = new GameObject("VP_AudioPool");
            UnityEngine.Object.DontDestroyOnLoad(root);
            _audioPoolRoot = root.transform;
        }

        private static AudioSource AcquireAudioSource()
        {
            while (PooledAudioSources.Count > 0)
            {
                AudioSource pooled = PooledAudioSources.Dequeue();
                if (pooled != null)
                    return pooled;
            }

            GameObject go = new GameObject("VP_AudioSource");
            if (_audioPoolRoot != null)
                go.transform.SetParent(_audioPoolRoot, false);
            UnityEngine.Object.DontDestroyOnLoad(go);
            return go.AddComponent<AudioSource>();
        }

        private static void RecycleAudioSource(AudioSource source)
        {
            if (source == null)
                return;

            source.Stop();
            source.clip = null;
            source.transform.SetParent(_audioPoolRoot, false);
            source.gameObject.SetActive(false);

            if (PooledAudioSources.Count >= Mathf.Max(1, Cfg.AudioPoolSize.Value))
            {
                UnityEngine.Object.Destroy(source.gameObject);
                return;
            }

            PooledAudioSources.Enqueue(source);
        }

        private static void ClearAudioPool()
        {
            for (int i = 0; i < ActivePooledSources.Count; i++)
            {
                AudioSource source = ActivePooledSources[i];
                if (source != null)
                    UnityEngine.Object.Destroy(source.gameObject);
            }
            ActivePooledSources.Clear();
            AudioReleaseTimes.Clear();

            while (PooledAudioSources.Count > 0)
            {
                AudioSource source = PooledAudioSources.Dequeue();
                if (source != null)
                    UnityEngine.Object.Destroy(source.gameObject);
            }

            if (_audioPoolRoot != null)
            {
                UnityEngine.Object.Destroy(_audioPoolRoot.gameObject);
                _audioPoolRoot = null;
            }
        }

        private void DoGC(string reason)
        {
            long before = GC.GetTotalMemory(false) / (1024L * 1024L);
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Optimized, false);
            GC.WaitForPendingFinalizers();
            Resources.UnloadUnusedAssets();
            long after = GC.GetTotalMemory(false) / (1024L * 1024L);
            if (before - after > 5)
                Plugin.Log.LogInfo($"[Memory] GC ({reason}): {before}->{after}MB");
        }

        private static bool TryMergeIntoNearbyDrop(ItemDrop.ItemData item, ref int amount, Vector3 position, out ItemDrop merged)
        {
            merged = null;
            if (item == null || amount <= 0)
                return false;

            ItemDrop[] drops = UnityEngine.Object.FindObjectsByType<ItemDrop>(FindObjectsSortMode.None);
            if (drops == null || drops.Length == 0)
                return false;

            float radiusSq = DropMergeRadius * DropMergeRadius;
            for (int i = 0; i < drops.Length && amount > 0; i++)
            {
                ItemDrop drop = drops[i];
                if (drop == null || drop.gameObject == null || !drop.gameObject.activeInHierarchy)
                    continue;

                if ((drop.transform.position - position).sqrMagnitude > radiusSq)
                    continue;

                ItemDrop.ItemData existing = drop.m_itemData;
                if (!AreStackCompatible(item, existing))
                    continue;

                int maxStack = Mathf.Max(1, existing.m_shared.m_maxStackSize);
                int room = maxStack - existing.m_stack;
                if (room <= 0)
                    continue;

                int add = Mathf.Min(room, amount);
                if (add <= 0)
                    continue;

                drop.SetStack(existing.m_stack + add);
                amount -= add;
                merged = drop;
            }

            return merged != null;
        }

        private static bool AreStackCompatible(ItemDrop.ItemData incoming, ItemDrop.ItemData existing)
        {
            if (incoming == null || existing == null)
                return false;
            if (incoming.m_shared == null || existing.m_shared == null)
                return false;
            if (existing.m_shared.m_maxStackSize <= 1)
                return false;

            if (incoming.m_dropPrefab != existing.m_dropPrefab)
                return false;
            if (incoming.m_quality != existing.m_quality)
                return false;
            if (incoming.m_variant != existing.m_variant)
                return false;
            if (incoming.m_worldLevel != existing.m_worldLevel)
                return false;

            int inCustom = incoming.m_customData != null ? incoming.m_customData.Count : 0;
            int exCustom = existing.m_customData != null ? existing.m_customData.Count : 0;
            if (inCustom > 0 || exCustom > 0)
                return false;

            return true;
        }

        [HarmonyPatch]
        private static class Patches
        {
            [HarmonyPostfix]
            [HarmonyPatch(typeof(Menu), "Show")]
            private static void OnPause()
            {
                if (!Cfg.GCOnPause.Value)
                    return;
                GC.Collect(GC.MaxGeneration, GCCollectionMode.Optimized, false);
                GC.WaitForPendingFinalizers();
            }

            [HarmonyPrefix]
            [HarmonyPatch(typeof(AudioSource), "PlayClipAtPoint", new[] { typeof(AudioClip), typeof(Vector3) })]
            private static bool Prefix_PlayClipAtPoint2(AudioClip clip, Vector3 position)
            {
                if (!Cfg.PoolAudio.Value)
                    return true;

                return !TryPlayClipAtPointPooled(clip, position, 1f);
            }

            [HarmonyPrefix]
            [HarmonyPatch(typeof(AudioSource), "PlayClipAtPoint", new[] { typeof(AudioClip), typeof(Vector3), typeof(float) })]
            private static bool Prefix_PlayClipAtPoint3(AudioClip clip, Vector3 position, float volume)
            {
                if (!Cfg.PoolAudio.Value)
                    return true;

                return !TryPlayClipAtPointPooled(clip, position, volume);
            }

            [HarmonyPrefix]
            [HarmonyPatch(typeof(ItemDrop), "DropItem", new[] { typeof(ItemDrop.ItemData), typeof(int), typeof(Vector3), typeof(Quaternion) })]
            private static bool Prefix_DropItem(ItemDrop.ItemData item, ref int amount, Vector3 position, Quaternion rotation, ref ItemDrop __result)
            {
                if (!Cfg.PoolItemDrops.Value)
                    return true;
                if (item == null || amount <= 0 || item.m_shared == null || item.m_shared.m_maxStackSize <= 1)
                    return true;

                if (TryMergeIntoNearbyDrop(item, ref amount, position, out ItemDrop merged) && amount <= 0)
                {
                    __result = merged;
                    return false;
                }

                return true;
            }
        }
    }
}
