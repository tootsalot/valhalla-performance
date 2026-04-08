using HarmonyLib;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace ValhallaPerformance
{
    public class BootSystem : ISystem
    {
        private bool _warmupDone;
        private bool _warmupPrepared;
        private float _warmupDelay = 5f;
        private int _warmupCursor;
        private int _warmupWarmed;
        private float _warmupStartedAt;
        private readonly List<RuntimeMethodHandle> _warmupHandles = new List<RuntimeMethodHandle>(2048);

        public void Init(Harmony harmony)
        {
            if (Cfg.SkipIntro.Value)
            {
                harmony.PatchAll(typeof(GameIntroPatches));
                harmony.PatchAll(typeof(SceneLoaderIntroPatches));
            }

            Plugin.Log.LogInfo("[Boot] Active");
        }

        public void Tick()
        {
            if (_warmupDone || !Cfg.JITWarmup.Value)
                return;
            if (Player.m_localPlayer == null)
                return;

            _warmupDelay -= Time.unscaledDeltaTime;
            if (_warmupDelay > 0f)
                return;

            if (!_warmupPrepared)
                PrepareJitWarmupQueue();

            RunWarmupSlice();
        }

        public void Cleanup() { }

        private void PrepareJitWarmupQueue()
        {
            _warmupPrepared = true;
            _warmupCursor = 0;
            _warmupWarmed = 0;
            _warmupStartedAt = Time.realtimeSinceStartup;
            _warmupHandles.Clear();

            Type[] types =
            {
                typeof(Player), typeof(Character), typeof(Humanoid), typeof(ItemDrop), typeof(Inventory),
                typeof(ZDOMan), typeof(WearNTear), typeof(Piece), typeof(Minimap), typeof(MonsterAI), typeof(BaseAI)
            };

            foreach (Type t in types)
            {
                try
                {
                    foreach (MethodInfo m in t.GetMethods(BindingFlags.Instance | BindingFlags.Static |
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                    {
                        if (m.IsAbstract || m.IsGenericMethod || m.GetMethodBody() == null)
                            continue;

                        try
                        {
                            RuntimeMethodHandle handle = m.MethodHandle;
                            if (handle.Value != IntPtr.Zero)
                                _warmupHandles.Add(handle);
                        }
                        catch { }
                    }
                }
                catch { }
            }
        }

        private void RunWarmupSlice()
        {
            if (_warmupCursor >= _warmupHandles.Count)
            {
                FinishWarmup();
                return;
            }

            float budgetMs = GetWarmupBudgetMs();
            float started = Time.realtimeSinceStartup;

            while (_warmupCursor < _warmupHandles.Count)
            {
                try
                {
                    RuntimeHelpers.PrepareMethod(_warmupHandles[_warmupCursor]);
                    _warmupWarmed++;
                }
                catch { }

                _warmupCursor++;

                float elapsedMs = (Time.realtimeSinceStartup - started) * 1000f;
                if (elapsedMs >= budgetMs)
                    break;
            }

            if (_warmupCursor >= _warmupHandles.Count)
                FinishWarmup();
        }

        private float GetWarmupBudgetMs()
        {
            return Cfg.Profile.Value switch
            {
                PerformanceProfile.Potato => 0.60f,
                PerformanceProfile.Low => 0.85f,
                PerformanceProfile.Medium => 1.20f,
                PerformanceProfile.High => 1.70f,
                _ => 2.20f
            };
        }

        private void FinishWarmup()
        {
            _warmupDone = true;
            float durationMs = Mathf.Max(0f, (Time.realtimeSinceStartup - _warmupStartedAt) * 1000f);
            Plugin.Log.LogInfo($"[Boot] JIT warmup: pre-compiled {_warmupWarmed} methods over {durationMs:F0}ms");
            _warmupHandles.Clear();
        }

        [HarmonyPatch]
        private static class GameIntroPatches
        {
            private static bool _logged;
            private static readonly FieldInfo InIntroField = AccessTools.Field(typeof(Game), "m_inIntro");
            private static readonly FieldInfo QueuedIntroField = AccessTools.Field(typeof(Game), "m_queuedIntro");

            [HarmonyPostfix]
            [HarmonyPatch(typeof(Game), "Start")]
            private static void Postfix_Start(Game __instance)
            {
                TrySkip(__instance);
            }

            [HarmonyPrefix]
            [HarmonyPatch(typeof(Game), "ShowIntro")]
            private static bool Prefix_ShowIntro(Game __instance)
            {
                TrySkip(__instance);
                return false;
            }

            [HarmonyPrefix]
            [HarmonyPatch(typeof(Game), "InIntro")]
            private static bool Prefix_InIntro(ref bool __result)
            {
                __result = false;
                return false;
            }

            private static void TrySkip(Game game)
            {
                if (game == null)
                    return;

                try
                {
                    InIntroField?.SetValue(game, false);
                    QueuedIntroField?.SetValue(game, false);
                    game.SkipIntro();
                }
                catch { }

                if (_logged)
                    return;

                _logged = true;
                Plugin.Log.LogInfo("[Boot] Intro skip patch engaged (Game path)");
            }
        }

        [HarmonyPatch]
        private static class SceneLoaderIntroPatches
        {
            private static readonly HashSet<int> Processed = new HashSet<int>();
            private static bool _logged;

            private static readonly Type T = typeof(SceneLoader);
            private static readonly FieldInfo ShowLogosField = AccessTools.Field(T, "_showLogos");
            private static readonly FieldInfo LogosSkippableField = AccessTools.Field(T, "_logosSkippable");
            private static readonly FieldInfo SkipEnabledField = AccessTools.Field(T, "_skipEnabled");
            private static readonly FieldInfo SkipAllAtOnceField = AccessTools.Field(T, "_skipAllAtOnce");
            private static readonly FieldInfo SkippedField = AccessTools.Field(T, "_skipped");

            private static readonly FieldInfo CoffeeLogoField = AccessTools.Field(T, "coffeeStainLogo");
            private static readonly FieldInfo IronGateLogoField = AccessTools.Field(T, "ironGateLogo");
            private static readonly FieldInfo GameLogoField = AccessTools.Field(T, "gameLogo");

            [HarmonyPostfix]
            [HarmonyPatch(typeof(SceneLoader), "Awake")]
            private static void Postfix_Awake(SceneLoader __instance)
            {
                ApplyNoLogos(__instance);
            }

            [HarmonyPostfix]
            [HarmonyPatch(typeof(SceneLoader), "Start")]
            private static void Postfix_Start(SceneLoader __instance)
            {
                ApplyNoLogos(__instance);
            }

            [HarmonyPrefix]
            [HarmonyPatch(typeof(SceneLoader), "FadeLogo")]
            private static bool Prefix_FadeLogo(SceneLoader __instance, ref IEnumerator __result)
            {
                ApplyNoLogos(__instance);
                __result = EmptyCoroutine();
                return false;
            }

            private static void ApplyNoLogos(SceneLoader loader)
            {
                if (loader == null)
                    return;

                int id = loader.GetInstanceID();
                if (Processed.Contains(id))
                    return;

                Processed.Add(id);

                try
                {
                    ShowLogosField?.SetValue(loader, false);
                    LogosSkippableField?.SetValue(loader, true);
                    SkipEnabledField?.SetValue(loader, true);
                    SkipAllAtOnceField?.SetValue(loader, true);
                    SkippedField?.SetValue(loader, true);

                    DisableLogo(loader, CoffeeLogoField);
                    DisableLogo(loader, IronGateLogoField);
                    DisableLogo(loader, GameLogoField);
                }
                catch { }

                if (_logged)
                    return;

                _logged = true;
                Plugin.Log.LogInfo("[Boot] Intro skip patch engaged (SceneLoader path)");
            }

            private static void DisableLogo(object loader, FieldInfo field)
            {
                try
                {
                    if (field?.GetValue(loader) is GameObject go && go != null)
                        go.SetActive(false);
                }
                catch { }
            }

            private static IEnumerator EmptyCoroutine()
            {
                yield break;
            }
        }
    }
}


