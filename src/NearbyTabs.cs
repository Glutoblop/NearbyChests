using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace NearbyChests
{
    /// <summary>
    /// Two extra tabs next to Craft / Upgrade:
    ///   Chests      - the Nearby chests panel (items in chests within range).
    ///   All recipes - the normal crafting list, but listing every recipe the character has unlocked, whatever
    ///                 station you're at (or none). Recipe Search and click-to-take ingredients work as usual.
    /// Plus a "Take missing" button above the Craft button for the selected recipe.
    /// </summary>
    internal static class NearbyTabs
    {
        public enum Mode { None, Chests, AllRecipes }

        private static InventoryGui _gui;
        private static Button _chestsTab, _recipesTab, _takeButton;
        private static float _tabSpacing;
        private static bool _savedCraftInteractable, _savedUpgradeInteractable;

        public static Mode Current { get; private set; } = Mode.None;
        public static IEnumerable<Button> Buttons => new[] { _gui?.m_tabCraft, _gui?.m_tabUpgrade, _chestsTab, _recipesTab };

        public static void Build(InventoryGui gui)
        {
            _gui = gui;
            _chestsTab = CloneTab(gui, "NearbyChests_ChestsTab", "Chests", () => Activate(Mode.Chests));
            _recipesTab = CloneTab(gui, "NearbyChests_RecipesTab", "All recipes", () => Activate(Mode.AllRecipes));

            // "Take missing" button sitting above the Craft button.
            var craft = (RectTransform)gui.m_craftButton.transform;
            var go = Object.Instantiate(gui.m_takeAllButton.gameObject, craft.parent, false);
            go.name = "NearbyChests_TakeMissing";
            var rt = (RectTransform)go.transform;
            rt.anchorMin = craft.anchorMin;
            rt.anchorMax = craft.anchorMax;
            rt.pivot = craft.pivot;
            rt.sizeDelta = new Vector2(craft.sizeDelta.x, Mathf.Max(28f, craft.rect.height * 0.6f));
            rt.anchoredPosition = craft.anchoredPosition + new Vector2(0f, craft.rect.height * (1f - craft.pivot.y) + rt.sizeDelta.y * rt.pivot.y + 6f);
            _takeButton = go.GetComponent<Button>();
            _takeButton.onClick = new Button.ButtonClickedEvent();
            _takeButton.onClick.AddListener(RecipeTaker.TakeSelectedRecipe);
            NearbyPanel.SetLabel(_takeButton, "Take missing from chests");
            go.SetActive(false);
        }

        public static void Destroy()
        {
            foreach (var b in new[] { _chestsTab, _recipesTab, _takeButton })
                if (b != null) Object.Destroy(b.gameObject);
            _chestsTab = _recipesTab = _takeButton = null;
            Current = Mode.None;
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

        public static void Activate(Mode mode)
        {
            if (_gui == null || mode == Mode.None || mode == Current) return;
            if (Current == Mode.None)
            {
                _savedCraftInteractable = _gui.m_tabCraft.interactable;
                _savedUpgradeInteractable = _gui.m_tabUpgrade.interactable;
            }
            Current = mode;
            ShowTabStates();
            if (mode == Mode.AllRecipes) _gui.UpdateCraftingPanel(focusView: true); // rebuild the list with every known recipe
            else NearbyPanel.OnChestsTabShown();
        }

        /// <summary>Back to Craft / Upgrade (vanilla tab pressed, or inventory closed).</summary>
        public static void Deactivate()
        {
            if (Current == Mode.None) return;
            bool wasAll = Current == Mode.AllRecipes;
            Current = Mode.None;
            if (_gui != null)
            {
                _gui.m_tabCraft.interactable = _savedCraftInteractable;
                _gui.m_tabUpgrade.interactable = _savedUpgradeInteractable;
            }
            ShowTabStates();
            if (wasAll && _gui != null && InventoryGui.IsVisible() && Player.m_localPlayer != null) _gui.UpdateCraftingPanel();
        }

        // Vanilla marks the selected tab by making it non-interactable and asks InCraftTab/InUpradeTab.
        // While one of ours is selected those are answered here instead (see Patches).
        public static bool? InCraftTabOverride =>
            Current == Mode.AllRecipes ? true
            : Current == Mode.Chests ? !_savedCraftInteractable && _gui.m_tabCraft.gameObject.activeSelf
            : (bool?)null;

        public static bool? InUpgradeTabOverride =>
            Current == Mode.AllRecipes ? false
            : Current == Mode.Chests ? !_savedUpgradeInteractable && _gui.m_tabUpgrade.gameObject.activeSelf
            : (bool?)null;

        /// <summary>UpdateCraftingPanel prefix: give vanilla its real tab state back for the duration of the call.</summary>
        public static void BeforeCraftingPanelUpdate()
        {
            if (Current == Mode.None || _gui == null) return;
            _gui.m_tabCraft.interactable = _savedCraftInteractable;
            _gui.m_tabUpgrade.interactable = _savedUpgradeInteractable;
        }

        /// <summary>UpdateCraftingPanel postfix: remember what vanilla chose, then show our tab as the selected one again.</summary>
        public static void AfterCraftingPanelUpdate()
        {
            if (Current == Mode.None || _gui == null) return;
            _savedCraftInteractable = _gui.m_tabCraft.interactable;
            _savedUpgradeInteractable = _gui.m_tabUpgrade.interactable;
            ShowTabStates();
        }

        private static void ShowTabStates()
        {
            if (_gui == null) return;
            if (Current != Mode.None)
            {
                _gui.m_tabCraft.interactable = true;
                _gui.m_tabUpgrade.interactable = true;
            }
            if (_chestsTab != null) _chestsTab.interactable = Current != Mode.Chests;
            if (_recipesTab != null) _recipesTab.interactable = Current != Mode.AllRecipes;
        }

        /// <summary>Player.GetAvailableRecipes postfix: in the All recipes tab, list every unlocked recipe.</summary>
        public static void OverrideAvailableRecipes(Player player, List<Recipe> available)
        {
            if (Current != Mode.AllRecipes || ObjectDB.instance == null) return;
            bool all = player.NoCostCheat() || (ZoneSystem.instance != null && ZoneSystem.instance.GetGlobalKey(GlobalKeys.AllRecipesUnlocked));
            var known = ObjectDB.instance.m_recipes.Where(r =>
                r != null && r.m_item != null && r.m_enabled &&
                r.m_resources != null && r.m_resources.Length > 0 &&
                (r.m_item.m_itemData.m_shared.m_dlc.Length == 0 || DLCMan.instance.IsDLCInstalled(r.m_item.m_itemData.m_shared.m_dlc)) &&
                (all || player.IsRecipeKnown(r.m_item.m_itemData.m_shared.m_name)));
            foreach (var r in known)
                if (!available.Contains(r)) available.Add(r);
        }

        /// <summary>Every frame while the inventory is open.</summary>
        public static void Tick(bool visible)
        {
            if (_gui == null || _chestsTab == null) return;
            bool tabsVisible = visible && !_gui.IsContainerOpen();
            SetActive(_chestsTab, tabsVisible);
            SetActive(_recipesTab, tabsVisible);
            if (tabsVisible) PlaceTabs();

            bool showTake = visible && !_gui.IsContainerOpen() && Current != Mode.Chests &&
                            _gui.InCraftTab() && RecipeTaker.HasSelectedRecipe();
            SetActive(_takeButton, showTake);

            if (visible && Plugin.ToggleKey.Value.MainKey != KeyCode.None && !NearbyPanel.BlockGameInput && Plugin.ToggleKey.Value.IsDown())
            {
                if (Current == Mode.Chests) Activate(Mode.AllRecipes);
                else if (Current == Mode.AllRecipes) _gui.OnTabCraftPressed();
                else Activate(Mode.Chests);
            }
        }

        private static void SetActive(Button b, bool on)
        {
            if (b != null && b.gameObject.activeSelf != on) b.gameObject.SetActive(on);
        }

        /// <summary>Put our tabs just right of the rightmost visible vanilla tab.</summary>
        private static void PlaceTabs()
        {
            var craft = (RectTransform)_gui.m_tabCraft.transform;
            var upgrade = (RectTransform)_gui.m_tabUpgrade.transform;
            if (craft.gameObject.activeSelf && upgrade.gameObject.activeSelf)
                _tabSpacing = upgrade.anchoredPosition.x - craft.anchoredPosition.x;
            var last = upgrade.gameObject.activeSelf ? upgrade : craft;
            float step = Mathf.Abs(_tabSpacing) > 1f ? _tabSpacing : last.rect.width + 4f;
            Place((RectTransform)_chestsTab.transform, last.anchoredPosition + new Vector2(step, 0f));
            Place((RectTransform)_recipesTab.transform, last.anchoredPosition + new Vector2(step * 2f, 0f));
        }

        private static void Place(RectTransform rt, Vector2 pos)
        {
            if ((rt.anchoredPosition - pos).sqrMagnitude > 0.25f) rt.anchoredPosition = pos;
        }
    }
}
