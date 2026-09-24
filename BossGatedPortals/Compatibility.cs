using System.Collections.Generic;
using System.Linq;
using BepInEx;
using BepInEx.Bootstrap;

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
        // Normalized name fragment -> what to tell the admin.
        private static readonly Dictionary<string, string> Conflicts = new Dictionary<string, string>
        {
            { "unrestrictedportals", "changes which items portals allow" },
            { "teleporteverything", "turn its item transport OFF (keep its creature/cart transport if you want it)" },
            { "advancedportals", "has its own item tiers for portals" },
            { "progressionportals", "has its own progression item gate" },
            { "worldadvancementprogression", "hooks the SAME item check with high priority and can switch a block back to allowed; never run it alongside this mod" },
            { "serversideqol", "its PortalProgression feature gates portal items too; turn that feature off" },
        };

        public static void LogInstalledMods()
        {
            List<PluginInfo> plugins = Chainloader.PluginInfos.Values
                .Where(p => p.Metadata.GUID != BossGatedPortals.PluginGUID).ToList();

            if (Settings.WarnOnConflictingMods.Value)
            {
                foreach (PluginInfo p in plugins)
                {
                    string id = Normalize(p.Metadata.GUID) + " " + Normalize(p.Metadata.Name);
                    foreach (var conflict in Conflicts.Where(c => id.Contains(c.Key)))
                    {
                        Jotunn.Logger.LogWarning($"WarnOnConflictingMods: {Describe(p)} also changes item teleport rules " +
                            $"({conflict.Value}). Two item-gate mods together give unpredictable results.");
                    }
                }
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

        private static bool Matches(PluginInfo p, string fragment) =>
            Normalize(p.Metadata.GUID).Contains(fragment) || Normalize(p.Metadata.Name).Contains(fragment);

        private static string Normalize(string s) =>
            new string((s ?? "").ToLowerInvariant().Where(c => c != ' ' && c != '-' && c != '_').ToArray());

        private static string Describe(PluginInfo p) => $"{p.Metadata.Name} {p.Metadata.Version} ({p.Metadata.GUID})";
    }
}
