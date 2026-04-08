using HarmonyLib;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace ValhallaPerformance
{
    public class CullingSystem : ISystem
    {
        private static Vector3 _playerPos;
        private int _scanPhase;
        private static readonly Dictionary<long, (bool result, float time)> _losCache
            = new Dictionary<long, (bool, float)>();
        private static readonly List<long> _staleLosKeys = new List<long>();

        private static readonly Dictionary<int, (float speed, AnimatorCullingMode mode)> _animDefaults
            = new Dictionary<int, (float, AnimatorCullingMode)>();
        private static readonly Dictionary<int, bool> _skinDefaults
            = new Dictionary<int, bool>();
        private static readonly HashSet<MethodBase> _patchedSenseMethods
            = new HashSet<MethodBase>();
        private static readonly HashSet<int> _liveAnimatorIds = new HashSet<int>();
        private static readonly List<int> _staleAnimatorIds = new List<int>();
        private static readonly HashSet<int> _liveSkinIds = new HashSet<int>();
        private static readonly List<int> _staleSkinIds = new List<int>();

        private static MethodInfo _wearUpdateMethod;

        public void Init(Harmony harmony)
        {
            harmony.PatchAll(typeof(Patches));

            MethodInfo havePath = ResolveDeclaredMethod(typeof(BaseAI), "HavePath", typeof(Vector3)) ??
                                  ResolveDeclaredMethod(typeof(BaseAI), "HavePath");
            if (havePath != null)
            {
                harmony.Patch(havePath,
                    postfix: new HarmonyMethod(typeof(LOSPatches), nameof(LOSPatches.Postfix_HavePath)));
            }

            PatchSenseMethod(harmony, typeof(BaseAI), "CanSeeTarget");
            PatchSenseMethod(harmony, typeof(MonsterAI), "CanSeeTarget");
            PatchSenseMethod(harmony, typeof(AnimalAI), "CanSeeTarget");
            PatchSenseMethod(harmony, typeof(BaseAI), "CanHearTarget");
            PatchSenseMethod(harmony, typeof(MonsterAI), "CanHearTarget");
            PatchSenseMethod(harmony, typeof(AnimalAI), "CanHearTarget");

            PatchWearUpdateMethod(harmony);
            Plugin.Log.LogInfo("[Culling] Active");
        }

        private static void PatchSenseMethod(Harmony harmony, System.Type owner, string methodName)
        {
            MethodInfo method = ResolveDeclaredMethod(owner, methodName, typeof(Character));
            if (method == null)
                return;

            if (!_patchedSenseMethods.Add(method))
                return;

            harmony.Patch(method,
                postfix: new HarmonyMethod(typeof(LOSPatches), nameof(LOSPatches.Postfix_SenseTarget)));
        }

        private static MethodInfo ResolveDeclaredMethod(System.Type owner, string methodName, params System.Type[] args)
        {
            if (owner == null)
                return null;

            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
            return owner.GetMethod(methodName, flags, null, args, null);
        }

        private static void PatchWearUpdateMethod(Harmony harmony)
        {
            _wearUpdateMethod = WearNTearCompat.ResolveUpdateMethod();
            if (_wearUpdateMethod == null)
            {
                Plugin.Log.LogWarning("[Culling] No compatible WearNTear update method found; piece-distance throttling disabled.");
                return;
            }

            harmony.Patch(_wearUpdateMethod,
                prefix: new HarmonyMethod(typeof(Patches), nameof(Patches.Prefix_WearNTear)),
                postfix: new HarmonyMethod(typeof(Patches), nameof(Patches.Postfix_WearNTear)));
        }

        public void Tick()
        {
            if (Player.m_localPlayer != null)
                _playerPos = Player.m_localPlayer.transform.position;

            if (StaggerScheduler.ShouldRun("culling.los_purge", 10f))
                PurgeLOSCache(Time.unscaledTime);

            float animInterval = Mathf.Max(0.1f, Cfg.AnimScanInterval.Value);
            RunOneHeavyScan(animInterval);

            if (StaggerScheduler.ShouldRun("culling.anim_cleanup", 30f))
                CleanupAnimationCaches();
        }

        public void Cleanup()
        {
            _scanPhase = 0;
            _losCache.Clear();
            _animDefaults.Clear();
            _skinDefaults.Clear();
            _patchedSenseMethods.Clear();
            _liveAnimatorIds.Clear();
            _staleAnimatorIds.Clear();
            _liveSkinIds.Clear();
            _staleSkinIds.Clear();
            _staleLosKeys.Clear();
        }

        private void PurgeLOSCache(float time)
        {
            float ttl = Mathf.Max(0.05f, Cfg.LOSCacheDuration.Value * 5f);
            _staleLosKeys.Clear();
            foreach (var kvp in _losCache)
            {
                if (time - kvp.Value.time > ttl)
                    _staleLosKeys.Add(kvp.Key);
            }

            foreach (long key in _staleLosKeys)
                _losCache.Remove(key);
        }

        private void RunOneHeavyScan(float animInterval)
        {
            // Run at most one expensive global scan each frame to reduce scan clumping hitches.
            for (int attempt = 0; attempt < 3; attempt++)
            {
                int phase = (_scanPhase + attempt) % 3;
                if (phase == 0)
                {
                    if (!StaggerScheduler.ShouldRun("culling.animators", animInterval))
                        continue;

                    UpdateAnimatorThrottling();
                    _scanPhase = 1;
                    return;
                }

                if (phase == 1)
                {
                    if (!StaggerScheduler.ShouldRun("culling.skins", animInterval * 1.4f))
                        continue;

                    UpdateSkinnedMeshThrottling();
                    _scanPhase = 2;
                    return;
                }

                if (!Cfg.SleepFarRagdolls.Value)
                {
                    _scanPhase = 0;
                    continue;
                }

                if (!StaggerScheduler.ShouldRun("culling.ragdolls", animInterval * 2.5f))
                    continue;

                UpdateRagdollSleeping();
                _scanPhase = 0;
                return;
            }

            _scanPhase = (_scanPhase + 1) % 3;
        }

        private static bool TryGetCachedSense(int aiId, int targetKey, out bool result)
        {
            result = false;

            long key = ((long)aiId << 32) | (uint)targetKey;
            if (!_losCache.TryGetValue(key, out var cached))
                return false;

            if (Time.unscaledTime - cached.time > Mathf.Max(0.05f, Cfg.LOSCacheDuration.Value))
                return false;

            result = cached.result;
            return true;
        }

        private static void UpdateAnimatorThrottling()
        {
            if (Player.m_localPlayer == null)
                return;

            float cullMultiplier = RuntimeTuning.CullingDistanceMultiplier;
            float throttleDist = Mathf.Max(10f, Cfg.AnimThrottleDist.Value * cullMultiplier);
            float throttleSq = throttleDist * throttleDist;
            float farSpeed = Mathf.Clamp(Cfg.AnimFarSpeed.Value, 0.05f, 1f);
            AnimatorCullingMode farMode = Cfg.AnimAggressiveCull.Value
                ? AnimatorCullingMode.CullCompletely
                : AnimatorCullingMode.CullUpdateTransforms;

            Animator[] animators = Object.FindObjectsByType<Animator>(FindObjectsSortMode.None);
            foreach (Animator animator in animators)
            {
                if (animator == null)
                    continue;

                int id = animator.GetInstanceID();
                if (ShouldSkipAnimator(animator))
                {
                    if (_animDefaults.TryGetValue(id, out var original))
                    {
                        animator.speed = original.speed;
                        animator.cullingMode = original.mode;
                        _animDefaults.Remove(id);
                    }
                    continue;
                }

                float distSq = (animator.transform.position - _playerPos).sqrMagnitude;
                if (distSq > throttleSq)
                {
                    if (!_animDefaults.ContainsKey(id))
                        _animDefaults[id] = (animator.speed, animator.cullingMode);

                    animator.speed = Mathf.Min(animator.speed, farSpeed);
                    animator.cullingMode = farMode;
                }
                else if (_animDefaults.TryGetValue(id, out var original))
                {
                    animator.speed = original.speed;
                    animator.cullingMode = original.mode;
                }
            }

        }

        private static void UpdateSkinnedMeshThrottling()
        {
            if (Player.m_localPlayer == null)
                return;

            float cullMultiplier = RuntimeTuning.CullingDistanceMultiplier;
            float throttleDist = Mathf.Max(10f, Cfg.AnimThrottleDist.Value * cullMultiplier);
            float throttleSq = throttleDist * throttleDist;

            SkinnedMeshRenderer[] skins = Object.FindObjectsByType<SkinnedMeshRenderer>(FindObjectsSortMode.None);
            foreach (SkinnedMeshRenderer skin in skins)
            {
                if (skin == null)
                    continue;

                int id = skin.GetInstanceID();
                if (ShouldSkipSkinnedMesh(skin))
                {
                    if (_skinDefaults.TryGetValue(id, out bool original))
                    {
                        skin.updateWhenOffscreen = original;
                        _skinDefaults.Remove(id);
                    }
                    continue;
                }

                float distSq = (skin.transform.position - _playerPos).sqrMagnitude;
                if (distSq > throttleSq)
                {
                    if (!_skinDefaults.ContainsKey(id))
                        _skinDefaults[id] = skin.updateWhenOffscreen;
                    skin.updateWhenOffscreen = false;
                }
                else if (_skinDefaults.TryGetValue(id, out bool original))
                {
                    skin.updateWhenOffscreen = original;
                }
            }
        }

        private static void UpdateRagdollSleeping()
        {
            if (Player.m_localPlayer == null)
                return;

            float cullMultiplier = RuntimeTuning.CullingDistanceMultiplier;
            float throttleDist = Mathf.Max(10f, Cfg.AnimThrottleDist.Value * cullMultiplier);
            float ragdollSq = throttleDist * throttleDist * 1.2f;

            Rigidbody[] bodies = Object.FindObjectsByType<Rigidbody>(FindObjectsSortMode.None);
            foreach (Rigidbody body in bodies)
            {
                if (body == null || body.isKinematic || !body.gameObject.activeInHierarchy)
                    continue;
                if ((body.transform.position - _playerPos).sqrMagnitude <= ragdollSq)
                    continue;
                if (body.linearVelocity.sqrMagnitude > 1f)
                    continue;
                body.Sleep();
            }
        }

        private static bool ShouldSkipAnimator(Animator animator)
        {
            Transform t = animator.transform;
            if (t == null)
                return true;

            Player local = Player.m_localPlayer;
            if (local != null)
            {
                Transform p = local.transform;
                if (t == p || t.IsChildOf(p) || animator.GetComponentInParent<Player>() != null)
                    return true;
            }

            return IsUiHierarchy(t);
        }

        private static bool ShouldSkipSkinnedMesh(SkinnedMeshRenderer skin)
        {
            Transform t = skin.transform;
            if (t == null)
                return true;

            Player local = Player.m_localPlayer;
            if (local != null)
            {
                Transform p = local.transform;
                if (t == p || t.IsChildOf(p) || skin.GetComponentInParent<Player>() != null)
                    return true;
            }

            return IsUiHierarchy(t);
        }

        private static bool IsUiHierarchy(Transform t)
        {
            return t.GetComponentInParent<RectTransform>() != null ||
                   t.GetComponentInParent<InventoryGui>() != null ||
                   t.GetComponentInParent<Hud>() != null ||
                   t.GetComponentInParent<Menu>() != null ||
                   t.GetComponentInParent<Minimap>() != null;
        }

        private static void CleanupAnimationCaches()
        {
            Animator[] animators = Object.FindObjectsByType<Animator>(FindObjectsSortMode.None);
            _liveAnimatorIds.Clear();
            foreach (Animator animator in animators)
            {
                if (animator != null)
                    _liveAnimatorIds.Add(animator.GetInstanceID());
            }

            _staleAnimatorIds.Clear();
            foreach (var kvp in _animDefaults)
            {
                if (!_liveAnimatorIds.Contains(kvp.Key))
                    _staleAnimatorIds.Add(kvp.Key);
            }
            foreach (int key in _staleAnimatorIds)
                _animDefaults.Remove(key);

            SkinnedMeshRenderer[] skins = Object.FindObjectsByType<SkinnedMeshRenderer>(FindObjectsSortMode.None);
            _liveSkinIds.Clear();
            foreach (SkinnedMeshRenderer skin in skins)
            {
                if (skin != null)
                    _liveSkinIds.Add(skin.GetInstanceID());
            }

            _staleSkinIds.Clear();
            foreach (var kvp in _skinDefaults)
            {
                if (!_liveSkinIds.Contains(kvp.Key))
                    _staleSkinIds.Add(kvp.Key);
            }
            foreach (int key in _staleSkinIds)
                _skinDefaults.Remove(key);
        }

        internal static void CacheLOS(int a, int b, bool result)
        {
            long key = ((long)a << 32) | (uint)b;
            _losCache[key] = (result, Time.unscaledTime);
        }

        [HarmonyPatch]
        private static class Patches
        {
            [HarmonyPrefix]
            [HarmonyPatch(typeof(MonsterAI), "UpdateAI")]
            private static bool Prefix_UpdateAI(MonsterAI __instance, float dt)
            {
                if (__instance == null || Player.m_localPlayer == null)
                    return true;

                float cullMultiplier = RuntimeTuning.CullingDistanceMultiplier;
                float throttleDist = Cfg.AIThrottleDist.Value * cullMultiplier;
                float distSq = (__instance.transform.position - _playerPos).sqrMagnitude;
                float throttleSq = throttleDist * throttleDist;
                if (distSq <= throttleSq)
                    return true;

                if (__instance.IsAlerted())
                    return true;

                // Hard sleep: completely skip AI beyond creature cull distance.
                float creatureCullDist = Cfg.CreatureCullDist.Value * cullMultiplier;
                if (distSq > creatureCullDist * creatureCullDist)
                    return false;

                float interval = Cfg.AIThrottleInterval.Value;
                Character target = __instance.GetTargetCreature();
                if (target != null && TryGetCachedSense(__instance.GetInstanceID(), target.GetInstanceID(), out bool sensed))
                {
                    if (sensed && distSq <= throttleSq * 1.75f)
                        return true;

                    if (!sensed)
                        interval *= 1.6f;
                }

                int id = __instance.GetInstanceID();
                float offset = (id % 100) * 0.01f * interval;
                float phase = (Time.time + offset) % interval;
                if (phase >= dt)
                    return false;

                return true;
            }

            [HarmonyPostfix]
            [HarmonyPatch(typeof(MonsterAI), "UpdateAI")]
            private static void Postfix_UpdateAI(MonsterAI __instance)
            {
            }

            [HarmonyPrefix]
            [HarmonyPatch(typeof(AnimalAI), "UpdateAI")]
            private static bool Prefix_AnimalAI(AnimalAI __instance, float dt)
            {
                if (!Cfg.TamedIdleLowPower.Value || __instance == null || Player.m_localPlayer == null)
                    return true;

                Tameable tameable = __instance.GetComponent<Tameable>();
                if (tameable == null || !tameable.IsTamed())
                    return true;
                if (tameable.IsHungry() || tameable.HaveRider())
                    return true;
                if (__instance.IsAlerted() || __instance.HaveTarget())
                    return true;

                float cullMultiplier = RuntimeTuning.CullingDistanceMultiplier;
                float nearDist = Mathf.Max(0f, Cfg.TamedIdleDistance.Value * cullMultiplier);
                float distSq = (__instance.transform.position - _playerPos).sqrMagnitude;
                if (distSq <= nearDist * nearDist)
                    return true;

                float interval = Mathf.Max(0.25f, Cfg.TamedIdleInterval.Value);
                int id = __instance.GetInstanceID();
                float offset = (id % 100) * 0.01f * interval;
                float phase = (Time.time + offset) % interval;
                if (phase >= dt)
                    return false;

                return true;
            }

            [HarmonyPostfix]
            [HarmonyPatch(typeof(AnimalAI), "UpdateAI")]
            private static void Postfix_AnimalAI(AnimalAI __instance)
            {
            }

            internal static bool Prefix_WearNTear(WearNTear __instance)
            {
                if (__instance == null || Player.m_localPlayer == null)
                    return true;

                float cullMultiplier = RuntimeTuning.CullingDistanceMultiplier;
                float pieceDist = Cfg.PieceCullDist.Value * cullMultiplier;
                float distSq = (__instance.transform.position - Player.m_localPlayer.transform.position).sqrMagnitude;
                if (distSq > pieceDist * pieceDist)
                    return false;

                return true;
            }

            internal static void Postfix_WearNTear(WearNTear __instance)
            {
            }
        }

        internal static class LOSPatches
        {
            public static void Postfix_HavePath(BaseAI __instance, Vector3 target, bool __result)
            {
                if (__instance == null)
                    return;
                CacheLOS(__instance.GetInstanceID(), target.GetHashCode(), __result);
            }

            public static void Postfix_SenseTarget(BaseAI __instance, Character target, bool __result)
            {
                if (__instance == null || target == null)
                    return;

                CacheLOS(__instance.GetInstanceID(), target.GetInstanceID(), __result);
            }
        }
    }
}
