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
            if (!Plugin.Enabled.Value || Req?.m_resItem == null) return;
            RecipeTaker.TakeMissing(Req, Need, report: true);
        }
    }

    internal static class RecipeTaker
    {
        private static int _quality = 1;
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
                                 (missing > 0 && nearby > 0 ? $"\nClick to take {Mathf.Min(missing, nearby)}" : "");
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

        /// <summary>"Take recipe" button: take everything the selected recipe is short of.</summary>
        public static void TakeSelectedRecipe()
        {
            if (!Plugin.Enabled.Value || !HasSelectedRecipe()) return;
            var gui = InventoryGui.instance;
            var player = Player.m_localPlayer;
            if (player == null) return;

            var reqs = gui.m_reqList.Where(r => r?.m_resItem != null).ToList();
            int Need(Piece.Requirement r) => r.GetAmount(_quality) * _multiplier;
            int Have(Piece.Requirement r) => player.GetInventory().CountItems(r.m_resItem.m_itemData.m_shared.m_name);

            if (gui.m_selectedRecipe.Recipe.m_requireOnlyOneIngredient)
            {
                // Any single ingredient will do: stop if one is already covered, else take the first one the chests can cover.
                if (reqs.Any(r => Have(r) >= Need(r))) { player.Message(MessageHud.MessageType.Center, "You already have what this recipe needs"); return; }
                var pick = reqs.FirstOrDefault(r => Have(r) + ChestPool.CountForCrafting(r.m_resItem.m_itemData.m_shared.m_name) >= Need(r))
                           ?? reqs.FirstOrDefault();
                if (pick != null) TakeMissing(pick, Need(pick), report: true);
                return;
            }

            int took = 0, short_ = 0;
            bool full = false;
            foreach (var r in reqs)
            {
                int missing = Need(r) - Have(r);
                if (missing <= 0) continue;
                int got = TakeMissing(r, Need(r), report: false, out bool f);
                took += got;
                if (got < missing) short_++;
                full |= f;
            }

            if (took == 0 && short_ == 0)
                player.Message(MessageHud.MessageType.Center, "You already have what this recipe needs");
            else if (full)
                player.Message(MessageHud.MessageType.Center, "$inventory_full");
            else if (short_ > 0)
                player.Message(MessageHud.MessageType.Center, took > 0 ? "Took what was available; nearby chests are still short" : "Nearby chests don't have the missing ingredients");
            else
                player.Message(MessageHud.MessageType.TopLeft, "Took the recipe's ingredients from nearby chests");

            if (took > 0) gui.m_moveItemEffects.Create(player.transform.position, Quaternion.identity);
            NearbyPanel.Rescan();
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
            int got = ChestPool.Take(it => it.m_shared.m_name == name && it.m_worldLevel >= Game.m_worldLevel, missing, out full);
            if (report) NearbyPanel.Report(name, got, missing, full);
            return got;
        }
    }
}
