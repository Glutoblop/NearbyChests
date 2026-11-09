using HarmonyLib;
using UnityEngine;

namespace NearbyChests
{
    // All recipes tab: every unlocked recipe, not just the ones for the current station.
    [HarmonyPatch(typeof(Player), nameof(Player.GetAvailableRecipes))]
    internal static class GetAvailableRecipesPatch
    {
        private static void Postfix(Player __instance, ref System.Collections.Generic.List<Recipe> available)
            => NearbyTabs.OverrideAvailableRecipes(__instance, available);
    }

    // Description text for build-piece stand-ins in the crafting panel.
    [HarmonyPatch(typeof(ItemDrop.ItemData), nameof(ItemDrop.ItemData.GetTooltip),
        new[] { typeof(ItemDrop.ItemData), typeof(int), typeof(bool), typeof(float), typeof(int), typeof(bool) })]
    internal static class BuildPieceTooltipPatch
    {
        private static bool Prefix(ItemDrop.ItemData item, ref string __result)
        {
            var text = BuildPieces.Tooltip(item);
            if (text == null) return true;
            __result = text;
            return false;
        }
    }

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
                CraftQueue.Build(__instance);
            }
            catch (System.Exception e) { Plugin.Warn("Failed to build Nearby chests UI: " + e); }
        }

        [HarmonyPrefix, HarmonyPatch(nameof(InventoryGui.OnDestroy))]
        private static void OnDestroy(InventoryGui __instance)
        {
            CraftQueue.Destroy(__instance);
            NearbyPanel.Destroy();
        }

        [HarmonyPostfix, HarmonyPatch(nameof(InventoryGui.Update))]
        private static void Update(InventoryGui __instance)
        {
            try
            {
                NearbyPanel.Tick();
                CraftQueue.Tick(__instance);
            }
            catch (System.Exception e) { Plugin.Warn("Nearby chests update failed: " + e); }
        }

        [HarmonyPrefix, HarmonyPatch(nameof(InventoryGui.UpdateRecipe))]
        private static void UpdateRecipe(InventoryGui __instance) => CraftQueue.CheckSelection(__instance);

        [HarmonyPrefix, HarmonyPatch(nameof(InventoryGui.Hide))]
        private static void Hide(InventoryGui __instance) => CraftQueue.Clear(__instance);

        [HarmonyPrefix, HarmonyPatch(nameof(InventoryGui.OnCraftCancelPressed))]
        private static void CancelQueue(InventoryGui __instance) => CraftQueue.Clear(__instance);

        // With no chest open, vanilla cancels any drag that didn't start in the player's inventory every frame.
        // Let drags from the Nearby grid survive; otherwise behave exactly like vanilla's no-container branch.
        [HarmonyPrefix, HarmonyPatch(nameof(InventoryGui.UpdateContainer))]
        private static bool UpdateContainer(InventoryGui __instance)
        {
            if (NearbyPanel.View == null || __instance.m_dragInventory != NearbyPanel.View) return true;
            if (__instance.m_currentContainer != null && __instance.m_currentContainer.IsOwner()) return true;
            if (InventoryGui.IsVisible()) __instance.m_container.gameObject.SetActive(false);
            return false;
        }

        // ---- Chests / All recipes tabs next to Craft / Upgrade ----

        [HarmonyPrefix, HarmonyPatch(nameof(InventoryGui.OnTabCraftPressed))]
        private static void OnTabCraftPressed() => NearbyTabs.Deactivate();

        [HarmonyPrefix, HarmonyPatch(nameof(InventoryGui.OnTabUpgradePressed))]
        private static void OnTabUpgradePressed() => NearbyTabs.Deactivate();

        [HarmonyPrefix, HarmonyPatch(nameof(InventoryGui.UpdateCraftingPanel))]
        private static void UpdateCraftingPanelPrefix() => NearbyTabs.BeforeCraftingPanelUpdate();

        [HarmonyPostfix, HarmonyPatch(nameof(InventoryGui.UpdateCraftingPanel))]
        private static void UpdateCraftingPanelPostfix()
        {
            NearbyTabs.AfterCraftingPanelUpdate();
            CraftQueue.CraftingPanelUpdated();
        }

        [HarmonyPrefix, HarmonyPatch(nameof(InventoryGui.AddRecipeToList))]
        private static void AddRecipeToListPrefix(Player player, Recipe recipe, ItemDrop.ItemData item, ref bool canCraft, out bool __state)
            => __state = NearbyTabs.PromoteCanCraft(player, recipe, item, ref canCraft);

        [HarmonyPostfix, HarmonyPatch(nameof(InventoryGui.AddRecipeToList))]
        private static void AddRecipeToListPostfix(InventoryGui __instance, Recipe recipe, bool __state)
        {
            if (__state) NearbyTabs.TintPromoted(__instance);
            NearbyTabs.TagBuildPiece(__instance, recipe);
        }

        // Build pieces are listed as stand-in recipes; they can't be crafted, only placed with their tool.
        [HarmonyPrefix, HarmonyPatch(nameof(InventoryGui.OnCraftPressed))]
        private static bool OnCraftPressed(InventoryGui __instance) =>
            !BuildPieces.IsBuildPiece(__instance.m_selectedRecipe.Recipe) && CraftQueue.OnCraftPressed(__instance);

        [HarmonyPrefix, HarmonyPatch(nameof(InventoryGui.DoCrafting))]
        private static bool DoCrafting(InventoryGui __instance, Player player)
        {
            if (!BuildPieces.IsBuildPiece(__instance.m_craftRecipe)) return CraftQueue.BeforeCompletion(__instance, player);
            __instance.m_craftRecipe = null;
            return false;
        }

        [HarmonyPostfix, HarmonyPatch(nameof(InventoryGui.DoCrafting))]
        private static void AfterDoCrafting(InventoryGui __instance) => CraftQueue.AfterCompletion(__instance);

        [HarmonyPrefix, HarmonyPatch(nameof(InventoryGui.InCraftTab))]
        private static bool InCraftTab(ref bool __result)
        {
            var o = NearbyTabs.InCraftTabOverride;
            if (o == null) return true;
            __result = o.Value;
            return false;
        }

        [HarmonyPrefix, HarmonyPatch(nameof(InventoryGui.InUpradeTab))]
        private static bool InUpradeTab(ref bool __result)
        {
            var o = NearbyTabs.InUpgradeTabOverride;
            if (o == null) return true;
            __result = o.Value;
            return false;
        }

        // Ctrl-click: from the Nearby grid takes the stack into your inventory; from your inventory (while the
        // Nearby panel is showing) stores it in the nearby chests. Vanilla would drop it on the ground here.
        [HarmonyPrefix, HarmonyPatch(nameof(InventoryGui.OnSelectedItem))]
        private static bool OnSelectedItem(InventoryGui __instance, InventoryGrid grid, ItemDrop.ItemData item, InventoryGrid.Modifier mod)
        {
            if (__instance.m_dragGo != null || item == null || grid == null || NearbyPanel.View == null) return true;
            var player = Player.m_localPlayer;
            if (player == null) return true;

            if (grid == NearbyPanel.Grid)
            {
                if (mod == InventoryGrid.Modifier.Move)
                {
                    NearbyPanel.TakeShown(item, item.m_stack, player.GetInventory(), null);
                    return false;
                }
                return mod != InventoryGrid.Modifier.Drop;
            }

            if (mod == InventoryGrid.Modifier.Move && grid == __instance.m_playerGrid && NearbyPanel.IsShown &&
                !__instance.IsContainerOpen() && !item.m_shared.m_questItem)
            {
                player.RemoveEquipAction(item);
                player.UnequipItem(item);
                string name = item.m_shared.m_name;
                int stored = ChestPool.Store(player.GetInventory(), item, item.m_stack, out bool full);
                if (stored == 0 && full) player.Message(MessageHud.MessageType.Center, "Nearby chests are full");
                NearbyPanel.AfterMove(name, stored, false);
                return false;
            }
            return true;
        }

        [HarmonyPostfix, HarmonyPatch(nameof(InventoryGui.SetupRequirement))]
        private static void SetupRequirement(Transform elementRoot, Piece.Requirement req, Player player, int quality, int craftMultiplier, bool __result)
            => RecipeTaker.AfterSetupRequirement(elementRoot, req, player, quality, craftMultiplier, __result);

        [HarmonyPostfix, HarmonyPatch(nameof(InventoryGui.HideRequirement))]
        private static void HideRequirement(Transform elementRoot) => RecipeTaker.ClearSlot(elementRoot);
    }

    // Dropping a dragged stack onto a grid. The Nearby grid only shows copies, so redirect:
    //   Nearby -> any other grid : take that many from the real chests into the slot it was dropped on
    //   other grid -> Nearby     : store into the nearby chests
    //   Nearby -> Nearby         : nothing (just ends the drag)
    [HarmonyPatch(typeof(InventoryGrid), nameof(InventoryGrid.DropItem))]
    internal static class InventoryGridDropPatch
    {
        private static bool Prefix(InventoryGrid __instance, Inventory fromInventory, ItemDrop.ItemData item, int amount, Vector2i pos, ref bool __result)
        {
            var view = NearbyPanel.View;
            if (view == null) return true;

            if (__instance == NearbyPanel.Grid)
            {
                __result = true;
                if (fromInventory != view && !item.m_shared.m_questItem)
                {
                    string name = item.m_shared.m_name;
                    int stored = ChestPool.Store(fromInventory, item, amount, out bool full);
                    if (stored == 0 && full) Player.m_localPlayer?.Message(MessageHud.MessageType.Center, "Nearby chests are full");
                    NearbyPanel.AfterMove(name, stored, false);
                }
                return false;
            }

            if (fromInventory == view)
            {
                NearbyPanel.TakeShown(item, amount, __instance.GetInventory(), pos);
                __result = true;
                return false;
            }
            return true;
        }
    }

    // Never drop or use the Nearby grid's copies directly (that would duplicate items).
    [HarmonyPatch(typeof(Humanoid))]
    internal static class HumanoidPatches
    {
        [HarmonyPrefix, HarmonyPatch(nameof(Humanoid.DropItem))]
        private static bool DropItem(Inventory inventory, ref bool __result)
        {
            if (inventory == null || inventory != NearbyPanel.View) return true;
            __result = false;
            return false;
        }

        [HarmonyPrefix, HarmonyPatch(nameof(Humanoid.UseItem))]
        private static bool UseItem(Inventory inventory) => inventory == null || inventory != NearbyPanel.View;
    }

    // While typing in the search box, swallow game hotkeys (Tab closes inventory, number keys use hotbar, etc).
    [HarmonyPatch(typeof(ZInput))]
    internal static class ZInputPatches
    {
        [HarmonyPrefix, HarmonyPatch(nameof(ZInput.GetButtonDown), typeof(string))]
        private static bool GetButtonDown(ref bool __result) => Pass(ref __result);

        [HarmonyPrefix, HarmonyPatch(nameof(ZInput.GetButtonUp), typeof(string))]
        private static bool GetButtonUp(ref bool __result) => Pass(ref __result);

        [HarmonyPrefix, HarmonyPatch(nameof(ZInput.GetButton), typeof(string))]
        private static bool GetButton(ref bool __result) => Pass(ref __result);

        [HarmonyPrefix, HarmonyPatch(nameof(ZInput.GetKeyDown), typeof(KeyCode), typeof(bool))]
        private static bool GetKeyDown(ref bool __result) => Pass(ref __result);

        [HarmonyPrefix, HarmonyPatch(nameof(ZInput.GetKeyUp), typeof(KeyCode), typeof(bool))]
        private static bool GetKeyUp(ref bool __result) => Pass(ref __result);

        [HarmonyPrefix, HarmonyPatch(nameof(ZInput.GetKey), typeof(KeyCode), typeof(bool))]
        private static bool GetKey(ref bool __result) => Pass(ref __result);

        private static bool Pass(ref bool result)
        {
            if (!NearbyPanel.BlockGameInput) return true;
            result = false;
            return false;
        }
    }
}
