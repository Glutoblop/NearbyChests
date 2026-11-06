using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace NearbyChests
{
    /// <summary>
    /// The Chests tab: a copy of the vanilla container panel, shown in the crafting panel's place, listing everything
    /// in the nearby chests as normal stacks. The grid is wired to the game's own click/drag handlers, so it behaves
    /// like a chest: drag, Shift-click to split, Ctrl-click to move. The patches in Patches.cs redirect the
    /// resulting item moves to the real chests (the grid itself only shows copies).
    /// </summary>
    internal static class NearbyPanel
    {
        private const int Columns = 8;
        private const int MinRows = 4;
        private const float SearchWidth = 230f;
        private const float SearchHeight = 26f;

        private static InventoryGui _gui;
        private static GameObject _root;
        public static InventoryGrid Grid { get; private set; }
        /// <summary>The fake inventory the grid displays. Never holds real items.</summary>
        public static Inventory View { get; private set; }
        private static TMP_Text _title;
        private static Button _takeRecipe;
        private static TMP_Text _takeRecipeLabel;
        private static TMP_Text _reqStrip;
        private static bool _loggedLayout;
        private static readonly HashSet<string> _filterNames = new HashSet<string>();

        /// <summary>When set, the panel only shows this recipe's ingredients.</summary>
        public static Recipe FilterRecipe { get; private set; }
        private static RectTransform _panelRt;
        private static TMP_InputField _search;
        private static string _query = string.Empty;
        private static int _shownSignature;
        private static bool _resetView;
        private static readonly Dictionary<ItemDrop.ItemData, ItemGroup> _byViewItem = new Dictionary<ItemDrop.ItemData, ItemGroup>();
        private static float _nextScan;
        private static bool _wasVisible;

        // Frame until which game hotkeys are suppressed (covers the frame the field loses focus on Escape/Enter).
        private static int _blockInputUntilFrame = -1;
        public static bool BlockGameInput => Time.frameCount <= _blockInputUntilFrame;

        public static bool IsShown => _root != null && _root.activeSelf;

        public static void Build(InventoryGui gui)
        {
            Destroy();
            _gui = gui;

            var src = gui.m_container;
            _root = Object.Instantiate(src.gameObject, src.parent, false); // m_container is inactive here, so nothing in the copy has woken up yet
            _root.name = "NearbyChests_Panel";
            _root.SetActive(false);
            _root.transform.SetSiblingIndex(src.GetSiblingIndex() + 1);
            _panelRt = (RectTransform)_root.transform;

            // Same handlers as the vanilla container grid.
            Grid = _root.GetComponentInChildren<InventoryGrid>(true);
            Grid.m_onSelected = gui.OnSelectedItem;
            Grid.m_onRightClick = gui.OnRightClickItem;
            Grid.m_onReleased = gui.OnReleasedItem;
            Grid.m_onEnter = gui.OnEnterElement;
            Grid.CanDropDragOntoItem = gui.CanDropDragOntoItem;
            Grid.OnMoveToLowerInventoryGrid = null;
            Grid.OnMoveToUpperInventoryGrid = null;
            Grid.OnSetTouchSelection = null;
            EnsureScrollable(Grid);

            _title = Find<TMP_Text>(src, gui.m_containerName.transform);
            if (_title != null) StripLocalize(_title.gameObject);

            var weight = Find<TMP_Text>(src, gui.m_containerWeight.transform);
            if (weight != null) weight.gameObject.SetActive(false);

            _takeRecipe = Find<Button>(src, gui.m_takeAllButton.transform);
            if (_takeRecipe != null)
            {
                _takeRecipe.onClick = new Button.ButtonClickedEvent();
                _takeRecipe.onClick.AddListener(ClearFilter);
                _takeRecipeLabel = SetLabel(_takeRecipe, "Clear filter");
                _takeRecipe.gameObject.SetActive(false);
            }

            var stackAll = Find<Button>(src, gui.m_stackAllButton.transform);
            if (stackAll != null) stackAll.gameObject.SetActive(false);

            var font = gui.m_tabCraft.GetComponentInChildren<TMP_Text>(true)?.font;
            _search = CreateSearch((RectTransform)_root.transform, font);

            _reqStrip = CreateRequirementStrip(_panelRt, font);

            // Move the panel to the top level of the inventory screen so nothing clips or covers it beside the
            // crafting panel (the container slot's parent may be masked).
            if (gui.m_inventoryRoot != null)
            {
                _root.transform.SetParent(gui.m_inventoryRoot, true);
                _root.transform.SetAsLastSibling();
                KeepBelowDialogs(gui);
            }

            View = new Inventory("Nearby", null, Columns, MinRows);
            NearbyTabs.Build(gui);
        }

        public static void Destroy()
        {
            NearbyTabs.Destroy();
            if (_root != null) Object.Destroy(_root);
            _root = null; Grid = null; _title = null; _takeRecipe = null; _takeRecipeLabel = null; _search = null; _gui = null;
            _byViewItem.Clear();
        }

        public static void OnTabShown()
        {
            _resetView = true;
            ForceRescan();
        }

        /// <summary>Called every frame after InventoryGui.Update.</summary>
        public static void Tick()
        {
            if (_root == null || _gui == null) return;
            if (_search != null && _search.isFocused) _blockInputUntilFrame = Time.frameCount + 1;

            var player = Player.m_localPlayer;
            bool visible = InventoryGui.IsVisible() && player != null && Plugin.Enabled.Value;
            NearbyTabs.Tick(visible);
            if (!visible)
            {
                if (_wasVisible) OnHidden();
                _wasVisible = false;
                _nextScan = 0f;
                return;
            }
            _wasVisible = true;

            // Keep scanning even with the panel hidden: the crafting panel's hints and click-to-take use the counts.
            if (Time.time >= _nextScan) Rescan();

            bool show = NearbyTabs.Active && !_gui.IsContainerOpen();
            if (_root.activeSelf != show)
            {
                _root.SetActive(show);
                if (show) _resetView = true;
            }
            if (!show) return;

            PlaceLeftOfCrafting();
            LogLayoutOnce();
            Grid.UpdateInventory(View, null, _gui.m_dragItem);
            if (_resetView) { Grid.ResetView(); _resetView = false; }
            if (_takeRecipe != null && _takeRecipe.gameObject.activeSelf != (FilterRecipe != null))
                _takeRecipe.gameObject.SetActive(FilterRecipe != null);
            UpdateRequirementStrip(player);
        }

        // ---- Recipe filter ----

        /// <summary>The "Filter chests" button (in place of Craft): show only the selected recipe's ingredients, or clear.</summary>
        public static void ToggleFilterForSelected()
        {
            var recipe = _gui != null && RecipeTaker.HasSelectedRecipe() ? _gui.m_selectedRecipe.Recipe : null;
            if (recipe == null || recipe == FilterRecipe) { ClearFilter(); return; }
            FilterRecipe = recipe;
            _filterNames.Clear();
            foreach (var req in recipe.m_resources)
                if (req?.m_resItem != null) _filterNames.Add(req.m_resItem.m_itemData.m_shared.m_name);
            _resetView = true;
            ForceRescan();
        }

        public static void ClearFilter()
        {
            if (FilterRecipe == null) return;
            FilterRecipe = null;
            _filterNames.Clear();
            _resetView = true;
            ForceRescan();
        }

        /// <summary>The ingredients and amounts for the filtered recipe (or, without a filter, the selected one).</summary>
        private static List<(Piece.Requirement req, int need)> ShownRequirements()
        {
            var list = new List<(Piece.Requirement, int)>();
            var recipe = FilterRecipe ?? (RecipeTaker.HasSelectedRecipe() ? _gui.m_selectedRecipe.Recipe : null);
            if (recipe == null) return list;
            bool selected = RecipeTaker.HasSelectedRecipe() && _gui.m_selectedRecipe.Recipe == recipe;
            int quality = selected ? RecipeTaker.Quality : 1;
            int mult = selected ? RecipeTaker.Multiplier : 1;
            var station = Player.m_localPlayer?.GetCurrentCraftingStation();
            foreach (var req in recipe.m_resources)
            {
                if (req?.m_resItem == null) continue;
                if (station != null ? station.m_upgrader != req.m_upgraderResource : req.m_upgraderResource) continue;
                int need = req.GetAmount(quality) * mult;
                if (need > 0) list.Add((req, need));
            }
            return list;
        }

        /// <summary>A box under the panel: what the recipe needs, what you carry, and what the chests hold.</summary>
        private static void UpdateRequirementStrip(Player player)
        {
            if (_reqStrip == null) return;
            var recipe = FilterRecipe ?? (RecipeTaker.HasSelectedRecipe() ? _gui.m_selectedRecipe.Recipe : null);
            var go = _reqStrip.transform.parent.gameObject;
            if (recipe == null || player == null) { if (go.activeSelf) go.SetActive(false); return; }
            if (!go.activeSelf) go.SetActive(true);

            var loc = Localization.instance;
            var inv = player.GetInventory();
            var sb = new System.Text.StringBuilder();
            sb.Append("<b>").Append(loc.Localize(recipe.m_item.m_itemData.m_shared.m_name)).Append("</b>");
            sb.Append(FilterRecipe == recipe ? "  <color=#9fd8ff>(filtered)</color>" : "  <color=#aaaaaa>(selected - press Filter chests)</color>");
            sb.Append("\n<size=85%><color=#aaaaaa>need / have / in chests</color></size>");
            foreach (var (req, need) in ShownRequirements())
            {
                string name = req.m_resItem.m_itemData.m_shared.m_name;
                int have = inv.CountItems(name);
                int chests = ChestPool.CountForCrafting(name);
                string col = have >= need ? "#8cff8c" : have + chests >= need ? "#ffd94d" : "#ff7373";
                sb.Append("\n<color=").Append(col).Append(">").Append(loc.Localize(name)).Append(": ")
                  .Append(need).Append(" / ").Append(have).Append(" / ").Append(chests).Append("</color>");
            }
            string text = sb.ToString();
            if (_reqStrip.text != text) _reqStrip.text = text;
        }

        private static TMP_Text CreateRequirementStrip(RectTransform panel, TMP_FontAsset font)
        {
            var bgGo = new GameObject("NearbyChests_Requirements", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            var rt = (RectTransform)bgGo.transform;
            rt.SetParent(panel, false);
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(1f, 0f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = new Vector2(0f, -4f);
            rt.sizeDelta = new Vector2(0f, 0f);
            bgGo.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.7f);
            var vlg = bgGo.GetComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(10, 10, 6, 6);
            vlg.childControlHeight = true;
            vlg.childControlWidth = true;
            vlg.childForceExpandHeight = false;
            bgGo.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var t = CreateText(rt, "Text", font);
            t.fontSize = 16f;
            t.textWrappingMode = TextWrappingModes.Normal;
            t.overflowMode = TextOverflowModes.Overflow;
            t.alignment = TextAlignmentOptions.TopLeft;
            t.richText = true;
            bgGo.SetActive(false);
            return t;
        }

        private static void LogLayoutOnce()
        {
            if (_loggedLayout) return;
            _loggedLayout = true;
            var c = new Vector3[4];
            string R(RectTransform r) { if (r == null) return "null"; r.GetWorldCorners(c); return $"({c[0].x:0},{c[0].y:0})-({c[2].x:0},{c[2].y:0})"; }
            Plugin.Log($"Layout: crafting={R(_gui.m_crafting)} panel={R(_panelRt)} player={R(_gui.m_player)} " +
                       $"canvas={R(_panelRt.GetComponentInParent<Canvas>()?.rootCanvas.transform as RectTransform)}");
        }

        private static void OnHidden()
        {
            _root.SetActive(false);
            NearbyTabs.Deactivate();
            FilterRecipe = null;
            _filterNames.Clear();
            if (Plugin.ClearSearchOnClose.Value && _search != null)
            {
                _search.SetTextWithoutNotify(string.Empty);
                _query = string.Empty;
            }
            if (_search != null && _search.isFocused) _search.DeactivateInputField();
            _shownSignature = 0;
        }

        /// <summary>
        /// Keep the panel in the middle of the screen, just left of the crafting panel and top-aligned with it,
        /// so recipes, ingredients and chest contents are all visible at once. Clamped to the screen; nudge with PanelOffsetX / PanelOffsetY.
        /// </summary>
        private static void PlaceLeftOfCrafting()
        {
            var anchor = _gui.m_crafting;
            if (anchor == null) return;
            float scale = _panelRt.lossyScale.x;
            if (scale <= 0f) return;

            var cc = new Vector3[4];
            var pc = new Vector3[4];
            anchor.GetWorldCorners(cc);
            _panelRt.GetWorldCorners(pc);

            float gap = 6f * scale;
            float dx = (cc[0].x - gap) - pc[2].x + Plugin.PanelOffsetX.Value * scale;
            float dy = cc[1].y - pc[1].y + Plugin.PanelOffsetY.Value * scale;

            var canvas = _panelRt.GetComponentInParent<Canvas>();
            if (canvas != null)
            {
                var sc = new Vector3[4];
                ((RectTransform)canvas.rootCanvas.transform).GetWorldCorners(sc);
                float overRight = pc[2].x + dx - (sc[2].x - 8f * scale);
                if (overRight > 0f) dx -= overRight;
                float overLeft = (sc[0].x + 8f * scale) - (pc[0].x + dx);
                if (overLeft > 0f) dx += overLeft;
                float underBottom = (sc[0].y + 8f * scale) - (pc[0].y + dy);
                if (underBottom > 0f) dy += underBottom;
            }

            if (Mathf.Abs(dx) > 0.5f * scale || Mathf.Abs(dy) > 0.5f * scale)
                _panelRt.anchoredPosition += new Vector2(dx, dy) / scale;
        }

        /// <summary>
        /// Draw the panel underneath the inventory screen's pop-up dialogs (split stack, variants, skills...), so
        /// they stay usable when they overlap it.
        /// </summary>
        private static void KeepBelowDialogs(InventoryGui gui)
        {
            var root = gui.m_inventoryRoot;
            int lowest = int.MaxValue;
            var dialogs = new Component[] { gui.m_splitDialog, gui.m_variantDialog, gui.m_skillsDialog, gui.m_textsDialog, gui.m_achievementsPanel };
            var transforms = dialogs.Where(d => d != null).Select(d => d.transform).ToList();
            if (gui.m_trophiesPanel != null) transforms.Add(gui.m_trophiesPanel.transform);
            foreach (var t in transforms)
            {
                var top = t;
                while (top != null && top.parent != root) top = top.parent;
                if (top != null && top != _root.transform) lowest = Mathf.Min(lowest, top.GetSiblingIndex());
            }
            if (lowest != int.MaxValue) _root.transform.SetSiblingIndex(lowest);
        }

        /// <summary>True while the player is dragging (or splitting) one of our fake items.</summary>
        private static bool Dragging =>
            _gui != null && View != null && (_gui.m_dragInventory == View || _gui.m_splitInventory == View);

        /// <summary>Rescans chests now (also used after moving items so the counts update straight away).</summary>
        public static void Rescan()
        {
            _nextScan = Time.time + Plugin.RefreshInterval.Value;
            if (Dragging) return; // rebuilding the view would cancel the drag
            ChestPool.Scan();
            int sig = (ChestPool.Signature * 31 + _query.GetHashCode()) * 31 + (FilterRecipe != null ? FilterRecipe.GetInstanceID() : 0);
            if (sig != _shownSignature)
            {
                _shownSignature = sig;
                RebuildView();
            }

            // In the Nearby tab the recipe list's "can make it" state depends on chests + inventory: refresh it when either changes.
            if (NearbyTabs.Active && _gui != null && Player.m_localPlayer != null)
            {
                int craftSig = ChestPool.Signature * 31 + InventorySignature(Player.m_localPlayer.GetInventory());
                if (craftSig != _craftSignature)
                {
                    _craftSignature = craftSig;
                    _gui.UpdateCraftingPanel();
                }
            }
        }

        private static int _craftSignature;

        private static int InventorySignature(Inventory inv)
        {
            int sig = 17;
            foreach (var it in inv.GetAllItems())
                sig = sig * 31 + it.m_shared.m_name.GetHashCode() * 7 + it.m_stack;
            return sig;
        }

        private static void RebuildView()
        {
            _byViewItem.Clear();
            var groups = Filter(ChestPool.Groups);

            var items = new List<(ItemDrop.ItemData item, ItemGroup group)>();
            foreach (var g in groups)
            {
                int left = g.Total, max = Mathf.Max(1, g.Sample.m_shared.m_maxStackSize);
                do
                {
                    var shown = g.Sample.Clone();
                    shown.m_stack = Mathf.Min(left, max);
                    shown.m_equipped = false;
                    items.Add((shown, g));
                    left -= shown.m_stack;
                } while (left > 0);
            }

            int rows = Mathf.Max(MinRows, (items.Count + Columns - 1) / Columns);
            View.m_inventory.Clear();
            View.m_width = Columns;
            View.SetHeight(rows);
            for (int i = 0; i < items.Count; i++)
            {
                var (shown, g) = items[i];
                shown.m_gridPos = new Vector2i(i % Columns, i / Columns);
                View.m_inventory.Add(shown);
                _byViewItem[shown] = g;
            }

            if (_title != null)
                _title.text = FilterRecipe != null
                    ? $"Nearby chests: {Localization.instance.Localize(FilterRecipe.m_item.m_itemData.m_shared.m_name)}"
                    : $"Nearby chests ({ChestPool.Chests.Count})";
        }

        private static List<ItemGroup> Filter(List<ItemGroup> groups)
        {
            if (FilterRecipe != null) groups = groups.Where(g => _filterNames.Contains(g.Sample.m_shared.m_name)).ToList();
            if (string.IsNullOrWhiteSpace(_query)) return groups;
            var loc = Localization.instance;
            return groups
                .Select(g => (g, score: Mathf.Max(
                    FuzzyMatcher.Score(_query, loc.Localize(g.Sample.m_shared.m_name)),
                    g.Sample.m_dropPrefab != null ? FuzzyMatcher.Score(_query, g.Sample.m_dropPrefab.name) - 1 : -1)))
                .Where(x => x.score >= 0)
                .OrderByDescending(x => x.score) // stable: keeps type/name order for ties
                .Select(x => x.g)
                .ToList();
        }

        public static ItemGroup GroupOf(ItemDrop.ItemData shown) =>
            shown != null && _byViewItem.TryGetValue(shown, out var g) ? g : null;

        /// <summary>Takes up to <paramref name="amount"/> of the shown item's type into <paramref name="to"/>.</summary>
        public static bool TakeShown(ItemDrop.ItemData shown, int amount, Inventory to, Vector2i? slot)
        {
            var g = GroupOf(shown);
            if (g == null) return false;
            int got = ChestPool.Take(g.Matches, amount, to, slot, out bool full);
            AfterMove(g.Sample.m_shared.m_name, got, full);
            return got > 0;
        }

        public static void AfterMove(string itemName, int moved, bool full)
        {
            var player = Player.m_localPlayer;
            if (player == null) return;
            if (moved > 0) _gui?.m_moveItemEffects.Create(player.transform.position, Quaternion.identity);
            else if (full) player.Message(MessageHud.MessageType.Center, "$inventory_full");
            ForceRescan();
        }

        public static void ForceRescan()
        {
            _shownSignature = 0;
            _nextScan = 0f;
            if (!Dragging) Rescan();
        }

        internal static void Report(string itemName, int got, int want, bool full)
        {
            var player = Player.m_localPlayer;
            if (player == null) return;
            string name = Localization.instance.Localize(itemName);
            if (got > 0)
                player.Message(MessageHud.MessageType.TopLeft, full ? $"Took {got} {name} (inventory full)" : $"Took {got} {name}");
            else
                player.Message(MessageHud.MessageType.Center, full ? "$inventory_full" : $"No {name} available in nearby chests");
            AfterMove(itemName, got, full);
        }

        // ---- UI helpers ----

        /// <summary>The container grid should already sit in a ScrollRect; add one if it doesn't.</summary>
        private static void EnsureScrollable(InventoryGrid grid)
        {
            if (grid.GetComponentInParent<ScrollRect>(true) != null || grid.m_gridRoot == null) return;
            var go = grid.gameObject;
            if (go.GetComponent<RectMask2D>() == null) go.AddComponent<RectMask2D>();
            if (go.GetComponent<Graphic>() == null) go.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0f);
            var sr = go.AddComponent<ScrollRect>();
            sr.content = grid.m_gridRoot;
            sr.viewport = (RectTransform)go.transform;
            sr.horizontal = false;
            sr.vertical = true;
            sr.movementType = ScrollRect.MovementType.Clamped;
            sr.scrollSensitivity = 30f;
            if (grid.m_scrollbar != null)
            {
                sr.verticalScrollbar = grid.m_scrollbar;
                sr.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
            }
            Plugin.Log("Container grid had no ScrollRect; added one.");
        }

        private static TMP_InputField CreateSearch(RectTransform panel, TMP_FontAsset font)
        {
            var go = new GameObject("NearbyChests_Search", typeof(RectTransform), typeof(Image));
            go.SetActive(false); // configure before TMP_InputField.OnEnable runs
            var rt = (RectTransform)go.transform;
            rt.SetParent(panel, false);
            rt.anchorMin = rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(1f, 1f);
            rt.anchoredPosition = new Vector2(Plugin.SearchOffsetX.Value, Plugin.SearchOffsetY.Value);
            rt.sizeDelta = new Vector2(SearchWidth, SearchHeight);

            var bg = go.GetComponent<Image>();
            bg.color = new Color(0f, 0f, 0f, 0.55f);

            var area = new GameObject("TextArea", typeof(RectTransform), typeof(RectMask2D));
            var areaRt = (RectTransform)area.transform;
            areaRt.SetParent(rt, false);
            areaRt.anchorMin = Vector2.zero;
            areaRt.anchorMax = Vector2.one;
            areaRt.offsetMin = new Vector2(8f, 2f);
            areaRt.offsetMax = new Vector2(-8f, -2f);

            var placeholder = CreateText(areaRt, "Placeholder", font);
            placeholder.text = "Search nearby chests...";
            placeholder.fontStyle = FontStyles.Italic;
            placeholder.color = new Color(1f, 1f, 1f, 0.4f);

            var text = CreateText(areaRt, "Text", font);
            text.color = Color.white;

            var input = go.AddComponent<TMP_InputField>();
            input.targetGraphic = bg;
            input.textViewport = areaRt;
            input.textComponent = text;
            input.placeholder = placeholder;
            input.fontAsset = font;
            input.pointSize = 16f;
            input.lineType = TMP_InputField.LineType.SingleLine;
            input.characterLimit = 64;
            input.caretColor = Color.white;
            input.customCaretColor = true;
            input.onFocusSelectAll = true;
            input.restoreOriginalTextOnEscape = false;
            input.onValueChanged.AddListener(new UnityAction<string>(OnSearchChanged));

            go.SetActive(true);
            return input;
        }

        private static TextMeshProUGUI CreateText(RectTransform parent, string name, TMP_FontAsset font)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            var t = go.AddComponent<TextMeshProUGUI>();
            if (font != null) t.font = font;
            t.fontSize = 16f;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.overflowMode = TextOverflowModes.Ellipsis;
            t.alignment = TextAlignmentOptions.MidlineLeft;
            t.raycastTarget = false;
            return t;
        }

        private static void OnSearchChanged(string value)
        {
            _query = value ?? string.Empty;
            _resetView = true;
            ForceRescan();
        }

        private static T Find<T>(Transform srcRoot, Transform srcChild) where T : Component
        {
            string path = RelPath(srcRoot, srcChild);
            if (path == null) return null;
            var t = path.Length == 0 ? _root.transform : _root.transform.Find(path);
            return t != null ? t.GetComponent<T>() : null;
        }

        private static string RelPath(Transform root, Transform child)
        {
            var parts = new List<string>();
            for (var t = child; t != null; t = t.parent)
            {
                if (t == root) { parts.Reverse(); return string.Join("/", parts); }
                parts.Add(t.name);
            }
            return null;
        }

        internal static TMP_Text SetLabel(Button b, string text)
        {
            var label = b.GetComponentInChildren<TMP_Text>(true);
            if (label == null) return null;
            StripLocalize(label.gameObject);
            label.text = text;
            return label;
        }

        // Vanilla's Localize component would overwrite our text with the original "$..." key when it wakes up.
        private static void StripLocalize(GameObject go)
        {
            foreach (var mb in go.GetComponents<MonoBehaviour>())
                if (mb != null && mb.GetType().Name == "Localize") Object.DestroyImmediate(mb);
        }
    }
}
