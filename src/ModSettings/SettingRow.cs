// SHARED FILE: edit the canonical copy in D:\Work\Valheim\Shared\ModSettings, then run Shared\sync.ps1.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using BepInEx;
using BepInEx.Configuration;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GlutoModSettings
{
    // One config entry: caption + description on the left, a control matching its type on the right, then Reset.
    internal class SettingRow
    {
        private const float ControlWidth = 330f;

        private static readonly KeyCode[] Modifiers =
        {
            KeyCode.LeftControl, KeyCode.RightControl, KeyCode.LeftShift, KeyCode.RightShift,
            KeyCode.LeftAlt, KeyCode.RightAlt,
        };

        internal static SettingRow Capturing; // the key row waiting for a key press, if any

        private readonly ConfigEntryBase _entry;
        private Action _refresh;
        private bool _updating;

        // Key capture
        private Button _keyButton;
        private bool _sawNonModifier;

        private SettingRow(ConfigEntryBase entry) => _entry = entry;

        internal static SettingRow Build(ConfigEntryBase entry, Transform parent)
        {
            var row = new SettingRow(entry);
            row.BuildUi(parent);
            return row;
        }

        internal void Refresh()
        {
            _updating = true;
            try { _refresh?.Invoke(); }
            finally { _updating = false; }
        }

        private void Set(object value)
        {
            if (_updating) return;
            try
            {
                _entry.BoxedValue = value;
            }
            catch (Exception e)
            {
                Shared.Warn($"Could not set {_entry.Definition}: {e.Message}");
            }
            Refresh(); // shows the value after the entry's AcceptableValues clamp
        }

        private void BuildUi(Transform parent)
        {
            RectTransform row = UiFactory.Rect("Row_" + _entry.Definition.Key, parent);
            var h = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.spacing = 14f;
            h.padding = new RectOffset(6, 6, 6, 6);
            h.childAlignment = TextAnchor.MiddleLeft;
            h.childControlWidth = h.childControlHeight = true;
            h.childForceExpandWidth = h.childForceExpandHeight = false;

            // Caption + description
            RectTransform left = UiFactory.Rect("Label", row);
            var v = left.gameObject.AddComponent<VerticalLayoutGroup>();
            v.spacing = 2f;
            v.childControlWidth = v.childControlHeight = true;
            v.childForceExpandWidth = true;
            v.childForceExpandHeight = false;
            UiFactory.Layout(left, 200, 300, -1, -1, 1);
            UiFactory.Text("Name", left, Pretty(_entry.Definition.Key), 20f, UiFactory.TextColor);
            string desc = _entry.Description?.Description;
            if (!string.IsNullOrEmpty(desc))
                UiFactory.Text("Description", left, desc, 15f, UiFactory.DimText);

            // Control
            RectTransform control = UiFactory.Rect("Control", row);
            var ch = control.gameObject.AddComponent<HorizontalLayoutGroup>();
            ch.spacing = 8f;
            ch.childAlignment = TextAnchor.MiddleLeft;
            ch.childControlWidth = ch.childControlHeight = true;
            ch.childForceExpandWidth = ch.childForceExpandHeight = false;
            UiFactory.Layout(control, ControlWidth, ControlWidth, 34, 34);
            BuildControl(control);

            // Reset
            Button reset = UiFactory.Button(row, "Reset", 15f);
            UiFactory.Layout(reset, 80, 80, 32, 32);
            reset.onClick.AddListener(() =>
            {
                CancelCapture();
                Set(_entry.DefaultValue);
            });

            Refresh();
        }

        private void BuildControl(Transform parent)
        {
            Type type = _entry.SettingType;
            AcceptableValueBase acceptable = _entry.Description?.AcceptableValues;

            if (type == typeof(bool))
            {
                Toggle toggle = UiFactory.Toggle(parent);
                toggle.onValueChanged.AddListener(on => Set(on));
                _refresh = () => toggle.isOn = (bool)_entry.BoxedValue;
                return;
            }

            if ((type == typeof(float) || type == typeof(int)) && TryGetRange(acceptable, out float min, out float max))
            {
                BuildSlider(parent, type == typeof(int), min, max);
                return;
            }

            object[] choices = Choices(type, acceptable);
            if (choices != null && choices.Length > 0)
            {
                Button b = UiFactory.Button(parent, "", 18f);
                UiFactory.Layout(b, 120, ControlWidth, 32, 32, 1);
                b.onClick.AddListener(() =>
                {
                    int i = Array.FindIndex(choices, c => Equals(c, _entry.BoxedValue));
                    Set(choices[(i + 1) % choices.Length]);
                });
                _refresh = () => UiFactory.SetButtonText(b, "< " + Pretty(_entry.BoxedValue?.ToString() ?? "") + " >");
                return;
            }

            if (type == typeof(KeyCode) || type == typeof(KeyboardShortcut))
            {
                _keyButton = UiFactory.Button(parent, "", 18f);
                UiFactory.Layout(_keyButton, 120, ControlWidth, 32, 32, 1);
                _keyButton.onClick.AddListener(() =>
                {
                    if (Capturing == this) { CancelCapture(); return; }
                    Capturing?.CancelCapture();
                    Capturing = this;
                    _sawNonModifier = false;
                    UiFactory.SetButtonText(_keyButton, "Press a key... (Esc cancels)");
                });
                _refresh = () => UiFactory.SetButtonText(_keyButton, KeyText(_entry.BoxedValue));
                return;
            }

            // Anything else (Color, string, unbounded numbers, ...) edits the value as it's written in the .cfg.
            Image swatch = null;
            if (type == typeof(Color))
            {
                swatch = UiFactory.Image("Swatch", parent, Color.white);
                UiFactory.Layout(swatch, 32, 32, 32, 32);
            }
            TMP_InputField input = UiFactory.Input(parent);
            input.onEndEdit.AddListener(text =>
            {
                if (_updating) return;
                try { _entry.SetSerializedValue(text.Trim()); }
                catch (Exception e) { Shared.Warn($"Invalid value for {_entry.Definition}: {e.Message}"); }
                Refresh();
            });
            _refresh = () =>
            {
                input.SetTextWithoutNotify(_entry.GetSerializedValue());
                if (swatch) swatch.color = (Color)_entry.BoxedValue;
            };
        }

        private void BuildSlider(Transform parent, bool whole, float min, float max)
        {
            Slider slider = UiFactory.Slider(parent);
            TextMeshProUGUI label = UiFactory.Text("Value", parent, "", 18f, UiFactory.TextColor, TextAlignmentOptions.MidlineRight);
            label.textWrappingMode = TextWrappingModes.NoWrap;
            UiFactory.Layout(label, 70, 70, 30, 30);

            float span = max - min;
            float step = whole || span >= 50f ? 1f : span >= 5f ? 0.1f : 0.01f;
            string format = step >= 1f ? "0" : step >= 0.1f ? "0.0" : "0.00";

            slider.minValue = min;
            slider.maxValue = max;
            slider.wholeNumbers = whole;
            slider.onValueChanged.AddListener(raw =>
            {
                float v = Mathf.Clamp(Mathf.Round(raw / step) * step, min, max);
                label.text = v.ToString(format, CultureInfo.InvariantCulture);
                if (whole) Set((int)Mathf.Round(v));
                else Set(v);
            });
            _refresh = () =>
            {
                float v = Convert.ToSingle(_entry.BoxedValue, CultureInfo.InvariantCulture);
                slider.SetValueWithoutNotify(v);
                label.text = v.ToString(format, CultureInfo.InvariantCulture);
            };
        }

        // ---- Key capture (driven from SettingsPanel.Tick while this row is Capturing) -------------------------

        internal void CancelCapture()
        {
            if (Capturing != this) return;
            Capturing = null;
            Refresh();
        }

        internal void TickCapture()
        {
            IInputSystem input = UnityInput.Current;
            bool shortcut = _entry.SettingType == typeof(KeyboardShortcut);

            foreach (KeyCode key in input.SupportedKeyCodes)
            {
                if (key == KeyCode.None || key == KeyCode.Mouse0 || key == KeyCode.Mouse1) continue;
                bool isModifier = Array.IndexOf(Modifiers, key) >= 0;

                if (input.GetKeyDown(key))
                {
                    if (key == KeyCode.Escape) { CancelCapture(); return; }
                    if (!shortcut) { Finish(key, null); return; }
                    if (isModifier) continue; // wait: it may be held for a combo
                    _sawNonModifier = true;
                    Finish(key, Modifiers.Where(m => m != key && input.GetKey(m)).ToArray());
                    return;
                }

                // A modifier pressed and released on its own binds by itself.
                if (shortcut && isModifier && !_sawNonModifier && input.GetKeyUp(key))
                {
                    Finish(key, Modifiers.Where(m => m != key && input.GetKey(m)).ToArray());
                    return;
                }
            }
        }

        private void Finish(KeyCode key, KeyCode[] modifiers)
        {
            Capturing = null;
            if (_entry.SettingType == typeof(KeyboardShortcut)) Set(new KeyboardShortcut(key, modifiers ?? new KeyCode[0]));
            else Set(key);
        }

        // ---- Helpers ----------------------------------------------------------------------------------------

        private static string KeyText(object value)
        {
            if (value is KeyboardShortcut ks)
                return ks.MainKey == KeyCode.None ? "(none)" : string.Join(" + ", ks.Modifiers.Concat(new[] { ks.MainKey }).Select(k => k.ToString()));
            if (value is KeyCode kc) return kc == KeyCode.None ? "(none)" : kc.ToString();
            return value?.ToString() ?? "";
        }

        private static bool TryGetRange(AcceptableValueBase acceptable, out float min, out float max)
        {
            min = max = 0f;
            switch (acceptable)
            {
                case AcceptableValueRange<float> f: min = f.MinValue; max = f.MaxValue; return true;
                case AcceptableValueRange<int> i: min = i.MinValue; max = i.MaxValue; return true;
                default: return false;
            }
        }

        private static object[] Choices(Type type, AcceptableValueBase acceptable)
        {
            if (acceptable != null)
            {
                var prop = acceptable.GetType().GetProperty("AcceptableValues");
                if (prop?.GetValue(acceptable) is Array arr) return arr.Cast<object>().ToArray();
            }
            if (type.IsEnum && !type.IsDefined(typeof(FlagsAttribute), false))
                return Enum.GetValues(type).Cast<object>().ToArray();
            return null;
        }

        // "HeightAboveSpear" -> "Height Above Spear"
        internal static string Pretty(string key)
        {
            string s = Regex.Replace(key, "(?<=[a-z0-9])(?=[A-Z])|(?<=[A-Z])(?=[A-Z][a-z])", " ");
            return s.Replace('_', ' ');
        }
    }
}
