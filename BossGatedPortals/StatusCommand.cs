using System.Collections.Generic;
using Jotunn.Entities;
using Jotunn.Managers;

namespace BossGatedPortals
{
    /// <summary>
    /// Admin console command "portalgate status": each tier's world key, whether the calling player
    /// has held the boss drop, and whether the tier is unlocked for them. Shows world keys and boss
    /// items (effectively boss names), so it's refused for non-admins.
    /// "portalgate limit on|off": whether the portal limit applies to this world (see PortalLimit.WorldKey).
    /// </summary>
    internal class StatusCommand : ConsoleCommand
    {
        public override string Name => "portalgate";

        public override string Help => "portalgate status - BossGatedPortals tiers for the world and for you; " +
            "portalgate limit on|off - whether the portal limit applies to this world (admin only)";

        public override List<string> CommandOptionList() => new List<string> { "status", "limit" };

        public override void Run(string[] args, Terminal context)
        {
            string sub = args.Length > 0 ? args[0].ToLowerInvariant() : "";
            string choice = args.Length > 1 ? args[1].ToLowerInvariant() : "";
            bool setLimit = sub == "limit" && (choice == "on" || choice == "off");
            if (sub != "status" && !setLimit)
            {
                context.AddString("Usage: portalgate status | portalgate limit on|off");
                return;
            }
            if (!setLimit && !Settings.StatusCommand.Value)
            {
                context.AddString("portalgate status is turned off (StatusCommand).");
                return;
            }
            if (!SynchronizationManager.Instance.PlayerIsAdmin)
            {
                context.AddString("portalgate is for server admins only.");
                return;
            }

            Player player = Player.m_localPlayer;
            if (player == null || ZoneSystem.instance == null)
            {
                context.AddString("Join a world first.");
                return;
            }

            if (setLimit)
            {
                PortalLimit.SetWorldChoice(choice == "on");
                context.AddString($"Portal limit for this world set to {choice}." +
                    (Settings.PortalLimitEnabled.Value ? "" : " (PortalLimitEnabled is off in the config, so nothing is limited.)"));
                return;
            }

            context.AddString($"BossGatedPortals {BossGatedPortals.PluginVersion} - Enabled={Settings.Enabled.Value}, " +
                $"UnlockWhen={Settings.UnlockWhen.Value}, " +
                $"Cumulative={Settings.CumulativeTiers.Value}, AdminBypass={Settings.AdminBypass.Value}");

            foreach (Tier t in Settings.Tiers)
            {
                bool worldKey = !string.IsNullOrEmpty(t.GlobalKey) && ZoneSystem.instance.GetGlobalKey(t.GlobalKey);
                bool held = !string.IsNullOrEmpty(t.BossItemToken) && player.IsMaterialKnown(t.BossItemToken);
                bool unlocked = Gate.IsTierUnlocked(player, t);
                context.AddString($"T{t.Index} {t.Name}: world {(worldKey ? "UNLOCKED" : "locked")} ({t.GlobalKey}), " +
                    $"you {(held ? "have" : "have not")} held {t.BossItem} -> {(unlocked ? "UNLOCKED" : "LOCKED")} for you" +
                    $" (MaxPortals {t.MaxPortals})");
            }

            int limit = PortalLimit.Limit(), count = PortalLimit.Count();
            bool? world = PortalLimit.WorldChoice();
            context.AddString($"Portal limit {(Settings.PortalLimitEnabled.Value ? "ON" : "off")} in the config, " +
                $"{(world == null ? "not decided yet" : world == true ? "ON" : "off")} for this world " +
                $"('portalgate limit on|off'): " +
                $"{(count < 0 ? "?" : count.ToString())} portals in the world, limit {(limit < 0 ? "none" : limit.ToString())}");
        }
    }
}
