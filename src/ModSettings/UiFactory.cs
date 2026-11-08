// SHARED FILE: edit the canonical copy in D:\Work\Valheim\Shared\ModSettings, then run Shared\sync.ps1.
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GlutoModSettings
{
    // Small helpers for building uGUI from code. Buttons are cloned from the vanilla pause-menu button so they look
    // native; everything else is plain Images in the game's colours.
    internal static class UiFactory
    {
        internal static TMP_FontAsset Font;
        internal static Button ButtonTemplate;

        internal static readonly Color Gold = new Color(1f, 0.79f, 0.36f, 1f);
        internal static readonly Color TextColor = new Color(0.92f, 0.9f, 0.85f, 1f);
        internal static readonly Color DimText = new Color(0.68f, 0.65f, 0.6f, 1f);
        internal static readonly Color FieldBack = new Color(0f, 0f, 0f, 0.55f);
        internal static readonly Color Track = new Color(0.25f, 0.22f, 0.18f, 1f);

        internal static RectTransform Rect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = parent.gameObject.layer;
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            return rt;
        }

        internal static void Stretch(RectTransform rt, float left = 0, float right = 0, float top = 0, float bottom = 0)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(left, bottom);
            rt.offsetMax = new Vector2(-right, -top);
        }

        internal static Image Image(string name, Transform parent, Color color)
        {
            RectTransform rt = Rect(name, parent);
            var img = rt.gameObject.AddComponent<Image>();
            img.color = color;
            return img;
        }

        internal static TextMeshProUGUI Text(string name, Transform parent, string text, float size, Color color,
            TextAlignmentOptions align = TextAlignmentOptions.MidlineLeft)
        {
            RectTransform rt = Rect(name, parent);
            var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
            if (Font) t.font = Font;
            t.text = text;
            t.fontSize = size;
            t.color = color;
            t.alignment = align;
            t.raycastTarget = false;
            t.textWrappingMode = TextWrappingModes.Normal;
            t.overflowMode = TextOverflowModes.Overflow;
            return t;
        }

        internal static LayoutElement Layout(Component c, float minW = -1, float prefW = -1, float minH = -1, float prefH = -1,
            float flexW = -1, float flexH = -1)
        {
            var le = c.GetComponent<LayoutElement>() ?? c.gameObject.AddComponent<LayoutElement>();
            le.minWidth = minW;
            le.preferredWidth = prefW;
            le.minHeight = minH;
            le.preferredHeight = prefH;
            le.flexibleWidth = flexW;
            le.flexibleHeight = flexH;
            return le;
        }

        // Clone of a vanilla button: drops localisation, tooltips and the persistent onClick listeners.
        internal static Button CloneButton(Button template, Transform parent, string label)
        {
            if (!ButtonTemplate) ButtonTemplate = template;
            GameObject go = Object.Instantiate(template.gameObject, parent, false);
            go.SetActive(true);
            foreach (Localize loc in go.GetComponentsInChildren<Localize>(true)) Object.DestroyImmediate(loc);
            foreach (UITooltip tip in go.GetComponentsInChildren<UITooltip>(true)) Object.DestroyImmediate(tip);
            foreach (UIGamePad pad in go.GetComponentsInChildren<UIGamePad>(true)) Object.DestroyImmediate(pad);
            var button = go.GetComponent<Button>();
            button.onClick = new Button.ButtonClickedEvent();
            button.interactable = true;
            Navigation nav = button.navigation;
            nav.mode = Navigation.Mode.Automatic;
            button.navigation = nav;
            SetButtonText(button, label);
            return button;
        }

        // A button for the settings window: cloned from the pause menu button when we have it, plain otherwise.
        internal static Button Button(Transform parent, string label, float fontSize = 0f)
        {
            Button b;
            if (ButtonTemplate)
            {
                b = CloneButton(ButtonTemplate, parent, label);
                var rt = (RectTransform)b.transform;
                rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            }
            else
            {
                Image img = Image("Button", parent, new Color(0.3f, 0.26f, 0.2f, 1f));
                b = img.gameObject.AddComponent<Button>();
                b.targetGraphic = img;
                TextMeshProUGUI t = Text("Text", img.transform, label, 18f, TextColor, TextAlignmentOptions.Center);
                Stretch(t.rectTransform);
            }
            if (fontSize > 0f)
            {
                TMP_Text t = b.GetComponentInChildren<TMP_Text>(true);
                if (t) { t.enableAutoSizing = false; t.fontSize = fontSize; }
            }
            return b;
        }

        internal static void SetButtonText(Button button, string label)
        {
            TMP_Text t = button.GetComponentInChildren<TMP_Text>(true);
            if (t) t.text = label;
        }

        internal static Toggle Toggle(Transform parent)
        {
            Image back = Image("Toggle", parent, FieldBack);
            Layout(back, 30, 30, 30, 30);
            Image check = Image("Check", back.transform, Gold);
            Stretch(check.rectTransform, 6, 6, 6, 6);
            var toggle = back.gameObject.AddComponent<Toggle>();
            toggle.targetGraphic = back;
            toggle.graphic = check;
            return toggle;
        }

        internal static Slider Slider(Transform parent)
        {
            RectTransform root = Rect("Slider", parent);
            Layout(root, 120, 200, 22, 22, 1);

            Image back = Image("Background", root, Track);
            Stretch(back.rectTransform, 0, 0, 7, 7);

            RectTransform fillArea = Rect("Fill Area", root);
            Stretch(fillArea, 0, 8, 7, 7);
            Image fill = Image("Fill", fillArea, Gold * new Color(1f, 1f, 1f, 0.85f));
            fill.rectTransform.sizeDelta = new Vector2(8f, 0f);

            RectTransform handleArea = Rect("Handle Area", root);
            Stretch(handleArea, 7, 7);
            Image handle = Image("Handle", handleArea, new Color(0.95f, 0.9f, 0.8f, 1f));
            handle.rectTransform.sizeDelta = new Vector2(12f, 0f);

            var slider = root.gameObject.AddComponent<Slider>();
            slider.fillRect = fill.rectTransform;
            slider.handleRect = handle.rectTransform;
            slider.targetGraphic = handle;
            slider.direction = UnityEngine.UI.Slider.Direction.LeftToRight;
            return slider;
        }

        internal static TMP_InputField Input(Transform parent, float fontSize = 18f)
        {
            Image back = Image("Input", parent, FieldBack);
            back.gameObject.SetActive(false); // TMP_InputField.OnEnable needs its references set first
            Layout(back, 80, 200, 32, 32, 1);

            var area = Rect("Text Area", back.transform);
            Stretch(area, 8, 8, 2, 2);
            area.gameObject.AddComponent<RectMask2D>();

            TextMeshProUGUI text = Text("Text", area, "", fontSize, TextColor);
            text.textWrappingMode = TextWrappingModes.NoWrap;
            Stretch(text.rectTransform);

            var input = back.gameObject.AddComponent<TMP_InputField>();
            input.targetGraphic = back;
            input.textViewport = area;
            input.textComponent = text;
            input.fontAsset = text.font;
            input.pointSize = fontSize;
            input.lineType = TMP_InputField.LineType.SingleLine;
            input.caretColor = Gold;
            input.customCaretColor = true;
            input.selectionColor = new Color(1f, 0.79f, 0.36f, 0.35f);
            back.gameObject.SetActive(true);
            return input;
        }

        internal static Scrollbar Scrollbar(Transform parent)
        {
            Image back = Image("Scrollbar", parent, new Color(0f, 0f, 0f, 0.35f));
            RectTransform area = Rect("Sliding Area", back.transform);
            Stretch(area, 2, 2, 2, 2);
            Image handle = Image("Handle", area, new Color(0.75f, 0.66f, 0.5f, 0.9f));
            Stretch(handle.rectTransform);
            var bar = back.gameObject.AddComponent<Scrollbar>();
            bar.handleRect = handle.rectTransform;
            bar.targetGraphic = handle;
            bar.direction = UnityEngine.UI.Scrollbar.Direction.BottomToTop;
            return bar;
        }
    }
}
