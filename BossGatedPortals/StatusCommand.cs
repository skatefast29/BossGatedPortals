using System.Collections.Generic;
using Jotunn.Entities;
using Jotunn.Managers;

namespace BossGatedPortals
{
    /// <summary>
    /// Admin console command "portalgate status": each tier's world key, whether the calling player
    /// has held the boss drop, and whether the tier is unlocked for them. Shows world keys and boss
    /// items (effectively boss names), so it's refused for non-admins.
    /// </summary>
    internal class StatusCommand : ConsoleCommand
    {
        public override string Name => "portalgate";

        public override string Help => "portalgate status - BossGatedPortals tiers for the world and for you (admin only)";

        public override List<string> CommandOptionList() => new List<string> { "status" };

        public override void Run(string[] args, Terminal context)
        {
            if (args.Length == 0 || args[0].ToLowerInvariant() != "status")
            {
                context.AddString("Usage: portalgate status");
                return;
            }
            if (!Settings.EnableStatusCommand.Value)
            {
                context.AddString("portalgate status is turned off (EnableStatusCommand).");
                return;
            }
            if (!SynchronizationManager.Instance.PlayerIsAdmin)
            {
                context.AddString("portalgate status is for server admins only.");
                return;
            }

            Player player = Player.m_localPlayer;
            if (player == null || ZoneSystem.instance == null)
            {
                context.AddString("Join a world first.");
                return;
            }

            context.AddString($"BossGatedPortals {BossGatedPortals.PluginVersion} - Enabled={Settings.Enabled.Value}, " +
                $"WorldKey={Settings.EffectiveRequireWorldKey}, PlayerBossItem={Settings.RequirePlayerBossItem.Value}, " +
                $"Cumulative={Settings.CumulativeTiers.Value}, AdminBypass={Settings.AdminBypass.Value}");

            foreach (Tier t in Settings.Tiers)
            {
                bool worldKey = !string.IsNullOrEmpty(t.GlobalKey) && ZoneSystem.instance.GetGlobalKey(t.GlobalKey);
                bool held = !string.IsNullOrEmpty(t.BossItemToken) && player.IsMaterialKnown(t.BossItemToken);
                bool unlocked = Gate.IsTierUnlocked(player, t);
                context.AddString($"T{t.Index} {t.Name}: world {(worldKey ? "UNLOCKED" : "locked")} ({t.GlobalKey}), " +
                    $"you {(held ? "have" : "have not")} held {t.BossItem} -> {(unlocked ? "UNLOCKED" : "LOCKED")} for you");
            }
        }
    }
}
