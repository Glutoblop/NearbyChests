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
        private static string _takeRecipeText;
        private static RectTransform _panelRt;
        private static float _origOffsetMaxY;
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
            _origOffsetMaxY = _panelRt.offsetMax.y;

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
                _takeRecipe.onClick.AddListener(RecipeTaker.TakeSelectedRecipe);
                _takeRecipeLabel = SetLabel(_takeRecipe, "Take recipe");
            }

            var stackAll = Find<Button>(src, gui.m_stackAllButton.transform);
            if (stackAll != null) stackAll.gameObject.SetActive(false);

            var font = gui.m_tabCraft.GetComponentInChildren<TMP_Text>(true)?.font;
            _search = CreateSearch((RectTransform)_root.transform, font);

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

        public static void OnChestsTabShown()
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

            bool show = NearbyTabs.Current == NearbyTabs.Mode.Chests && !_gui.IsContainerOpen();
            if (_root.activeSelf != show)
            {
                _root.SetActive(show);
                if (show) _resetView = true;
            }
            if (!show) return;

            KeepTabsClear();
            Grid.UpdateInventory(View, null, _gui.m_dragItem);
            if (_resetView) { Grid.ResetView(); _resetView = false; }
            if (_takeRecipe != null)
            {
                bool has = RecipeTaker.HasSelectedRecipe();
                _takeRecipe.interactable = has;
                string label = has
                    ? "Take: " + Localization.instance.Localize(_gui.m_selectedRecipe.Recipe.m_item.m_itemData.m_shared.m_name)
                    : "Take recipe";
                if (_takeRecipeLabel != null && label != _takeRecipeText) { _takeRecipeLabel.text = label; _takeRecipeText = label; }
            }
        }

        private static void OnHidden()
        {
            _root.SetActive(false);
            NearbyTabs.Deactivate();
            if (Plugin.ClearSearchOnClose.Value && _search != null)
            {
                _search.SetTextWithoutNotify(string.Empty);
                _query = string.Empty;
            }
            if (_search != null && _search.isFocused) _search.DeactivateInputField();
            _shownSignature = 0;
        }

        /// <summary>
        /// The panel sits where the crafting panel is; make sure it stops below the tab strip so the
        /// Craft / Upgrade / Chests / All recipes tabs stay clickable.
        /// </summary>
        private static void KeepTabsClear()
        {
            var panel = new Vector3[4];
            _panelRt.GetWorldCorners(panel);
            float scale = _panelRt.lossyScale.y;
            if (scale <= 0f) return;
            float origTop = panel[1].y + (_origOffsetMaxY - _panelRt.offsetMax.y) * scale;
            float bottom = panel[0].y;

            float tabsBottom = float.MaxValue;
            var c = new Vector3[4];
            foreach (var b in NearbyTabs.Buttons)
            {
                if (b == null || !b.gameObject.activeInHierarchy) continue;
                ((RectTransform)b.transform).GetWorldCorners(c);
                bool overlapsX = c[2].x > panel[0].x && c[0].x < panel[2].x;
                if (overlapsX && c[1].y > bottom) tabsBottom = Mathf.Min(tabsBottom, c[0].y);
            }

            float desiredTop = origTop;
            if (tabsBottom < origTop && tabsBottom > bottom + 50f * scale) desiredTop = tabsBottom - 2f * scale;
            float newOffset = _origOffsetMaxY - (origTop - desiredTop) / scale;
            if (Mathf.Abs(newOffset - _panelRt.offsetMax.y) > 0.5f)
                _panelRt.offsetMax = new Vector2(_panelRt.offsetMax.x, newOffset);
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
            int sig = ChestPool.Signature * 31 + _query.GetHashCode();
            if (sig != _shownSignature)
            {
                _shownSignature = sig;
                RebuildView();
            }
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
                _title.text = $"Nearby chests ({ChestPool.Chests.Count})";
        }

        private static List<ItemGroup> Filter(List<ItemGroup> groups)
        {
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
