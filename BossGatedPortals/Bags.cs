using System;
using System.Linq;
using System.Reflection;
using BepInEx.Bootstrap;

namespace BossGatedPortals
{
    /// <summary>
    /// Bag mods that keep a bag's contents inside the bag item, and mark the bag item non-teleportable
    /// using VANILLA rules for its contents whenever they change:
    /// - Smoothbrain's Backpacks (org.bepinex.plugins.backpacks): bag data via its ItemDataManager.
    /// - RustyBags (RustyMods.RustyBags): the item itself is a RustyBags.Bag. Its mark goes on the item
    ///   type's shared data, so it flips for every bag of that type at once.
    /// Without help, ore in such a bag stays blocked after its boss. Gate.CanTeleport ignores the bag's
    /// mark and checks the contents itself. Read through reflection: no build reference, and nothing runs
    /// without the mods. AdventureBackpacks needs nothing here: it asks Inventory.IsTeleportable about the
    /// bag during the player's own check (see InventoryIsTeleportablePatch).
    /// </summary>
    internal static class Bags
    {
        public const string BackpacksGUID = "org.bepinex.plugins.backpacks";
        public const string RustyBagsGUID = "RustyMods.RustyBags";

        // Smoothbrain's Backpacks
        private static bool backpacksResolved;
        private static MethodInfo data;          // ItemDataManager.ItemExtensions.Data(ItemData)
        private static MethodInfo getContainer;  // ItemInfo.Get<Backpacks.ItemContainer>(string key)
        private static FieldInfo containerInventory; // ItemContainer.Inventory
        // Reused argument arrays: the inventory grid asks about every item every frame. Main thread only.
        private static readonly object[] dataArgs = new object[1];
        private static readonly object[] getArgs = { "" };

        // RustyBags
        private static bool rustyResolved;
        private static Type rustyBag;            // RustyBags.Bag (an ItemData subclass)
        private static FieldInfo rustyInventory; // Bag.inventory

        /// <summary>
        /// The bag's own inventory if this item is a Backpacks or RustyBags bag, else null.
        /// The bag itself then counts as teleportable; its contents decide.
        /// </summary>
        public static Inventory Contents(ItemDrop.ItemData item)
        {
            return RustyBagContents(item) ?? BackpackContents(item);
        }

        private static Inventory BackpackContents(ItemDrop.ItemData item)
        {
            if (!ResolveBackpacks()) return null;
            try
            {
                dataArgs[0] = item;
                object info = data.Invoke(null, dataArgs);
                dataArgs[0] = null;
                object container = info == null ? null : getContainer.Invoke(info, getArgs);
                return container == null ? null : containerInventory.GetValue(container) as Inventory;
            }
            catch (Exception e)
            {
                // Bags fall back to Backpacks' own (vanilla) rule: blocked until the final tier.
                Failsafe.Report("Backpacks bag check", e);
                data = null;
                return null;
            }
        }

        private static Inventory RustyBagContents(ItemDrop.ItemData item)
        {
            if (!ResolveRustyBags() || !rustyBag.IsInstanceOfType(item)) return null;
            try
            {
                return rustyInventory.GetValue(item) as Inventory;
            }
            catch (Exception e)
            {
                Failsafe.Report("RustyBags bag check", e);
                rustyBag = null;
                return null;
            }
        }

        private static bool ResolveBackpacks()
        {
            if (backpacksResolved) return data != null;
            Assembly asm = LoadedAssembly(BackpacksGUID, ref backpacksResolved);
            if (asm == null) return false;

            Type container = asm.GetType("Backpacks.ItemContainer");
            data = asm.GetType("ItemDataManager.ItemExtensions")?.GetMethod("Data", new[] { typeof(ItemDrop.ItemData) });
            MethodInfo get = data?.ReturnType.GetMethods().FirstOrDefault(m =>
                m.Name == "Get" && m.IsGenericMethodDefinition && m.GetParameters().Length == 1);
            containerInventory = container?.GetField("Inventory");

            if (container == null || get == null || containerInventory == null)
            {
                data = null;
                ApiChanged("Backpacks");
                return false;
            }
            getContainer = get.MakeGenericMethod(container);
            Jotunn.Logger.LogInfo("Backpacks detected: items in its bags unlock with their tiers.");
            return true;
        }

        private static bool ResolveRustyBags()
        {
            if (rustyResolved) return rustyBag != null;
            Assembly asm = LoadedAssembly(RustyBagsGUID, ref rustyResolved);
            if (asm == null) return false;

            rustyBag = asm.GetType("RustyBags.Bag");
            rustyInventory = rustyBag?.GetField("inventory");
            if (rustyBag == null || rustyInventory == null || !typeof(ItemDrop.ItemData).IsAssignableFrom(rustyBag) ||
                !typeof(Inventory).IsAssignableFrom(rustyInventory.FieldType))
            {
                rustyBag = null;
                ApiChanged("RustyBags");
                return false;
            }
            Jotunn.Logger.LogInfo("RustyBags detected: items in its bags unlock with their tiers.");
            return true;
        }

        /// <summary>The mod's assembly once it has loaded; sets resolved when there's nothing more to wait for.</summary>
        private static Assembly LoadedAssembly(string guid, ref bool resolved)
        {
            if (!Chainloader.PluginInfos.TryGetValue(guid, out var plugin))
            {
                resolved = true;
                return null;
            }
            if (plugin.Instance == null) return null; // still loading: try again next time
            resolved = true;
            return plugin.Instance.GetType().Assembly;
        }

        private static void ApiChanged(string mod) =>
            Jotunn.Logger.LogWarning($"{mod} is installed but its bag code changed. Items in its bags stay " +
                $"blocked until the final tier ({mod}'s own rule) until this mod is updated.");
    }
}
