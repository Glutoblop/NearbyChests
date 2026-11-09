using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace NearbyChests
{
    internal static class CraftQueue
    {
        private static RectTransform _row;
        private static Button _less, _more;
        private static TMP_Text _counter;
        private static Recipe _recipe;
        private static int _variant, _remaining;
        private static bool _running, _starting, _finishing, _crafted;

        public static void Build(InventoryGui gui)
        {
            Destroy(gui);
            var craft = (RectTransform)gui.m_craftButton.transform;
            _row = new GameObject("NearbyChests_CraftQueue", typeof(RectTransform)).GetComponent<RectTransform>();
            _row.gameObject.SetActive(false);
            _row.SetParent(craft.parent, false);
            _row.anchorMin = craft.anchorMin;
            _row.anchorMax = craft.anchorMax;
            _row.offsetMin = craft.offsetMin;
            _row.offsetMax = craft.offsetMax;

            _less = AddButton(gui, "Less", "−", 0f, 0.18f, -1);
            _more = AddButton(gui, "More", "+", 0.82f, 1f, 1);
            var background = new GameObject("QueueCountBackground", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
            background.rectTransform.SetParent(_row, false);
            PlaceBelow(background.rectTransform, 0.18f, 0.82f);
            background.color = new Color(0f, 0f, 0f, 0.82f);
            background.raycastTarget = false;

            _counter = new GameObject("QueueCount", typeof(RectTransform), typeof(TextMeshProUGUI)).GetComponent<TMP_Text>();
            _counter.rectTransform.SetParent(background.rectTransform, false);
            _counter.rectTransform.anchorMin = Vector2.zero;
            _counter.rectTransform.anchorMax = Vector2.one;
            _counter.rectTransform.offsetMin = new Vector2(6f, 2f);
            _counter.rectTransform.offsetMax = new Vector2(-6f, -2f);
            var craftLabel = gui.m_craftButton.GetComponentInChildren<TMP_Text>(true);
            _counter.font = craftLabel.font;
            _counter.color = Color.white;
            _counter.alignment = TextAlignmentOptions.Center;
            _counter.textWrappingMode = TextWrappingModes.NoWrap;
            _counter.enableAutoSizing = true;
            _counter.fontSizeMin = 8f;
            _counter.fontSizeMax = craftLabel.fontSize;
            _counter.raycastTarget = false;
        }

        public static void Destroy(InventoryGui gui)
        {
            Clear(gui);
            if (_row != null) Object.Destroy(_row.gameObject);
            _row = null;
        }

        private static Button AddButton(InventoryGui gui, string name, string label, float left, float right, int delta)
        {
            var button = Object.Instantiate(gui.m_craftButton, _row, false);
            button.name = name;
            var tooltip = button.GetComponent<UITooltip>();
            if (tooltip != null) Object.Destroy(tooltip);
            button.onClick = new Button.ButtonClickedEvent();
            button.onClick.AddListener(() => Change(gui, delta));
            NearbyPanel.SetLabel(button, label);
            PlaceBelow((RectTransform)button.transform, left, right);
            button.gameObject.SetActive(true);
            return button;
        }

        private static void PlaceBelow(RectTransform rect, float left, float right)
        {
            rect.localScale = Vector3.one;
            rect.anchorMin = new Vector2(left, -1f);
            rect.anchorMax = new Vector2(right, 0f);
            rect.offsetMin = new Vector2(2f, -4f);
            rect.offsetMax = new Vector2(-2f, -4f);
        }

        private static bool CanShow(InventoryGui gui) => _row != null && _counter != null && Plugin.Enabled.Value &&
            InventoryGui.IsVisible() && !NearbyTabs.Active && gui.InCraftTab() &&
            gui.m_selectedRecipe.Recipe != null && gui.m_selectedRecipe.ItemData == null &&
            !BuildPieces.IsBuildPiece(gui.m_selectedRecipe.Recipe);

        private static bool MatchesSelection(InventoryGui gui) => CanShow(gui) &&
            gui.m_selectedRecipe.Recipe == _recipe && gui.m_selectedVariant == _variant;

        public static void CheckSelection(InventoryGui gui)
        {
            if (_remaining > 0 && !MatchesSelection(gui)) Clear(gui);
        }

        public static void Clear(InventoryGui gui)
        {
            if (_running && gui != null) gui.m_craftTimer = -1f;
            _recipe = null;
            _remaining = 0;
            _running = _finishing = false;
        }

        private static void Change(InventoryGui gui, int delta)
        {
            CheckSelection(gui);
            var player = Player.m_localPlayer;
            if (player == null || !CanShow(gui)) return;
            var recipe = gui.m_selectedRecipe.Recipe;
            int available = CountAvailable(recipe, player.GetInventory(), player.GetCurrentCraftingStation());
            if (delta < 0 && _remaining > InFlight(gui))
            {
                if (--_remaining == 0) Clear(gui);
            }
            else if (delta > 0 ? CanAdd(gui, available) : CanWrap(gui, available))
            {
                _recipe = recipe;
                _variant = gui.m_selectedVariant;
                _remaining = delta > 0 ? _remaining + 1 : available;
            }
            RefreshCounter(gui, available);
        }

        private static int InFlight(InventoryGui gui) => _running && gui.m_craftTimer >= 0f ? 1 : 0;

        private static bool CanAdd(InventoryGui gui, int available) =>
            _remaining < available && (_running || gui.m_craftTimer < 0f);

        private static bool CanWrap(InventoryGui gui, int available) =>
            _remaining == 0 && gui.m_craftTimer < 0f && available > 0 && available != int.MaxValue;

        private static void RefreshCounter(InventoryGui gui, int available)
        {
            _counter.SetText(available == int.MaxValue ? "{0} / ∞" : "{0} / {1}", _remaining, available);
            _less.interactable = _remaining > InFlight(gui) || CanWrap(gui, available);
            _more.interactable = CanAdd(gui, available);
        }

        // When a count is staged, Craft starts the queue. With an empty queue, the button works as usual.
        public static bool OnCraftPressed(InventoryGui gui)
        {
            if (_starting) return true;
            CheckSelection(gui);
            if (_remaining == 0) return true;
            _running = true;
            return false;
        }

        public static void Tick(InventoryGui gui)
        {
            CheckSelection(gui);
            if (_row == null) return;
            var player = Player.m_localPlayer;
            bool show = player != null && CanShow(gui);
            if (_row.gameObject.activeSelf != show) _row.gameObject.SetActive(show);
            if (!show) return;

            var station = player.GetCurrentCraftingStation();
            int available = CountAvailable(gui.m_selectedRecipe.Recipe, player.GetInventory(), station);
            if (_running && gui.m_craftTimer < 0f)
            {
                if (available == 0 || !CanCraft(player, station)) Clear(gui);
                // Wait for the vanilla bulk-craft modifier to be released so each timer crafts once.
                else if (!ZInput.GetButton("AltPlace") && !ZInput.GetButton("JoyLStick"))
                {
                    _starting = true;
                    try
                    {
                        gui.m_touchMultiCrafting = false;
                        gui.OnCraftPressed();
                    }
                    finally { _starting = false; }
                    if (gui.m_craftTimer < 0f) Clear(gui);
                }
            }
            RefreshCounter(gui, available);
        }

        private static bool CanCraft(Player player, CraftingStation station)
        {
            if (!player.HaveRequirements(_recipe, discover: false, qualityLevel: 1)) return false;
            if (_recipe.GetRequiredStation(1) != null && station != null && !station.CheckUsable(player, showMessage: false)) return false;
            // GetAmount expects a qualifying item to exist for alternative-ingredient recipes.
            return !_recipe.m_requireOnlyOneIngredient ||
                player.GetFirstRequiredItem(player.GetInventory(), _recipe, 1, out _, out _) != null;
        }

        public static bool BeforeCompletion(InventoryGui gui, Player player)
        {
            if (!_running) return true;
            var station = player.GetCurrentCraftingStation();
            if (!MatchesSelection(gui) || !CanCraft(player, station) || CountAvailable(_recipe, player.GetInventory(), station) == 0)
            {
                Clear(gui);
                return false;
            }
            _finishing = true;
            _crafted = false;
            return true;
        }

        // Vanilla rebuilds the crafting panel inside DoCrafting only after adding the crafted item.
        public static void CraftingPanelUpdated()
        {
            if (_finishing) _crafted = true;
        }

        public static void AfterCompletion(InventoryGui gui)
        {
            if (!_finishing) return;
            _finishing = false;
            if (!_crafted || --_remaining == 0) Clear(gui);
        }

        private static bool UsesRequirement(Piece.Requirement requirement, bool upgrader) =>
            requirement.m_resItem != null && requirement.m_upgraderResource == upgrader && requirement.GetAmount(1) > 0;

        internal static int CountAvailable(Recipe recipe, Inventory inventory, CraftingStation station)
        {
            bool upgrader = station != null && station.m_upgrader;
            if (recipe.m_requireOnlyOneIngredient) return CountAlternatives(recipe, inventory, upgrader);

            int available = int.MaxValue;
            foreach (var requirement in recipe.m_resources)
            {
                if (!UsesRequirement(requirement, upgrader)) continue;
                var material = requirement.m_resItem.m_itemData.m_shared;
                int need = requirement.GetAmount(1);
                int cost = 0;
                foreach (var part in recipe.m_resources)
                    if (UsesRequirement(part, upgrader) && part.m_resItem.m_itemData.m_shared.m_name == material.m_name)
                        cost += part.GetAmount(1);

                int total = inventory.CountItems(material.m_name);
                int repeats = total / cost;
                if (material.m_maxQuality > 1 || inventory.CountItems(material.m_name, 1) != total)
                {
                    // Vanilla checks a single quality but consumes stacks in inventory order across qualities.
                    // Find the latest position where one quality still has enough left for another craft.
                    int lastStart = 0;
                    var items = inventory.GetAllItems();
                    for (int quality = 1; quality <= material.m_maxQuality; quality++)
                    {
                        int position = total;
                        int needed = need;
                        for (int i = items.Count - 1; i >= 0; i--)
                        {
                            var item = items[i];
                            if (item.m_shared.m_name != material.m_name || item.m_worldLevel < Game.m_worldLevel) continue;
                            position -= item.m_stack;
                            if (item.m_quality != quality) continue;
                            if (needed <= item.m_stack)
                            {
                                lastStart = Mathf.Max(lastStart, position + item.m_stack - needed + 1);
                                break;
                            }
                            needed -= item.m_stack;
                        }
                    }
                    repeats = Mathf.Min(repeats, lastStart == 0 ? 0 : 1 + (lastStart - 1) / cost);
                }
                available = Mathf.Min(available, repeats);
            }
            return available;
        }

        private static readonly Dictionary<(string, int), int> AlternativeRemainders = new Dictionary<(string, int), int>();

        private static int CountAlternatives(Recipe recipe, Inventory inventory, bool upgrader)
        {
            AlternativeRemainders.Clear();
            int available = 0;
            foreach (var requirement in recipe.m_resources)
            {
                if (!UsesRequirement(requirement, upgrader)) continue;
                var material = requirement.m_resItem.m_itemData.m_shared;
                int need = requirement.GetAmount(1);
                for (int quality = 1; quality <= material.m_maxQuality; quality++)
                {
                    var key = (material.m_name, quality);
                    if (!AlternativeRemainders.TryGetValue(key, out int count)) count = inventory.CountItems(material.m_name, quality);
                    available += count / need;
                    AlternativeRemainders[key] = count % need;
                }
            }
            return available;
        }
    }
}
