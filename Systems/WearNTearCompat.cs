using HarmonyLib;
using System;
using System.Reflection;

namespace ValhallaPerformance
{
    internal static class WearNTearCompat
    {
        private static readonly BindingFlags AnyInstance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        private static readonly string[] PreferredUpdateMethods =
        {
            "UpdateWear",
            "UpdateSupport",
            "UpdateStructure",
            "UpdateStability"
        };

        internal static MethodInfo ResolveUpdateMethod()
        {
            foreach (string name in PreferredUpdateMethods)
            {
                MethodInfo method = AccessTools.Method(typeof(WearNTear), name);
                if (IsVoidNoArgInstance(method))
                    return method;
            }

            MethodInfo bestFallback = null;
            foreach (MethodInfo method in typeof(WearNTear).GetMethods(AnyInstance))
            {
                if (!IsVoidNoArgInstance(method))
                    continue;

                string name = method.Name;
                if (name.IndexOf("wear", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    name.IndexOf("support", StringComparison.OrdinalIgnoreCase) >= 0)
                    return method;

                if (bestFallback == null && name.StartsWith("Update", StringComparison.Ordinal))
                    bestFallback = method;
            }

            return bestFallback;
        }

        internal static MethodInfo ResolveGetSupportMethod()
        {
            MethodInfo direct = AccessTools.Method(typeof(WearNTear), "GetSupport");
            if (IsFloatNoArgInstance(direct))
                return direct;

            foreach (MethodInfo method in typeof(WearNTear).GetMethods(AnyInstance))
            {
                if (!IsFloatNoArgInstance(method))
                    continue;

                if (method.Name.IndexOf("support", StringComparison.OrdinalIgnoreCase) >= 0)
                    return method;
            }

            return null;
        }

        private static bool IsVoidNoArgInstance(MethodInfo method)
        {
            return method != null &&
                   !method.IsStatic &&
                   method.ReturnType == typeof(void) &&
                   method.GetParameters().Length == 0;
        }

        private static bool IsFloatNoArgInstance(MethodInfo method)
        {
            return method != null &&
                   !method.IsStatic &&
                   method.ReturnType == typeof(float) &&
                   method.GetParameters().Length == 0;
        }
    }
}
