using System;
using HarmonyLib;
using UnityEngine;

namespace PerspexModpackPatcher;

// MyriadJewels 0.1.4 retries its sage/mint cultivator lookup for 180 frames.
// Missing prefabs otherwise trigger two global object scans on every frame.
internal static class MyriadJewelsPerformancePatch
{
    private static ZNetScene currentScene;
    private static GameObject sage;
    private static GameObject mint;
    private static int nextScanFrame = -1;

    internal static void Install(Harmony harmony)
    {
        var type = AccessTools.TypeByName("MyriadJewels.HerbPlantableFix");
        var target = type == null ? null : AccessTools.Method(type, "FindPickablePrefab",
            new[] { typeof(ZNetScene), typeof(string), typeof(GameObject) });
        if (target == null) throw new MissingMethodException("MyriadJewels.HerbPlantableFix.FindPickablePrefab");
        harmony.Patch(target, prefix: new HarmonyMethod(typeof(MyriadJewelsPerformancePatch), nameof(FindPickablePrefab)));
    }

    private static bool FindPickablePrefab(ZNetScene zns, string pickableName, GameObject item, ref GameObject __result)
    {
        if (pickableName != "Pickable_Sage_bal" && pickableName != "Pickable_Mint_bal") return true;
        if (!ReferenceEquals(currentScene, zns))
        {
            currentScene = zns;
            sage = mint = null;
            nextScanFrame = -1;
        }

        var registered = zns.GetPrefab(pickableName);
        if (IsWorldPickable(registered, item))
        {
            __result = registered;
            return false;
        }
        if (!item)
        {
            __result = null;
            return false;
        }

        if (Time.frameCount >= nextScanFrame)
        {
            ScanUnregisteredPrefabs();
            nextScanFrame = Time.frameCount + 90;
        }
        var candidate = pickableName == "Pickable_Sage_bal" ? sage : mint;
        __result = IsWorldPickable(candidate, item) ? candidate : null;
        return false;
    }

    private static bool IsWorldPickable(GameObject go, GameObject item) =>
        go && go != item && go.GetComponent<Pickable>() && !go.GetComponent<ItemDrop>();

    private static void ScanUnregisteredPrefabs()
    {
        foreach (var go in Resources.FindObjectsOfTypeAll<GameObject>())
        {
            if (!go) continue;
            var name = go.name;
            if (name != "Pickable_Sage_bal" && name != "Pickable_Mint_bal") continue;
            var scene = go.scene;
            if (scene.IsValid() && !string.IsNullOrEmpty(scene.name)) continue;
            if (!IsWorldPickable(go, null)) continue;
            if (name == "Pickable_Sage_bal") sage = go;
            else mint = go;
        }
    }
}
