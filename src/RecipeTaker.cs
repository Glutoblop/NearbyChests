using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace NearbyChests
{
    /// <summary>
    /// Sits on each vanilla crafting requirement slot. Remembers which requirement the slot is showing and how many
    /// are needed, so clicking the slot takes the missing amount from nearby chests.
    /// </summary>
    internal sealed class RequirementSlot : MonoBehaviour, IPointerClickHandler
    {
        public Piece.Requirement Req;
        public int Need;

        public void OnPointerClick(PointerEventData eventData)
        {
            if (!Plugin.Enabled.Value || !Plugin.ClickIngredientToTake.Value || Req?.m_resItem == null) return;
            RecipeTaker.TakeMissing(Req, Need, report: true);
        }
    }

    internal static class RecipeTaker
    {
        private static int _quality = 1;
        public static int Quality => _quality;
        public static int Multiplier => _multiplier;
        private static int _multiplier = 1;

        public static void AttachSlots(InventoryGui gui)
        {
            foreach (var el in gui.m_recipeRequirementList)
            {
                if (el == null || el.GetComponent<RequirementSlot>() != null) continue;
                el.AddComponent<RequirementSlot>();
                // Make sure the whole slot catches clicks, not only the icon.
                if (el.GetComponent<Graphic>() == null)
                {
                    var img = el.AddComponent<Image>();
                    img.color = new Color(0f, 0f, 0f, 0f);
                }
            }
        }

        /// <summary>Postfix of InventoryGui.SetupRequirement.</summary>
        public static void AfterSetupRequirement(Transform elementRoot, Piece.Requirement req, Player player, int quality, int craftMultiplier, bool shown)
        {
            var slot = elementRoot.GetComponent<RequirementSlot>();
            if (slot == null) return; // build-menu requirement, not ours

            if (!shown || req?.m_resItem == null) { slot.Req = null; return; }
            slot.Req = req;
            slot.Need = req.GetAmount(quality) * craftMultiplier;
            _quality = quality;
            _multiplier = craftMultiplier;

            if (!Plugin.Enabled.Value || !Plugin.RequirementHints.Value || player == null) return;

            string name = req.m_resItem.m_itemData.m_shared.m_name;
            int have = player.GetInventory().CountItems(name);
            int nearby = ChestPool.CountForCrafting(name);
            int missing = slot.Need - have;

            var tooltip = elementRoot.GetComponent<UITooltip>();
            if (tooltip != null)
            {
                tooltip.m_text = Localization.instance.Localize(name) + $"\nNearby chests: {nearby}" +
                                 (Plugin.ClickIngredientToTake.Value && missing > 0 && nearby > 0 ? $"\nClick to take {Mathf.Min(missing, nearby)}" : "");
            }

            if (missing > 0 && nearby >= missing)
            {
                var amount = elementRoot.Find("res_amount")?.GetComponent<TMPro.TMP_Text>();
                if (amount != null) amount.color = new Color(1f, 0.85f, 0.3f);
            }
        }

        public static void ClearSlot(Transform elementRoot)
        {
            var slot = elementRoot.GetComponent<RequirementSlot>();
            if (slot != null) slot.Req = null;
        }

        public static bool HasSelectedRecipe()
        {
            var gui = InventoryGui.instance;
            return gui != null && gui.m_selectedRecipe.Recipe != null && gui.m_reqList.Count > 0;
        }

        public static void UpdateGrabButtons(InventoryGui gui, Inventory inventory, int[] craftCounts, Button[] buttons)
        {
            var stock = GetGrabStock(gui);
            for (int i = 0; i < buttons.Length; i++)
                buttons[i].interactable = PlanGrab(gui, inventory, craftCounts[i], stock)?.Count > 0;
        }

        public static void GrabSelected(InventoryGui gui, int crafts)
        {
            var player = Player.m_localPlayer;
            if (player == null || !Plugin.Enabled.Value || !NearbyTabs.Active || !InventoryGui.IsVisible() ||
                gui.IsContainerOpen() || !HasSelectedRecipe() || gui.m_dragItem != null) return;

            var inventory = player.GetInventory();
            ChestPool.Scan();
            var acquired = new HashSet<Container>();
            var blocked = new HashSet<Container>();
            while (true)
            {
                var moves = PlanGrab(gui, inventory, crafts, GetGrabStock(gui, blocked));
                if (moves == null || moves.Count == 0)
                {
                    player.Message(MessageHud.MessageType.Center, "Available ingredients or inventory space changed");
                    break;
                }

                var next = moves.FirstOrDefault(move => !acquired.Contains(move.Chest)).Chest;
                if (next == null)
                {
                    if (ChestPool.TakeGrab(moves, inventory))
                    {
                        gui.m_moveItemEffects.Create(player.transform.position, Quaternion.identity);
                        player.Message(MessageHud.MessageType.TopLeft, "Took the recipe's ingredients from nearby chests");
                    }
                    else player.Message(MessageHud.MessageType.Center, "Could not finish taking the recipe's ingredients");
                    break;
                }

                // Acquiring a chest reloads its inventory. Plan again from the actual stacks before moving anything.
                if (ChestPool.AcquireForGrab(next)) acquired.Add(next);
                else blocked.Add(next);
            }

            NearbyPanel.ForceRescan();
        }

        private static ILookup<string, ChestPool.GrabMove> GetGrabStock(InventoryGui gui, HashSet<Container> blocked = null)
        {
            var names = new HashSet<string>(gui.m_reqList.Select(req => req.m_resItem.m_itemData.m_shared.m_name));
            return ChestPool.GrabbableChests()
                .Where(chest => blocked == null || !blocked.Contains(chest))
                .SelectMany(chest => chest.GetInventory().GetAllItems()
                    .Where(item => item.m_stack > 0 && item.m_worldLevel >= Game.m_worldLevel && names.Contains(item.m_shared.m_name))
                    .Select(item => new ChestPool.GrabMove(chest, item, item.m_stack)))
                .ToLookup(move => move.Item.m_shared.m_name);
        }

        private static List<ChestPool.GrabMove> PlanGrab(InventoryGui gui, Inventory inventory, int crafts,
            ILookup<string, ChestPool.GrabMove> stock)
        {
            var recipe = gui.m_selectedRecipe.Recipe;
            int quality = (gui.m_selectedRecipe.ItemData?.m_quality ?? 0) + 1;
            int slotsLeft = Mathf.Max(0, inventory.GetEmptySlots());
            var plan = new List<ChestPool.GrabMove>();

            // Vanilla checks the amount available at a single ingredient quality.
            if (recipe.m_requireOnlyOneIngredient)
            {
                List<ChestPool.GrabMove> best = null;
                foreach (var req in gui.m_reqList)
                {
                    var ingredient = req.m_resItem.m_itemData.m_shared;
                    int need = req.GetAmount(quality) * crafts;
                    for (int q = 1; q <= ingredient.m_maxQuality; q++)
                    {
                        var moves = PlanIngredient(inventory, ingredient.m_name, q, need, stock[ingredient.m_name], out int slots);
                        if (moves == null) continue;
                        if (moves.Count == 0) return moves;
                        if (slots > slotsLeft) continue;
                        best = moves;
                        slotsLeft = slots - 1;
                    }
                }
                return best;
            }

            foreach (var reqs in gui.m_reqList.GroupBy(req => req.m_resItem.m_itemData.m_shared.m_name))
            {
                int need = reqs.Sum(req => req.GetAmount(quality)) * crafts;
                int maxQuality = reqs.First().m_resItem.m_itemData.m_shared.m_maxQuality;
                List<ChestPool.GrabMove> best = null;
                int bestSlots = slotsLeft + 1;
                for (int q = 1; q <= maxQuality; q++)
                {
                    var moves = PlanIngredient(inventory, reqs.Key, q, need, stock[reqs.Key], out int slots);
                    if (moves == null) continue;
                    if (moves.Count == 0)
                    {
                        best = moves;
                        bestSlots = 0;
                        break;
                    }
                    if (slots >= bestSlots) continue;
                    best = moves;
                    bestSlots = slots;
                }
                if (best == null) return null;
                slotsLeft -= bestSlots;
                plan.AddRange(best);
            }
            return plan;
        }

        private static List<ChestPool.GrabMove> PlanIngredient(Inventory inventory, string name, int quality, int need,
            IEnumerable<ChestPool.GrabMove> stock, out int slots)
        {
            slots = 0;
            var plan = new List<ChestPool.GrabMove>();
            int missing = need - inventory.CountItems(name, quality);
            if (missing <= 0) return plan;

            var stacks = stock.Where(move => move.Item.m_quality == quality)
                .GroupBy(move => move.Item.m_worldLevel)
                .Select(group =>
                {
                    var sample = group.First().Item;
                    int total = group.Sum(move => move.Amount);
                    int room = Mathf.Min(total, ChestPool.StackSpace(inventory, sample));
                    return (sources: group, total, room, maxStack: sample.m_shared.m_maxStackSize);
                })
                .ToList();
            if (stacks.Sum(stack => stack.total) < missing) return null;

            // Fill compatible stacks first, then use whichever stock would fill the most of the next slot.
            var amounts = new int[stacks.Count];
            for (int i = 0; i < stacks.Count && missing > 0; i++)
            {
                amounts[i] = Mathf.Min(missing, stacks[i].room);
                missing -= amounts[i];
            }

            int freeSlots = inventory.GetEmptySlots();
            while (missing > 0)
            {
                if (slots >= freeSlots) return null;
                int best = 0, capacity = 0;
                for (int i = 0; i < stacks.Count; i++)
                {
                    int available = Mathf.Min(stacks[i].maxStack, stacks[i].total - amounts[i]);
                    if (available <= capacity) continue;
                    best = i;
                    capacity = available;
                }
                if (capacity == 0) return null;
                int take = Mathf.Min(missing, capacity);
                amounts[best] += take;
                missing -= take;
                slots++;
            }

            for (int i = 0; i < stacks.Count; i++)
            {
                int take = amounts[i];
                foreach (var source in stacks[i].sources)
                {
                    if (take == 0) break;
                    int amount = Mathf.Min(take, source.Amount);
                    plan.Add(new ChestPool.GrabMove(source.Chest, source.Item, amount));
                    take -= amount;
                }
            }
            return plan;
        }

        public static int TakeMissing(Piece.Requirement req, int need, bool report) => TakeMissing(req, need, report, out _);

        public static int TakeMissing(Piece.Requirement req, int need, bool report, out bool full)
        {
            full = false;
            var player = Player.m_localPlayer;
            if (player == null) return 0;
            if (ChestPool.Chests.Count == 0) ChestPool.Scan();

            string name = req.m_resItem.m_itemData.m_shared.m_name;
            int missing = need - player.GetInventory().CountItems(name);
            if (missing <= 0)
            {
                if (report) player.Message(MessageHud.MessageType.Center, $"You already have {need} {Localization.instance.Localize(name)}");
                return 0;
            }

            // Only items that count for crafting (world level at least the current one), same as Inventory.CountItems.
            int got = ChestPool.Take(it => it.m_shared.m_name == name && it.m_worldLevel >= Game.m_worldLevel, missing, player.GetInventory(), null, out full);
            if (report) NearbyPanel.Report(name, got, missing, full);
            return got;
        }
    }
}
