// SHARED FILE: edit the canonical copy in D:\Work\Valheim\Shared\ModSettings, then run Shared\sync.ps1.
using System;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GlutoModSettings
{
    // Menu is the in-game pause menu (Esc). It's created with the game scene, so the button is added on Start.
    [HarmonyPatch]
    internal static class MenuPatches
    {
        private const string ButtonName = "GlutoModSettings_Button";

        [HarmonyPostfix]
        [HarmonyPatch(typeof(Menu), "Start")]
        private static void StartPostfix(Menu __instance)
        {
            try
            {
                if (Shared.IsOwner) AddButton(__instance);
            }
            catch (Exception e)
            {
                Shared.Warn($"Could not add the Mod Settings button to the pause menu: {e}");
            }
        }

        // Opening the pause menu always starts on the normal button list.
        [HarmonyPostfix]
        [HarmonyPatch(typeof(Menu), nameof(Menu.Show))]
        private static void ShowPostfix() { if (Shared.IsOwner) SettingsPanel.Close(); }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(Menu), nameof(Menu.Hide))]
        private static void HidePostfix() { if (Shared.IsOwner) SettingsPanel.Close(); }

        // While our window is open it owns Esc / B, so the pause menu must not react to them.
        [HarmonyPrefix]
        [HarmonyPatch(typeof(Menu), "Update")]
        private static bool UpdatePrefix(Menu __instance)
        {
            if (!Shared.IsOwner || !SettingsPanel.IsOpen) return true;
            if (Game.instance != null && Game.instance.IsShuttingDown()) return true;
            if (!__instance.m_root.gameObject.activeSelf) { SettingsPanel.Close(); return true; }
            SettingsPanel.Tick();
            return false;
        }

        private static void AddButton(Menu menu)
        {
            Button template = menu.m_settingsButton;
            if (!template || template.transform.parent.Find(ButtonName)) return;

            Button button = UiFactory.CloneButton(template, template.transform.parent, "Mod Settings");
            button.name = ButtonName;
            button.transform.SetSiblingIndex(template.transform.GetSiblingIndex() + 1);
            button.onClick.AddListener(() => SettingsPanel.Open(menu));

            UiFactory.Font = template.GetComponentInChildren<TMP_Text>(true)?.font;
            if (menu.menuEntriesParent) LayoutRebuilder.ForceRebuildLayoutImmediate(menu.menuEntriesParent);
        }
    }
}
