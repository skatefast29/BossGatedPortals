using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;

namespace BossGatedPortals
{
    /// <summary>
    /// Replaces vanilla's "an item prevents teleport" message with a zone hint for the earliest
    /// locked tier. Hints come from config text and never name a boss.
    /// </summary>
    internal static class Hints
    {
        /// <summary>Items that failed Gate.CanTeleport in the last local-inventory portal check.</summary>
        public static readonly List<ItemDrop.ItemData> LastBlocked = new List<ItemDrop.ItemData>();

        private static float lastHintTime = float.NegativeInfinity;

        /// <summary>
        /// The hint message for the current blocking items, or null to keep vanilla's message
        /// (e.g. only NeverTeleport or cheat-tier items that no tier can unlock).
        /// </summary>
        public static string BuildMessage()
        {
            List<Tier> tiers = Settings.Tiers;
            if (tiers.Count == 0) return null;
            Tier final = tiers[tiers.Count - 1];

            Tier earliest = null;
            ItemDrop.ItemData blocker = null;
            foreach (ItemDrop.ItemData item in LastBlocked)
            {
                string prefab = Gate.PrefabName(item);
                if (prefab != null && Settings.NeverTeleportItems.Contains(prefab)) continue;

                Tier tier;
                if (item.m_shared.m_toolTier >= 1000)
                {
                    if (!Settings.FinalTierIncludesCheatTier.Value || !final.AllowEverything) continue;
                    tier = final;
                }
                else
                {
                    // Unmapped items wait for the final tier.
                    tier = (prefab != null ? Gate.TierFor(prefab) : null) ?? final;
                }

                if (earliest == null || tier.Index < earliest.Index)
                {
                    earliest = tier;
                    blocker = item;
                }
            }

            if (earliest == null || string.IsNullOrEmpty(earliest.Hint)) return null;

            // Item names stay as $tokens; MessageHud localizes the whole message.
            return Settings.HintFormat.Value
                .Replace("{hint}", earliest.Hint)
                .Replace("{item}", blocker.m_shared.m_name);
        }

        /// <summary>At startup, lists teleport-blocked items that no tier mentions.</summary>
        public static void LogUnmappedItems()
        {
            if (ObjectDB.instance == null || !Settings.LogUnmappedItems.Value) return;

            var unmapped = new List<string>();
            foreach (GameObject prefab in ObjectDB.instance.m_items)
            {
                ItemDrop drop = prefab ? prefab.GetComponent<ItemDrop>() : null;
                if (drop == null || drop.m_itemData.m_shared.m_teleportable) continue;
                if (Settings.NeverTeleportItems.Contains(prefab.name) || Gate.TierFor(prefab.name) != null) continue;
                unmapped.Add(prefab.name);
            }

            if (unmapped.Count == 0)
                Jotunn.Logger.LogInfo("LogUnmappedItems: every teleport-blocked item is listed in a tier.");
            else
                Jotunn.Logger.LogInfo("LogUnmappedItems: teleport-blocked items not in any tier " +
                    "(locked until the final tier): " + string.Join(", ", unmapped.OrderBy(n => n)));
        }

        /// <summary>
        /// Vanilla shows "$msg_noteleport" when the portal's item check fails. For the local player,
        /// swap it for the hint at the player's chosen position, at most once per cooldown.
        /// </summary>
        [HarmonyPatch(typeof(Player), nameof(Player.Message))]
        private static class PlayerMessagePatch
        {
            private static bool Prefix(Player __instance, ref MessageHud.MessageType type, ref string msg)
            {
                try
                {
                    return SwapForHint(__instance, ref type, ref msg);
                }
                catch (Exception e)
                {
                    Failsafe.Report("Unlock hint", e);
                    return true; // vanilla message
                }
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static bool SwapForHint(Player player, ref MessageHud.MessageType type, ref string msg)
            {
                if (msg != "$msg_noteleport" || player != Player.m_localPlayer ||
                    !Settings.Enabled.Value || !Settings.ShowUnlockHint.Value)
                    return true;

                string hint = BuildMessage();
                if (hint == null) return true; // nothing a tier can unlock: vanilla message

                if (Time.time - lastHintTime < Settings.HintCooldownSeconds.Value)
                    return false; // too soon: show nothing
                lastHintTime = Time.time;

                msg = hint;
                type = Settings.HintPositionSetting.Value == HintPosition.TopLeft
                    ? MessageHud.MessageType.TopLeft
                    : MessageHud.MessageType.Center;
                return true;
            }
        }
    }
}
