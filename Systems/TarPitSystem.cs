using HarmonyLib;
using System;
using System.Reflection;
using System.Threading;
using UnityEngine;

namespace ValhallaPerformance
{
    public class TarPitSystem : ISystem
    {
        private static readonly string[] TarVfxNames = { "vfx_tar", "fx_tar", "TarBubble", "TarSplash" };
        private static readonly FieldInfo LiquidTypeField = AccessTools.Field(typeof(LiquidVolume), "m_liquidType");
        private static readonly FieldInfo BuilderField = AccessTools.Field(typeof(LiquidVolume), "m_builder");
        private static readonly FieldInfo StopThreadField = AccessTools.Field(typeof(LiquidVolume), "m_stopThread");
        private static readonly FieldInfo TimerLockField = AccessTools.Field(typeof(LiquidVolume), "m_timerLock");
        private static readonly FieldInfo MeshDataLockField = AccessTools.Field(typeof(LiquidVolume), "m_meshDataLock");
        private static readonly FieldInfo MeshField = AccessTools.Field(typeof(LiquidVolume), "m_mesh");
        private static readonly FieldInfo RaycastResultsField = AccessTools.Field(typeof(LiquidVolume), "m_raycastResults");
        private static readonly FieldInfo RaycastCommandsField = AccessTools.Field(typeof(LiquidVolume), "m_raycastCommands");

        public void Init(Harmony harmony)
        {
            harmony.PatchAll(typeof(Patches));
            Plugin.Log.LogInfo("[TarPit] VFX leak fix active");
        }

        public void Tick()
        {
            if (!StaggerScheduler.ShouldRun("tarpit.scan", Cfg.TarCleanupInterval.Value, RuntimeTuning.CleanupIntervalMultiplier))
                return;

            if (Player.m_localPlayer == null)
                return;

            ParticleSystem[] all = UnityEngine.Object.FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None);
            int cleaned = 0;

            foreach (ParticleSystem ps in all)
            {
                if (ps == null)
                    continue;

                GameObject go = ps.gameObject;
                if (go == null)
                    continue;

                string name = go.name;
                bool isTar = false;
                for (int i = 0; i < TarVfxNames.Length; i++)
                {
                    if (name.IndexOf(TarVfxNames[i], StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        isTar = true;
                        break;
                    }
                }

                if (!isTar)
                    continue;

                bool orphan = (go.transform.parent == null && !ps.isPlaying && ps.particleCount == 0) ||
                              (!go.activeInHierarchy && !ps.isPlaying) ||
                              (ps.isStopped && ps.particleCount == 0 && ps.time > 5f);

                if (!orphan)
                    continue;

                UnityEngine.Object.Destroy(go);
                cleaned++;
            }

            if (cleaned > 0)
                Plugin.Log.LogInfo($"[TarPit] Cleaned {cleaned} orphaned VFX");
        }

        public void Cleanup() { }

        private static bool IsTarVolume(LiquidVolume liquid)
        {
            if (liquid == null)
                return false;

            try
            {
                if (LiquidTypeField != null)
                {
                    object value = LiquidTypeField.GetValue(liquid);
                    if (value != null && value.ToString().IndexOf("tar", StringComparison.OrdinalIgnoreCase) >= 0)
                        return true;
                }
            }
            catch { }

            string name = liquid.gameObject != null ? liquid.gameObject.name : string.Empty;
            return name.IndexOf("tar", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static void HardenTarVolume(LiquidVolume liquid)
        {
            Thread builder = BuilderField?.GetValue(liquid) as Thread;
            if (builder != null)
                builder.IsBackground = true;
        }

        private static void CleanupTarVolume(LiquidVolume liquid)
        {
            if (StopThreadField != null)
                StopThreadField.SetValue(liquid, true);

            JoinBuilderThread(liquid);
            CloseHandle(TimerLockField?.GetValue(liquid));
            CloseHandle(MeshDataLockField?.GetValue(liquid));

            Mesh mesh = MeshField?.GetValue(liquid) as Mesh;
            if (mesh != null)
                UnityEngine.Object.Destroy(mesh);

            if (MeshField != null)
                MeshField.SetValue(liquid, null);

            DisposeNativeArrayField(liquid, RaycastResultsField);
            DisposeNativeArrayField(liquid, RaycastCommandsField);
        }

        private static void JoinBuilderThread(LiquidVolume liquid)
        {
            Thread builder = BuilderField?.GetValue(liquid) as Thread;
            if (builder == null)
                return;

            try
            {
                if (builder.IsAlive)
                    builder.Join(1500);
            }
            catch { }

            try
            {
                if (BuilderField != null)
                    BuilderField.SetValue(liquid, null);
            }
            catch { }
        }

        private static void CloseHandle(object handle)
        {
            if (handle == null)
                return;

            try
            {
                if (handle is IDisposable disposable)
                    disposable.Dispose();
            }
            catch
            {
                try
                {
                    MethodInfo close = AccessTools.Method(handle.GetType(), "Close");
                    close?.Invoke(handle, null);
                }
                catch { }
            }
        }

        private static void DisposeNativeArrayField(object owner, FieldInfo field)
        {
            if (owner == null || field == null)
                return;

            object value = field.GetValue(owner);
            if (value == null)
                return;

            Type arrayType = value.GetType();
            try
            {
                PropertyInfo isCreatedProp = arrayType.GetProperty("IsCreated", BindingFlags.Public | BindingFlags.Instance);
                if (isCreatedProp != null)
                {
                    bool isCreated = Convert.ToBoolean(isCreatedProp.GetValue(value, null));
                    if (!isCreated)
                        return;
                }

                MethodInfo dispose = arrayType.GetMethod("Dispose", BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);
                dispose?.Invoke(value, null);

                object empty = Activator.CreateInstance(arrayType);
                field.SetValue(owner, empty);
            }
            catch { }
        }

        [HarmonyPatch]
        private static class Patches
        {
            [HarmonyPostfix]
            [HarmonyPatch(typeof(LiquidVolume), "Awake")]
            private static void Postfix_LiquidVolumeAwake(LiquidVolume __instance)
            {
                if (!Cfg.TarLiquidLeakFix.Value)
                    return;

                if (!IsTarVolume(__instance))
                    return;

                try
                {
                    HardenTarVolume(__instance);
                }
                catch (Exception ex)
                {
                    Plugin.Log?.LogWarning($"[TarPit] LiquidVolume Awake harden failed: {ex.Message}");
                }
            }

            [HarmonyPrefix]
            [HarmonyPatch(typeof(LiquidVolume), "OnDestroy")]
            private static bool Prefix_LiquidVolumeOnDestroy(LiquidVolume __instance)
            {
                if (!Cfg.TarLiquidLeakFix.Value)
                    return true;

                if (!IsTarVolume(__instance))
                    return true;

                try
                {
                    CleanupTarVolume(__instance);
                    return false;
                }
                catch (Exception ex)
                {
                    Plugin.Log?.LogWarning($"[TarPit] LiquidVolume cleanup fallback to vanilla: {ex.Message}");
                    return true;
                }
            }
        }
    }
}
