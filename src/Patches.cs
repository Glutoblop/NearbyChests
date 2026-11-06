using HarmonyLib;
using UnityEngine;

namespace NearbyChests
{
    [HarmonyPatch(typeof(Container), nameof(Container.Awake))]
    internal static class ContainerAwakePatch
    {
        private static void Postfix(Container __instance) => ChestPool.Register(__instance);
    }

    [HarmonyPatch(typeof(InventoryGui))]
    internal static class InventoryGuiPatches
    {
        [HarmonyPostfix, HarmonyPatch(nameof(InventoryGui.Awake))]
        private static void Awake(InventoryGui __instance)
        {
            try
            {
                NearbyPanel.Build(__instance);
                RecipeTaker.AttachSlots(__instance);
            }
            catch (System.Exception e) { Plugin.Warn("Failed to build Nearby chests UI: " + e); }
        }

        [HarmonyPrefix, HarmonyPatch(nameof(InventoryGui.OnDestroy))]
        private static void OnDestroy() => NearbyPanel.Destroy();

        [HarmonyPostfix, HarmonyPatch(nameof(InventoryGui.Update))]
        private static void Update()
        {
            try { NearbyPanel.Tick(); }
            catch (System.Exception e) { Plugin.Warn("Nearby chests update failed: " + e); }
        }

        [HarmonyPostfix, HarmonyPatch(nameof(InventoryGui.SetupRequirement))]
        private static void SetupRequirement(Transform elementRoot, Piece.Requirement req, Player player, int quality, int craftMultiplier, bool __result)
            => RecipeTaker.AfterSetupRequirement(elementRoot, req, player, quality, craftMultiplier, __result);

        [HarmonyPostfix, HarmonyPatch(nameof(InventoryGui.HideRequirement))]
        private static void HideRequirement(Transform elementRoot) => RecipeTaker.ClearSlot(elementRoot);
    }

    [HarmonyPatch(typeof(InventoryGrid), nameof(InventoryGrid.UpdateGui))]
    internal static class InventoryGridUpdatePatch
    {
        private static void Postfix(InventoryGrid __instance) => NearbyPanel.AfterGridUpdate(__instance);
    }
}
