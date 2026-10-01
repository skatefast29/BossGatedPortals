using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Jotunn.Managers;
using UnityEngine;

namespace BossGatedPortals
{
    /// <summary>
    /// The one place that decides whether a player may carry an item through a portal right now.
    /// The portal check, tooltips, cart cargo and hints must all go through CanTeleport.
    /// </summary>
    internal static class Gate
    {
        /// <summary>
        /// True if this player may teleport this item right now.
        /// portalAllowsAll = the portal lets everything through in vanilla (e.g. the Stone Portal).
        /// Order: NeverTeleport, cheat tier, vanilla allow-all, then the boss tiers.
        /// </summary>
        public static bool CanTeleport(Player player, ItemDrop.ItemData item, bool portalAllowsAll = false) =>
            CollectBlockers(player, item, portalAllowsAll, null, "");

        /// <summary>
        /// CanTeleport, also adding what blocks the item to `blocked` (if given): the item itself and, for a
        /// bag (see Bags), the blocking items inside it. `where` says where the item is (see Hints.Blocked);
        /// items inside a bag are "in your" that bag. Returns true if nothing blocks.
        /// </summary>
        public static bool CollectBlockers(Player player, ItemDrop.ItemData item, bool portalAllowsAll,
            List<Hints.Blocked> blocked, string where)
        {
            // Bag mods mark a bag by vanilla rules for its contents: the bag itself counts as teleportable,
            // and its contents are judged by our rules.
            Inventory bag = Bags.Contents(item);
            bool vanillaTeleportable = bag != null || item.m_shared.m_teleportable;

            bool allowed = CanTeleportItem(player, item, vanillaTeleportable, portalAllowsAll);
            if (!allowed)
            {
                if (blocked == null) return false;
                blocked.Add(new Hints.Blocked { Item = item, Where = where });
            }
            if (bag != null)
            {
                string inBag = " in your " + item.m_shared.m_name;
                foreach (ItemDrop.ItemData inner in bag.GetAllItems())
                {
                    if (CollectBlockers(player, inner, portalAllowsAll, blocked, inBag)) continue;
                    if (blocked == null) return false;
                    allowed = false;
                }
            }
            return allowed;
        }

        private static bool CanTeleportItem(Player player, ItemDrop.ItemData item, bool vanillaTeleportable,
            bool portalAllowsAll)
        {
            bool cheatTier = item.m_shared.m_toolTier >= 1000;
            bool vanillaAllowAll = portalAllowsAll || ThisFrame(player).TeleportAll;

            // Mod switched off: exactly vanilla.
            if (!Settings.Enabled.Value)
                return !cheatTier && (vanillaAllowAll || vanillaTeleportable);

            string prefab = PrefabName(item);
            if (prefab != null && Settings.NeverTeleportItems.Contains(prefab))
                return false;

            // Vanilla blocks tool-tier 1000+ items at every portal. Only the final tier can lift that.
            if (cheatTier)
                return Settings.FinalTierIncludesCheatTier.Value && FinalTierUnlocksEverything(player);

            if (vanillaAllowAll && Settings.RespectVanillaAllowAll.Value)
                return true;

            Tier tier = prefab != null ? TierFor(prefab) : null;

            // Vanilla lets it through: only gated if an admin listed it and LockListedItemsAlways is on.
            if (vanillaTeleportable && (tier == null || !Settings.LockListedItemsAlways.Value))
            {
                if (tier != null) Compatibility.WarnListedButTeleportable(prefab, tier);
                return true;
            }

            return (tier != null && IsTierUnlocked(player, tier)) || FinalTierUnlocksEverything(player);
        }

        /// <summary>
        /// Tier unlocked for this player: earned directly, or (CumulativeTiers) any later tier earned.
        /// AdminBypass treats every tier as unlocked for admins.
        /// </summary>
        public static bool IsTierUnlocked(Player player, Tier tier)
        {
            if (IsBypassing(player)) return true;

            bool[] earned = ThisFrame(player).Earned;
            int last = Settings.CumulativeTiers.Value ? earned.Length - 1 : tier.Index;
            for (int i = tier.Index; i <= last && i < earned.Length; i++)
            {
                if (earned[i]) return true;
            }
            return false;
        }

        /// <summary>World state the gate reads for every item, worked out once per frame (see ThisFrame).</summary>
        private class FrameState
        {
            public int Frame = -1;
            public Player Player;
            public List<Tier> Tiers;
            public bool TeleportAll;
            public bool[] Earned = new bool[0];
        }

        private static readonly FrameState frame = new FrameState();

        /// <summary>
        /// Which tiers this player has earned, and the "teleport all" world modifier, for this frame. The
        /// inventory grid asks about every slot every frame, and each world-key lookup makes the game build a
        /// lower-case copy of the key's name: asking once per frame instead of once per item per tier.
        /// Recomputed for a new frame, another player, or new settings (Settings.Parse makes a new Tiers list).
        /// </summary>
        private static FrameState ThisFrame(Player player)
        {
            List<Tier> tiers = Settings.Tiers;
            if (frame.Frame == Time.frameCount && frame.Player == player && frame.Tiers == tiers) return frame;

            frame.Frame = Time.frameCount;
            frame.Player = player;
            frame.Tiers = tiers;
            frame.TeleportAll = Failsafe.TeleportAllSet();
            if (frame.Earned.Length != tiers.Count) frame.Earned = new bool[tiers.Count];
            for (int i = 0; i < tiers.Count; i++)
                frame.Earned[i] = IsTierEarned(player, tiers[i]);
            return frame;
        }

        /// <summary>
        /// The boss killed on this world and/or the player has held its drop, as UnlockWhen says.
        /// Missing keys or items fail closed: the tier stays locked.
        /// </summary>
        public static bool IsTierEarned(Player player, Tier tier)
        {
            if (Settings.NeedsBossKill)
            {
                if (string.IsNullOrEmpty(tier.GlobalKey) || !ZoneSystem.instance.GetGlobalKey(tier.GlobalKey))
                    return false;
            }
            if (Settings.NeedsBossDrop)
            {
                if (string.IsNullOrEmpty(tier.BossItemToken) || !player.IsMaterialKnown(tier.BossItemToken))
                    return false;
            }
            return true;
        }

        /// <summary>The tier that lists this item, or null if it's unmapped.</summary>
        public static Tier TierFor(string prefab) =>
            Settings.TierOfItem.TryGetValue(prefab, out Tier tier) ? tier : null;

        private static bool FinalTierUnlocksEverything(Player player)
        {
            List<Tier> tiers = Settings.Tiers;
            if (tiers.Count == 0) return false;
            Tier final = tiers[tiers.Count - 1];
            return final.AllowEverything && IsTierUnlocked(player, final);
        }

        private static bool IsBypassing(Player player) =>
            Settings.AdminBypass.Value && player == Player.m_localPlayer && SynchronizationManager.Instance.PlayerIsAdmin;

        // Prefab name per item type. Every item of a type shares one SharedData, and the inventory grid asks
        // about every slot every frame: Unity builds a new string for each .name read, so read it once per type.
        private static readonly Dictionary<ItemDrop.ItemData.SharedData, string> prefabNames =
            new Dictionary<ItemDrop.ItemData.SharedData, string>();

        /// <summary>
        /// The item's prefab name (e.g. "Iron"), or null if the game doesn't know it.
        /// Recipe previews use the prefab's own item data, which has no m_dropPrefab: look it up instead.
        /// </summary>
        public static string PrefabName(ItemDrop.ItemData item)
        {
            if (prefabNames.TryGetValue(item.m_shared, out string name)) return name;

            if (item.m_dropPrefab) name = item.m_dropPrefab.name;
            else if (ObjectDB.instance && ObjectDB.instance.TryGetItemPrefab(item.m_shared, out var prefab) && prefab)
                name = prefab.name;
            if (name != null) prefabNames[item.m_shared] = name; // unknown items are asked again later
            return name;
        }
    }

    /// <summary>
    /// Vanilla portals ask Inventory.IsTeleportable (via Humanoid.IsTeleportable) whether you may enter,
    /// both to light up the portal and when you step in. For the local player's own inventory,
    /// replace the answer with Gate.CanTeleport on every item. Other inventories stay vanilla.
    /// The blocking items are kept in Hints.LastBlocked so the "blocked" message can pick a hint.
    /// GateCompanionCargo: a cart the local player is pulling counts as carried, so its cargo is
    /// checked with the player's inventory, and also when a cart mod asks about the cart itself.
    /// A postfix, not a prefix that skips vanilla (Harmony's advice for compatibility): vanilla answers
    /// first, then we replace the answer. Postfixes always run, so the gate still has the last word if
    /// another mod's prefix skips the original, and if we fail, vanilla's answer stands.
    /// Bag mods (e.g. AdventureBackpacks) add their own postfix that checks the bag's inventory from
    /// inside the player's check and can only turn the answer to "blocked". Our postfix runs first
    /// (Priority.First) so it can't overwrite their "blocked", and any inventory checked while the
    /// player's own check is running counts as carried, so the bag gets our rules, not vanilla's.
    /// </summary>
    [HarmonyPatch(typeof(Inventory), nameof(Inventory.IsTeleportable))]
    internal static class InventoryIsTeleportablePatch
    {
        // > 0 while the local player's own inventory is being checked (from our prefix to our finalizer).
        private static int ownCheckDepth;

        [HarmonyPriority(Priority.First)]
        private static void Prefix(Inventory __instance, out bool __state)
        {
            __state = false;
            try
            {
                __state = EnterOwnCheck(__instance);
            }
            catch (Exception e)
            {
                Failsafe.Report("Portal item check (start)", e);
            }
        }

        [HarmonyPriority(Priority.First)]
        private static void Postfix(Inventory __instance, bool allowAllItems, ref bool __result)
        {
            try
            {
                Gatekeep(__instance, allowAllItems, ref __result);
            }
            catch (Exception e)
            {
                Failsafe.Report("Portal item check", e); // __result keeps vanilla's answer
            }
        }

        // Finalizers run after every mod's postfix, so the bag checks above happen inside the window.
        private static void Finalizer(bool __state)
        {
            if (__state) ownCheckDepth--;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static bool EnterOwnCheck(Inventory inventory)
        {
            Player player = Player.m_localPlayer;
            if (player == null || player.GetInventory() != inventory) return false;
            ownCheckDepth++;
            return true;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void Gatekeep(Inventory inventory, bool allowAllItems, ref bool result)
        {
            Player player = Player.m_localPlayer;
            if (!Settings.Enabled.Value || player == null)
                return; // vanilla's answer stands

            Vagon cartVagon = AttachedCart(player);
            Inventory cart = cartVagon ? cartVagon.m_container.GetInventory() : null;
            bool own = player.GetInventory() == inventory;
            if (!own && ownCheckDepth > 0)
            {
                // A bag (or other carried inventory, e.g. an AdventureBackpacks bag) checked by another mod during
                // the player's check: our rules decide, and its blocked items join the player's for the hint.
                int before = Hints.LastBlocked.Count;
                AddBlocked(player, inventory, allowAllItems, " in your backpack");
                result = Hints.LastBlocked.Count == before;
                return;
            }
            if (!own && inventory != cart)
                return; // someone else's inventory: vanilla's answer stands

            Hints.LastBlocked.Clear();
            AddBlocked(player, inventory, allowAllItems, own ? "" : " in your " + cartVagon.GetHoverName());
            if (own && cart != null)
                AddBlocked(player, cart, allowAllItems, " in your " + cartVagon.GetHoverName());
            if (own)
            {
                foreach (Tames.Bag bag in Tames.Cargo(player))
                    AddBlocked(player, bag.Inventory, allowAllItems, bag.Where);
            }
            result = Hints.LastBlocked.Count == 0;
        }

        private static void AddBlocked(Player player, Inventory inventory, bool allowAllItems, string where)
        {
            foreach (ItemDrop.ItemData item in inventory.GetAllItems())
                Gate.CollectBlockers(player, item, allowAllItems, Hints.LastBlocked, where);
        }

        /// <summary>
        /// The cart (Vagon) this player is pulling, if its cargo is gated; else null. Always gated at a portal
        /// when BringCart takes the cart through, so that setting can't carry locked cargo.
        /// </summary>
        private static Vagon AttachedCart(Player player)
        {
            if (!Settings.GateCompanionCargo.Value && !(Travel.atPortal && Travel.BringsCart)) return null;
            Vagon vagon = Travel.CartPulledBy(player);
            return vagon && vagon.m_container ? vagon : null;
        }
    }
}
