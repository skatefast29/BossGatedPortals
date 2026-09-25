using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Jotunn.Managers;

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
        public static bool CanTeleport(Player player, ItemDrop.ItemData item, bool portalAllowsAll = false)
        {
            bool vanillaTeleportable = item.m_shared.m_teleportable;
            bool cheatTier = item.m_shared.m_toolTier >= 1000;
            bool vanillaAllowAll = portalAllowsAll || Failsafe.TeleportAllSet();

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

            // Vanilla lets it through: only gated if an admin listed it and GateListedVanillaItems is on.
            if (vanillaTeleportable && (tier == null || !Settings.GateListedVanillaItems.Value))
                return true;

            return (tier != null && IsTierUnlocked(player, tier)) || FinalTierUnlocksEverything(player);
        }

        /// <summary>
        /// Tier unlocked for this player: earned directly, or (CumulativeTiers) any later tier earned.
        /// AdminBypass treats every tier as unlocked for admins.
        /// </summary>
        public static bool IsTierUnlocked(Player player, Tier tier)
        {
            if (IsBypassing(player)) return true;

            List<Tier> tiers = Settings.Tiers;
            int last = Settings.CumulativeTiers.Value ? tiers.Count - 1 : tier.Index;
            for (int i = tier.Index; i <= last; i++)
            {
                if (IsTierEarned(player, tiers[i])) return true;
            }
            return false;
        }

        /// <summary>
        /// World key set (if required) AND the player has held the boss drop (if required).
        /// Missing keys or items fail closed: the tier stays locked.
        /// </summary>
        public static bool IsTierEarned(Player player, Tier tier)
        {
            if (Settings.EffectiveRequireWorldKey)
            {
                if (string.IsNullOrEmpty(tier.GlobalKey) || !ZoneSystem.instance.GetGlobalKey(tier.GlobalKey))
                    return false;
            }
            if (Settings.RequirePlayerBossItem.Value)
            {
                if (string.IsNullOrEmpty(tier.BossItemToken) || !player.IsMaterialKnown(tier.BossItemToken))
                    return false;
            }
            return true;
        }

        /// <summary>The tier that lists this item, or null if it's unmapped.</summary>
        public static Tier TierFor(string prefab)
        {
            foreach (Tier t in Settings.Tiers)
            {
                if (t.Items.Contains(prefab)) return t;
            }
            return null;
        }

        private static bool FinalTierUnlocksEverything(Player player)
        {
            List<Tier> tiers = Settings.Tiers;
            if (tiers.Count == 0) return false;
            Tier final = tiers[tiers.Count - 1];
            return final.AllowEverything && IsTierUnlocked(player, final);
        }

        private static bool IsBypassing(Player player) =>
            Settings.AdminBypass.Value && player == Player.m_localPlayer && SynchronizationManager.Instance.PlayerIsAdmin;

        /// <summary>
        /// The item's prefab name (e.g. "Iron"), or null if the game doesn't know it.
        /// Recipe previews use the prefab's own item data, which has no m_dropPrefab: look it up instead.
        /// </summary>
        public static string PrefabName(ItemDrop.ItemData item)
        {
            if (item.m_dropPrefab) return item.m_dropPrefab.name;
            if (ObjectDB.instance && ObjectDB.instance.TryGetItemPrefab(item.m_shared, out var prefab) && prefab)
                return prefab.name;
            return null;
        }
    }

    /// <summary>
    /// Vanilla portals ask Inventory.IsTeleportable (via Humanoid.IsTeleportable) whether you may enter,
    /// both to light up the portal and when you step in. For the local player's own inventory,
    /// replace the answer with Gate.CanTeleport on every item. Other inventories stay vanilla.
    /// The blocking items are kept in Hints.LastBlocked so the "blocked" message can pick a hint.
    /// GateAttachedCartCargo: a cart the local player is pulling counts as carried, so its cargo is
    /// checked with the player's inventory, and also when a cart mod asks about the cart itself.
    /// A postfix, not a prefix that skips vanilla (Harmony's advice for compatibility): vanilla answers
    /// first, then we replace the answer. Postfixes always run, so the gate still has the last word if
    /// another mod's prefix skips the original, and if we fail, vanilla's answer stands.
    /// </summary>
    [HarmonyPatch(typeof(Inventory), nameof(Inventory.IsTeleportable))]
    internal static class InventoryIsTeleportablePatch
    {
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

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void Gatekeep(Inventory inventory, bool allowAllItems, ref bool result)
        {
            Player player = Player.m_localPlayer;
            if (!Settings.Enabled.Value || player == null)
                return; // vanilla's answer stands

            Inventory cart = AttachedCartCargo(player);
            bool own = player.GetInventory() == inventory;
            if (!own && inventory != cart)
                return; // someone else's inventory: vanilla's answer stands

            Hints.LastBlocked.Clear();
            AddBlocked(player, inventory, allowAllItems);
            if (own && cart != null)
                AddBlocked(player, cart, allowAllItems);
            result = Hints.LastBlocked.Count == 0;
        }

        private static void AddBlocked(Player player, Inventory inventory, bool allowAllItems)
        {
            foreach (ItemDrop.ItemData item in inventory.GetAllItems())
            {
                if (!Gate.CanTeleport(player, item, allowAllItems))
                    Hints.LastBlocked.Add(item);
            }
        }

        // Vagon.m_instances is private in the game: reading it directly throws FieldAccessException at runtime.
        private static readonly FieldInfo VagonInstances = AccessTools.Field(typeof(Vagon), "m_instances");

        /// <summary>Cargo of the cart (Vagon) this player is pulling, or null (none, or GateAttachedCartCargo off).</summary>
        private static Inventory AttachedCartCargo(Player player)
        {
            if (!Settings.GateAttachedCartCargo.Value) return null;
            if (!(VagonInstances?.GetValue(null) is List<Vagon> carts)) return null;
            foreach (Vagon vagon in carts)
            {
                if (vagon && vagon.m_container && vagon.IsAttached(player))
                    return vagon.m_container.GetInventory();
            }
            return null;
        }
    }
}
