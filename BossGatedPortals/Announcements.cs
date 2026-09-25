using System;
using System.Runtime.CompilerServices;
using HarmonyLib;

namespace BossGatedPortals
{
    /// <summary>
    /// When a tier's world key is set for the first time, tell everyone on the server with the
    /// tier's UnlockMessage (config text; never names a boss).
    /// World keys are only ever added on the server, in ZoneSystem.RPC_SetGlobalKey (boss kills and
    /// the "setkey" command both end up there), so this runs on the server/host only.
    /// </summary>
    [HarmonyPatch(typeof(ZoneSystem), "RPC_SetGlobalKey")]
    internal static class Announcements
    {
        // __state = the key was already set before this call (then nothing is announced).
        private static void Prefix(ZoneSystem __instance, string name, out bool __state)
        {
            try
            {
                __state = WasSet(__instance, name);
            }
            catch (Exception e)
            {
                Failsafe.Report("Tier unlock announcement", e);
                __state = true; // treat as already set: announce nothing
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static bool WasSet(ZoneSystem zoneSystem, string name) => zoneSystem.GetGlobalKey(name);

        private static void Postfix(ZoneSystem __instance, string name, bool __state)
        {
            try
            {
                Announce(__instance, name, __state);
            }
            catch (Exception e)
            {
                Failsafe.Report("Tier unlock announcement", e);
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void Announce(ZoneSystem zoneSystem, string name, bool wasSet)
        {
            if (wasSet || !Settings.Enabled.Value || !Settings.AnnounceTierUnlock.Value) return;
            if (!zoneSystem.GetGlobalKey(name)) return;

            string key = name.Trim().ToLowerInvariant();
            foreach (Tier tier in Settings.Tiers)
            {
                if (tier.GlobalKey != key || string.IsNullOrEmpty(tier.UnlockMessage)) continue;

                Jotunn.Logger.LogInfo($"AnnounceTierUnlock: '{key}' set; announcing T{tier.Index} {tier.Name}.");
                // Same RPC as MessageHud.MessageAll, which needs a HUD (a dedicated server has none).
                ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.Everybody, "ShowMessage",
                    (int)MessageHud.MessageType.Center, tier.UnlockMessage);
            }
        }
    }
}
