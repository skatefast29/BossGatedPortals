using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;

namespace BossGatedPortals
{
    /// <summary>
    /// Startup log lines about other installed mods: item-teleport mods that clash with this one
    /// (WarnOnConflictingMods) and the portal mods we work alongside (LogPortalModDetection).
    /// Mods are matched by GUID or plugin name, ignoring case, spaces, dashes and underscores,
    /// because several of them don't publish their GUID.
    /// </summary>
    internal static class Compatibility
    {
        // Normalized name fragment -> what to tell the admin. Mods listed in SettingConflicts are skipped here.
        private static readonly Dictionary<string, string> Conflicts = new Dictionary<string, string>
        {
            { "unrestrictedportals", "it changes which items portals allow" },
            { "teleporteverything", "turn its item transport OFF; keep its creature/cart transport if you want it" },
            { "advancedportals", "it has its own item tiers for portals; two item gates together give unpredictable results" },
            { "progressionportals", "it has its own progression item gate; two item gates together give unpredictable results" },
            { "worldadvancementprogression", "it hooks the SAME item check and can switch a block back to allowed; never run it alongside this mod" },
            { "serversideqol.portalprogression", "it holds blocked items for you near a portal and gives them back " +
                "after you travel, which skips this gate; remove it or set its Enabled = false" },
            { "teleportationmeads", "its meads teleport you as if every item were allowed, so they carry items this mod's tiers still lock" },
        };

        // Mods that are fine to run alongside, except for one setting that lets every item through.
        // Where that setting overrides the item check, the gate's check runs first, so the setting wins.
        // Values compared as text.
        private static readonly SettingConflict[] SettingConflicts =
        {
            new SettingConflict("Azumatt.AzuMiscPatches", "set 'Disable Teleport check for items' to Off",
                ("3 - Inventory & Items", "Disable Teleport check for items", "On")),
            new SettingConflict("org.bepinex.plugins.valheim_plus", "set noTeleportPrevention to false in its [Items] section",
                ("Items", "enabled", "True"), ("Items", "noTeleportPrevention", "True")),
            new SettingConflict("zenox.teleporteverything", "set TransportRestrictedItems to false in its [Items] section",
                ("General", "Enabled", "True"), ("Items", "TransportRestrictedItems", "True")),
            new SettingConflict("org.bepinex.plugins.targetportal", "set 'Ignore item teleport restrictions' to Default or Never",
                ("1 - General", "Ignore item teleport restrictions", "Always")),
            new SettingConflict("com.xman0922.unifiedtargetportal", "set IgnoreItemTeleportRestrictions to Default or Never",
                ("Portals", "IgnoreItemTeleportRestrictions", "Always")),
            new SettingConflict("RustyMods.PortalStations", "set '1 - Teleport Anything' to Off",
                ("Settings", "1 - Teleport Anything", "On")),
            new SettingConflict("RustyMods.Waypoints", "set '3 - Teleport Anything' to Off",
                ("2 - Settings", "3 - Teleport Anything", "On")),
            new SettingConflict("shudnal.Waystones", "set 'Ignore nonteleportable items to start search' to false",
                ("Restrictions", "Ignore nonteleportable items to start search", "True")),
            new SettingConflict("marlthon.ReturnScroll", "set 'Allow Teleport Without Restriction' to Off",
                ("1 - General", "Allow Teleport Without Restriction", "On")),
            new SettingConflict("sighsorry.PortalRules", "set 'Portal Fare Mode' to Off (Pay lets players pay to carry " +
                "items this mod's tiers still lock)",
                ("5 - Portal Travel Costs", "Portal Fare Mode", "Pay")),
        };

        private class SettingConflict
        {
            public readonly string GUID;
            public readonly string Fix;
            public readonly (string Section, string Key, string Value)[] When; // all must match

            public SettingConflict(string guid, string fix, params (string, string, string)[] when)
            {
                GUID = guid;
                Fix = fix;
                When = when;
            }
        }

        private static readonly HashSet<string> warnedListed = new HashSet<string>();

        public static void LogInstalledMods()
        {
            List<PluginInfo> plugins = Chainloader.PluginInfos.Values
                .Where(p => p.Metadata.GUID != BossGatedPortals.PluginGUID).ToList();

            if (Settings.WarnOnConflictingMods.Value)
            {
                foreach (PluginInfo p in plugins)
                {
                    if (SettingConflicts.Any(c => c.GUID == p.Metadata.GUID)) continue;
                    string id = Normalize(p.Metadata.GUID) + " " + Normalize(p.Metadata.Name);
                    foreach (var conflict in Conflicts.Where(c => id.Contains(c.Key)))
                    {
                        Jotunn.Logger.LogWarning($"WarnOnConflictingMods: {Describe(p)} also changes item teleport rules: " +
                            $"{conflict.Value}.");
                    }
                }
                foreach (SettingConflict conflict in SettingConflicts)
                    CheckSetting(conflict);
            }

            if (Settings.LogPortalModDetection.Value)
            {
                PluginInfo xportal = plugins.FirstOrDefault(p => p.Metadata.GUID == BossGatedPortals.XPortalGUID);
                PluginInfo networks = plugins.FirstOrDefault(p => Matches(p, "xportalnetworks"));
                PluginInfo anyPortal = plugins.FirstOrDefault(p => p.Metadata.GUID == "com.sweetgiorni.anyportal" || Matches(p, "anyportal"));

                Jotunn.Logger.LogInfo("LogPortalModDetection: XPortal " + (xportal != null ? Describe(xportal) : "not installed") +
                    ", XPortalNetworks " + (networks != null ? Describe(networks) : "not installed") + ".");
                if (anyPortal != null)
                {
                    Jotunn.Logger.LogWarning($"LogPortalModDetection: {Describe(anyPortal)} is installed. " +
                        "XPortal refuses to load alongside it; remove AnyPortal and use XPortal instead.");
                }
            }
        }

        /// <summary>
        /// Warns if the mod is installed with the setting that lets every item through. Reads this machine's
        /// config: on a server that's the value players get; on a client the server's synced value may differ.
        /// </summary>
        private static void CheckSetting(SettingConflict conflict)
        {
            if (!Chainloader.PluginInfos.TryGetValue(conflict.GUID, out PluginInfo p) || p.Instance == null) return;

            ConfigFile config = p.Instance.Config;
            bool found = true, on = true;
            foreach (var (section, key, value) in conflict.When)
            {
                var definition = new ConfigDefinition(section, key);
                if (!config.ContainsKey(definition)) { found = false; break; }
                on &= string.Equals(config[definition].BoxedValue?.ToString(), value, StringComparison.OrdinalIgnoreCase);
            }

            if (!found)
                Jotunn.Logger.LogWarning($"WarnOnConflictingMods: {Describe(p)} can let every item through portals, " +
                    $"overriding this mod. Make sure it's off ({conflict.Fix}).");
            else if (on)
                Jotunn.Logger.LogWarning($"WarnOnConflictingMods: {Describe(p)} is set to let every item through " +
                    $"portals, which overrides this mod's tiers. To use the tiers, {conflict.Fix}.");
        }

        /// <summary>
        /// Once per item: a tier lists it, but it's teleportable anyway (in vanilla, or because another mod such
        /// as Creature Level &amp; Loot Control or ValheimPlus made it so), so its tier never applies.
        /// </summary>
        public static void WarnListedButTeleportable(string prefab, Tier tier)
        {
            if (!Settings.WarnOnConflictingMods.Value || !warnedListed.Add(prefab)) return;
            Jotunn.Logger.LogWarning($"WarnOnConflictingMods: '{prefab}' is listed in the {tier.Name} tier but is " +
                "teleportable anyway (vanilla, or another mod changed it), so it passes portals before that tier " +
                "unlocks. To gate it, set GateListedVanillaItems = true.");
        }

        private static bool Matches(PluginInfo p, string fragment) =>
            Normalize(p.Metadata.GUID).Contains(fragment) || Normalize(p.Metadata.Name).Contains(fragment);

        private static string Normalize(string s) =>
            new string((s ?? "").ToLowerInvariant().Where(c => c != ' ' && c != '-' && c != '_').ToArray());

        private static string Describe(PluginInfo p) => $"{p.Metadata.Name} {p.Metadata.Version} ({p.Metadata.GUID})";
    }
}
