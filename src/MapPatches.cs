using System.IO;
using HarmonyLib;
using UnityEngine;

namespace LegacyMapExpansion
{
    [HarmonyPatch(typeof(BetterStreamingAssets), nameof(BetterStreamingAssets.OpenRead), typeof(string))]
    internal static class MapStreamPatch
    {
        private static bool Prefix(string __0, ref Stream __result)
        {
            if (!Plugin.Ready || !IsMap(__0)) return true;
            Plugin.ReportMapOverride();
            __result = Plugin.OpenResource("map");
            return false;
        }

        internal static bool IsMap(string path) => path != null && path.Replace('\\', '/').TrimStart('/') == "Map.bytes";
    }

    [HarmonyPatch(typeof(BetterStreamingAssets), nameof(BetterStreamingAssets.ReadAllBytes), typeof(string))]
    internal static class MapBytesPatch
    {
        private static bool Prefix(string __0, ref byte[] __result)
        {
            if (!Plugin.Ready || !MapStreamPatch.IsMap(__0)) return true;
            Plugin.ReportMapOverride();
            __result = Plugin.ReadResource("map");
            return false;
        }
    }

    [HarmonyPatch(typeof(MapProObjInfo), "isReady")]
    internal static class MapPrefabPatch
    {
        private static bool Prefix(MapProObjInfo __instance, ref bool __result)
        {
            if (!Plugin.Ready || !MapProObjInfo.canCreate || __instance.isLoaded) return true;
            GameObject prefab;
            if (!Plugin.Assets.TryPrefab(__instance, out prefab)) return true;
            // The headless bundle already has no visuals. Let MapPro use this
            // prepared template directly instead of creating another BCL clone.
            if (Plugin.Assets.Headless) MapTemplatePatch.SetPrepared(__instance, prefab, true);
            else __instance.SetLoadedObject(prefab);
            __result = true;
            return false;
        }
    }

    [HarmonyPatch(typeof(MapProObjInfo), "SetLoadedObject")]
    internal static class MapTemplatePatch
    {
        private static bool Prefix(MapProObjInfo __instance, Object __0)
        {
            var prefab = __0 as GameObject;
            if (!Plugin.Ready || NetManager.isServer || !Plugin.Assets.IsTemplate(__instance, prefab)) return true;
            SetPrepared(__instance, prefab, false);
            return false;
        }

        internal static void SetPrepared(MapProObjInfo info, GameObject prefab, bool headless)
        {
            info.isLoaded = true;
            info.obj = info.copyFrom = prefab;
            info.decalData = null;
            info.isDecal = false;
            info.isMovable = prefab.GetComponentInChildren<Rigidbody>(!headless);
            info.quality = null;
        }
    }

    [HarmonyPatch(typeof(GroundItem), "SetObject")]
    internal static class GroundPrefabPatch
    {
        private static void Prefix(GroundItem __instance, ref Object __0)
        {
            if (!Plugin.Ready) return;
            GameObject prefab;
            if (Plugin.Assets.TryGround(__instance.x, __instance.z, __0 as GameObject, out prefab)) __0 = prefab;
        }
    }

    [HarmonyPatch(typeof(GroundItem), "Clear")]
    internal static class GroundReleasePatch
    {
        private static bool Prefix(GroundItem __instance)
        {
            if (!Plugin.Ready || !Plugin.Assets.OwnsGround(__instance)) return true;
            if (__instance.Instanciated) Object.Destroy(__instance.Instanciated);
            __instance.Instanciated = null;
            __instance.isLoaded = false;
            return false;
        }
    }
}
