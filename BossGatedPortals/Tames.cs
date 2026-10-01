using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace BossGatedPortals
{
    /// <summary>
    /// Saddlebags on tames that teleport with the player (GateCompanionCargo), and on the mount and tames
    /// BringMount / BringTames take along (always gated, see Travel). In vanilla tames never teleport.
    /// These mods bring them along, and their bags are gated with the player's own inventory:
    /// - TeleportEverything (zenox.teleporteverything) takes nearby tames on every player teleport, chosen
    ///   by its own GetTransportableAllies; we call that same method, so the tames always match.
    /// - The TeleportEverything 1.0 fork (com.kpro.TeleportEverything) does the same with its own
    ///   SetIncludeMode + IsValidAlly, called here the way it calls them.
    /// - Waypoints (RustyMods.Waypoints) with "Teleport Tames" on takes every following tame, but only on
    ///   its own waypoint teleports: those bags are added only while its item check runs.
    /// Bags found on a tame:
    /// - any Container on the tame or its children (LoxSaddleBags puts one on the lox);
    /// - OdinHorse's saddlebags, saved in the horse's ZDO. Its "SaddlebagContainer" child only shows them
    ///   while open and is skipped; the saved data is the truth.
    /// Other mods are read through reflection: no build reference, and nothing runs without them.
    /// </summary>
    internal static class Tames
    {
        public const string TeleportEverythingGUID = "zenox.teleporteverything";
        public const string WaypointsGUID = "RustyMods.Waypoints";
        private const string OdinHorseContainerName = "SaddlebagContainer";
        private static readonly int OdinHorseBagsKey = "rae_saddlebags".GetStableHashCode();

        // > 0 while Waypoints' item check runs (see WaypointsCanTeleportPatch).
        internal static int waypointsCheckDepth;

        // TeleportEverything
        private static bool teResolved;
        private static ConfigEntryBase teEnabled;                       // [General] Enabled
        private static Func<Vector3, GameObject, bool, List<Character>> teAllies; // GetTransportableAllies
        private static FieldInfo tePortalPosition;                      // Vector3? set during a portal teleport

        // TeleportEverything 1.0 fork
        private static bool forkResolved;
        private static ConfigEntryBase forkEnabled;                     // [--- Mod ---] Enable Mod
        private static Action forkSetIncludeMode;                       // Plugin.SetIncludeMode()
        private static Func<Character, bool> forkIsValidAlly;           // Plugin.IsValidAlly(Character)

        // Waypoints
        private static bool waypointsResolved;
        private static ConfigEntryBase waypointsTeleportTames;          // [2 - Settings] 5 - Teleport Tames

        /// <summary>An inventory travelling with the player, and where it is for the hint ({where}).</summary>
        public struct Bag
        {
            public Inventory Inventory;
            public string Where;
        }

        private static readonly List<Bag> cargo = new List<Bag>();
        private static readonly HashSet<Character> seen = new HashSet<Character>();
        // OdinHorse bags parsed from their saved text, per horse; parsed again only when the text changes.
        private static readonly Dictionary<ZDOID, KeyValuePair<string, Inventory>> savedBags =
            new Dictionary<ZDOID, KeyValuePair<string, Inventory>>();

        /// <summary>
        /// Bags on the tames that will teleport with the player; empty if none.
        /// A failure here only skips the tames: the player's own inventory is still gated.
        /// </summary>
        public static List<Bag> Cargo(Player player)
        {
            cargo.Clear();
            seen.Clear();
            try
            {
                // The mount and tames BringMount / BringTames take through a portal: their saddlebags are always
                // gated, like the player's bags. Only at a portal: nothing comes along on other teleports.
                if (Travel.atPortal && Travel.BringsMount)
                {
                    AddBags(Travel.RiddenMount(player));
                    // A cart the mount pulls (OdinHorse) comes along too when BringCart is on.
                    Vagon towed = Travel.TowedCart(player);
                    if (towed && towed.m_container)
                        cargo.Add(new Bag
                        {
                            Inventory = towed.m_container.GetInventory(),
                            Where = $" in {Travel.RiddenMount(player).GetHoverName()}'s {towed.GetHoverName()}",
                        });
                }
                if (Travel.atPortal && Travel.BringsTames)
                {
                    foreach (Character tame in Travel.SelectTames(player))
                        AddBags(tame);
                }

                if (!Settings.GateCompanionCargo.Value) return cargo;

                if (TeleportEverythingTakesTames())
                {
                    // Same position TeleportEverything uses: the portal during a portal teleport, else the player.
                    Vector3 from = tePortalPosition.GetValue(null) as Vector3? ?? player.transform.position;
                    foreach (Character tame in teAllies(from, player.gameObject, false))
                        AddBags(tame);
                }

                if (ForkTakesTames())
                {
                    // Its own selection: the ally mode first (it sets which tames count), then each tame.
                    forkSetIncludeMode();
                    foreach (Character character in Character.GetAllCharacters())
                    {
                        if (character && !character.IsPlayer() && forkIsValidAlly(character))
                            AddBags(character);
                    }
                }

                if (waypointsCheckDepth > 0 && WaypointsTakesTames())
                {
                    // Same tames Waypoints takes: tameable and following this player, at any distance.
                    foreach (Character character in Character.GetAllCharacters())
                    {
                        MonsterAI ai = character.GetComponent<MonsterAI>();
                        if (character.GetComponent<Tameable>() && ai && ai.GetFollowTarget() == player.gameObject)
                            AddBags(character);
                    }
                }
            }
            catch (Exception e)
            {
                Failsafe.Report("Tame saddlebag check", e);
                cargo.Clear();
            }
            return cargo;
        }

        private static void AddBags(Character tame)
        {
            if (!tame || !seen.Add(tame)) return;
            // The tame's own name if it has one, else its kind (e.g. "Lox").
            string where = $" in {tame.GetHoverName()}'s saddlebags";

            foreach (Container container in tame.GetComponentsInChildren<Container>(true))
            {
                Inventory inventory = container.GetInventory();
                if (inventory != null && container.gameObject.name != OdinHorseContainerName)
                    cargo.Add(new Bag { Inventory = inventory, Where = where });
            }

            ZNetView nview = tame.GetComponent<ZNetView>();
            if (nview && nview.IsValid())
            {
                Inventory saved = SavedBags(nview.GetZDO());
                if (saved != null) cargo.Add(new Bag { Inventory = saved, Where = where });
            }
        }

        private static Inventory SavedBags(ZDO zdo)
        {
            string text = zdo.GetString(OdinHorseBagsKey, "");
            if (string.IsNullOrEmpty(text)) return null;
            if (savedBags.TryGetValue(zdo.m_uid, out var cached) && cached.Key == text) return cached.Value;

            // Big enough for any bag size OdinHorse allows, so no saved item is left out.
            var inventory = new Inventory("Saddlebags", null, 32, 32);
            inventory.Load(new ZPackage(text));
            savedBags[zdo.m_uid] = new KeyValuePair<string, Inventory>(text, inventory);
            return inventory;
        }

        private static bool ForkTakesTames()
        {
            if (!forkResolved)
            {
                forkResolved = true;
                Type type = PluginType(Travel.TeleportEverythingForkGUID, "TeleportEverything.Plugin", out bool installed);
                if (installed)
                {
                    MethodInfo setMode = type == null ? null : AccessTools.Method(type, "SetIncludeMode", Type.EmptyTypes);
                    MethodInfo isAlly = type == null ? null : AccessTools.Method(type, "IsValidAlly", new[] { typeof(Character) });
                    forkEnabled = Compatibility.FindSetting(Travel.TeleportEverythingForkGUID, "--- Mod ---", "Enable Mod");
                    if (setMode != null && setMode.IsStatic && isAlly != null && isAlly.IsStatic &&
                        isAlly.ReturnType == typeof(bool) && forkEnabled != null)
                    {
                        forkSetIncludeMode = AccessTools.MethodDelegate<Action>(setMode);
                        forkIsValidAlly = AccessTools.MethodDelegate<Func<Character, bool>>(isAlly);
                    }
                    else
                        Jotunn.Logger.LogWarning("GateCompanionCargo: the TeleportEverything fork's tame code changed " +
                            "(update?). Saddlebags on tames it teleports aren't gated.");
                }
            }
            return forkIsValidAlly != null && Compatibility.IsSet(forkEnabled, "True");
        }

        private static bool TeleportEverythingTakesTames()
        {
            if (!teResolved)
            {
                teResolved = true;
                Type type = PluginType(TeleportEverythingGUID, "TeleportEverything.TeleportEverythingPlugin", out bool installed);
                if (installed)
                {
                    MethodInfo allies = type == null ? null : AccessTools.Method(type, "GetTransportableAllies",
                        new[] { typeof(Vector3), typeof(GameObject), typeof(bool) });
                    tePortalPosition = type == null ? null : AccessTools.Field(type, "activePortalPosition");
                    teEnabled = Compatibility.FindSetting(TeleportEverythingGUID, "General", "Enabled");

                    if (allies != null && allies.ReturnType == typeof(List<Character>) &&
                        tePortalPosition?.FieldType == typeof(Vector3?) && teEnabled != null)
                        teAllies = AccessTools.MethodDelegate<Func<Vector3, GameObject, bool, List<Character>>>(allies);
                    else
                        Jotunn.Logger.LogWarning("GateCompanionCargo: TeleportEverything's tame code changed " +
                            "(TeleportEverything update?). Saddlebags on tames it teleports aren't gated.");
                }
            }
            return teAllies != null && Compatibility.IsSet(teEnabled, "True");
        }

        private static bool WaypointsTakesTames()
        {
            if (!waypointsResolved)
            {
                waypointsResolved = true;
                waypointsTeleportTames = Compatibility.FindSetting(WaypointsGUID, "2 - Settings", "5 - Teleport Tames");
                if (waypointsTeleportTames == null)
                    Jotunn.Logger.LogWarning("GateCompanionCargo: Waypoints' 'Teleport Tames' setting wasn't found " +
                        "(Waypoints update?). Saddlebags on tames it teleports aren't gated.");
            }
            return Compatibility.IsSet(waypointsTeleportTames, "On");
        }

        /// <summary>
        /// A type from another mod's assembly, looked up without logging when the mod isn't there.
        /// installed = the mod has loaded (the type may still be missing after an update).
        /// </summary>
        internal static Type PluginType(string guid, string typeName, out bool installed)
        {
            installed = Chainloader.PluginInfos.TryGetValue(guid, out var plugin) && plugin.Instance != null;
            return installed ? plugin.Instance.GetType().Assembly.GetType(typeName) : null;
        }
    }

    /// <summary>
    /// Marks Waypoints' item check (Waypoint.CanTeleport, private) so the gate adds following tames'
    /// saddlebags. Skipped when Waypoints isn't installed. BossGatedPortals loads after Waypoints (soft
    /// dependency) so its type is found here. Nothing in here can fail, so no try/catch.
    /// </summary>
    [HarmonyPatch]
    internal static class WaypointsCanTeleportPatch
    {
        private static bool resolved;
        private static MethodBase target;

        // Harmony may call Prepare more than once: look up (and warn) only the first time.
        private static bool Prepare()
        {
            if (resolved) return target != null;
            resolved = true;
            Type waypoint = Tames.PluginType(Tames.WaypointsGUID, "Waypoints.Behaviors.Waypoint", out bool installed);
            target = waypoint == null ? null : AccessTools.Method(waypoint, "CanTeleport", new[] { typeof(Player), typeof(bool) });
            if (installed && target == null)
                Jotunn.Logger.LogWarning("GateCompanionCargo: Waypoints' item check wasn't found (Waypoints update?). " +
                    "Saddlebags on tames it teleports aren't gated.");
            return target != null;
        }

        private static MethodBase TargetMethod() => target;

        private static void Prefix() => Tames.waypointsCheckDepth++;

        private static void Finalizer() => Tames.waypointsCheckDepth--;
    }
}
