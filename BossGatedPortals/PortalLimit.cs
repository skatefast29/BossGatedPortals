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
    /// </summary>
    internal static class PortalLimit
    {
        private const string CountRpc = "BossGatedPortals_PortalCount";
        private const float CountSeconds = 2f;

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
            if (!Settings.Enabled.Value || !Settings.PortalLimitEnabled.Value) return true;
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
        /// joined) doesn't lock.
        /// </summary>
        public static bool PortalsUsable(out string message)
        {
            message = null;
            if (!Settings.Enabled.Value || !Settings.PortalLimitEnabled.Value || !Settings.PortalLimitLockWhenOver.Value)
                return true;

            int limit = Limit();
            int count = Count();
            if (limit < 0 || count <= limit) return true;

            message = Settings.PortalLimitOverMessage.Value
                .Replace("{count}", count.ToString())
                .Replace("{limit}", limit.ToString());
            return false;
        }

        /// <summary>
        /// The world's portal limit, or -1 for no limit: StartLimit, raised by each tier whose boss has been
        /// killed on this world. Always the world key, whatever UnlockWhen says (one limit for everyone).
        /// </summary>
        public static int Limit()
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
        public static int Count() => ZNet.instance && ZNet.instance.IsServer() ? CountNow() : knownCount;

        private static int CountNow()
        {
            int count = 0;
            foreach (List<ZDO> sector in ZDOMan.instance.GetPortals().Values)
                count += sector.Count;
            return count;
        }

        /// <summary>A portal was just placed here: count it now, so a quick second one waits for the server.</summary>
        public static void CountPlaced()
        {
            if (knownCount >= 0) knownCount++;
        }

        /// <summary>Every frame from the plugin: the server sends the count to everyone every few seconds.</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void Tick()
        {
            if (Time.time < nextCount) return;
            nextCount = Time.time + CountSeconds;
            if (!Settings.Enabled.Value || !Settings.PortalLimitEnabled.Value) return;
            if (!ZNet.instance || !ZNet.instance.IsServer() || ZRoutedRpc.instance == null || ZDOMan.instance == null)
                return;
            ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.Everybody, CountRpc, CountNow());
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void Register()
        {
            knownCount = -1;
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
                if (player != Player.m_localPlayer || !IsPortal(piece)) return true;
                if (CanBuildPortal(out string message)) return true;
                player.Message(MessageHud.MessageType.Center, message);
                return false;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static void Placed(Player player, Piece piece)
            {
                if (player == Player.m_localPlayer && IsPortal(piece)) CountPlaced();
            }

            private static bool IsPortal(Piece piece) =>
                piece && Game.instance && Game.instance.PortalPrefabHash.Contains(piece.gameObject.name.GetStableHashCode());
        }
    }
}
