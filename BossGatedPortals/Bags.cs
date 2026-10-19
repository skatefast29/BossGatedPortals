using System;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using BepInEx.Bootstrap;

namespace BossGatedPortals
{
    /// <summary>
    /// Bag mods that keep a bag's contents inside the bag item, and mark the bag item non-teleportable
    /// using VANILLA rules for its contents whenever they change:
    /// - Smoothbrain's Backpacks (org.bepinex.plugins.backpacks): bag data via its ItemDataManager.
    /// - RustyBags (RustyMods.RustyBags): the item itself is a RustyBags.Bag. Its mark goes on the item
    ///   type's shared data, so it flips for every bag of that type at once.
    /// - AdventureBackpacks (vapok.mods.adventurebackpacks) 2.4.5+: bag data via its own copy of
    ///   ItemDataManager. It asks Inventory.IsTeleportable about the bag (our rules, see
    ///   InventoryIsTeleportablePatch), but only when the contents change, so its mark goes stale when a
    ///   boss is defeated. Older versions never mark the bag; checking the contents here is still right.
    /// Without help, ore in such a bag stays blocked after its boss. Gate.CanTeleport ignores the bag's
    /// mark and checks the contents itself. Read through reflection: no build reference, and nothing runs
    /// without the mods.
    /// </summary>
    internal static class Bags
    {
        public const string BackpacksGUID = "org.bepinex.plugins.backpacks";
        public const string RustyBagsGUID = "RustyMods.RustyBags";
        public const string AdventureBackpacksGUID = "vapok.mods.adventurebackpacks";

        // Smoothbrain's Backpacks: item.Data().Get<ItemContainer>()?.Inventory, compiled once into a direct
        // call (the inventory grid asks about every item every frame; a reflection Invoke costs ~10x more).
        private static bool backpacksResolved;
        private static Func<ItemDrop.ItemData, Inventory> backpackInventory;

        // AdventureBackpacks: item.Data().Get<BackpackComponent>()?.GetInventory(), compiled the same way, and
        // only asked for its backpacks. Its IsBackpack allocates, so the answer is kept per item type. It gives
        // each backpack its own copy of the shared data, so the table lets go of copies nobody uses any more.
        private static bool adventureResolved;
        private static Func<ItemDrop.ItemData, Inventory> adventureInventory;
        private static Func<ItemDrop.ItemData, bool> isAdventureBackpack;
        private static readonly ConditionalWeakTable<ItemDrop.ItemData.SharedData, object> adventureTypes =
            new ConditionalWeakTable<ItemDrop.ItemData.SharedData, object>();
        private static readonly object IsBag = new object(), NotBag = new object();

        // RustyBags
        private static bool rustyResolved;
        private static Type rustyBag;            // RustyBags.Bag (an ItemData subclass)
        private static FieldInfo rustyInventory; // Bag.inventory

        /// <summary>
        /// The bag's own inventory if this item is a Backpacks, RustyBags or AdventureBackpacks bag, else null.
        /// The bag itself then counts as teleportable; its contents decide.
        /// </summary>
        public static Inventory Contents(ItemDrop.ItemData item)
        {
            return RustyBagContents(item) ?? BackpackContents(item) ?? AdventureContents(item);
        }

        /// <summary>True if this inventory is the contents of a bag in `carrier` (not nested deeper).</summary>
        public static bool IsBagIn(Inventory carrier, Inventory inventory)
        {
            foreach (ItemDrop.ItemData item in carrier.GetAllItems())
            {
                if (Contents(item) == inventory) return true;
            }
            return false;
        }

        private static Inventory AdventureContents(ItemDrop.ItemData item)
        {
            if (!ResolveAdventureBackpacks()) return null;
            try
            {
                if (!adventureTypes.TryGetValue(item.m_shared, out object kind))
                    adventureTypes.Add(item.m_shared, kind = isAdventureBackpack(item) ? IsBag : NotBag);
                return kind == IsBag ? adventureInventory(item) : null;
            }
            catch (Exception e)
            {
                // The bag falls back to its own mark (our rules when its contents last changed).
                Failsafe.Report("AdventureBackpacks bag check", e);
                adventureInventory = null;
                return null;
            }
        }

        private static Inventory BackpackContents(ItemDrop.ItemData item)
        {
            if (!ResolveBackpacks()) return null;
            try
            {
                return backpackInventory(item);
            }
            catch (Exception e)
            {
                // Bags fall back to Backpacks' own (vanilla) rule: blocked until the final tier.
                Failsafe.Report("Backpacks bag check", e);
                backpackInventory = null;
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
            if (backpacksResolved) return backpackInventory != null;
            Assembly asm = LoadedAssembly(BackpacksGUID, ref backpacksResolved);
            if (asm == null) return false;

            Type container = asm.GetType("Backpacks.ItemContainer");
            MethodInfo data = asm.GetType("ItemDataManager.ItemExtensions")?.GetMethod("Data", new[] { typeof(ItemDrop.ItemData) });
            MethodInfo get = data?.ReturnType.GetMethods().FirstOrDefault(m =>
                m.Name == "Get" && m.IsGenericMethodDefinition && m.GetParameters().Length == 1);
            FieldInfo inventory = container?.GetField("Inventory");

            if (container == null || get == null || inventory == null || !typeof(Inventory).IsAssignableFrom(inventory.FieldType))
            {
                ApiChanged("Backpacks");
                return false;
            }
            backpackInventory = DataReader("Backpacks", data, get.MakeGenericMethod(container), inventory);
            Jotunn.Logger.LogInfo("Backpacks detected: items in its bags unlock with their tiers.");
            return true;
        }

        private static bool ResolveAdventureBackpacks()
        {
            if (adventureResolved) return adventureInventory != null;
            Assembly asm = LoadedAssembly(AdventureBackpacksGUID, ref adventureResolved);
            if (asm == null) return false;

            Type component = asm.GetType("AdventureBackpacks.Components.BackpackComponent");
            MethodInfo isBackpack = asm.GetType("AdventureBackpacks.Extensions.ItemDataExtensions")
                ?.GetMethod("IsBackpack", new[] { typeof(ItemDrop.ItemData) });
            MethodInfo data = asm.GetType("Vapok.Common.Managers.ItemExtensions")
                ?.GetMethod("Data", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(ItemDrop.ItemData) }, null);
            MethodInfo get = data?.ReturnType.GetMethods().FirstOrDefault(m =>
                m.Name == "Get" && m.IsGenericMethodDefinition && m.GetParameters().Length == 1);
            MethodInfo inventory = component?.GetMethod("GetInventory", Type.EmptyTypes);

            if (component == null || isBackpack == null || isBackpack.ReturnType != typeof(bool) || get == null ||
                inventory == null || !typeof(Inventory).IsAssignableFrom(inventory.ReturnType))
            {
                // Before 2.4.5 it never marks the bag, and the player's check still gates its contents.
                Jotunn.Logger.LogWarning("AdventureBackpacks is installed but its bag code changed. With version " +
                    "2.4.5 or newer, a backpack holding unlocked items can stay blocked until this mod is updated.");
                return false;
            }
            isAdventureBackpack = (Func<ItemDrop.ItemData, bool>)Delegate.CreateDelegate(typeof(Func<ItemDrop.ItemData, bool>), isBackpack);
            adventureInventory = DataReader("AdventureBackpacks", data, get.MakeGenericMethod(component), inventory);
            Jotunn.Logger.LogInfo("AdventureBackpacks detected: items in its bags unlock with their tiers.");
            return true;
        }

        /// <summary>
        /// item => Data(item).Get&lt;T&gt;("")?.inventory (a field or a method), compiled once into a direct call.
        /// If that can't compile here: the same calls through reflection, slower but the same answer.
        /// </summary>
        private static Func<ItemDrop.ItemData, Inventory> DataReader(string mod, MethodInfo data, MethodInfo get,
            MemberInfo inventory)
        {
            try
            {
                ParameterExpression item = Expression.Parameter(typeof(ItemDrop.ItemData), "item");
                ParameterExpression bag = Expression.Variable(get.ReturnType, "bag");
                Expression read = inventory is FieldInfo field
                    ? Expression.Field(bag, field)
                    : Expression.Call(bag, (MethodInfo)inventory);
                Expression body = Expression.Block(new[] { bag },
                    Expression.Assign(bag, Expression.Call(Expression.Call(data, item), get, Expression.Constant(""))),
                    Expression.Condition(
                        Expression.Equal(bag, Expression.Constant(null, get.ReturnType)),
                        Expression.Constant(null, typeof(Inventory)),
                        Expression.Convert(read, typeof(Inventory))));
                return Expression.Lambda<Func<ItemDrop.ItemData, Inventory>>(body, item).Compile();
            }
            catch (Exception e)
            {
                Jotunn.Logger.LogInfo($"{mod}: using the slower bag check ({e.Message}).");
                object[] args = { "" };
                return item =>
                {
                    object found = get.Invoke(data.Invoke(null, new object[] { item }), args);
                    if (found == null) return null;
                    return (inventory is FieldInfo field ? field.GetValue(found) : ((MethodInfo)inventory).Invoke(found, null)) as Inventory;
                };
            }
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
