using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Jotunn.Managers;
using UnityEngine;

namespace BossGatedPortals
{
    /// <summary>
    /// [9 - Portal Limit]: how many portals the whole world may hold, raised as bosses fall.
    /// Only the server knows every portal (a client only loads the ones near it), so the server counts
    /// them and sends the count to everyone every few seconds. Never touches portal pairing.
    /// Each world remembers whether the limit applies to it (world key "bgp_portallimit on/off"), decided by the
    /// server the first time it loads the world with the limit switched on: worlds that already have portals are
    /// left alone, so updating the mod never locks portals people use. Admins switch it with "portalgate limit".
    /// </summary>
    internal static class PortalLimit
    {
        private const string CountRpc = "BossGatedPortals_PortalCount";
        private const float CountSeconds = 2f;
        /// <summary>World key holding this world's choice: "bgp_portallimit on" or "bgp_portallimit off".</summary>
        public const string WorldKey = "bgp_portallimit";
        // Wait this long after the world starts before deciding, so it's fully loaded.
        private const float DecideAfterSeconds = 3f;
        private static float gameStartTime = float.PositiveInfinity;
        // Decided this session: once only, even if another mod blocks world keys (no retrying every tick).
        private static bool decided;

        // The count as the server last sent it (-1 = not heard yet). Unused on the server itself.
        private static int knownCount = -1;
        private static float nextCount;

        /// <summary>
        /// The one check for "may the local player build another portal right now". Returns false and fills
        /// `message` when the world's limit is reached (or the count isn't known yet: fails closed).
        /// </summary>
        public static bool CanBuildPortal(out string message)
        {
            message = null;
            if (!Active()) return true;
            if (Settings.PortalLimitAdminsExempt.Value && SynchronizationManager.Instance.PlayerIsAdmin) return true;

            int limit = Limit();
            if (limit < 0) return true;
            int count = Count();
            if (count < 0)
            {
                message = "Counting this world's portals; try again in a moment.";
                return false;
            }
            if (count < limit) return true;

            message = Settings.PortalLimitMessage.Value
                .Replace("{count}", count.ToString())
                .Replace("{limit}", limit.ToString());
            return false;
        }

        /// <summary>
        /// PortalLimitLockWhenOver: while the world holds more portals than its limit, no portal works for
        /// anyone until enough are destroyed. Returns false and fills `message` then. An unknown count (just
        /// joined) doesn't lock. portalAllowsAll = the portal lets every item through (the Stone Portal): with
        /// PortalLimitCountStonePortals off those are outside the limit, so they keep working.
        /// </summary>
        public static bool PortalsUsable(bool portalAllowsAll, out string message)
        {
            message = null;
            if (!Settings.PortalLimitLockWhenOver.Value || !Active()) return true;
            if (portalAllowsAll && !Settings.PortalLimitCountStonePortals.Value) return true;

            int limit = Limit();
            int count = Count();
            if (limit < 0 || count <= limit) return true;

            message = Settings.PortalLimitOverMessage.Value
                .Replace("{count}", count.ToString())
                .Replace("{limit}", limit.ToString());
            return false;
        }

        /// <summary>The limit is switched on in the config and applies to this world (see WorldKey).</summary>
        public static bool Active() =>
            Settings.Enabled.Value && Settings.PortalLimitEnabled.Value && WorldChoice() == true;

        /// <summary>This world's choice: true = limited, false = left alone, null = not decided yet.</summary>
        public static bool? WorldChoice()
        {
            if (ZoneSystem.instance == null || !ZoneSystem.instance.GetGlobalKey(WorldKey, out string value)) return null;
            return value == "on";
        }

        /// <summary>Sets this world's choice (admin command; any client can ask the server, like "setkey").</summary>
        public static void SetWorldChoice(bool on) => ZoneSystem.instance.SetGlobalKey(WorldKey + (on ? " on" : " off"));

        private static int limitFrame = -1, frameLimit;

        /// <summary>
        /// The world's portal limit, or -1 for no limit: StartLimit, raised by each tier whose boss has been
        /// killed on this world. Always the world key, whatever UnlockWhen says (one limit for everyone).
        /// </summary>
        public static int Limit()
        {
            // Portals ask every frame (their glow), and each world-key lookup copies the key's name: once per frame.
            if (limitFrame == Time.frameCount) return frameLimit;
            limitFrame = Time.frameCount;
            frameLimit = LimitNow();
            return frameLimit;
        }

        private static int LimitNow()
        {
            int limit = Settings.PortalLimitStart.Value;
            foreach (Tier t in Settings.Tiers)
            {
                if (string.IsNullOrEmpty(t.GlobalKey) || !ZoneSystem.instance.GetGlobalKey(t.GlobalKey)) continue;
                if (t.MaxPortals < 0) return -1;
                limit = Math.Max(limit, t.MaxPortals);
            }
            return limit;
        }

        /// <summary>Portals in the world: counted on the server, as last sent by it on a client (-1 = unknown).</summary>
        public static int Count()
        {
            if (!ZNet.instance || !ZNet.instance.IsServer()) return knownCount;
            // On the server/host, portals ask every frame (their glow): count once per frame.
            if (countFrame != Time.frameCount)
            {
                countFrame = Time.frameCount;
                frameCount = CountNow();
            }
            return frameCount;
        }

        private static int countFrame = -1, frameCount;

        private static int CountNow()
        {
            bool countAll = Settings.PortalLimitCountStonePortals.Value;
            int count = 0;
            foreach (List<ZDO> sector in ZDOMan.instance.GetPortals().Values)
            {
                if (countAll)
                {
                    count += sector.Count;
                    continue;
                }
                foreach (ZDO zdo in sector)
                {
                    if (!AllowsAllItems(zdo.GetPrefab())) count++;
                }
            }
            return count;
        }

        // Portal prefab hash -> it lets every item through (the Stone Portal). Filled as portals are looked up.
        private static readonly Dictionary<int, bool> allowsAll = new Dictionary<int, bool>();

        /// <summary>
        /// This portal prefab lets every item through, like the Stone Portal (its TeleportWorld.m_allowAllItems,
        /// the same flag the item gate respects). Read from the prefab, so modded all-items portals count too.
        /// </summary>
        public static bool AllowsAllItems(int prefabHash)
        {
            if (allowsAll.TryGetValue(prefabHash, out bool all)) return all;
            GameObject prefab = ZNetScene.instance ? ZNetScene.instance.GetPrefab(prefabHash) : null;
            if (!prefab) return false; // unknown for now: counts, asked again later
            TeleportWorld portal = prefab.GetComponentInChildren<TeleportWorld>(true);
            all = portal && portal.m_allowAllItems;
            allowsAll[prefabHash] = all;
            return all;
        }

        /// <summary>A portal was just placed here: count it now, so a quick second one waits for the server.</summary>
        public static void CountPlaced()
        {
            if (knownCount >= 0) knownCount++;
        }

        /// <summary>
        /// Every frame from the plugin, on the server only: decides a new world's choice once it has loaded, then
        /// sends the portal count to everyone every few seconds while the limit applies.
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void Tick()
        {
            if (Time.time < nextCount) return;
            nextCount = Time.time + CountSeconds;
            if (!Settings.Enabled.Value || !Settings.PortalLimitEnabled.Value) return;
            if (!ZNet.instance || !ZNet.instance.IsServer() || ZRoutedRpc.instance == null || ZDOMan.instance == null ||
                ZoneSystem.instance == null || Time.time < gameStartTime + DecideAfterSeconds)
                return;

            int count = CountNow();
            if (WorldChoice() == null)
            {
                if (decided) return;
                decided = true;
                // First time this world runs with the limit on: only a world without portals gets it.
                bool on = count == 0;
                SetWorldChoice(on);
                Jotunn.Logger.LogInfo(on
                    ? "Portal limit: this world has no portals yet, so the limit applies here."
                    : $"Portal limit: this world already has {count} portals, so the limit is left off here. " +
                      "An admin can turn it on with 'portalgate limit on'.");
                return;
            }
            if (Active()) ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.Everybody, CountRpc, count);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void Register()
        {
            knownCount = -1;
            decided = false;
            gameStartTime = Time.time;
            ZRoutedRpc.instance.Register<int>(CountRpc, (sender, count) => knownCount = count);
        }

        /// <summary>The count message needs registering once per session, after the network starts.</summary>
        [HarmonyPatch(typeof(Game), "Start")]
        private static class GameStartPatch
        {
            private static void Postfix()
            {
                try
                {
                    Register();
                }
                catch (Exception e)
                {
                    Failsafe.Report("Portal limit (count message)", e);
                }
            }
        }

        /// <summary>
        /// Stops placing a portal over the limit, before the game takes the materials. A prefix that skips the
        /// game's placement: there's no other way to refuse one build. On error the build goes ahead.
        /// </summary>
        [HarmonyPatch(typeof(Player), nameof(Player.TryPlacePiece))]
        private static class TryPlacePiecePatch
        {
            private static bool Prefix(Player __instance, Piece piece)
            {
                try
                {
                    return MayPlace(__instance, piece);
                }
                catch (Exception e)
                {
                    Failsafe.Report("Portal limit", e);
                    return true; // vanilla: build it
                }
            }

            private static void Postfix(Player __instance, Piece piece, bool __result)
            {
                try
                {
                    if (__result) Placed(__instance, piece);
                }
                catch (Exception e)
                {
                    Failsafe.Report("Portal limit (count)", e);
                }
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static bool MayPlace(Player player, Piece piece)
            {
                if (player != Player.m_localPlayer || !IsCountedPortal(piece)) return true;
                if (CanBuildPortal(out string message)) return true;
                player.Message(MessageHud.MessageType.Center, message);
                return false;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static void Placed(Player player, Piece piece)
            {
                if (player == Player.m_localPlayer && IsCountedPortal(piece)) CountPlaced();
            }

            /// <summary>A portal the limit counts: every portal, or with PortalLimitCountStonePortals off, not the Stone Portal.</summary>
            private static bool IsCountedPortal(Piece piece)
            {
                if (!piece || !Game.instance) return false;
                int hash = piece.gameObject.name.GetStableHashCode();
                if (!Game.instance.PortalPrefabHash.Contains(hash)) return false;
                return Settings.PortalLimitCountStonePortals.Value || !AllowsAllItems(hash);
            }
        }
    }
}
