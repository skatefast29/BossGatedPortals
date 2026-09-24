using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;

namespace BossGatedPortals
{
    public enum HintPosition { Centered, TopLeft }

    /// <summary>
    /// Binds every setting from the BossGatedPortals.cfg spec and keeps a parsed copy of the tiers.
    /// Everything is server-synced and admin-only except HintPosition and HintCooldownSeconds.
    /// </summary>
    internal static class Settings
    {
        // [0.1 - General]
        public static ConfigEntry<bool> Enabled;
        public static ConfigEntry<bool> RequireWorldKey;
        public static ConfigEntry<bool> RequirePlayerBossItem;
        public static ConfigEntry<bool> CumulativeTiers;

        // [0.2 - Item Rules]
        public static ConfigEntry<string> NeverTeleport;
        public static ConfigEntry<bool> HildirChestsByTier;
        public static ConfigEntry<bool> GateListedVanillaItems;
        public static ConfigEntry<bool> FinalTierIncludesCheatTier;
        public static ConfigEntry<bool> LogUnmappedItems;
        public static ConfigEntry<bool> ValidateItemIds;

        // [0.3 - Messages]
        public static ConfigEntry<bool> ShowUnlockHint;
        public static ConfigEntry<string> HintFormat;
        public static ConfigEntry<HintPosition> HintPositionSetting;
        public static ConfigEntry<float> HintCooldownSeconds;
        public static ConfigEntry<bool> AccurateTooltips;
        public static ConfigEntry<bool> AnnounceTierUnlock;

        // [0.4 - Admin]
        public static ConfigEntry<bool> AdminBypass;
        public static ConfigEntry<bool> EnableStatusCommand;

        // [0.5 - Compatibility] and [0.6 - XPortal Compatibility]
        public static ConfigEntry<bool> RespectVanillaAllowAll;
        public static ConfigEntry<bool> GateAttachedCartCargo;
        public static ConfigEntry<bool> WarnOnConflictingMods;
        public static ConfigEntry<bool> LogPortalModDetection;

        /// <summary>Parsed tiers, in order T0..T7. Rebuilt whenever a setting changes.</summary>
        public static List<Tier> Tiers = new List<Tier>();
        public static HashSet<string> NeverTeleportItems = new HashSet<string>();

        /// <summary>
        /// Safety rule: if both Require* settings are off, the world key is still required.
        /// </summary>
        public static bool EffectiveRequireWorldKey => RequireWorldKey.Value || !RequirePlayerBossItem.Value;

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

            const string general = "0.1 - General";
            Enabled = Admin(general, "Enabled", true, "Master switch for the mod.");
            RequireWorldKey = Admin(general, "RequireWorldKey", true,
                "The boss must have been killed ON THIS WORLD (world-wide global key, saved on the server).");
            RequirePlayerBossItem = Admin(general, "RequirePlayerBossItem", true,
                "The individual player must also have picked up that boss's drop at least once. " +
                "Makes EVERY player follow progression.\n" +
                "Safety rule: if this and RequireWorldKey are BOTH false, the world key is still required " +
                "and a warning is logged.");
            CumulativeTiers = Admin(general, "CumulativeTiers", false,
                "If true, unlocking a later tier also unlocks every earlier tier for that player. " +
                "false = each tier must be earned on its own.");

            const string items = "0.2 - Item Rules";
            NeverTeleport = Admin(items, "NeverTeleport", "",
                "Items that can NEVER be teleported, even after the final tier. Prefab names, comma-separated. " +
                "Example: DragonEgg");
            HildirChestsByTier = Admin(items, "HildirChestsByTier", false,
                "If true, Hildir's chests unlock with the tier of the biome they're found in " +
                "(Brass = Black Forest, Silver = Mountains, Bronze = Plains). " +
                "false = they never teleport, as in vanilla.");
            GateListedVanillaItems = Admin(items, "GateListedVanillaItems", false,
                "If true, items listed in a tier are gated even if vanilla normally lets them through portals. " +
                "false = tiers only UNLOCK items vanilla already blocks.");
            FinalTierIncludesCheatTier = Admin(items, "FinalTierIncludesCheatTier", false,
                "Vanilla blocks tool-tier 1000+ items at every portal. The only such item is FrozenKingDrop. " +
                "If true, the final tier also unlocks those.");
            LogUnmappedItems = Admin(items, "LogUnmappedItems", true,
                "At startup, log every teleport-blocked item not listed in any tier. " +
                "Unlisted blocked items stay locked until the final tier.");
            ValidateItemIds = Admin(items, "ValidateItemIds", true,
                "At startup, warn about any item or boss-drop ID in this file that doesn't exist in the game.");

            const string messages = "0.3 - Messages";
            ShowUnlockHint = Admin(messages, "ShowUnlockHint", true,
                "Show a hint when a blocked item stops teleporting. Never names a boss.");
            HintFormat = Admin(messages, "HintFormat", "An item blocks the portal. {hint}",
                "Message text. {hint} = the tier's Hint. Optional {item} names the blocked item. " +
                "If several tiers are locked, only the earliest locked tier's hint is shown.");
            HintPositionSetting = Client(messages, "HintPosition", HintPosition.Centered,
                "Where hints appear. Client setting: each player can choose.");
            HintCooldownSeconds = Client(messages, "HintCooldownSeconds", 5f,
                "Minimum seconds between repeat hints while standing in a portal. Client setting.",
                new AcceptableValueRange<float>(0f, 60f));
            AccurateTooltips = Admin(messages, "AccurateTooltips", true,
                "Item tooltips show whether the item can be teleported RIGHT NOW for this player, " +
                "using the same check the portal uses.");
            AnnounceTierUnlock = Admin(messages, "AnnounceTierUnlock", true,
                "When a boss's world key is first set, announce to everyone that portals accept more goods. " +
                "Uses the tier's UnlockMessage; never names a boss.");

            const string admin = "0.4 - Admin";
            AdminBypass = Admin(admin, "AdminBypass", false,
                "Server admins ignore the gate (useful for building/testing).");
            EnableStatusCommand = Admin(admin, "EnableStatusCommand", true,
                "Admin console command 'portalgate status': lists each tier as locked/unlocked for the world " +
                "and for the calling player. Admin-only; shows boss names.");

            const string compat = "0.5 - Compatibility";
            RespectVanillaAllowAll = Admin(compat, "RespectVanillaAllowAll", true,
                "Never block a portal that already lets everything through in vanilla (the Stone Portal), " +
                "and respect the 'Portals' world modifier when it allows all items.");
            GateAttachedCartCargo = Admin(compat, "GateAttachedCartCargo", true,
                "If a cart-teleport mod carries an attached cart through, gate its cargo with the same check.");
            WarnOnConflictingMods = Admin(compat, "WarnOnConflictingMods", true,
                "Log a warning at startup if another mod that changes item teleport rules is installed.");

            LogPortalModDetection = Admin("0.6 - XPortal Compatibility", "LogPortalModDetection", true,
                "Log the detected XPortal / XPortalNetworks version at startup, and warn if AnyPortal is present.");

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

            Parse();
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

            // Hildir's chests: never teleport, unless HildirChestsByTier puts each in its biome's tier.
            foreach (var chest in HildirChestTiers)
            {
                if (HildirChestsByTier.Value) tiers[chest.Value].Items.Add(chest.Key);
                else NeverTeleportItems.Add(chest.Key);
            }
            Tiers = tiers;

            // Keep boss-item tokens resolved if the game's item list is already loaded.
            if (ObjectDB.instance != null)
            {
                foreach (Tier t in tiers)
                    t.BossItemToken = GetItemToken(t.BossItem);
            }
        }

        /// <summary>Logs the parsed tier table and the both-false warning.</summary>
        public static void LogSummary()
        {
            if (!RequireWorldKey.Value && !RequirePlayerBossItem.Value)
            {
                Jotunn.Logger.LogWarning("RequireWorldKey and RequirePlayerBossItem are both false. " +
                    "Ignoring that: the boss's world key is still required for each tier.");
            }

            var lines = new List<string>
            {
                $"Settings: Enabled={Enabled.Value}, WorldKey={EffectiveRequireWorldKey}, " +
                $"PlayerBossItem={RequirePlayerBossItem.Value}, Cumulative={CumulativeTiers.Value}, " +
                $"GateListedVanillaItems={GateListedVanillaItems.Value}, AdminBypass={AdminBypass.Value}",
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
        /// Warns about item/boss-drop IDs that don't exist in the game, and items listed in two tiers.
        /// Needs ObjectDB, so it runs once items are registered (and again after config changes).
        /// </summary>
        public static void Validate()
        {
            if (ObjectDB.instance == null) return;

            foreach (Tier t in Tiers)
                t.BossItemToken = GetItemToken(t.BossItem);

            if (!ValidateItemIds.Value) return;

            int problems = 0;
            void Check(string id, string where)
            {
                if (ObjectDB.instance.GetItemPrefab(id) != null) return;
                problems++;
                Jotunn.Logger.LogWarning($"ValidateItemIds: '{id}' in {where} is not an item in this version of Valheim.");
            }

            foreach (string id in NeverTeleportItems) Check(id, "NeverTeleport");

            var seen = new Dictionary<string, int>();
            foreach (Tier t in Tiers)
            {
                string section = $"[T{t.Index} - {t.Name}]";
                if (string.IsNullOrEmpty(t.GlobalKey))
                {
                    problems++;
                    Jotunn.Logger.LogWarning($"ValidateItemIds: {section} has no GlobalKey.");
                }
                if (string.IsNullOrEmpty(t.BossItem))
                {
                    if (RequirePlayerBossItem.Value)
                    {
                        problems++;
                        Jotunn.Logger.LogWarning($"ValidateItemIds: {section} has no BossItem.");
                    }
                }
                else Check(t.BossItem, section + " BossItem");

                foreach (string id in t.Items)
                {
                    Check(id, section + " Items");
                    if (seen.TryGetValue(id, out int other))
                    {
                        problems++;
                        Jotunn.Logger.LogWarning($"ValidateItemIds: '{id}' is listed in both T{other} and T{t.Index}.");
                    }
                    else seen[id] = t.Index;
                }
            }

            if (problems == 0)
                Jotunn.Logger.LogInfo("ValidateItemIds: all item and boss-drop IDs exist.");
        }

        private static string GetItemToken(string prefab)
        {
            if (string.IsNullOrEmpty(prefab)) return null;
            return ObjectDB.instance.GetItemPrefab(prefab)?.GetComponent<ItemDrop>()?.m_itemData.m_shared.m_name;
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
