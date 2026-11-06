using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace NearbyChests
{
    /// <summary>
    /// "Recipes" view of the Nearby tab: every recipe the character knows (any crafting station), searchable.
    /// Select one to see its ingredients with what you carry and what the nearby chests hold; click an ingredient
    /// to take the missing amount, or "Take all missing" for the whole recipe.
    /// </summary>
    internal static class RecipeBrowser
    {
        private const float RowHeight = 30f;
        private const int MaxReqRows = 8;

        private static readonly Color RowColor = new Color(0f, 0f, 0f, 0.35f);
        private static readonly Color RowSelectedColor = new Color(1f, 0.65f, 0.2f, 0.35f);
        private static readonly Color HaveColor = new Color(0.55f, 1f, 0.55f);
        private static readonly Color NearbyColor = new Color(1f, 0.85f, 0.3f);
        private static readonly Color MissingColor = new Color(1f, 0.45f, 0.45f);
        private static readonly Color DimColor = new Color(1f, 1f, 1f, 0.6f);

        private sealed class Row
        {
            public Recipe Recipe;
            public string Name;
            public string SearchText;   // ingredient names, for search
            public GameObject Go;
            public Image Bg;
            public TMP_Text Status;
        }

        private sealed class ReqRow
        {
            public GameObject Go;
            public Image Icon;
            public TMP_Text Name, Have, Nearby;
            public Piece.Requirement Req;
            public int Need;
        }

        private static RectTransform _root;
        private static RectTransform _listContent;
        private static ScrollRect _listScroll;
        private static TMP_Text _title, _station, _hint;
        private static readonly List<Row> _rows = new List<Row>();
        private static readonly List<ReqRow> _reqRows = new List<ReqRow>();
        private static TMP_FontAsset _font;
        private static int _knownCount = -1;
        private static string _query = string.Empty;
        private static Recipe _selected;

        public static Recipe Selected => _selected;
        public static bool Active => _root != null && _root.gameObject.activeSelf;

        public static void Build(RectTransform panel, RectTransform area, TMP_FontAsset font)
        {
            _font = font;
            var go = new GameObject("NearbyChests_Recipes", typeof(RectTransform));
            _root = (RectTransform)go.transform;
            _root.SetParent(panel, false);
            _root.anchorMin = area.anchorMin;
            _root.anchorMax = area.anchorMax;
            _root.pivot = area.pivot;
            _root.offsetMin = area.offsetMin;
            _root.offsetMax = area.offsetMax;
            _root.SetSiblingIndex(area.GetSiblingIndex() + 1);

            // Left: recipe list.
            var list = NewRect("List", _root, new Vector2(0f, 0f), new Vector2(0.56f, 1f));
            list.offsetMin = new Vector2(4f, 4f);
            list.offsetMax = new Vector2(-4f, -4f);
            list.gameObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.25f);
            list.gameObject.AddComponent<RectMask2D>();
            _listScroll = list.gameObject.AddComponent<ScrollRect>();
            _listScroll.horizontal = false;
            _listScroll.movementType = ScrollRect.MovementType.Clamped;
            _listScroll.scrollSensitivity = 40f;
            _listScroll.viewport = list;

            _listContent = NewRect("Content", list, new Vector2(0f, 1f), new Vector2(1f, 1f));
            _listContent.pivot = new Vector2(0.5f, 1f);
            _listContent.offsetMin = Vector2.zero;
            _listContent.offsetMax = Vector2.zero;
            var vlg = _listContent.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = 2f;
            vlg.padding = new RectOffset(2, 2, 2, 2);
            vlg.childControlHeight = true;
            vlg.childControlWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.childForceExpandWidth = true;
            _listContent.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            _listScroll.content = _listContent;

            // Right: selected recipe details.
            var details = NewRect("Details", _root, new Vector2(0.56f, 0f), new Vector2(1f, 1f));
            details.offsetMin = new Vector2(4f, 4f);
            details.offsetMax = new Vector2(-4f, -4f);
            details.gameObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.25f);

            _title = NewText(details, "Title", 18f, TextAlignmentOptions.TopLeft);
            Place(_title.rectTransform, 8f, 6f, 24f);
            _title.fontStyle = FontStyles.Bold;
            _station = NewText(details, "Station", 14f, TextAlignmentOptions.TopLeft);
            Place(_station.rectTransform, 8f, 30f, 20f);
            _station.color = DimColor;

            for (int i = 0; i < MaxReqRows; i++)
                _reqRows.Add(NewReqRow(details, 56f + i * (RowHeight + 2f)));

            _hint = NewText(details, "Hint", 13f, TextAlignmentOptions.BottomLeft);
            var hrt = _hint.rectTransform;
            hrt.anchorMin = new Vector2(0f, 0f);
            hrt.anchorMax = new Vector2(1f, 0f);
            hrt.pivot = new Vector2(0f, 0f);
            hrt.offsetMin = new Vector2(8f, 6f);
            hrt.offsetMax = new Vector2(-8f, 46f);
            _hint.textWrappingMode = TextWrappingModes.Normal;
            _hint.color = DimColor;

            go.SetActive(false);
            ShowDetails();
        }

        public static void Destroy()
        {
            if (_root != null) Object.Destroy(_root.gameObject);
            _root = null; _rows.Clear(); _reqRows.Clear(); _knownCount = -1; _selected = null;
        }

        public static void SetActive(bool on)
        {
            if (_root == null) return;
            if (_root.gameObject.activeSelf != on) _root.gameObject.SetActive(on);
            if (on) { RebuildIfNeeded(); Refresh(); }
        }

        public static void SetQuery(string q)
        {
            _query = q ?? string.Empty;
            if (Active) ApplyFilter();
        }

        /// <summary>Every recipe the character has unlocked, regardless of crafting station.</summary>
        private static List<Recipe> KnownRecipes(Player player)
        {
            bool all = player.NoCostCheat() || (ZoneSystem.instance != null && ZoneSystem.instance.GetGlobalKey(GlobalKeys.AllRecipesUnlocked));
            return ObjectDB.instance.m_recipes
                .Where(r => r != null && r.m_item != null && r.m_enabled && !r.m_noCraftOnlyUpgrade &&
                            r.m_resources != null && r.m_resources.Length > 0)
                .Where(r => r.m_item.m_itemData.m_shared.m_dlc.Length == 0 || DLCMan.instance.IsDLCInstalled(r.m_item.m_itemData.m_shared.m_dlc))
                .Where(r => all || player.IsRecipeKnown(r.m_item.m_itemData.m_shared.m_name))
                .ToList();
        }

        private static void RebuildIfNeeded()
        {
            var player = Player.m_localPlayer;
            if (player == null || ObjectDB.instance == null) return;
            var recipes = KnownRecipes(player);
            if (recipes.Count == _knownCount) return;
            _knownCount = recipes.Count;

            foreach (var r in _rows) Object.Destroy(r.Go);
            _rows.Clear();

            var loc = Localization.instance;
            foreach (var recipe in recipes
                         .Select(r => (r, name: loc.Localize(r.m_item.m_itemData.m_shared.m_name)))
                         .OrderBy(x => x.name, System.StringComparer.CurrentCultureIgnoreCase)
                         .Select(x => x.r))
            {
                _rows.Add(NewRecipeRow(recipe));
            }
            if (_selected != null && !recipes.Contains(_selected)) _selected = null;
            ApplyFilter();
        }

        private static void ApplyFilter()
        {
            bool searching = !string.IsNullOrWhiteSpace(_query);
            var scored = new List<(Row row, int score)>();
            foreach (var row in _rows)
            {
                int score = 0;
                if (searching)
                {
                    score = FuzzyMatcher.Score(_query, row.Name);
                    if (score < 0)
                    {
                        int s2 = FuzzyMatcher.Score(_query, row.SearchText);
                        score = s2 >= 0 ? s2 / 4 : -1; // ingredient matches rank below name matches
                    }
                }
                row.Go.SetActive(score >= 0);
                if (score >= 0) scored.Add((row, score));
            }
            if (searching)
            {
                int i = 0;
                foreach (var (row, _) in scored.OrderByDescending(x => x.score)) row.Go.transform.SetSiblingIndex(i++);
            }
            else
            {
                for (int i = 0; i < _rows.Count; i++) _rows[i].Go.transform.SetSiblingIndex(i);
            }
            if (_listContent != null) _listContent.anchoredPosition = Vector2.zero;
        }

        /// <summary>Update row colours and the details pane (called after each chest rescan).</summary>
        public static void Refresh()
        {
            if (!Active) return;
            var player = Player.m_localPlayer;
            if (player == null) return;
            var inv = player.GetInventory();
            foreach (var row in _rows)
            {
                if (!row.Go.activeSelf) continue;
                var state = Coverage(row.Recipe, inv);
                row.Status.text = state == 2 ? "Ready" : state == 1 ? "Nearby" : "";
                row.Status.color = state == 2 ? HaveColor : NearbyColor;
            }
            ShowDetails();
        }

        /// <summary>0 = can't cover, 1 = can cover using nearby chests, 2 = already carrying everything.</summary>
        private static int Coverage(Recipe r, Inventory inv)
        {
            var reqs = Requirements(r).ToList();
            if (reqs.Count == 0) return 0;
            bool any = r.m_requireOnlyOneIngredient;
            bool allHave = true, allCover = true, oneHave = false, oneCover = false;
            foreach (var (req, need) in reqs)
            {
                string name = req.m_resItem.m_itemData.m_shared.m_name;
                int have = inv.CountItems(name);
                bool h = have >= need, c = have + ChestPool.CountForCrafting(name) >= need;
                allHave &= h; allCover &= c; oneHave |= h; oneCover |= c;
            }
            if (any ? oneHave : allHave) return 2;
            return (any ? oneCover : allCover) ? 1 : 0;
        }

        /// <summary>Ingredients for crafting the recipe at quality 1 (what a crafting station would ask for).</summary>
        public static IEnumerable<(Piece.Requirement req, int need)> Requirements(Recipe r)
        {
            foreach (var req in r.m_resources)
            {
                if (req?.m_resItem == null || req.m_upgraderResource) continue;
                int need = req.GetAmount(1);
                if (need > 0) yield return (req, need);
            }
        }

        private static void Select(Row row)
        {
            _selected = row.Recipe;
            foreach (var r in _rows) r.Bg.color = r == row ? RowSelectedColor : RowColor;
            ShowDetails();
        }

        private static void ShowDetails()
        {
            var player = Player.m_localPlayer;
            var loc = Localization.instance;
            if (_selected == null || player == null)
            {
                _title.text = "Select a recipe";
                _station.text = "";
                foreach (var rr in _reqRows) { rr.Go.SetActive(false); rr.Req = null; }
                _hint.text = "Every recipe you've unlocked is listed. Search by name or ingredient.";
                return;
            }

            int amount = _selected.m_amount;
            _title.text = loc.Localize(_selected.m_item.m_itemData.m_shared.m_name) + (amount > 1 ? $" x{amount}" : "");
            var st = _selected.m_craftingStation;
            _station.text = st != null
                ? loc.Localize(st.m_name) + (_selected.m_minStationLevel > 1 ? $" (level {_selected.m_minStationLevel})" : "")
                : "Crafted by hand";

            var inv = player.GetInventory();
            var reqs = Requirements(_selected).ToList();
            for (int i = 0; i < _reqRows.Count; i++)
            {
                var rr = _reqRows[i];
                if (i >= reqs.Count) { rr.Go.SetActive(false); rr.Req = null; continue; }
                var (req, need) = reqs[i];
                string name = req.m_resItem.m_itemData.m_shared.m_name;
                int have = inv.CountItems(name);
                int nearby = ChestPool.CountForCrafting(name);
                rr.Req = req;
                rr.Need = need;
                rr.Go.SetActive(true);
                rr.Icon.sprite = req.m_resItem.m_itemData.GetIcon();
                rr.Name.text = loc.Localize(name);
                rr.Have.text = $"{have}/{need}";
                rr.Have.color = have >= need ? HaveColor : have + nearby >= need ? NearbyColor : MissingColor;
                rr.Nearby.text = nearby > 0 ? $"+{nearby}" : "-";
                rr.Nearby.color = nearby > 0 ? Color.white : DimColor;
            }
            _hint.text = _selected.m_requireOnlyOneIngredient
                ? "Needs only one of these. Click an ingredient to take what's missing."
                : "Click an ingredient to take what's missing, or use Take all missing.";
        }

        /// <summary>Take everything the selected recipe is short of.</summary>
        public static void TakeAllForSelected()
        {
            if (_selected == null) return;
            RecipeTaker.TakeFor(Requirements(_selected).ToList(), _selected.m_requireOnlyOneIngredient);
        }

        // ---- UI construction ----

        private static Row NewRecipeRow(Recipe recipe)
        {
            var loc = Localization.instance;
            var row = new Row
            {
                Recipe = recipe,
                Name = loc.Localize(recipe.m_item.m_itemData.m_shared.m_name),
                SearchText = string.Join(" ", recipe.m_resources.Where(r => r?.m_resItem != null)
                    .Select(r => loc.Localize(r.m_resItem.m_itemData.m_shared.m_name)))
            };

            var go = new GameObject("Recipe", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            go.transform.SetParent(_listContent, false);
            go.GetComponent<LayoutElement>().preferredHeight = RowHeight;
            row.Go = go;
            row.Bg = go.GetComponent<Image>();
            row.Bg.color = recipe == _selected ? RowSelectedColor : RowColor;
            var btn = go.GetComponent<Button>();
            btn.targetGraphic = row.Bg;
            btn.onClick.AddListener(() => Select(row));

            var icon = NewRect("Icon", go.transform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f));
            icon.pivot = new Vector2(0f, 0.5f);
            icon.sizeDelta = new Vector2(RowHeight - 4f, RowHeight - 4f);
            icon.anchoredPosition = new Vector2(4f, 0f);
            var img = icon.gameObject.AddComponent<Image>();
            img.sprite = recipe.m_item.m_itemData.GetIcon();
            img.preserveAspect = true;
            img.raycastTarget = false;

            var name = NewText(go.transform, "Name", 15f, TextAlignmentOptions.MidlineLeft);
            name.rectTransform.anchorMin = new Vector2(0f, 0f);
            name.rectTransform.anchorMax = new Vector2(1f, 1f);
            name.rectTransform.offsetMin = new Vector2(RowHeight + 6f, 0f);
            name.rectTransform.offsetMax = new Vector2(-62f, 0f);
            name.text = row.Name + (recipe.m_amount > 1 ? $" x{recipe.m_amount}" : "");

            row.Status = NewText(go.transform, "Status", 13f, TextAlignmentOptions.MidlineRight);
            row.Status.rectTransform.anchorMin = new Vector2(1f, 0f);
            row.Status.rectTransform.anchorMax = new Vector2(1f, 1f);
            row.Status.rectTransform.pivot = new Vector2(1f, 0.5f);
            row.Status.rectTransform.sizeDelta = new Vector2(58f, 0f);
            row.Status.rectTransform.anchoredPosition = new Vector2(-4f, 0f);
            return row;
        }

        private static ReqRow NewReqRow(RectTransform parent, float top)
        {
            var rr = new ReqRow();
            var go = new GameObject("Ingredient", typeof(RectTransform), typeof(Image), typeof(Button));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            Place(rt, 6f, top, RowHeight);
            var bg = go.GetComponent<Image>();
            bg.color = RowColor;
            var btn = go.GetComponent<Button>();
            btn.targetGraphic = bg;
            btn.onClick.AddListener(() =>
            {
                if (rr.Req != null) RecipeTaker.TakeMissing(rr.Req, rr.Need, report: true);
            });
            rr.Go = go;

            var icon = NewRect("Icon", rt, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f));
            icon.pivot = new Vector2(0f, 0.5f);
            icon.sizeDelta = new Vector2(RowHeight - 4f, RowHeight - 4f);
            icon.anchoredPosition = new Vector2(3f, 0f);
            rr.Icon = icon.gameObject.AddComponent<Image>();
            rr.Icon.preserveAspect = true;
            rr.Icon.raycastTarget = false;

            rr.Name = NewText(rt, "Name", 14f, TextAlignmentOptions.MidlineLeft);
            rr.Name.rectTransform.anchorMin = Vector2.zero;
            rr.Name.rectTransform.anchorMax = Vector2.one;
            rr.Name.rectTransform.offsetMin = new Vector2(RowHeight + 4f, 0f);
            rr.Name.rectTransform.offsetMax = new Vector2(-104f, 0f);

            rr.Have = NewText(rt, "Have", 14f, TextAlignmentOptions.MidlineRight);
            rr.Have.rectTransform.anchorMin = new Vector2(1f, 0f);
            rr.Have.rectTransform.anchorMax = new Vector2(1f, 1f);
            rr.Have.rectTransform.pivot = new Vector2(1f, 0.5f);
            rr.Have.rectTransform.sizeDelta = new Vector2(56f, 0f);
            rr.Have.rectTransform.anchoredPosition = new Vector2(-48f, 0f);

            rr.Nearby = NewText(rt, "Nearby", 14f, TextAlignmentOptions.MidlineRight);
            rr.Nearby.rectTransform.anchorMin = new Vector2(1f, 0f);
            rr.Nearby.rectTransform.anchorMax = new Vector2(1f, 1f);
            rr.Nearby.rectTransform.pivot = new Vector2(1f, 0.5f);
            rr.Nearby.rectTransform.sizeDelta = new Vector2(44f, 0f);
            rr.Nearby.rectTransform.anchoredPosition = new Vector2(-4f, 0f);

            go.SetActive(false);
            return rr;
        }

        /// <summary>Stretch horizontally, fixed height, placed <paramref name="top"/> px below the parent's top.</summary>
        private static void Place(RectTransform rt, float side, float top, float height)
        {
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.offsetMin = new Vector2(side, -top - height);
            rt.offsetMax = new Vector2(-side, -top);
        }

        private static RectTransform NewRect(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            return rt;
        }

        private static TextMeshProUGUI NewText(Transform parent, string name, float size, TextAlignmentOptions align)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<TextMeshProUGUI>();
            if (_font != null) t.font = _font;
            t.fontSize = size;
            t.alignment = align;
            t.color = Color.white;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.overflowMode = TextOverflowModes.Ellipsis;
            t.raycastTarget = false;
            return t;
        }
    }
}
