using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace NearbyChests
{
    /// <summary>
    /// A "Nearby" tab next to Craft / Upgrade. While it's selected:
    ///   - the normal crafting list shows every recipe the character has unlocked, whatever station you're at
    ///     (or none), with its ingredients; Recipe Search works on it as usual;
    ///   - the Nearby chests panel is shown beside the crafting panel;
    ///   - grab buttons above "Filter chests" take the ingredients the selected recipe is missing.
    /// </summary>
    internal static class NearbyTabs
    {
        private static InventoryGui _gui;
        private static Button _tab, _filterButton;
        private static readonly int[] GrabQuantities = { 1, 5, 10 };
        private static readonly Button[] GrabButtons = new Button[GrabQuantities.Length];
        private static Recipe _grabRecipe;
        private static int _grabState;
        private static float _tabSpacing;
        private static bool _savedCraftInteractable, _savedUpgradeInteractable;

        public static bool Active { get; private set; }

        public static void Build(InventoryGui gui)
        {
            _gui = gui;
            _tab = CloneTab(gui, "NearbyChests_Tab", "Nearby", Activate);

            // "Filter" button that takes the Craft button's place in the Nearby tab.
            var craft = gui.m_craftButton.transform;
            var go = Object.Instantiate(gui.m_craftButton.gameObject, craft.parent, false);
            go.name = "NearbyChests_Filter";
            go.transform.SetSiblingIndex(craft.GetSiblingIndex() + 1);
            foreach (var mb in go.GetComponents<MonoBehaviour>())
                if (mb is UITooltip) Object.Destroy(mb);
            _filterButton = go.GetComponent<Button>();
            _filterButton.onClick = new Button.ButtonClickedEvent(); // drop the cloned craft listener
            _filterButton.onClick.AddListener(NearbyPanel.ToggleFilterForSelected);
            _filterButton.interactable = true;
            _filterLabel = NearbyPanel.SetLabel(_filterButton, "Filter chests");

            for (int i = 0; i < GrabButtons.Length; i++)
            {
                int quantity = GrabQuantities[i];
                var button = Object.Instantiate(gui.m_craftButton, go.transform, false);
                GrabButtons[i] = button;
                button.name = $"NearbyChests_Grab{quantity}";
                var tooltip = button.GetComponent<UITooltip>();
                if (tooltip != null) Object.Destroy(tooltip);
                button.onClick = new Button.ButtonClickedEvent();
                button.onClick.AddListener(() => Grab(quantity));
                button.interactable = false;
                button.gameObject.SetActive(true);

                var rect = (RectTransform)button.transform;
                rect.localScale = Vector3.one;
                rect.anchorMin = new Vector2((float)i / GrabButtons.Length, 1f);
                rect.anchorMax = new Vector2((float)(i + 1) / GrabButtons.Length, 1.65f);
                rect.offsetMin = new Vector2(2f, 4f);
                rect.offsetMax = new Vector2(-2f, 4f);
                var label = NearbyPanel.SetLabel(button, $"x{quantity}");
                if (label != null)
                {
                    label.enableAutoSizing = true;
                    label.fontSizeMax = label.fontSize * 0.8f;
                    label.fontSizeMin = Mathf.Min(10f, label.fontSizeMax);
                }
            }
            go.SetActive(false);
        }

        private static TMPro.TMP_Text _filterLabel;
        private static string _filterText;

        public static void Destroy()
        {
            foreach (var b in new[] { _tab, _filterButton })
                if (b != null) Object.Destroy(b.gameObject);
            _tab = _filterButton = null;
            _filterLabel = null; _filterText = null;
            System.Array.Clear(GrabButtons, 0, GrabButtons.Length);
            InvalidateGrabButtons();
            Active = false;
            _gui = null;
        }

        private static Button CloneTab(InventoryGui gui, string name, string label, UnityEngine.Events.UnityAction onClick)
        {
            var src = gui.m_tabUpgrade;
            var go = Object.Instantiate(src.gameObject, src.transform.parent, false);
            go.name = name;
            var b = go.GetComponent<Button>();
            b.onClick = new Button.ButtonClickedEvent(); // drop the cloned persistent OnTabUpgradePressed listener
            b.onClick.AddListener(onClick);
            b.interactable = true;
            var text = NearbyPanel.SetLabel(b, label);
            if (text != null)
            {
                text.enableAutoSizing = true;
                text.fontSizeMax = text.fontSize;
                text.fontSizeMin = Mathf.Min(10f, text.fontSize);
            }
            go.SetActive(false);
            return b;
        }

        public static void Activate()
        {
            if (_gui == null || Active) return;
            _savedCraftInteractable = _gui.m_tabCraft.interactable;
            _savedUpgradeInteractable = _gui.m_tabUpgrade.interactable;
            Active = true;
            ShowTabStates();
            NearbyPanel.OnTabShown();                  // scan chests first: the list's highlighting uses the counts
            _gui.UpdateCraftingPanel(focusView: true); // rebuild the list with every known recipe
        }

        /// <summary>Back to Craft / Upgrade (vanilla tab pressed, or inventory closed).</summary>
        public static void Deactivate()
        {
            if (!Active) return;
            Active = false;
            SetActive(_filterButton, false);
            InvalidateGrabButtons();
            if (_gui != null)
            {
                _gui.m_tabCraft.interactable = _savedCraftInteractable;
                _gui.m_tabUpgrade.interactable = _savedUpgradeInteractable;
            }
            ShowTabStates();
            if (_gui != null && InventoryGui.IsVisible() && Player.m_localPlayer != null) _gui.UpdateCraftingPanel();
        }

        // Vanilla marks the selected tab by making it non-interactable and asks InCraftTab/InUpradeTab.
        // While Nearby is selected the game is told it's in the Craft tab (see Patches).
        public static bool? InCraftTabOverride => Active ? true : (bool?)null;
        public static bool? InUpgradeTabOverride => Active ? false : (bool?)null;

        /// <summary>UpdateCraftingPanel prefix: give vanilla its real tab state back for the duration of the call.</summary>
        public static void BeforeCraftingPanelUpdate()
        {
            if (!Active || _gui == null) return;
            _gui.m_tabCraft.interactable = _savedCraftInteractable;
            _gui.m_tabUpgrade.interactable = _savedUpgradeInteractable;
        }

        /// <summary>UpdateCraftingPanel postfix: remember what vanilla chose, then show Nearby as the selected tab again.</summary>
        public static void AfterCraftingPanelUpdate()
        {
            if (!Active || _gui == null) return;
            _savedCraftInteractable = _gui.m_tabCraft.interactable;
            _savedUpgradeInteractable = _gui.m_tabUpgrade.interactable;
            ShowTabStates();
        }

        private static void ShowTabStates()
        {
            if (_gui == null) return;
            if (Active)
            {
                _gui.m_tabCraft.interactable = true;
                _gui.m_tabUpgrade.interactable = true;
            }
            if (_tab != null) _tab.interactable = !Active;
        }

        /// <summary>Player.GetAvailableRecipes postfix: in the Nearby tab, list every unlocked recipe.</summary>
        public static void OverrideAvailableRecipes(Player player, List<Recipe> available)
        {
            if (!Active || ObjectDB.instance == null) return;
            bool all = player.NoCostCheat() || (ZoneSystem.instance != null && ZoneSystem.instance.GetGlobalKey(GlobalKeys.AllRecipesUnlocked));
            var known = ObjectDB.instance.m_recipes.Where(r =>
                r != null && r.m_item != null && r.m_enabled &&
                r.m_resources != null && r.m_resources.Length > 0 &&
                (r.m_item.m_itemData.m_shared.m_dlc.Length == 0 || DLCMan.instance.IsDLCInstalled(r.m_item.m_itemData.m_shared.m_dlc)) &&
                (all || player.IsRecipeKnown(r.m_item.m_itemData.m_shared.m_name)));
            foreach (var r in known)
                if (!available.Contains(r)) available.Add(r);
            if (Plugin.IncludeBuildPieces.Value) BuildPieces.AddKnown(player, available);
        }

        /// <summary>AddRecipeToList postfix: label build pieces with the tool that builds them.</summary>
        public static void TagBuildPiece(InventoryGui gui, Recipe recipe)
        {
            if (!BuildPieces.TryGet(recipe, out string tool) || gui.m_availableRecipes.Count == 0) return;
            var el = gui.m_availableRecipes[gui.m_availableRecipes.Count - 1].InterfaceElement;
            var name = el != null ? el.transform.Find("name")?.GetComponent<TMPro.TMP_Text>() : null;
            if (name != null) name.text += " <size=75%><color=#9fd8ff>[" + Localization.instance.Localize(tool) + "]</color></size>";
        }

        private static readonly Color NearbyNameColor = new Color(1f, 0.85f, 0.4f, 1f);

        /// <summary>
        /// AddRecipeToList prefix: in the Nearby tab a recipe counts as makeable when your inventory plus the nearby
        /// chests hold its ingredients (station not required), so it's listed bright instead of greyed out.
        /// Returns true when we changed it, so the postfix can tint it.
        /// </summary>
        public static bool PromoteCanCraft(Player player, Recipe recipe, ItemDrop.ItemData item, ref bool canCraft)
        {
            if (!Active || canCraft || item != null || player == null || recipe == null) return false;
            if (!MaterialsAvailable(player, recipe)) return false;
            canCraft = true;
            return true;
        }

        /// <summary>AddRecipeToList postfix: tint recipes that need chest items (or a station) so they stand out from "craft now".</summary>
        public static void TintPromoted(InventoryGui gui)
        {
            if (gui.m_availableRecipes.Count == 0) return;
            var el = gui.m_availableRecipes[gui.m_availableRecipes.Count - 1].InterfaceElement;
            var name = el != null ? el.transform.Find("name")?.GetComponent<TMPro.TMP_Text>() : null;
            if (name != null) name.color = NearbyNameColor;
        }

        private static bool MaterialsAvailable(Player player, Recipe recipe)
        {
            var inv = player.GetInventory();
            bool any = false, all = true;
            foreach (var req in recipe.m_resources)
            {
                if (req?.m_resItem == null || req.m_upgraderResource) continue;
                int need = req.GetAmount(1);
                if (need <= 0) continue;
                string name = req.m_resItem.m_itemData.m_shared.m_name;
                bool ok = inv.CountItems(name) + ChestPool.CountForCrafting(name) >= need;
                any |= ok;
                all &= ok;
            }
            return recipe.m_requireOnlyOneIngredient ? any : all;
        }

        /// <summary>Every frame.</summary>
        public static void Tick(bool visible)
        {
            if (_gui == null || _tab == null) return;
            bool tabVisible = visible && !_gui.IsContainerOpen();
            SetActive(_tab, tabVisible);
            if (tabVisible) PlaceTab();

            // In the Nearby tab the Craft button becomes "Filter chests" (runs after vanilla's UpdateRecipe each frame).
            bool showFilter = visible && Active && !_gui.IsContainerOpen() && RecipeTaker.HasSelectedRecipe();
            if (showFilter && _gui.m_craftButton.gameObject.activeSelf) _gui.m_craftButton.gameObject.SetActive(false);
            SetActive(_filterButton, showFilter);
            if (showFilter)
            {
                string label = NearbyPanel.FilterRecipe != null && NearbyPanel.FilterRecipe == _gui.m_selectedRecipe.Recipe
                    ? "Clear filter" : "Filter chests";
                if (_filterLabel != null && label != _filterText) { _filterLabel.text = label; _filterText = label; }
                RefreshGrabButtons();
            }

            if (visible && Plugin.ToggleKey.Value.MainKey != KeyCode.None && !NearbyPanel.BlockGameInput && Plugin.ToggleKey.Value.IsDown())
            {
                if (Active) _gui.OnTabCraftPressed(); else Activate();
            }
        }

        public static void InvalidateGrabButtons() => _grabRecipe = null;

        private static void Grab(int quantity)
        {
            RecipeTaker.GrabSelected(_gui, quantity);
            InvalidateGrabButtons();
            if (RecipeTaker.HasSelectedRecipe() && Player.m_localPlayer != null) RefreshGrabButtons();
        }

        private static void RefreshGrabButtons()
        {
            var inventory = Player.m_localPlayer.GetInventory();
            var recipe = _gui.m_selectedRecipe.Recipe;
            var state = new System.HashCode();
            state.Add(_gui.m_dragItem != null);
            state.Add(_gui.m_selectedRecipe.ItemData?.m_quality ?? 0);
            state.Add(Game.m_worldLevel);
            state.Add(inventory.GetEmptySlots());
            foreach (var req in _gui.m_reqList) state.Add(req);
            foreach (var item in inventory.GetAllItems())
            {
                state.Add(item.m_shared.m_name);
                state.Add(item.m_stack);
                state.Add(item.m_quality);
                state.Add(item.m_worldLevel);
            }
            int signature = state.ToHashCode();
            if (recipe == _grabRecipe && signature == _grabState) return;
            _grabRecipe = recipe;
            _grabState = signature;
            if (_gui.m_dragItem == null)
                RecipeTaker.UpdateGrabButtons(_gui, inventory, GrabQuantities, GrabButtons);
            else
                foreach (var button in GrabButtons) button.interactable = false;
        }

        private static void SetActive(Button b, bool on)
        {
            if (b != null && b.gameObject.activeSelf != on) b.gameObject.SetActive(on);
        }

        /// <summary>Put the Nearby tab just right of the rightmost visible vanilla tab.</summary>
        private static void PlaceTab()
        {
            var craft = (RectTransform)_gui.m_tabCraft.transform;
            var upgrade = (RectTransform)_gui.m_tabUpgrade.transform;
            if (craft.gameObject.activeSelf && upgrade.gameObject.activeSelf)
                _tabSpacing = upgrade.anchoredPosition.x - craft.anchoredPosition.x;
            var last = upgrade.gameObject.activeSelf ? upgrade : craft;
            float step = Mathf.Abs(_tabSpacing) > 1f ? _tabSpacing : last.rect.width + 4f;
            var rt = (RectTransform)_tab.transform;
            var pos = last.anchoredPosition + new Vector2(step, 0f);
            if ((rt.anchoredPosition - pos).sqrMagnitude > 0.25f) rt.anchoredPosition = pos;
        }
    }
}
