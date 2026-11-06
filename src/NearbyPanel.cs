using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace NearbyChests
{
    /// <summary>
    /// A copy of the vanilla container panel, shown in its place when no chest is open, listing everything in the
    /// nearby chests. Left-click takes one stack, Shift/Ctrl-click or right-click takes all of it.
    /// </summary>
    internal static class NearbyPanel
    {
        private const int Columns = 8;
        private const int MinRows = 4;

        private static InventoryGui _gui;
        private static GameObject _root;
        public static InventoryGrid Grid { get; private set; }
        private static TMP_Text _title;
        private static Button _takeRecipe;
        private static Inventory _view;
        private static readonly Dictionary<ItemDrop.ItemData, ItemGroup> _byViewItem = new Dictionary<ItemDrop.ItemData, ItemGroup>();
        private static float _nextScan;

        public static void Build(InventoryGui gui)
        {
            Destroy();
            _gui = gui;

            var src = gui.m_container;
            _root = Object.Instantiate(src.gameObject, src.parent, false); // m_container is inactive here, so nothing in the copy has woken up yet
            _root.name = "NearbyChests_Panel";
            _root.SetActive(false);
            _root.transform.SetSiblingIndex(src.GetSiblingIndex() + 1);

            Grid = _root.GetComponentInChildren<InventoryGrid>(true);
            Grid.m_onSelected = OnSelected;
            Grid.m_onRightClick = OnRightClick;
            Grid.m_onReleased = null;
            Grid.m_onEnter = null;
            Grid.OnMoveToLowerInventoryGrid = null;
            Grid.OnMoveToUpperInventoryGrid = null;
            Grid.OnSetTouchSelection = null;
            Grid.CanDropDragOntoItem = null;

            _title = Find<TMP_Text>(src, gui.m_containerName.transform);
            if (_title != null) StripLocalize(_title.gameObject);

            var weight = Find<TMP_Text>(src, gui.m_containerWeight.transform);
            if (weight != null) weight.gameObject.SetActive(false);

            _takeRecipe = Find<Button>(src, gui.m_takeAllButton.transform);
            if (_takeRecipe != null)
            {
                _takeRecipe.onClick = new Button.ButtonClickedEvent();
                _takeRecipe.onClick.AddListener(RecipeTaker.TakeSelectedRecipe);
                SetLabel(_takeRecipe, "Take recipe");
            }

            var stackAll = Find<Button>(src, gui.m_stackAllButton.transform);
            if (stackAll != null) stackAll.gameObject.SetActive(false);

            var drop = gui.m_dropButton != null ? Find<Button>(src, gui.m_dropButton.transform) : null;
            if (drop != null) drop.gameObject.SetActive(false);

            _view = new Inventory("Nearby", gui.m_container.GetComponentInChildren<Image>()?.sprite, Columns, MinRows);
        }

        public static void Destroy()
        {
            if (_root != null) Object.Destroy(_root);
            _root = null; Grid = null; _title = null; _takeRecipe = null; _gui = null;
            _byViewItem.Clear();
        }

        /// <summary>Called every frame after InventoryGui.Update.</summary>
        public static void Tick()
        {
            if (_root == null || _gui == null) return;
            var player = Player.m_localPlayer;
            bool visible = InventoryGui.IsVisible() && player != null && Plugin.Enabled.Value;

            if (visible && Plugin.ToggleKey.Value.MainKey != KeyCode.None && Plugin.ToggleKey.Value.IsDown() && !TextInputFocused())
                Plugin.ShowPanel.Value = !Plugin.ShowPanel.Value;

            if (!visible)
            {
                if (_root.activeSelf) _root.SetActive(false);
                _nextScan = 0f;
                return;
            }

            // Keep scanning even with the panel hidden: the crafting panel's hints and click-to-take use the counts.
            if (Time.time >= _nextScan) Rescan();

            bool show = Plugin.ShowPanel.Value && !_gui.IsContainerOpen();
            if (_root.activeSelf != show) _root.SetActive(show);
            if (!show) return;

            Grid.UpdateInventory(_view, null, null);
            if (_takeRecipe != null) _takeRecipe.interactable = RecipeTaker.HasSelectedRecipe();
        }

        /// <summary>Rescans chests now (also used after taking items so the counts update straight away).</summary>
        public static void Rescan()
        {
            _nextScan = Time.time + Plugin.RefreshInterval.Value;
            ChestPool.Scan();
            RebuildView();
        }

        private static void RebuildView()
        {
            _byViewItem.Clear();
            var groups = ChestPool.Groups;
            int rows = Mathf.Max(MinRows, (groups.Count + Columns - 1) / Columns);
            _view.m_inventory.Clear();
            _view.m_width = Columns;
            _view.SetHeight(rows);

            for (int i = 0; i < groups.Count; i++)
            {
                var g = groups[i];
                var shown = g.Sample.Clone();
                shown.m_stack = g.Total;
                shown.m_equipped = false;
                shown.m_gridPos = new Vector2i(i % Columns, i / Columns);
                _view.m_inventory.Add(shown);
                _byViewItem[shown] = g;
            }

            if (_title != null)
            {
                int n = ChestPool.Chests.Count;
                _title.text = $"Nearby chests ({n}, {Plugin.Range.Value:0}m)";
            }
        }

        /// <summary>The grid draws "500/50" for stacks bigger than a stack; show just the total.</summary>
        public static void AfterGridUpdate(InventoryGrid grid)
        {
            if (grid != Grid || _view == null) return;
            foreach (var item in _view.GetAllItems())
            {
                if (item.m_shared.m_maxStackSize <= 1) continue;
                var el = grid.GetElement(item.m_gridPos.x, item.m_gridPos.y, _view.GetWidth());
                if (el != null) el.m_amount.text = item.m_stack.ToString();
            }
        }

        private static void OnSelected(InventoryGrid grid, ItemDrop.ItemData item, Vector2i pos, InventoryGrid.Modifier mod)
        {
            if (item == null) return;
            bool all = mod == InventoryGrid.Modifier.Split || mod == InventoryGrid.Modifier.Move;
            TakeGroup(item, all);
        }

        private static void OnRightClick(InventoryGrid grid, ItemDrop.ItemData item, Vector2i pos)
        {
            if (item != null) TakeGroup(item, all: true);
        }

        private static void TakeGroup(ItemDrop.ItemData shown, bool all)
        {
            if (!_byViewItem.TryGetValue(shown, out var g)) return;
            int want = all ? g.Total : Mathf.Min(g.Total, g.Sample.m_shared.m_maxStackSize);
            int got = ChestPool.Take(g.Matches, want, out bool full);
            Report(g.Sample.m_shared.m_name, got, want, full);
        }

        internal static void Report(string itemName, int got, int want, bool full)
        {
            var player = Player.m_localPlayer;
            if (player == null) return;
            string name = Localization.instance.Localize(itemName);
            if (got > 0)
            {
                _gui?.m_moveItemEffects.Create(player.transform.position, Quaternion.identity);
                player.Message(MessageHud.MessageType.TopLeft, full ? $"Took {got} {name} (inventory full)" : $"Took {got} {name}");
            }
            else
            {
                player.Message(MessageHud.MessageType.Center,
                    full ? "$inventory_full" : $"No {name} available in nearby chests");
            }
            if (got > 0 || want > 0) Rescan();
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

        private static void SetLabel(Button b, string text)
        {
            var label = b.GetComponentInChildren<TMP_Text>(true);
            if (label == null) return;
            StripLocalize(label.gameObject);
            label.text = text;
        }

        // Vanilla's Localize component would overwrite our text with the original "$..." key when it wakes up.
        private static void StripLocalize(GameObject go)
        {
            foreach (var mb in go.GetComponents<MonoBehaviour>())
                if (mb != null && mb.GetType().Name == "Localize") Object.DestroyImmediate(mb);
        }

        private static bool TextInputFocused() =>
            (Chat.instance != null && Chat.instance.HasFocus()) || Console.IsVisible() ||
            (UnityEngine.EventSystems.EventSystem.current?.currentSelectedGameObject?.GetComponent<TMP_InputField>()?.isFocused ?? false);
    }
}
