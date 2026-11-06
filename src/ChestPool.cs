using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace NearbyChests
{
    /// <summary>One line in the Nearby panel: a stackable item type (all stacks summed) or a single unstackable item.</summary>
    internal sealed class ItemGroup
    {
        public ItemDrop.ItemData Sample;   // a real item from one of the chests (never modified)
        public int Total;
        public bool Stackable => Sample.m_shared.m_maxStackSize > 1;

        public bool Matches(ItemDrop.ItemData item) =>
            Stackable
                ? item.m_shared.m_name == Sample.m_shared.m_name && item.m_quality == Sample.m_quality
                : ReferenceEquals(item, Sample);
    }

    /// <summary>Finds accessible chests near the player and moves items out of them.</summary>
    internal static class ChestPool
    {
        private static readonly HashSet<Container> _all = new HashSet<Container>();

        /// <summary>Accessible chests in range, nearest first. Rebuilt by <see cref="Scan"/>.</summary>
        public static readonly List<Container> Chests = new List<Container>();
        public static readonly List<ItemGroup> Groups = new List<ItemGroup>();
        /// <summary>Per item name: amount that counts for crafting (world level matches).</summary>
        private static readonly Dictionary<string, int> _craftable = new Dictionary<string, int>();

        public static void Register(Container c) { if (c != null) _all.Add(c); }

        public static int CountForCrafting(string name) => _craftable.TryGetValue(name, out int n) ? n : 0;

        public static void Scan()
        {
            Chests.Clear();
            Groups.Clear();
            _craftable.Clear();

            var player = Player.m_localPlayer;
            if (player == null) return;
            Vector3 pos = player.transform.position;
            float range = Plugin.Range.Value;
            long playerId = PlayerId();

            _all.RemoveWhere(c => c == null);
            foreach (var c in _all
                         .Where(c => c.GetInventory() != null && Vector3.Distance(c.transform.position, pos) <= range)
                         .OrderBy(c => Vector3.Distance(c.transform.position, pos)))
            {
                if (IsListed(c, playerId)) Chests.Add(c);
            }

            var stackGroups = new Dictionary<(string, int), ItemGroup>();
            foreach (var c in Chests)
            {
                foreach (var item in c.GetInventory().GetAllItems())
                {
                    if (item.m_worldLevel >= Game.m_worldLevel)
                    {
                        _craftable.TryGetValue(item.m_shared.m_name, out int n);
                        _craftable[item.m_shared.m_name] = n + item.m_stack;
                    }

                    if (item.m_shared.m_maxStackSize > 1)
                    {
                        var key = (item.m_shared.m_name, item.m_quality);
                        if (!stackGroups.TryGetValue(key, out var g))
                        {
                            g = new ItemGroup { Sample = item };
                            stackGroups[key] = g;
                            Groups.Add(g);
                        }
                        g.Total += item.m_stack;
                    }
                    else
                    {
                        Groups.Add(new ItemGroup { Sample = item, Total = 1 });
                    }
                }
            }

            var loc = Localization.instance;
            Groups.Sort((a, b) =>
            {
                int t = ((int)a.Sample.m_shared.m_itemType).CompareTo((int)b.Sample.m_shared.m_itemType);
                if (t != 0) return t;
                int n = string.Compare(loc.Localize(a.Sample.m_shared.m_name), loc.Localize(b.Sample.m_shared.m_name),
                    StringComparison.CurrentCultureIgnoreCase);
                return n != 0 ? n : b.Sample.m_quality.CompareTo(a.Sample.m_quality);
            });
        }

        /// <summary>
        /// Moves up to <paramref name="amount"/> matching items from nearby chests (nearest first) into the player's
        /// inventory. Returns how many were moved; stops early if the inventory is full.
        /// </summary>
        public static int Take(Func<ItemDrop.ItemData, bool> match, int amount, out bool inventoryFull)
        {
            inventoryFull = false;
            var player = Player.m_localPlayer;
            if (player == null || amount <= 0) return 0;
            var to = player.GetInventory();
            long playerId = PlayerId();
            int moved = 0;

            foreach (var c in Chests.ToList())
            {
                if (moved >= amount) break;
                if (c == null || c.GetInventory() == null) continue;
                if (!c.GetInventory().GetAllItems().Any(match)) continue;
                if (!Acquire(c, playerId)) continue;

                var from = c.GetInventory();
                foreach (var item in from.GetAllItems().Where(match).ToList())
                {
                    int wanted = Mathf.Min(item.m_stack, amount - moved);
                    int n = Move(from, item, amount - moved, to);
                    moved += n;
                    if (n < wanted) { inventoryFull = true; break; }
                    if (moved >= amount) break;
                }
                if (inventoryFull) break;
            }
            return moved;
        }

        /// <summary>Should this chest appear in the pool at all (read-only checks, no ownership changes).</summary>
        private static bool IsListed(Container c, long playerId)
        {
            var nview = c.m_nview;
            if (nview == null || !nview.IsValid()) return false;
            if (!Plugin.IncludeTombstones.Value && c.GetComponent<TombStone>() != null) return false;
            if (!Plugin.IncludeCartsAndShips.Value && (c.m_wagon != null || c.GetComponentInParent<Ship>() != null)) return false;
            if (!c.CheckAccess(playerId)) return false;
            if (c.m_checkGuardStone && !PrivateArea.CheckAccess(c.transform.position, 0f, flash: false)) return false;
            return true;
        }

        /// <summary>Make this client the chest's owner so our edits stick, unless someone else is using it.</summary>
        private static bool Acquire(Container c, long playerId)
        {
            if (!IsListed(c, playerId)) return false;
            var gui = InventoryGui.instance;
            if (gui != null && gui.m_currentContainer == c) return true; // we have it open ourselves

            var nview = c.m_nview;
            bool inUse = nview.IsOwner() ? c.IsInUse() : nview.GetZDO().GetInt(ZDOVars.s_inUse) == 1;
            if (inUse) return false;
            if (c.m_wagon != null && c.m_wagon.InUse()) return false;

            if (!nview.IsOwner()) nview.ClaimOwnership();
            c.Load(); // pick up any change made just before we claimed it
            return nview.IsOwner();
        }

        /// <summary>Moves up to <paramref name="want"/> of <paramref name="item"/> into <paramref name="to"/>, limited by free space.</summary>
        private static int Move(Inventory from, ItemDrop.ItemData item, int want, Inventory to)
        {
            int n = Mathf.Min(want, item.m_stack, Capacity(to, item));
            if (n <= 0) return 0;

            var copy = item.Clone();
            copy.m_stack = n;
            copy.m_equipped = false;
            if (!to.AddItem(copy)) return 0;
            from.RemoveItem(item, n);
            return n;
        }

        private static int Capacity(Inventory inv, ItemDrop.ItemData item)
        {
            int max = item.m_shared.m_maxStackSize;
            int free = inv.GetEmptySlots() * max;
            if (max > 1)
            {
                foreach (var it in inv.GetAllItems())
                    if (it.m_shared.m_name == item.m_shared.m_name && it.m_quality == item.m_quality &&
                        it.m_worldLevel == item.m_worldLevel && it.m_stack < max)
                        free += max - it.m_stack;
            }
            return free;
        }

        private static long PlayerId() => Game.instance != null ? Game.instance.GetPlayerProfile().GetPlayerID() : 0L;
    }
}
