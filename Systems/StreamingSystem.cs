using HarmonyLib;
using System;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace ValhallaPerformance
{
    public class StreamingSystem : ISystem
    {
        private Vector3 _lastPos;
        private float _lastSampleTime;
        private float _speed;
        private bool _travelMode;
        private float _aboveThresholdTime;
        private float _belowThresholdTime;

        private static readonly BindingFlags AnyInstance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private static readonly BindingFlags AnyMember = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        private static readonly MethodInfo ZoneGetZoneMethod =
            typeof(ZoneSystem).GetMethod("GetZone", AnyMember, null, new[] { typeof(Vector3) }, null);

        private static readonly MethodInfo ZoneGetBiomeMethod =
            typeof(ZoneSystem).GetMethod("GetBiome", AnyMember, null, new[] { typeof(Vector3) }, null);

        private static readonly MethodInfo ZoneGetGroundHeightMethod = FindZoneGroundHeight();

        private static readonly Type HeightmapBuilderType = FindType("HeightmapBuilder");
        private static readonly FieldInfo HeightmapInstanceField = FindHeightmapInstanceField();
        private static readonly MethodInfo HeightmapForceGenerateMethod = FindHeightmapForceGenerate();
        private static readonly bool CanForceHeightmap = HeightmapInstanceField != null && HeightmapForceGenerateMethod != null;

        public void Init(Harmony harmony)
        {
            RuntimeTuning.Reset();
            Plugin.Log.LogInfo("[Streaming] Active");
            Plugin.Log.LogInfo(
                $"[Streaming] Prefetch hooks zone={(ZoneGetZoneMethod != null)} biome={(ZoneGetBiomeMethod != null)} ground={(ZoneGetGroundHeightMethod != null && CanForceHeightmap)} hmap={CanForceHeightmap}");
        }

        public void Tick()
        {
            Player player = Player.m_localPlayer;
            if (player == null)
            {
                _aboveThresholdTime = 0f;
                _belowThresholdTime = 0f;
                ApplyTravelMode(false);
                return;
            }

            Vector3 pos = player.transform.position;
            float now = Time.unscaledTime;
            if (_lastSampleTime <= 0f)
            {
                _lastSampleTime = now;
                _lastPos = pos;
                return;
            }

            float dt = Mathf.Max(0.01f, now - _lastSampleTime);
            Vector3 delta = pos - _lastPos;
            float instantSpeed = delta.magnitude / dt;
            _speed = Mathf.Lerp(_speed, instantSpeed, 0.25f);
            _lastSampleTime = now;
            _lastPos = pos;

            UpdateTravelMode(dt);
            bool fastTravel = _travelMode;

            if (!StaggerScheduler.ShouldRun("streaming.prefetch", Cfg.StreamingPrefetchInterval.Value))
                return;

            Vector3 forward = delta.sqrMagnitude > 0.01f ? delta.normalized : player.transform.forward;
            int zonesAhead = Mathf.Max(1, Cfg.StreamingZonesAhead.Value + (fastTravel ? Cfg.StreamingExtraZonesSailing.Value : 0));

            const float zoneSize = 64f;
            for (int i = 1; i <= zonesAhead; i++)
            {
                Vector3 probe = pos + forward * (zoneSize * i);
                TryPrefetchZone(probe);
            }
        }

        public void Cleanup()
        {
            _aboveThresholdTime = 0f;
            _belowThresholdTime = 0f;
            RuntimeTuning.Reset();
        }

        private void UpdateTravelMode(float dt)
        {
            if (!Cfg.StreamingSafeMode.Value)
            {
                _aboveThresholdTime = 0f;
                _belowThresholdTime = 0f;
                ApplyTravelMode(false);
                return;
            }

            float threshold = Mathf.Max(0f, Cfg.StreamingSpeedThreshold.Value);
            float hysteresis = Mathf.Clamp(Cfg.StreamingSpeedHysteresis.Value, 0f, Mathf.Max(0.5f, threshold));
            float enterThreshold = threshold + hysteresis;
            float exitThreshold = Mathf.Max(0f, threshold - hysteresis);
            float enterDelay = Mathf.Max(0f, Cfg.StreamingEnterDelay.Value);
            float exitDelay = Mathf.Max(0f, Cfg.StreamingExitDelay.Value);

            if (_travelMode)
            {
                _aboveThresholdTime = 0f;
                _belowThresholdTime = _speed <= exitThreshold ? _belowThresholdTime + dt : 0f;
                if (_belowThresholdTime >= exitDelay)
                {
                    _belowThresholdTime = 0f;
                    ApplyTravelMode(false);
                }
                return;
            }

            _belowThresholdTime = 0f;
            _aboveThresholdTime = _speed >= enterThreshold ? _aboveThresholdTime + dt : 0f;
            if (_aboveThresholdTime >= enterDelay)
            {
                _aboveThresholdTime = 0f;
                ApplyTravelMode(true);
            }
        }

        private void ApplyTravelMode(bool enabled)
        {
            if (_travelMode == enabled)
                return;

            _travelMode = enabled;
            float cleanupMultiplier = Cfg.StreamingRelaxCleanup.Value ? Cfg.StreamingSweepMultiplier.Value : 1f;
            RuntimeTuning.SetTravelMode(enabled, Cfg.StreamingCullBoost.Value, cleanupMultiplier);

            if (enabled)
                Plugin.Log.LogInfo("[Streaming] Travel-safe mode enabled");
            else
                Plugin.Log.LogInfo("[Streaming] Travel-safe mode disabled");
        }

        private static void TryPrefetchZone(Vector3 point)
        {
            try
            {
                ZoneSystem zs = ZoneSystem.instance;
                InvokeZoneMethod(zs, ZoneGetZoneMethod, point);
                InvokeZoneMethod(zs, ZoneGetBiomeMethod, point);
                if (CanForceHeightmap)
                    InvokeZoneMethod(zs, ZoneGetGroundHeightMethod, point);
            }
            catch { }

            try
            {
                if (!CanForceHeightmap)
                    return;

                object builder = HeightmapInstanceField.GetValue(null);
                if (builder == null)
                    return;

                HeightmapForceGenerateMethod.Invoke(builder, new object[] { point });
            }
            catch { }
        }

        private static void InvokeZoneMethod(ZoneSystem zs, MethodInfo method, Vector3 point)
        {
            if (method == null)
                return;

            object target = method.IsStatic ? null : zs;
            if (!method.IsStatic && target == null)
                return;

            ParameterInfo[] parameters = method.GetParameters();
            if (parameters.Length == 1 && parameters[0].ParameterType == typeof(Vector3))
            {
                method.Invoke(target, new object[] { point });
                return;
            }

            if (parameters.Length == 2 && parameters[0].ParameterType == typeof(Vector3) && parameters[1].IsOut)
            {
                object[] args = { point, 0f };
                method.Invoke(target, args);
            }
        }

        private static Type FindType(string name)
        {
            Type direct = Type.GetType(name);
            if (direct != null)
                return direct;

            foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type t = asm.GetType(name);
                if (t != null)
                    return t;

                try
                {
                    t = asm.GetTypes().FirstOrDefault(type => type.Name == name || type.FullName == name);
                    if (t != null)
                        return t;
                }
                catch { }
            }

            return null;
        }

        private static FieldInfo FindHeightmapInstanceField()
        {
            if (HeightmapBuilderType == null)
                return null;

            FieldInfo direct = HeightmapBuilderType.GetField("instance", AnyMember) ??
                               HeightmapBuilderType.GetField("m_instance", AnyMember);
            if (direct != null && direct.IsStatic)
                return direct;

            return HeightmapBuilderType
                .GetFields(AnyMember)
                .FirstOrDefault(field => field.IsStatic && field.FieldType == HeightmapBuilderType &&
                                         field.Name.IndexOf("instance", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static MethodInfo FindHeightmapForceGenerate()
        {
            if (HeightmapBuilderType == null)
                return null;

            MethodInfo exact = HeightmapBuilderType.GetMethod("ForceGenerate", AnyInstance, null, new[] { typeof(Vector3) }, null);
            if (exact != null)
                return exact;

            return HeightmapBuilderType
                .GetMethods(AnyInstance)
                .FirstOrDefault(m => m.Name == "ForceGenerate" && m.GetParameters().Length == 1 &&
                                     m.GetParameters()[0].ParameterType == typeof(Vector3));
        }

        private static MethodInfo FindZoneGroundHeight()
        {
            MethodInfo exact = typeof(ZoneSystem).GetMethod("GetGroundHeight", AnyMember, null, new[] { typeof(Vector3) }, null);
            if (exact != null)
                return exact;

            return typeof(ZoneSystem)
                .GetMethods(AnyMember)
                .FirstOrDefault(m =>
                {
                    if (m.Name != "GetGroundHeight")
                        return false;

                    ParameterInfo[] p = m.GetParameters();
                    return p.Length == 1 && p[0].ParameterType == typeof(Vector3) ||
                           p.Length == 2 && p[0].ParameterType == typeof(Vector3) && p[1].IsOut;
                });
        }
    }
}


