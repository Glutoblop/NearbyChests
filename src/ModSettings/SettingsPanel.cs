// SHARED FILE: edit the canonical copy in D:\Work\Valheim\Shared\ModSettings, then run Shared\sync.ps1.
using System.Collections.Generic;
using System.Linq;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GlutoModSettings
{
    // The Mod Settings window: tabs down the left (one per plugin), that plugin's settings on the right,
    // grouped by config section. It lives under the pause menu's root and hides the menu's button list while open.
    internal static class SettingsPanel
    {
        private const float Width = 1180f, Height = 720f, TabWidth = 230f;

        private static Menu _menu;
        private static GameObject _root;
        private static RectTransform _tabList;
        private static RectTransform _content;
        private static ScrollRect _scroll;
        private static TextMeshProUGUI _title;
        private static readonly List<(PluginInfo info, Button button)> Tabs = new List<(PluginInfo, Button)>();
        private static readonly List<SettingRow> Rows = new List<SettingRow>();
        private static string _selectedGuid;

        internal static bool IsOpen => _root && _root.activeSelf;

        internal static void Open(Menu menu)
        {
            if (_menu != menu || !_root) Build(menu);
            _root.SetActive(true);
            _root.transform.SetAsLastSibling();
            menu.m_menuDialog.gameObject.SetActive(false);
            RebuildTabs();
        }

        internal static void Close()
        {
            SettingRow.Capturing = null;
            if (!IsOpen) return;
            _root.SetActive(false);
            if (_menu && _menu.m_root.gameObject.activeSelf) _menu.m_menuDialog.gameObject.SetActive(true);
        }

        // Called from Menu.Update while we're open (the menu's own Update is skipped).
        internal static void Tick()
        {
            if (SettingRow.Capturing != null)
            {
                SettingRow.Capturing.TickCapture();
                return;
            }
            if (ZInput.GetKeyDown(KeyCode.Escape) || ZInput.GetButtonDown("JoyButtonB") || ZInput.GetButtonDown("JoyMenu"))
                Close();
        }

        private static List<PluginInfo> Plugins()
        {
            return Shared.Participants()
                .Select(p => p.info)
                .Where(p => p.Instance.Config != null && p.Instance.Config.Count > 0)
                .OrderBy(p => p.Metadata.Name)
                .ToList();
        }

        private static void RebuildTabs()
        {
            foreach (var t in Tabs) if (t.button) Object.Destroy(t.button.gameObject);
            Tabs.Clear();

            List<PluginInfo> plugins = Plugins();
            foreach (PluginInfo p in plugins)
            {
                Button b = UiFactory.Button(_tabList, p.Metadata.Name, 19f);
                UiFactory.Layout(b, TabWidth - 16f, TabWidth - 16f, 40, 40);
                string guid = p.Metadata.GUID;
                b.onClick.AddListener(() => Select(guid));
                Tabs.Add((p, b));
            }

            if (plugins.Count == 0)
            {
                _selectedGuid = null;
                ShowEmpty();
                return;
            }
            Select(plugins.Any(p => p.Metadata.GUID == _selectedGuid) ? _selectedGuid : plugins[0].Metadata.GUID);
        }

        private static void Select(string guid)
        {
            SettingRow.Capturing = null;
            _selectedGuid = guid;
            ClearContent();

            PluginInfo info = null;
            foreach (var (p, b) in Tabs)
            {
                bool on = p.Metadata.GUID == guid;
                if (on) info = p;
                TMP_Text t = b.GetComponentInChildren<TMP_Text>(true);
                if (t) t.color = on ? UiFactory.Gold : UiFactory.TextColor;
            }
            if (info == null) { ShowEmpty(); return; }

            _title.text = $"{info.Metadata.Name} <size=60%><color=#A89F92>v{info.Metadata.Version}</color></size>";
            ConfigFile cfg = info.Instance.Config;

            // ConfigFile keeps bind order; group by section in the order sections first appear.
            var entries = cfg.Select(kv => kv.Value).ToList();
            foreach (string section in entries.Select(e => e.Definition.Section).Distinct())
            {
                TextMeshProUGUI header = UiFactory.Text("Section_" + section, _content, SettingRow.Pretty(section), 24f, UiFactory.Gold);
                UiFactory.Layout(header, -1, -1, 40, 40);
                header.alignment = TextAlignmentOptions.BottomLeft;
                Image line = UiFactory.Image("Line", _content, new Color(1f, 0.79f, 0.36f, 0.3f));
                UiFactory.Layout(line, -1, -1, 2, 2);

                foreach (ConfigEntryBase e in entries.Where(e => e.Definition.Section == section))
                    Rows.Add(SettingRow.Build(e, _content));
            }

            LayoutRebuilder.ForceRebuildLayoutImmediate(_content);
            _scroll.verticalNormalizedPosition = 1f;
        }

        private static void ShowEmpty()
        {
            ClearContent();
            _title.text = "Mod Settings";
            UiFactory.Text("Empty", _content, "No plugins with settings found.", 20f, UiFactory.DimText);
        }

        private static void ClearContent()
        {
            Rows.Clear();
            for (int i = _content.childCount - 1; i >= 0; i--) Object.Destroy(_content.GetChild(i).gameObject);
        }

        // ---- Construction -------------------------------------------------------------------------------------

        private static void Build(Menu menu)
        {
            if (_root) Object.Destroy(_root);
            _menu = menu;
            if (!UiFactory.ButtonTemplate && menu.m_settingsButton) UiFactory.ButtonTemplate = menu.m_settingsButton;
            if (!UiFactory.Font && menu.m_settingsButton)
                UiFactory.Font = menu.m_settingsButton.GetComponentInChildren<TMP_Text>(true)?.font;

            // Full-screen blocker so clicks don't reach anything behind.
            Image blocker = UiFactory.Image("ModSettings_Panel", menu.m_root, new Color(0f, 0f, 0f, 0.5f));
            UiFactory.Stretch(blocker.rectTransform);
            _root = blocker.gameObject;

            // Window
            Image window = UiFactory.Image("Window", blocker.transform, new Color(0.13f, 0.11f, 0.09f, 0.97f));
            CopyLook(menu, window);
            RectTransform wrt = window.rectTransform;
            wrt.anchorMin = wrt.anchorMax = wrt.pivot = new Vector2(0.5f, 0.5f);
            wrt.sizeDelta = new Vector2(Width, Height);
            Image border = UiFactory.Image("Border", wrt, new Color(1f, 0.79f, 0.36f, 0.35f));
            UiFactory.Stretch(border.rectTransform, -2, -2, -2, -2);
            border.transform.SetAsFirstSibling();
            border.raycastTarget = false;

            TextMeshProUGUI heading = UiFactory.Text("Heading", wrt, "Mod Settings", 30f, UiFactory.Gold, TextAlignmentOptions.Center);
            heading.rectTransform.anchorMin = new Vector2(0f, 1f);
            heading.rectTransform.anchorMax = new Vector2(1f, 1f);
            heading.rectTransform.pivot = new Vector2(0.5f, 1f);
            heading.rectTransform.anchoredPosition = new Vector2(0f, -14f);
            heading.rectTransform.sizeDelta = new Vector2(0f, 40f);

            // Tabs column
            Image tabsBack = UiFactory.Image("Tabs", wrt, new Color(0f, 0f, 0f, 0.3f));
            RectTransform trt = tabsBack.rectTransform;
            trt.anchorMin = new Vector2(0f, 0f);
            trt.anchorMax = new Vector2(0f, 1f);
            trt.pivot = new Vector2(0f, 1f);
            trt.offsetMin = new Vector2(20f, 80f);
            trt.offsetMax = new Vector2(20f + TabWidth, -66f);
            _tabList = trt;
            var tv = _tabList.gameObject.AddComponent<VerticalLayoutGroup>();
            tv.padding = new RectOffset(8, 8, 8, 8);
            tv.spacing = 6f;
            tv.childAlignment = TextAnchor.UpperCenter;
            tv.childControlWidth = tv.childControlHeight = true;
            tv.childForceExpandWidth = tv.childForceExpandHeight = false;

            // Title of the selected plugin
            _title = UiFactory.Text("Title", wrt, "", 26f, UiFactory.TextColor);
            RectTransform titleRt = _title.rectTransform;
            titleRt.anchorMin = new Vector2(0f, 1f);
            titleRt.anchorMax = new Vector2(1f, 1f);
            titleRt.pivot = new Vector2(0f, 1f);
            titleRt.offsetMin = new Vector2(40f + TabWidth, -102f);
            titleRt.offsetMax = new Vector2(-24f, -66f);

            // Scrolling settings list
            Image view = UiFactory.Image("Scroll", wrt, new Color(0f, 0f, 0f, 0.2f));
            RectTransform vrt = view.rectTransform;
            UiFactory.Stretch(vrt, 40f + TabWidth, 24f, 108f, 80f);
            _scroll = view.gameObject.AddComponent<ScrollRect>();
            _scroll.horizontal = false;
            _scroll.movementType = ScrollRect.MovementType.Clamped;
            _scroll.scrollSensitivity = 40f;

            RectTransform viewport = UiFactory.Rect("Viewport", vrt);
            UiFactory.Stretch(viewport, 0, 18, 0, 0);
            viewport.gameObject.AddComponent<RectMask2D>();
            _scroll.viewport = viewport;

            _content = UiFactory.Rect("Content", viewport);
            _content.anchorMin = new Vector2(0f, 1f);
            _content.anchorMax = new Vector2(1f, 1f);
            _content.pivot = new Vector2(0.5f, 1f);
            _content.sizeDelta = Vector2.zero;
            var cv = _content.gameObject.AddComponent<VerticalLayoutGroup>();
            cv.padding = new RectOffset(14, 14, 6, 14);
            cv.spacing = 4f;
            cv.childControlWidth = cv.childControlHeight = true;
            cv.childForceExpandWidth = true;
            cv.childForceExpandHeight = false;
            _content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            _scroll.content = _content;

            Scrollbar bar = UiFactory.Scrollbar(vrt);
            RectTransform brt = (RectTransform)bar.transform;
            brt.anchorMin = new Vector2(1f, 0f);
            brt.anchorMax = new Vector2(1f, 1f);
            brt.pivot = new Vector2(1f, 0.5f);
            brt.sizeDelta = new Vector2(14f, 0f);
            brt.anchoredPosition = Vector2.zero;
            _scroll.verticalScrollbar = bar;
            _scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;

            // Footer
            TextMeshProUGUI note = UiFactory.Text("Note", wrt, "Changes are saved straight away.", 16f, UiFactory.DimText);
            RectTransform nrt = note.rectTransform;
            nrt.anchorMin = new Vector2(0f, 0f);
            nrt.anchorMax = new Vector2(1f, 0f);
            nrt.pivot = new Vector2(0f, 0f);
            nrt.offsetMin = new Vector2(40f + TabWidth, 24f);
            nrt.offsetMax = new Vector2(-220f, 64f);

            Button close = UiFactory.Button(wrt, "Close");
            RectTransform crt = (RectTransform)close.transform;
            crt.anchorMin = crt.anchorMax = crt.pivot = new Vector2(1f, 0f);
            crt.sizeDelta = new Vector2(180f, 46f);
            crt.anchoredPosition = new Vector2(-24f, 20f);
            close.onClick.AddListener(Close);

            _root.SetActive(false);
        }

        // Reuse the pause menu window's background sprite if it has one, so the panel matches.
        private static void CopyLook(Menu menu, Image target)
        {
            Image source = menu.m_menuDialog ? menu.m_menuDialog.GetComponent<Image>() : null;
            if (!source || !source.sprite) return;
            target.sprite = source.sprite;
            target.type = source.type;
            target.pixelsPerUnitMultiplier = source.pixelsPerUnitMultiplier;
            target.color = new Color(source.color.r, source.color.g, source.color.b, Mathf.Max(0.95f, source.color.a));
        }
    }
}
