using System.Collections.Generic;
using UnityEngine;

namespace NearbyChests
{
    /// <summary>
    /// Build pieces (Hammer, Hoe, Cultivator, ...) for the Nearby tab's recipe list. The crafting list only takes
    /// Recipes, so each known piece gets a stand-in Recipe whose "item" is an inactive placeholder carrying the
    /// piece's name, icon and description, and whose requirements are the piece's own. These recipes only ever
    /// appear in the Nearby tab, and crafting them is blocked (see Patches).
    /// </summary>
    internal static class BuildPieces
    {
        private sealed class Info
        {
            public Piece Piece;
            public string Tool;   // localization key of the tool that builds it
            public Recipe Recipe;
        }

        private static GameObject _holder;
        private static readonly Dictionary<string, Info> _byKey = new Dictionary<string, Info>();
        private static readonly Dictionary<Recipe, Info> _byRecipe = new Dictionary<Recipe, Info>();
        private static readonly Dictionary<ItemDrop.ItemData.SharedData, Info> _byShared = new Dictionary<ItemDrop.ItemData.SharedData, Info>();

        public static bool IsBuildPiece(Recipe r) => r != null && _byRecipe.ContainsKey(r);

        public static bool TryGet(Recipe r, out string tool)
        {
            tool = null;
            if (r == null || !_byRecipe.TryGetValue(r, out var info)) return false;
            tool = info.Tool;
            return true;
        }

        /// <summary>Tooltip text for a placeholder item, or null if it isn't one.</summary>
        public static string Tooltip(ItemDrop.ItemData item)
        {
            if (item?.m_shared == null || !_byShared.TryGetValue(item.m_shared, out var info)) return null;
            var sb = new System.Text.StringBuilder();
            if (!string.IsNullOrEmpty(info.Piece.m_description)) sb.Append(info.Piece.m_description).Append("\n\n");
            sb.Append("<color=orange>Build piece: place it with the ").Append(info.Tool).Append("</color>");
            if (info.Piece.m_craftingStation != null)
                sb.Append("\nNeeds a nearby <color=orange>").Append(info.Piece.m_craftingStation.m_name).Append("</color>");
            return sb.ToString();
        }

        /// <summary>Adds a stand-in recipe for every build piece the player has unlocked.</summary>
        public static void AddKnown(Player player, List<Recipe> available)
        {
            if (ObjectDB.instance == null) return;
            bool all = player.m_noPlacementCost || (ZoneSystem.instance != null && ZoneSystem.instance.GetGlobalKey(GlobalKeys.AllPiecesUnlocked));

            foreach (var go in ObjectDB.instance.m_items)
            {
                var tool = go != null ? go.GetComponent<ItemDrop>() : null;
                var table = tool?.m_itemData?.m_shared?.m_buildPieces;
                if (table == null) continue;

                foreach (var pieceGo in table.m_pieces)
                {
                    var piece = pieceGo != null ? pieceGo.GetComponent<Piece>() : null;
                    if (piece == null || !piece.m_enabled || piece.m_icon == null) continue;
                    if (piece.m_resources == null || piece.m_resources.Length == 0) continue;
                    if (piece.m_dlc.Length > 0 && !DLCMan.instance.IsDLCInstalled(piece.m_dlc)) continue;
                    if (!all && !player.IsRecipeKnown(piece.m_name)) continue;

                    var r = Get(piece, tool.m_itemData.m_shared.m_name);
                    if (r != null && !available.Contains(r)) available.Add(r);
                }
            }
        }

        private static Recipe Get(Piece piece, string toolName)
        {
            string key = piece.gameObject.name;
            if (_byKey.TryGetValue(key, out var info) && info.Recipe != null && info.Recipe.m_item != null)
                return info.Recipe;

            if (_holder == null)
            {
                _holder = new GameObject("NearbyChests_BuildPieceItems");
                _holder.SetActive(false); // children never wake up: no Awake/ZNetView, never in the world
                Object.DontDestroyOnLoad(_holder);
            }

            var itemGo = new GameObject(piece.gameObject.name);
            itemGo.transform.SetParent(_holder.transform, false);
            var drop = itemGo.AddComponent<ItemDrop>();
            var shared = new ItemDrop.ItemData.SharedData
            {
                m_name = piece.m_name,
                m_description = piece.m_description,
                m_icons = new[] { piece.m_icon },
                m_maxQuality = 1,
                m_maxStackSize = 1,
                m_weight = 0f,
                m_teleportable = true,
                m_itemType = ItemDrop.ItemData.ItemType.Misc,
            };
            drop.m_itemData = new ItemDrop.ItemData { m_shared = shared, m_stack = 1, m_quality = 1, m_dropPrefab = itemGo };

            var recipe = ScriptableObject.CreateInstance<Recipe>();
            recipe.name = "NearbyChests_" + piece.gameObject.name;
            recipe.m_item = drop;
            recipe.m_amount = 1;
            recipe.m_enabled = true;
            recipe.m_craftingStation = piece.m_craftingStation;
            recipe.m_minStationLevel = 1;
            recipe.m_resources = piece.m_resources;

            info = new Info { Piece = piece, Tool = toolName, Recipe = recipe };
            _byKey[key] = info;
            _byRecipe[recipe] = info;
            _byShared[shared] = info;
            return recipe;
        }
    }
}
