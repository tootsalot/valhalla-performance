using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace ValhallaPerformance
{
    public class SmokeSystem : ISystem
    {
        private static readonly Dictionary<int, int> _areaCount = new Dictionary<int, int>();
        private static readonly Dictionary<int, int> _instanceArea = new Dictionary<int, int>();
        private static readonly BindingFlags AnyInstance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private static readonly FieldInfo SmokeTimeField = typeof(Smoke).GetField("m_time", AnyInstance);
        private static readonly FieldInfo SmokeRendererField = typeof(Smoke).GetField("m_renderer", AnyInstance);
        private static Vector3 _playerPos;
        private static float _lastPosUpdate;
        private static RaycastHit _hitInfo;

        public void Init(Harmony harmony)
        {
            harmony.PatchAll(typeof(Patches));
            Plugin.Log.LogInfo("[Smoke] Active");
        }

        public void Tick() { }

        public void Cleanup()
        {
            _areaCount.Clear();
            _instanceArea.Clear();
        }

        private static void RefreshPos()
        {
            if (Time.unscaledTime - _lastPosUpdate < 1f)
                return;

            _lastPosUpdate = Time.unscaledTime;
            if (Player.m_localPlayer != null)
                _playerPos = Player.m_localPlayer.transform.position;
        }

        private static int AreaHash(Vector3 p)
        {
            // Horizontal source area hash only; smoke naturally rises so y should not split buckets.
            int gx = Mathf.FloorToInt(p.x / 6f);
            int gz = Mathf.FloorToInt(p.z / 6f);
            return gx * 73856093 ^ gz * 83492791;
        }

        [HarmonyPatch]
        private static class Patches
        {
            [HarmonyPrefix]
            [HarmonyPatch(typeof(Smoke), "CustomUpdate")]
            private static bool Prefix_CustomUpdate(Smoke __instance, float deltaTime, float time)
            {
                if (Plugin.IsDedicatedProcess)
                    return true;

                if (__instance == null)
                    return false;

                if (SmokeTimeField == null || SmokeRendererField == null)
                    return true;

                float mTime = 0f;
                try
                {
                    mTime = Convert.ToSingle(SmokeTimeField.GetValue(__instance));
                }
                catch
                {
                    return true;
                }
                mTime += deltaTime;
                SmokeTimeField.SetValue(__instance, mTime);

                float maxLife = Mathf.Max(1f, Cfg.SmokeLife.Value);
                if (mTime > maxLife)
                {
                    UnityEngine.Object.Destroy(__instance.gameObject);
                    return false;
                }

                RefreshPos();
                Vector3 pos = __instance.transform.position;
                float cullSq = Cfg.SmokeCullDist.Value * Cfg.SmokeCullDist.Value;
                float t = Mathf.Clamp01(mTime / maxLife);

                // More sustained upward movement so plumes actually rise over their lifetime.
                float baseLift = Cfg.SmokeLift.Value * (1.9f - 1.1f * t);
                float lift = Mathf.Max(0.05f, baseLift);

                if ((pos - _playerPos).sqrMagnitude > cullSq)
                {
                    // Keep a cheap far update so smoke does not appear to die in place when culled.
                    __instance.transform.position += Vector3.up * (lift * 0.75f * deltaTime);
                    return false;
                }

                int id = __instance.GetInstanceID();
                float px = id * 0.137f;
                float wx = Mathf.Sin(Time.time * 0.3f + px) * 0.12f;
                float wz = Mathf.Cos(Time.time * 0.23f + px + 2.1f) * 0.12f;
                Vector3 vel = new Vector3(wx, lift, wz);

                if (Cfg.SmokeCollision.Value && lift > 0.01f)
                {
                    float checkDist = lift * deltaTime * 3f + 0.5f;
                    if (Physics.SphereCast(pos, Cfg.SmokeCollisionRadius.Value, Vector3.up,
                        out _hitInfo, checkDist,
                        LayerMask.GetMask("piece", "static_solid", "Default_small", "terrain")))
                    {
                        vel.y = Mathf.Min(vel.y, 0.01f);
                        vel.x *= 3f;
                        vel.z *= 3f;
                        if (_hitInfo.distance < 0.3f)
                        {
                            vel.x *= 0.5f;
                            vel.z *= 0.5f;
                        }
                    }
                }

                __instance.transform.position += vel * deltaTime;

                float alpha = t < 0.25f ? 1f : 1f - (t - 0.25f) / 0.75f;
                MeshRenderer rend = SmokeRendererField.GetValue(__instance) as MeshRenderer;
                if (rend != null && rend.material != null)
                {
                    Color c = rend.material.color;
                    c.a = Mathf.Clamp01(alpha);
                    rend.material.color = c;
                }

                return false;
            }

            [HarmonyPrefix]
            [HarmonyPatch(typeof(Smoke), "Awake")]
            private static bool Prefix_Awake(Smoke __instance)
            {
                if (Plugin.IsDedicatedProcess)
                    return true;

                int key = AreaHash(__instance.transform.position);
                _areaCount.TryGetValue(key, out int count);
                if (count >= Cfg.SmokeMaxPerSource.Value)
                {
                    UnityEngine.Object.Destroy(__instance.gameObject);
                    return false;
                }

                _areaCount[key] = count + 1;
                _instanceArea[__instance.GetInstanceID()] = key;
                return true;
            }

            [HarmonyPostfix]
            [HarmonyPatch(typeof(Smoke), "OnDestroy")]
            private static void Postfix_OnDestroy(Smoke __instance)
            {
                if (__instance == null)
                    return;

                int id = __instance.GetInstanceID();
                int key;
                if (!_instanceArea.TryGetValue(id, out key))
                    key = AreaHash(__instance.transform.position);

                if (_areaCount.TryGetValue(key, out int c))
                {
                    c--;
                    if (c <= 0)
                        _areaCount.Remove(key);
                    else
                        _areaCount[key] = c;
                }

                _instanceArea.Remove(id);
            }
        }
    }
}
