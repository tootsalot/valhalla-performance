using HarmonyLib;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ValhallaPerformance
{
    public class PieceSystem : ISystem
    {
        private static readonly Dictionary<int, (float val, float time)> _cache
            = new Dictionary<int, (float, float)>();
        private static readonly Queue<Vector3> _dirtyQueue = new Queue<Vector3>();

        private static readonly Queue<WearNTear> _asyncInitQueue = new Queue<WearNTear>();
        private static readonly HashSet<int> _asyncQueuedIds = new HashSet<int>();
        private static readonly Dictionary<int, bool> _asyncOriginalEnabled = new Dictionary<int, bool>();
        private static bool _sceneScanPending;
        private static float _asyncQueueWindowUntil;

        private static MethodInfo _wearUpdateMethod;

        public void Init(Harmony harmony)
        {
            _wearUpdateMethod = WearNTearCompat.ResolveUpdateMethod();
            if (_wearUpdateMethod != null)
            {
                harmony.Patch(_wearUpdateMethod,
                    postfix: new HarmonyMethod(typeof(SupportCapturePatches), nameof(SupportCapturePatches.Postfix_UpdateWear)));
            }
            else
            {
                Plugin.Log.LogWarning("[Pieces] No compatible WearNTear update method found; dirty-region updates will be limited.");
            }

            MethodInfo getSupportMethod = WearNTearCompat.ResolveGetSupportMethod();
            if (getSupportMethod != null)
            {
                harmony.Patch(getSupportMethod,
                    prefix: new HarmonyMethod(typeof(SupportCachePatches), nameof(SupportCachePatches.Prefix_GetSupport)),
                    postfix: new HarmonyMethod(typeof(SupportCachePatches), nameof(SupportCachePatches.Postfix_GetSupport)));
            }
            else
            {
                Plugin.Log.LogWarning("[Pieces] No compatible WearNTear support method found; support cache disabled.");
            }

            harmony.PatchAll(typeof(DirtyMarkerPatches));
            harmony.PatchAll(typeof(AsyncInitPatches));
            SceneManager.sceneLoaded += OnSceneLoaded;

            Plugin.Log.LogInfo("[Pieces] Active - support caching + dirty-region updates");
        }

        public void Tick()
        {
            if (StaggerScheduler.ShouldRun("pieces.cache_purge", 15f, RuntimeTuning.CleanupIntervalMultiplier))
                PurgeSupportCache(Time.unscaledTime);

            if (Cfg.PieceDirtyRegion.Value && _dirtyQueue.Count > 0 &&
                StaggerScheduler.ShouldRun("pieces.dirty", Cfg.PieceDirtyInterval.Value))
            {
                ProcessDirtyRegions();
            }

            if (Cfg.AsyncWearInit.Value && _sceneScanPending &&
                StaggerScheduler.ShouldRun("pieces.async_scan", 0.2f))
            {
                _sceneScanPending = false;
                QueueSceneWearNTear();
            }

            if (Cfg.AsyncWearInit.Value && _asyncInitQueue.Count > 0)
                ProcessAsyncInitQueue();
        }

        public void Cleanup()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            _cache.Clear();
            _dirtyQueue.Clear();
            _asyncInitQueue.Clear();
            _asyncQueuedIds.Clear();
            _asyncOriginalEnabled.Clear();
            _sceneScanPending = false;
            _asyncQueueWindowUntil = 0f;
        }

        private static void OnSceneLoaded(Scene _, LoadSceneMode __)
        {
            if (!Cfg.AsyncWearInit.Value)
                return;

            _sceneScanPending = true;
            _asyncQueueWindowUntil = Time.unscaledTime + 20f;
        }

        private static void PurgeSupportCache(float time)
        {
            float ttl = Cfg.SupportCacheTTL.Value * 3f;
            var stale = new List<int>();
            foreach (var kvp in _cache)
            {
                if (time - kvp.Value.time > ttl)
                    stale.Add(kvp.Key);
            }

            foreach (int key in stale)
                _cache.Remove(key);
        }

        private static void ProcessDirtyRegions()
        {
            if (_dirtyQueue.Count == 0)
                return;

            if (_wearUpdateMethod == null)
                return;

            Vector3 center = _dirtyQueue.Dequeue();
            float radius = Mathf.Max(4f, Cfg.PieceDirtyRadius.Value * RuntimeTuning.CullingDistanceMultiplier);
            float radiusSq = radius * radius;
            int batch = Mathf.Max(1, Cfg.PieceDirtyBatch.Value);

            WearNTear[] pieces = Object.FindObjectsByType<WearNTear>(FindObjectsSortMode.None);
            int processed = 0;
            foreach (WearNTear piece in pieces)
            {
                if (piece == null)
                    continue;
                if ((piece.transform.position - center).sqrMagnitude > radiusSq)
                    continue;

                if (!InvokeWearUpdate(piece))
                    continue;

                processed++;
                if (processed >= batch)
                    break;
            }
        }

        private static void QueueSceneWearNTear()
        {
            WearNTear[] all = Object.FindObjectsByType<WearNTear>(FindObjectsSortMode.None);
            int queued = 0;

            foreach (WearNTear piece in all)
            {
                if (QueueForAsyncInit(piece))
                    queued++;
            }

            if (queued > 0)
                Plugin.Log.LogInfo($"[Pieces] Async init queued {queued} WearNTear components");
        }

        private static void ProcessAsyncInitQueue()
        {
            int batch = Mathf.Max(1, Cfg.AsyncWearBatchSize.Value);
            int processed = 0;

            while (processed < batch && _asyncInitQueue.Count > 0)
            {
                WearNTear piece = _asyncInitQueue.Dequeue();
                if (piece == null)
                {
                    processed++;
                    continue;
                }

                int id = piece.GetInstanceID();
                _asyncQueuedIds.Remove(id);

                bool originalEnabled = true;
                if (_asyncOriginalEnabled.TryGetValue(id, out bool wasEnabled))
                    originalEnabled = wasEnabled;
                _asyncOriginalEnabled.Remove(id);

                if (!originalEnabled)
                {
                    piece.enabled = false;
                    processed++;
                    continue;
                }

                if (!piece.enabled)
                    piece.enabled = true;

                InvokeWearUpdate(piece);
                processed++;
            }
        }

        private static bool QueueForAsyncInit(WearNTear piece)
        {
            if (!Cfg.AsyncWearInit.Value || piece == null)
                return false;

            if (piece.gameObject == null || !piece.gameObject.activeInHierarchy)
                return false;

            int id = piece.GetInstanceID();
            if (_asyncQueuedIds.Contains(id))
                return false;

            _asyncQueuedIds.Add(id);
            _asyncOriginalEnabled[id] = piece.enabled;

            if (piece.enabled)
                piece.enabled = false;

            _asyncInitQueue.Enqueue(piece);
            return true;
        }

        private static bool ShouldQueueAsyncAwake()
        {
            if (!Cfg.AsyncWearInit.Value)
                return false;

            if (_sceneScanPending || _asyncInitQueue.Count > 0)
                return true;

            return Time.unscaledTime <= _asyncQueueWindowUntil;
        }

        private static bool InvokeWearUpdate(WearNTear piece)
        {
            try
            {
                _wearUpdateMethod?.Invoke(piece, null);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static void MarkDirty(Vector3 position)
        {
            // Flush support cache on structural changes to avoid stale values.
            _cache.Clear();

            if (_dirtyQueue.Count > 512)
                _dirtyQueue.Dequeue();
            _dirtyQueue.Enqueue(position);
        }

        private static class SupportCapturePatches
        {
            public static void Postfix_UpdateWear(WearNTear __instance)
            {
                if (__instance == null)
                    return;

                int id = __instance.GetInstanceID();
                float support = Traverse.Create(__instance).Field("m_support").GetValue<float>();
                _cache[id] = (support, Time.unscaledTime);
            }
        }

        private static class SupportCachePatches
        {
            public static bool Prefix_GetSupport(WearNTear __instance, ref float __result)
            {
                if (__instance == null)
                    return true;

                float ttl = Cfg.SupportCacheTTL.Value;
                if (ttl <= 0f)
                    return true;

                int id = __instance.GetInstanceID();
                if (_cache.TryGetValue(id, out var cached) && Time.unscaledTime - cached.time <= ttl)
                {
                    __result = cached.val;
                    return false;
                }

                return true;
            }

            public static void Postfix_GetSupport(WearNTear __instance, float __result)
            {
                if (__instance == null)
                    return;

                _cache[__instance.GetInstanceID()] = (__result, Time.unscaledTime);
            }
        }

        [HarmonyPatch]
        private static class DirtyMarkerPatches
        {
            private static IEnumerable<MethodBase> TargetMethods()
            {
                string[] names =
                {
                    "Damage",
                    "ApplyDamage",
                    "RPC_Damage",
                    "RPC_HealthChanged",
                    "Remove"
                };

                foreach (string name in names)
                {
                    MethodInfo method = AccessTools.Method(typeof(WearNTear), name);
                    if (method != null)
                        yield return method;
                }
            }

            [HarmonyPostfix]
            private static void Postfix(WearNTear __instance)
            {
                if (__instance == null || !Cfg.PieceDirtyRegion.Value)
                    return;
                MarkDirty(__instance.transform.position);
            }
        }

        [HarmonyPatch]
        private static class AsyncInitPatches
        {
            [HarmonyPostfix]
            [HarmonyPatch(typeof(WearNTear), "Awake")]
            private static void Postfix_WearNTearAwake(WearNTear __instance)
            {
                if (!ShouldQueueAsyncAwake())
                    return;

                QueueForAsyncInit(__instance);
            }
        }
    }
}
