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
            __result = new MemoryStream(Plugin.MapBytes, false);
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
            __result = Plugin.MapBytes;
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
            __instance.SetLoadedObject(prefab);
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
            if (!Plugin.Ready || NetManager.isServer || !Plugin.Assets.IsTemplate(prefab)) return true;
            __instance.isLoaded = true;
            __instance.obj = __instance.copyFrom = prefab;
            __instance.decalData = null;
            __instance.isDecal = false;
            __instance.isMovable = prefab.GetComponentInChildren<Rigidbody>(true);
            __instance.quality = null;
            return false;
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
            if (!Plugin.Ready || !Plugin.Assets.IsGround(__instance.x, __instance.z)) return true;
            if (__instance.Instanciated) Object.Destroy(__instance.Instanciated);
            __instance.Instanciated = null;
            __instance.isLoaded = false;
            return false;
        }
    }
}
