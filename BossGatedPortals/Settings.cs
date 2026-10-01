using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using HarmonyLib;

namespace BossGatedPortals
{
    public enum HintPosition { Centered, TopLeft }

    /// <summary>What unlocks a tier: the boss killed on this world, the player having held its drop, or both.</summary>
    public enum UnlockRule { BossKilled, BossKilledAndDropHeld, DropHeld }

    /// <summary>Which tames BringTames takes along (tames following another player never come).</summary>
    public enum TameSelection { Following, FollowingOrNamed, Named, AllTamed }

    /// <summary>
    /// Binds every setting from the BossGatedPortals.cfg spec and keeps a parsed copy of the tiers.
    /// Everything is server-synced and admin-only except the [8 - Client] settings.
    /// Sections are numbered so the file and the F1 menu list them in this order.
    /// </summary>
    internal static class Settings
    {
        // [1 - General]
        public static ConfigEntry<bool> Enabled;
        public static ConfigEntry<UnlockRule> UnlockWhen;
        public static ConfigEntry<bool> CumulativeTiers;

        // [2 - Item Rules]
        public static ConfigEntry<string> NeverTeleport;
        public static ConfigEntry<bool> HildirChestsByTier;
        public static ConfigEntry<bool> LockListedItemsAlways;
        public static ConfigEntry<bool> FinalTierIncludesCheatTier;
        public static ConfigEntry<bool> RespectVanillaAllowAll;

        // [3 - Messages]
        public static ConfigEntry<string> HintFormat;
        public static ConfigEntry<bool> AnnounceTierUnlock;
        public static ConfigEntry<bool> AccurateTooltips;

        // [4 - Portal Speed]
        public static ConfigEntry<float> FadeSeconds;
        public static ConfigEntry<float> MinimumLoadingSeconds;

        // [5 - Travel]
        public static ConfigEntry<bool> BringMount;
        public static ConfigEntry<bool> BringCart;
        public static ConfigEntry<bool> BringTames;

        // [5 - Travel], the Tame... settings right under BringTames (only used when it's on)
        public static ConfigEntry<TameSelection> TameMode;
        public static ConfigEntry<float> TameRadius;
        public static ConfigEntry<float> TameHeightRange;
        public static ConfigEntry<int> TameMaxCount;
        public static ConfigEntry<string> TameAllowList;
        public static ConfigEntry<string> TameBlockList;
        public static ConfigEntry<bool> TameIncludeSummons;

        // [6 - Compatibility]
        public static ConfigEntry<bool> GateCompanionCargo;
        public static ConfigEntry<bool> WarnOnConflictingMods;

        // [7 - Admin]
        public static ConfigEntry<bool> AdminBypass;
        public static ConfigEntry<bool> StatusCommand;
        public static ConfigEntry<bool> LogItemChecks;

        // [8 - Client] (each player's own; not synced)
        public static ConfigEntry<HintPosition> HintPositionSetting;
        public static ConfigEntry<float> HintCooldownSeconds;

        /// <summary>Parsed tiers, in order T0..T7. Rebuilt whenever a setting changes.</summary>
        public static List<Tier> Tiers = new List<Tier>();
        public static HashSet<string> NeverTeleportItems = new HashSet<string>();
        /// <summary>Item prefab -> the first tier that lists it (Gate.TierFor). Rebuilt with Tiers.</summary>
        public static Dictionary<string, Tier> TierOfItem = new Dictionary<string, Tier>();

        /// <summary>Parsed tame AllowList / BlockList (creature prefab names, any case).</summary>
        public static HashSet<string> TameAllow = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public static HashSet<string> TameBlock = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>A tier needs its boss killed on this world.</summary>
        public static bool NeedsBossKill => UnlockWhen.Value != UnlockRule.DropHeld;

        /// <summary>A tier needs this player to have held its boss drop.</summary>
        public static bool NeedsBossDrop => UnlockWhen.Value != UnlockRule.BossKilled;

        private class TierEntries
        {
            public string Name;
            public ConfigEntry<string> GlobalKey, BossItem, Items, Hint, UnlockMessage;
            public ConfigEntry<bool> AllowEverything; // final tier only
        }

        private static readonly List<TierEntries> tierEntries = new List<TierEntries>();

        // Defaults = the verified values in the BossGatedPortals.cfg spec.
        private static readonly string[][] TierDefaults =
        {
            // name, global key, boss item, items, hint, unlock message
            new[] { "Meadows", "defeated_eikthyr", "HardAntler", "", "", "" },
            new[] { "Black Forest", "defeated_gdking", "CryptKey",
                "CopperOre, Copper, CopperScrap, TinOre, Tin, Bronze, BronzeScrap",
                "Something old still stirs beneath the Black Forest canopy.",
                "The portals hum. Metals of the forest may now pass." },
            new[] { "Swamp", "defeated_bonemass", "Wishbone", "IronOre, IronScrap, Iron, Ironpit",
                "The Swamp's guardian has not yet been put to rest.",
                "The portals hum. Iron from the Swamp may now pass." },
            new[] { "Mountains", "defeated_dragon", "DragonTear", "SilverOre, Silver, DragonEgg",
                "The Mountain peaks answer to a ruler you have not faced.",
                "The portals hum. Treasures of the Mountains may now pass." },
            new[] { "Plains", "defeated_goblinking", "YagluthDrop", "BlackMetalScrap, BlackMetal",
                "The Plains still bow to their fallen king.",
                "The portals hum. Black metal may now pass." },
            new[] { "Mistlands", "defeated_queen", "QueenDrop", "MechanicalSpring, DvergrNeedle",
                "Something waits deep within the Mistlands.",
                "The portals hum. Relics of the Mistlands may now pass." },
            new[] { "Ashlands", "defeated_fader", "FaderDrop",
                "FlametalOreNew, FlametalNew, FlametalOre, Flametal, CharredCogwheel",
                "The Ashlands' guardian still smolders.",
                "The portals hum. Flametal may now pass." },
            new[] { "Deep North", "defeated_frozenking_p3", "FrozenKingDrop", "GoldOre, Gold",
                "The portal will not carry this until the far North is conquered.",
                "The portals roar. Nothing is barred from passage now." },
        };

        // Hildir's quest chests -> tier of the biome whose Hildir quest drops them (VERIFIED 1.0.15).
        private static readonly Dictionary<string, int> HildirChestTiers = new Dictionary<string, int>
        {
            { "chest_hildir1", 1 }, // Brass: Black Forest
            { "chest_hildir2", 3 }, // Silver: Mountains
            { "chest_hildir3", 4 }, // Bronze: Plains
        };

        private static ConfigFile config;
        private static int order;

        public static void Bind(ConfigFile cfg)
        {
            config = cfg;

            // Write the config file once, not after every setting (Valheim modding wiki advice).
            bool saveOnSet = cfg.SaveOnConfigSet;
            cfg.SaveOnConfigSet = false;
            try
            {
                BindEntries();
                Migrate();
            }
            finally
            {
                cfg.Save();
                cfg.SaveOnConfigSet = saveOnSet;
            }

            Parse();
        }

        private static void BindEntries()
        {
            const string general = "1 - General";
            Enabled = Admin(general, "Enabled", true, "Master switch for the mod.");
            UnlockWhen = Admin(general, "UnlockWhen", UnlockRule.BossKilled,
                "What unlocks a tier. BossKilled = the boss has been killed on this world (for everyone). " +
                "BossKilledAndDropHeld = also, each player must have picked up the boss's drop at least once. " +
                "DropHeld = only the player's own boss drop counts.");
            CumulativeTiers = Admin(general, "CumulativeTiers", false,
                "If true, unlocking a later tier also unlocks every earlier tier for that player. " +
                "false = each tier must be earned on its own.");

            const string items = "2 - Item Rules";
            NeverTeleport = Admin(items, "NeverTeleport", "",
                "Items that can NEVER be teleported, even after the final tier. Prefab names, comma-separated. " +
                "Example: DragonEgg");
            HildirChestsByTier = Admin(items, "HildirChestsByTier", false,
                "If true, Hildir's chests unlock with the tier of the biome they're found in " +
                "(Brass = Black Forest, Silver = Mountains, Bronze = Plains). " +
                "false = they never teleport, as in vanilla.");
            LockListedItemsAlways = Admin(items, "LockListedItemsAlways", false,
                "If true, every item listed in a tier stays locked until that tier, even items the game (or another " +
                "mod) would let through portals anyway. Use it to lock extra items by progression, e.g. Coal in the " +
                "Black Forest tier. false = tiers only unlock items the game blocks; listing an item the game " +
                "already allows does nothing.");
            FinalTierIncludesCheatTier = Admin(items, "FinalTierIncludesCheatTier", false,
                "Vanilla blocks tool-tier 1000+ items at every portal. The only such item is FrozenKingDrop. " +
                "If true, the final tier also unlocks those.");
            RespectVanillaAllowAll = Admin(items, "RespectVanillaAllowAll", true,
                "Never block a portal that already lets everything through in vanilla (the Stone Portal), " +
                "and respect the 'Portals' world modifier when it allows all items.");

            const string messages = "3 - Messages";
            HintFormat = Admin(messages, "HintFormat", Hints.DefaultFormat,
                "Shown when a locked item stops you at a portal. {hint} = the earliest locked tier's Hint. " +
                "{where} = where that item is, if not in your inventory (\" in your Cart\", \" in Lox's saddlebags\", " +
                "\" in your backpack\"). Optional {item} names the item. Never names a boss. " +
                "Empty = the game's usual message instead.");
            AnnounceTierUnlock = Admin(messages, "AnnounceTierUnlock", true,
                "When a boss's world key is first set, announce to everyone that portals accept more goods. " +
                "Uses the tier's UnlockMessage; never names a boss.");
            AccurateTooltips = Admin(messages, "AccurateTooltips", true,
                "Item tooltips and inventory icons show whether the item can be teleported RIGHT NOW for this " +
                "player, using the same check the portal uses.");

            const string speed = "4 - Portal Speed";
            FadeSeconds = Admin(speed, "FadeSeconds", 1f,
                "How long the screen takes to fade to black when you step into a portal, and back in when you " +
                "arrive. 1 = the game's usual. 0 = the teleport screen appears the moment you touch the portal. " +
                "Does nothing while QuickTeleport, FastTeleport or Proper Portals is installed (they set this themselves).",
                new AcceptableValueRange<float>(0f, 3f));
            MinimumLoadingSeconds = Admin(speed, "MinimumLoadingSeconds", 8f,
                "Shortest time a teleport's loading screen lasts. 8 = the game's usual wait. Lower it to cut the " +
                "built-in delay, down to 0 for none. The screen still stays up while the destination is loading, " +
                "and you're only moved once the screen is black (and after at least a quarter second, so a pulled " +
                "cart can let go first). Does nothing while QuickTeleport, FastTeleport or Proper Portals is installed.",
                new AcceptableValueRange<float>(0f, 8f));

            const string travel = "5 - Travel";
            BringMount = Admin(travel, "BringMount", false,
                "Ride into a portal and your mount comes with you; you're put back in the saddle on the other side. " +
                "Items in its saddlebags are gated like your own. Does nothing while TeleportEverything is on (either version).");
            BringCart = Admin(travel, "BringCart", false,
                "A cart you're pulling comes through a portal with you and is hitched back up on the other side. " +
                "Its cargo is gated like your own inventory. Does nothing while TeleportEverything is on (either version).");
            BringTames = Admin(travel, "BringTames", false,
                "Tames near you come through a portal with you. The Tame... settings right below choose which, and " +
                "only apply while this is on. Items in their saddlebags are gated like your own. Does nothing while " +
                "TeleportEverything is on (either version).");

            const string tameOnly = "Only used when BringTames is on. ";
            TameMode = Admin(travel, "TameMode", TameSelection.Following,
                tameOnly + "Which tames come along. Following = only tames following you. FollowingOrNamed = also tames " +
                "you've named. Named = only named tames. AllTamed = every tame in range (penned animals too). " +
                "Tames following another player never come.");
            TameRadius = Admin(travel, "TameRadius", 10f,
                tameOnly + "How far from you (metres, sideways) a tame can be and still come along.",
                new AcceptableValueRange<float>(1f, 50f));
            TameHeightRange = Admin(travel, "TameHeightRange", 3f,
                tameOnly + "How far above or below you (metres) a tame can be and still come along. Keeps animals on other " +
                "floors of your base behind.",
                new AcceptableValueRange<float>(0.5f, 50f));
            TameMaxCount = Admin(travel, "TameMaxCount", 5,
                tameOnly + "Most tames one trip takes along. The nearest come first.",
                new AcceptableValueRange<int>(1, 50));
            TameAllowList = Admin(travel, "TameAllowList", "",
                tameOnly + "If not empty, only these creatures can come along. Prefab names, comma-separated. " +
                "Example: Wolf, Lox, Boar, Hen, Asksvin");
            TameBlockList = Admin(travel, "TameBlockList", "",
                tameOnly + "These creatures never come along. Prefab names, comma-separated. Example: Hen, Chicken");
            TameIncludeSummons = Admin(travel, "TameIncludeSummons", false,
                tameOnly + "Also take summoned creatures that follow you (skeletons, trolls and the like from staffs).");

            const string compat = "6 - Compatibility";
            GateCompanionCargo = Admin(compat, "GateCompanionCargo", true,
                "When another mod takes a cart or tames through a portal with you, gate the cart's cargo and the " +
                "tames' saddlebags (LoxSaddleBags, OdinHorse, or any container on a tame) like your own inventory. " +
                "Covers a cart you're pulling, and tames from TeleportEverything or from Waypoints with " +
                "'Teleport Tames' on. (What [5 - Travel] brings along is always gated.)");
            WarnOnConflictingMods = Admin(compat, "WarnOnConflictingMods", true,
                "Log a warning at startup if another mod that changes item teleport rules is installed (or set to " +
                "let every item through), and once per item if a tier-listed item is teleportable anyway.");

            const string admin = "7 - Admin";
            AdminBypass = Admin(admin, "AdminBypass", false,
                "Server admins ignore the gate (useful for building/testing).");
            StatusCommand = Admin(admin, "StatusCommand", true,
                "Admin console command 'portalgate status': lists each tier as locked/unlocked for the world " +
                "and for the calling player. Admin-only; shows boss names.");
            LogItemChecks = Admin(admin, "LogItemChecks", true,
                "At startup, check the item lists and log problems: names that aren't items in this version of " +
                "Valheim, items listed in two tiers, and teleport-blocked items no tier lists (those wait for " +
                "the final tier).");

            const string client = "8 - Client";
            HintPositionSetting = Client(client, "HintPosition", HintPosition.Centered,
                "Where hints appear: Centered or TopLeft.");
            HintCooldownSeconds = Client(client, "HintCooldownSeconds", 5f,
                "Minimum seconds between repeat hints while standing in a portal.",
                new AcceptableValueRange<float>(0f, 60f));

            for (int i = 0; i < TierDefaults.Length; i++)
            {
                string[] d = TierDefaults[i];
                string section = $"T{i} - {d[0]}";
                bool final = i == TierDefaults.Length - 1;
                order = 0;
                tierEntries.Add(new TierEntries
                {
                    Name = d[0],
                    GlobalKey = Admin(section, "GlobalKey", d[1], "World kill flag for this tier's boss."),
                    BossItem = Admin(section, "BossItem", d[2], "Boss drop (prefab name) the player must have held."),
                    AllowEverything = final
                        ? Admin(section, "AllowEverything", true,
                            "Final tier: unlocks every teleport-blocked item, including unlisted ones " +
                            "(except NeverTeleport, and cheat-tier unless FinalTierIncludesCheatTier).")
                        : null,
                    Items = Admin(section, "Items", d[3], "Item prefab names this tier unlocks, comma-separated."),
                    Hint = Admin(section, "Hint", d[4],
                        final ? "Hint while locked. Also used for any unlisted blocked item. Never name a boss."
                              : "Hint while locked. Never name a boss."),
                    UnlockMessage = Admin(section, "UnlockMessage", d[5],
                        "Server-wide message when this tier's world key is first set. Never name a boss."),
                });
            }
        }

        /// <summary>Rebuilds Tiers and NeverTeleportItems from the current config values.</summary>
        public static void Parse()
        {
            var tiers = new List<Tier>();
            for (int i = 0; i < tierEntries.Count; i++)
            {
                TierEntries e = tierEntries[i];
                tiers.Add(new Tier
                {
                    Index = i,
                    Name = e.Name,
                    GlobalKey = e.GlobalKey.Value.Trim().ToLowerInvariant(), // world keys are stored lower-case
                    BossItem = e.BossItem.Value.Trim(),
                    Items = SplitList(e.Items.Value),
                    Hint = e.Hint.Value.Trim(),
                    UnlockMessage = e.UnlockMessage.Value.Trim(),
                    AllowEverything = e.AllowEverything != null && e.AllowEverything.Value,
                });
            }
            NeverTeleportItems = SplitList(NeverTeleport.Value);
            TameAllow = new HashSet<string>(SplitList(TameAllowList.Value), StringComparer.OrdinalIgnoreCase);
            TameBlock = new HashSet<string>(SplitList(TameBlockList.Value), StringComparer.OrdinalIgnoreCase);

            // Hildir's chests: never teleport, unless HildirChestsByTier puts each in its biome's tier.
            foreach (var chest in HildirChestTiers)
            {
                if (HildirChestsByTier.Value) tiers[chest.Value].Items.Add(chest.Key);
                else NeverTeleportItems.Add(chest.Key);
            }
            var tierOfItem = new Dictionary<string, Tier>();
            foreach (Tier t in tiers)
            {
                foreach (string item in t.Items)
                {
                    if (!tierOfItem.ContainsKey(item)) tierOfItem[item] = t; // listed twice: the earlier tier (LogItemChecks warns)
                }
            }
            TierOfItem = tierOfItem;
            Tiers = tiers;

            // Keep boss-item tokens resolved if the game's item list is already loaded.
            if (ObjectDB.instance != null)
            {
                foreach (Tier t in tiers)
                    t.BossItemToken = GetItemToken(t.BossItem);
            }
        }

        /// <summary>Logs the parsed tier table.</summary>
        public static void LogSummary()
        {
            var lines = new List<string>
            {
                $"Settings: Enabled={Enabled.Value}, UnlockWhen={UnlockWhen.Value}, Cumulative={CumulativeTiers.Value}, " +
                $"LockListedItemsAlways={LockListedItemsAlways.Value}, AdminBypass={AdminBypass.Value}, " +
                $"BringMount={BringMount.Value}, BringCart={BringCart.Value}, BringTames={BringTames.Value}",
                "NeverTeleport: " + Describe(NeverTeleportItems),
                "Tiers:",
            };
            foreach (Tier t in Tiers)
            {
                string token = t.BossItemToken != null ? $" ({t.BossItemToken})" : "";
                string items = t.AllowEverything ? "EVERYTHING" + (t.Items.Count > 0 ? " + " + Describe(t.Items) : "")
                                                 : Describe(t.Items);
                lines.Add($"  T{t.Index} {t.Name,-12} key={t.GlobalKey}  bossItem={t.BossItem}{token}  items={items}");
            }
            Jotunn.Logger.LogInfo(string.Join("\n", lines));
        }

        /// <summary>
        /// LogItemChecks: warns about item/boss-drop IDs that don't exist in the game, missing tier keys and
        /// items listed in two tiers, then lists blocked items no tier mentions (Hints.LogUntieredItems).
        /// Needs ObjectDB, so it runs once items are registered (and again after config changes).
        /// </summary>
        public static void Validate()
        {
            if (ObjectDB.instance == null) return;

            foreach (Tier t in Tiers)
                t.BossItemToken = GetItemToken(t.BossItem);

            if (!LogItemChecks.Value) return;

            int problems = 0;
            void Check(string id, string where)
            {
                if (ObjectDB.instance.GetItemPrefab(id) != null) return;
                problems++;
                Jotunn.Logger.LogWarning($"LogItemChecks: '{id}' in {where} is not an item in this version of Valheim.");
            }

            foreach (string id in NeverTeleportItems) Check(id, "NeverTeleport");

            var seen = new Dictionary<string, int>();
            foreach (Tier t in Tiers)
            {
                string section = $"[T{t.Index} - {t.Name}]";
                // An empty key or boss item that UnlockWhen needs keeps the tier locked forever (fails closed).
                if (string.IsNullOrEmpty(t.GlobalKey) && NeedsBossKill)
                {
                    problems++;
                    Jotunn.Logger.LogWarning($"LogItemChecks: {section} has no GlobalKey, so it never unlocks.");
                }
                if (string.IsNullOrEmpty(t.BossItem))
                {
                    if (NeedsBossDrop)
                    {
                        problems++;
                        Jotunn.Logger.LogWarning($"LogItemChecks: {section} has no BossItem, so it never unlocks.");
                    }
                }
                else Check(t.BossItem, section + " BossItem");

                foreach (string id in t.Items)
                {
                    Check(id, section + " Items");
                    if (seen.TryGetValue(id, out int other))
                    {
                        problems++;
                        Jotunn.Logger.LogWarning($"LogItemChecks: '{id}' is listed in both T{other} and T{t.Index}.");
                    }
                    else seen[id] = t.Index;
                }
            }

            if (problems == 0)
                Jotunn.Logger.LogInfo("LogItemChecks: all item and boss-drop IDs exist.");
            Hints.LogUntieredItems();
        }

        /// <summary>
        /// Settings renamed or merged in 1.0.0. Their old lines stay in existing config files, where BepInEx keeps
        /// them as "orphaned" entries: carry each old value over to its new setting, then drop the old line.
        /// Runs before the file is first saved, so an old value is never overwritten by a new default.
        /// </summary>
        private static void Migrate()
        {
            // The hint text from before {where} existed, never changed by the admin: give it the new default.
            if (HintFormat.Value == Hints.OldDefaultFormat) HintFormat.Value = Hints.DefaultFormat;

            try
            {
                if (!(AccessTools.Property(typeof(ConfigFile), "OrphanedEntries")?.GetValue(config)
                        is Dictionary<ConfigDefinition, string> old) || old.Count == 0)
                    return;

                string Take(string section, string key)
                {
                    var definition = new ConfigDefinition(section, key);
                    if (!old.TryGetValue(definition, out string value)) return null;
                    old.Remove(definition);
                    return value;
                }
                bool? Flag(string section, string key) =>
                    bool.TryParse(Take(section, key), out bool b) ? b : (bool?)null;
                void Copy(string section, string key, ConfigEntryBase to)
                {
                    string value = Take(section, key);
                    if (value != null) to.SetSerializedValue(value);
                }

                const string g = "0.1 - General", i = "0.2 - Item Rules", m = "0.3 - Messages", a = "0.4 - Admin",
                    c = "0.5 - Compatibility";

                Copy(g, "Enabled", Enabled);
                Copy(g, "CumulativeTiers", CumulativeTiers);
                bool? worldKey = Flag(g, "RequireWorldKey"), bossItem = Flag(g, "RequirePlayerBossItem");
                if (bossItem == true)
                    UnlockWhen.Value = worldKey == false ? UnlockRule.DropHeld : UnlockRule.BossKilledAndDropHeld;
                else if (bossItem == false || worldKey != null)
                    UnlockWhen.Value = UnlockRule.BossKilled; // both off always fell back to the world key

                Copy(i, "NeverTeleport", NeverTeleport);
                Copy(i, "HildirChestsByTier", HildirChestsByTier);
                Copy(i, "GateListedVanillaItems", LockListedItemsAlways);
                Copy(i, "FinalTierIncludesCheatTier", FinalTierIncludesCheatTier);
                bool? unmapped = Flag(i, "LogUntieredItems"), ids = Flag(i, "ValidateItemIds");
                if (unmapped != null || ids != null)
                    LogItemChecks.Value = unmapped != false || ids != false;

                bool? showHint = Flag(m, "ShowUnlockHint");
                Copy(m, "HintFormat", HintFormat);
                if (HintFormat.Value == Hints.OldDefaultFormat) HintFormat.Value = Hints.DefaultFormat;
                if (showHint == false) HintFormat.Value = "";
                Copy(m, "AccurateTooltips", AccurateTooltips);
                Copy(m, "AnnounceTierUnlock", AnnounceTierUnlock);
                Copy(m, "HintPosition", HintPositionSetting);
                Copy(m, "HintCooldownSeconds", HintCooldownSeconds);

                Copy(a, "AdminBypass", AdminBypass);
                Copy(a, "EnableStatusCommand", StatusCommand);

                Copy(c, "RespectVanillaAllowAll", RespectVanillaAllowAll);
                Copy(c, "WarnOnConflictingMods", WarnOnConflictingMods);
                // Two switches became one: keep gating unless both were off.
                bool? cart = Flag(c, "GateAttachedCartCargo"), tames = Flag(c, "GateTameCargo");
                if (cart != null || tames != null)
                    GateCompanionCargo.Value = cart != false || tames != false;

                Take("0.6 - XPortal Compatibility", "LogPortalModDetection"); // folded into WarnOnConflictingMods
            }
            catch (Exception e)
            {
                // A BepInEx update could rename what this reads: then old settings start from the new defaults.
                Jotunn.Logger.LogWarning($"Couldn't carry settings over from an older version; check the config. {e.Message}");
            }
        }

        private static string GetItemToken(string prefab)
        {
            if (string.IsNullOrEmpty(prefab)) return null;
            // Unity's own null checks, not ?. (which misses destroyed Unity objects).
            var prefabObject = ObjectDB.instance.GetItemPrefab(prefab);
            if (!prefabObject) return null;
            ItemDrop drop = prefabObject.GetComponent<ItemDrop>();
            return drop ? drop.m_itemData.m_shared.m_name : null;
        }

        private static HashSet<string> SplitList(string value) =>
            new HashSet<string>(value.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                                     .Select(s => s.Trim()).Where(s => s.Length > 0));

        private static string Describe(HashSet<string> set) => set.Count == 0 ? "(none)" : string.Join(", ", set);

        // Server-synced; only admins can change it.
        private static ConfigEntry<T> Admin<T>(string section, string key, T value, string description,
            AcceptableValueBase range = null) => Bind(section, key, value, description, range, adminOnly: true);

        // Local to each player; not synced.
        private static ConfigEntry<T> Client<T>(string section, string key, T value, string description,
            AcceptableValueBase range = null) => Bind(section, key, value, description, range, adminOnly: false);

        private static ConfigEntry<T> Bind<T>(string section, string key, T value, string description,
            AcceptableValueBase range, bool adminOnly)
        {
            // Descending Order keeps the F1 menu in the same order as the spec.
            var attributes = new ConfigurationManagerAttributes { IsAdminOnly = adminOnly, Order = --order };
            if (!adminOnly) description += " (Not synced.)";
            return config.Bind(section, key, value, new ConfigDescription(description, range, attributes));
        }
    }
}
