using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace NearbyChests
{
    /// <summary>One item type in the pool: a stackable item (all stacks summed) or a single unstackable item.</summary>
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

    /// <summary>Finds accessible chests near the player and moves items between them and an inventory.</summary>
    internal static class ChestPool
    {
        internal readonly struct GrabMove
        {
            public readonly Container Chest;
            public readonly ItemDrop.ItemData Item;
            public readonly int Amount;

            public GrabMove(Container chest, ItemDrop.ItemData item, int amount)
            {
                Chest = chest;
                Item = item;
                Amount = amount;
            }
        }

        private static readonly HashSet<Container> _all = new HashSet<Container>();

        /// <summary>Accessible chests in range, nearest first. Rebuilt by <see cref="Scan"/>.</summary>
        public static readonly List<Container> Chests = new List<Container>();
        public static readonly List<ItemGroup> Groups = new List<ItemGroup>();
        /// <summary>Changes whenever the pool's contents change, so the UI only rebuilds when needed.</summary>
        public static int Signature { get; private set; }
        /// <summary>Per item name: amount that counts for crafting (world level matches).</summary>
        private static readonly Dictionary<string, int> _craftable = new Dictionary<string, int>();

        public static void Register(Container c) { if (c != null) _all.Add(c); }

        public static int CountForCrafting(string name) => _craftable.TryGetValue(name, out int n) ? n : 0;

        public static IEnumerable<Container> GrabbableChests()
        {
            long playerId = PlayerId();
            return Chests.Where(c => c != null && IsListed(c, playerId) && !IsBusy(c));
        }

        public static bool AcquireForGrab(Container chest) => Acquire(chest, PlayerId());

        public static bool TakeGrab(List<GrabMove> moves, Inventory to)
        {
            // Check every source after the ownership hand-off, before moving any ingredients.
            long playerId = PlayerId();
            if (moves.Any(move => move.Chest == null || !IsListed(move.Chest, playerId) || !move.Chest.IsOwner() || IsBusy(move.Chest) ||
                !move.Chest.GetInventory().ContainsItem(move.Item) || move.Item.m_stack < move.Amount))
                return false;

            foreach (var move in moves)
            {
                var from = move.Chest.GetInventory();
                int remaining = move.Amount;
                while (remaining > 0)
                {
                    int count = Mathf.Min(remaining, move.Item.m_shared.m_maxStackSize);
                    if (Move(from, move.Item, count, to) != count) return false;
                    remaining -= count;
                }
            }
            return true;
        }

        public static void Scan()
        {
            Chests.Clear();
            Groups.Clear();
            _craftable.Clear();
            Signature = 0;

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

            int sig = 17;
            var stackGroups = new Dictionary<(string, int), ItemGroup>();
            foreach (var c in Chests)
            {
                sig = sig * 31 + c.GetInstanceID();
                foreach (var item in c.GetInventory().GetAllItems())
                {
                    sig = sig * 31 + item.m_shared.m_name.GetHashCode();
                    sig = sig * 31 + item.m_stack * 7 + item.m_quality;

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
            Signature = sig;

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
        /// Moves up to <paramref name="amount"/> matching items from nearby chests (nearest first) into
        /// <paramref name="to"/>. With <paramref name="slot"/>, fills only that slot (like dropping a dragged stack);
        /// if that slot holds a different item, the items go wherever they fit instead.
        /// </summary>
        public static int Take(Func<ItemDrop.ItemData, bool> match, int amount, Inventory to, Vector2i? slot, out bool full)
        {
            full = false;
            if (to == null || amount <= 0) return 0;

            if (slot.HasValue)
            {
                var sample = Chests.Where(c => c != null && c.GetInventory() != null)
                    .SelectMany(c => c.GetInventory().GetAllItems()).FirstOrDefault(match);
                if (sample == null) return 0;
                int cap = SlotCapacity(to, slot.Value, sample);
                if (cap < 0) slot = null;          // different item there: put it anywhere
                else amount = Mathf.Min(amount, cap);
                if (amount <= 0) { full = true; return 0; }
            }

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
                    int n = slot.HasValue ? MoveToSlot(from, item, wanted, to, slot.Value) : Move(from, item, wanted, to);
                    moved += n;
                    if (n < wanted) { full = true; break; }
                    if (moved >= amount) break;
                }
                if (full) break;
            }
            return moved;
        }

        /// <summary>Moves up to <paramref name="amount"/> of <paramref name="item"/> from <paramref name="from"/> into nearby chests.</summary>
        public static int Store(Inventory from, ItemDrop.ItemData item, int amount, out bool full)
        {
            full = false;
            if (from == null || item == null || amount <= 0 || item.m_shared.m_questItem) return 0;
            amount = Mathf.Min(amount, item.m_stack);
            long playerId = PlayerId();
            string name = item.m_shared.m_name;

            // Chests that already hold this item first, then the rest; nearest first within each.
            var order = Chests.Where(c => c != null && c.GetInventory() != null)
                .OrderBy(c => c.GetInventory().ContainsItemByName(name) ? 0 : 1).ToList();

            int moved = 0;
            foreach (var c in order)
            {
                if (moved >= amount) break;
                var inv = c.GetInventory();
                if (Capacity(inv, item) <= 0) continue;
                if (!Acquire(c, playerId)) continue;
                moved += Move(from, item, amount - moved, inv);
            }
            full = moved < amount;
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
            if (IsBusy(c)) return false;

            if (!nview.IsOwner()) nview.ClaimOwnership();
            c.Load(); // pick up any change made just before we claimed it
            return nview.IsOwner();
        }

        private static bool IsBusy(Container c)
        {
            var nview = c.m_nview;
            bool inUse = nview.IsOwner() ? c.IsInUse() : nview.GetZDO().GetInt(ZDOVars.s_inUse) == 1;
            return inUse || (c.m_wagon != null && c.m_wagon.InUse());
        }

        /// <summary>Moves up to <paramref name="want"/> of <paramref name="item"/> into <paramref name="to"/>, wherever it fits.</summary>
        private static int Move(Inventory from, ItemDrop.ItemData item, int want, Inventory to)
        {
            int n = Mathf.Min(want, item.m_stack, Capacity(to, item));
            if (n <= 0) return 0;

            var copy = item.Clone();
            copy.m_stack = n;
            copy.m_equipped = false;
            if (!from.RemoveItem(item, n)) return 0;
            if (to.AddItem(copy)) return n;

            // Vanilla may fill some existing stacks before failing to place the remainder.
            int remainder = copy.m_stack;
            if (!from.ContainsItem(item))
            {
                item.m_stack = 0;
                from.GetAllItems().Add(item);
            }
            item.m_stack += remainder;
            from.Changed();
            return n - remainder;
        }

        /// <summary>Moves up to <paramref name="want"/> of <paramref name="item"/> into one slot of <paramref name="to"/>.</summary>
        private static int MoveToSlot(Inventory from, ItemDrop.ItemData item, int want, Inventory to, Vector2i slot)
        {
            int n = Mathf.Min(want, item.m_stack);
            if (n <= 0) return 0;
            var copy = item.Clone();
            copy.m_stack = n;
            copy.m_equipped = false;
            to.AddItem(copy, n, slot.x, slot.y);   // stacks onto / places at that slot; copy.m_stack drops by what was added
            int added = n - copy.m_stack;
            if (added > 0) from.RemoveItem(item, added);
            return added;
        }

        /// <summary>Room for <paramref name="item"/> at a slot: free stack space, or -1 if a different item is there.</summary>
        private static int SlotCapacity(Inventory inv, Vector2i slot, ItemDrop.ItemData item)
        {
            var at = inv.GetItemAt(slot.x, slot.y);
            if (at == null) return item.m_shared.m_maxStackSize;
            if (at.m_shared.m_maxStackSize > 1 && at.IsSameType(item)) return at.m_shared.m_maxStackSize - at.m_stack;
            return -1;
        }

        private static int Capacity(Inventory inv, ItemDrop.ItemData item)
        {
            return Mathf.Max(0, inv.GetEmptySlots()) * item.m_shared.m_maxStackSize + StackSpace(inv, item);
        }

        public static int StackSpace(Inventory inv, ItemDrop.ItemData item)
        {
            if (item.m_shared.m_maxStackSize <= 1) return 0;
            int free = 0;
            foreach (var it in inv.GetAllItems())
                if (it.m_shared.m_name == item.m_shared.m_name && it.m_quality == item.m_quality &&
                    it.m_worldLevel == item.m_worldLevel)
                    free += Mathf.Max(0, it.m_shared.m_maxStackSize - it.m_stack);
            return free;
        }

        private static long PlayerId() => Game.instance != null ? Game.instance.GetPlayerProfile().GetPlayerID() : 0L;
    }
}
