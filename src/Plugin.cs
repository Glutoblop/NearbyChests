using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace NearbyChests
{
    // Client-only. While the inventory is open with no chest open, a "Nearby chests" panel fills the empty
    // container slot under your inventory and lists everything in the chests around you. Click an item to take
    // it, or use the grab buttons to take what the selected recipe still needs.
    //
    // Taking from a chest uses the same ownership hand-off vanilla "Take all" uses (ZNetView.ClaimOwnership, then
    // edit the inventory, which saves to the ZDO). Chests another player has open, private chests that aren't
    // yours, and chests inside a ward you can't access are skipped.
    [BepInPlugin(Guid, Name, Version)]
    [BepInProcess("valheim.exe")]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "jordan.valheim.nearbychests";
        public const string Name = "Easy Crafting";
        public const string Version = "0.1.8";

        internal static Plugin Instance;
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<float> Range;
        internal static ConfigEntry<KeyboardShortcut> ToggleKey;
        internal static ConfigEntry<bool> IncludeTombstones;
        internal static ConfigEntry<bool> IncludeCartsAndShips;
        internal static ConfigEntry<bool> RequirementHints;
        internal static ConfigEntry<float> RefreshInterval;
        internal static ConfigEntry<bool> ClearSearchOnClose;
        internal static ConfigEntry<float> SearchOffsetX;
        internal static ConfigEntry<float> SearchOffsetY;
        internal static ConfigEntry<float> PanelOffsetX;
        internal static ConfigEntry<float> PanelOffsetY;
        internal static ConfigEntry<bool> ClickIngredientToTake;
        internal static ConfigEntry<bool> IncludeBuildPieces;

        private Harmony _harmony;

        private void Awake()
        {
            Instance = this;

            Enabled = Config.Bind("General", "Enabled", true, "Turn the whole plugin on or off.");
            Range = Config.Bind("General", "Range", 30f,
                new ConfigDescription("Chests within this many metres of you are included.",
                    new AcceptableValueRange<float>(1f, 100f)));
            IncludeTombstones = Config.Bind("General", "IncludeTombstones", false,
                "Include tombstones (your own and other players') in the nearby pool.");
            IncludeCartsAndShips = Config.Bind("General", "IncludeCartsAndShips", true,
                "Include carts and ship storage in the nearby pool.");

            ToggleKey = Config.Bind("Panel", "ToggleKey", new KeyboardShortcut(KeyCode.None),
                "Key that switches between the Nearby tab and the Craft tab while the inventory is open (None = no hotkey).");
            RefreshInterval = Config.Bind("Panel", "RefreshInterval", 0.5f,
                new ConfigDescription("Seconds between rescans of nearby chests while the inventory is open.",
                    new AcceptableValueRange<float>(0.1f, 5f)));

            ClearSearchOnClose = Config.Bind("Panel", "ClearSearchOnClose", true,
                "Clear the Nearby chests search box when the inventory closes.");
            SearchOffsetX = Config.Bind("Panel", "SearchOffsetX", -12f,
                "Horizontal position of the search box, from the panel's top-right corner (negative = left).");
            SearchOffsetY = Config.Bind("Panel", "SearchOffsetY", -8f,
                "Vertical position of the search box, from the panel's top-right corner (negative = down).");

            PanelOffsetX = Config.Bind("Panel", "PanelOffsetX", 0f,
                "Nudge the Nearby chests panel horizontally from its spot left of the crafting panel (pixels, + = right).");
            PanelOffsetY = Config.Bind("Panel", "PanelOffsetY", 0f,
                "Nudge the Nearby chests panel vertically from its spot left of the crafting panel (pixels, + = up).");

            RequirementHints = Config.Bind("Crafting", "RequirementHints", true,
                "In the crafting panel, show yellow ingredient amounts when nearby chests can cover what you're missing, " +
                "and add the nearby count to the ingredient tooltip.");
            ClickIngredientToTake = Config.Bind("Crafting", "ClickIngredientToTake", false,
                "Clicking an ingredient in the crafting panel takes the missing amount from nearby chests automatically.");
            IncludeBuildPieces = Config.Bind("Crafting", "IncludeBuildPieces", true,
                "List the build pieces you've unlocked (Hammer, Hoe, Cultivator...) in the Nearby tab alongside crafting recipes.");

            _harmony = new Harmony(Guid);
            _harmony.PatchAll(typeof(Plugin).Assembly);
        }

        private void OnDestroy()
        {
            _harmony?.UnpatchSelf();
            CraftQueue.Destroy(InventoryGui.instance);
            NearbyPanel.Destroy();
        }

        internal static void Log(string msg) => Instance?.Logger.LogInfo(msg);
        internal static void Warn(string msg) => Instance?.Logger.LogWarning(msg);
    }
}
