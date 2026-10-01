using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace BossGatedPortals
{
    /// <summary>
    /// BringMount / BringCart / BringTames (each off by default): what you travel with comes through a
    /// portal with you. You're put back in the saddle and the cart is hitched back up on the other side.
    /// In vanilla all of it stays behind. Their cargo is gated like your own inventory (see
    /// InventoryIsTeleportablePatch and Tames.Cargo), so a locked item in a cart or saddlebag blocks the
    /// portal before anything moves.
    ///
    /// How a trip works (all on the teleporting player's own client):
    /// 1. TeleportWorld.Teleport marks "this teleport is a portal" (atPortal). Other teleports (map mods,
    ///    admin commands, death) never take anything along.
    /// 2. Player.TeleportTo: before it runs, note the mount, cart and tames; once it has started the
    ///    teleport, get off the mount and send everything to the portal's exit. Their saved position (ZDO)
    ///    is what matters: the game unloads them and spawns them again when you arrive.
    /// 3. After you land, each one is found again and set on the ground, and the game's own "use" is
    ///    pressed for you: the saddle's (ride) and the cart's (hitch). Same as pressing E, so ownership
    ///    rules are the game's.
    /// Stands down when another mod already does this (AdjustablePortals, or either TeleportEverything while
    /// enabled: Zenox's, or the 1.0 fork of OdinPlus').
    /// </summary>
    internal static class Travel
    {
        public const string AdjustablePortalsGUID = "MidnightsFX.AdjustablePortals";
        public const string TeleportEverythingForkGUID = "com.kpro.TeleportEverything";

        // Player.UpdateTeleport gives up on a destination after 15 s; anything later never arrives.
        private const float ArrivalTimeoutSeconds = 20f;
        // Vagon.Update unhitches the cart one frame after the teleport starts; never wait longer than this.
        private const float UnhitchTimeoutSeconds = 1f;
        // How long to wait for the player to touch the ground after landing before reattaching anyway.
        private const float LandingTimeoutSeconds = 3f;
        // Mount and tames are parked on arcs in front of the exit, never behind the portal or on the spot
        // where the player lands; the cart straight ahead (it is placed properly once the player lands).
        private static readonly float[] SlotAngles = { 45f, -45f, 90f, -90f };
        private const float RingSpacing = 2.5f;
        private const float CartParkDistance = 4f;
        // How far above a buried spot to look for the ground over it.
        private const int FloorSearchHeight = 3;
        // The player only steps forward to make room for the cart onto ground about as high as where they stand.
        private const float MaxStepHeight = 1f;

        // Same layers the game uses for "solid ground or building" (ZoneSystem.m_solidRayMask), plus carts/ships.
        // Looked up on first use, not when this class loads: Gate reads atPortal from the main menu on.
        private static int solidMask;
        private static int SolidMask => solidMask != 0 ? solidMask
            : solidMask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "terrain", "vehicle");

        /// <summary>
        /// True while a portal checks the player: when they step in (TeleportWorld.Teleport, which also starts
        /// the trip) and while it decides whether to glow (TeleportWorld.UpdatePortal). Cargo of what travels
        /// along is gated only then, so the glow and the step-in always agree. See PortalCheckPatch.
        /// </summary>
        internal static bool atPortal;

        public static bool BringsMount => Settings.Enabled.Value && Settings.BringMount.Value && !OtherModCarries();
        public static bool BringsCart => Settings.Enabled.Value && Settings.BringCart.Value && !OtherModCarries();
        public static bool BringsTames => Settings.Enabled.Value && Settings.BringTames.Value && !OtherModCarries();

        // Mods that move creatures and carts through portals themselves.
        private static bool resolved;
        private static string adjustablePortals;          // name, if installed
        private static string teleportEverything;         // name, if installed
        private static ConfigEntryBase teleportEverythingOn;  // its [General] Enabled
        private static string teleportEverythingFork;     // name, if installed
        private static ConfigEntryBase teleportEverythingForkOn; // its [--- Mod ---] Enable Mod

        /// <summary>
        /// Another mod already moves creatures and carts: AdjustablePortals, or a TeleportEverything while it's
        /// enabled. Doing it twice would fight over the same creature.
        /// </summary>
        private static bool OtherModCarries() => OtherMod() != null;

        private static string OtherMod()
        {
            if (!resolved)
            {
                resolved = true;
                adjustablePortals = InstalledName(AdjustablePortalsGUID);
                teleportEverything = InstalledName(Tames.TeleportEverythingGUID);
                if (teleportEverything != null)
                    teleportEverythingOn = Compatibility.FindSetting(Tames.TeleportEverythingGUID, "General", "Enabled");
                teleportEverythingFork = InstalledName(TeleportEverythingForkGUID);
                if (teleportEverythingFork != null)
                    teleportEverythingForkOn = Compatibility.FindSetting(TeleportEverythingForkGUID, "--- Mod ---", "Enable Mod");
            }
            if (adjustablePortals != null) return adjustablePortals;
            // Setting not found (TeleportEverything update?): assume it's on, the safe side.
            if (teleportEverything != null && (teleportEverythingOn == null || Compatibility.IsSet(teleportEverythingOn, "True")))
                return teleportEverything;
            if (teleportEverythingFork != null &&
                (teleportEverythingForkOn == null || Compatibility.IsSet(teleportEverythingForkOn, "True")))
                return teleportEverythingFork;
            return null;
        }

        private static string InstalledName(string guid) =>
            Chainloader.PluginInfos.TryGetValue(guid, out var p) && p.Instance != null ? p.Metadata.Name : null;

        /// <summary>Logs once at startup if another mod's companion travel takes over from ours.</summary>
        public static void LogStandDown()
        {
            if (OtherMod() != null && (Settings.BringMount.Value || Settings.BringCart.Value || Settings.BringTames.Value))
                Jotunn.Logger.LogWarning($"BringMount/BringCart/BringTames: {OtherMod()} is installed and moves " +
                    "creatures and carts through portals itself, so these settings do nothing (cargo is still gated).");
        }

        /// <summary>The creature this player is riding, or null.</summary>
        public static Character RiddenMount(Player player)
        {
            Sadle saddle = player.GetDoodadController() as Sadle;
            if (saddle == null || !saddle.IsValid()) return null;
            Character mount = saddle.GetCharacter();
            return mount && !mount.IsDead() ? mount : null;
        }

        // Vagon.m_instances is private in the game: reading it directly throws FieldAccessException at runtime.
        private static readonly FieldInfo VagonInstances = AccessTools.Field(typeof(Vagon), "m_instances");

        // Vagon.Detach is private in the game.
        private static readonly MethodInfo VagonDetach = AccessTools.Method(typeof(Vagon), "Detach");

        /// <summary>
        /// The cart (Vagon) this character is pulling, or null. Usually the player; with mods such as OdinHorse
        /// a tamed animal can pull one too (the hitch is then on the animal, not the rider).
        /// </summary>
        public static Vagon CartPulledBy(Character puller)
        {
            if (!puller || !(VagonInstances?.GetValue(null) is List<Vagon> carts)) return null;
            foreach (Vagon vagon in carts)
            {
                if (vagon && IsPulling(vagon, puller)) return vagon;
            }
            return null;
        }

        // Vagon.IsAttached throws for the moment a hitch's other end has gone (it detaches on its next update).
        // One such cart must not stop the check for all the others.
        private static bool IsPulling(Vagon cart, Character puller)
        {
            try
            {
                return cart.IsAttached(puller);
            }
            catch (NullReferenceException)
            {
                return false;
            }
        }

        /// <summary>
        /// The cart the player's mount is pulling, if BringMount and BringCart both take it along; else null.
        /// Its cargo is gated like the player's (Tames.Cargo).
        /// </summary>
        public static Vagon TowedCart(Player player) =>
            BringsMount && BringsCart ? CartPulledBy(RiddenMount(player)) : null;

        private static readonly List<Character> tames = new List<Character>();

        /// <summary>
        /// Tames BringTames takes along, nearest first, at most TameMaxCount: in range (TameRadius,
        /// TameHeightRange), picked by TameMode and the allow/block lists. Never the player's own mount
        /// (BringMount's), one someone else rides, or one following another player.
        /// The list is reused: copy it to keep it.
        /// </summary>
        public static List<Character> SelectTames(Player player)
        {
            tames.Clear();
            Character mount = RiddenMount(player);
            Vector3 at = player.transform.position;
            float radius = Settings.TameRadius.Value;
            float vertical = Settings.TameHeightRange.Value;

            foreach (Character c in Character.GetAllCharacters())
            {
                if (!c || c == mount || c.IsPlayer() || c.IsDead() || !c.IsTamed()) continue;
                Vector3 p = c.transform.position;
                if (Utils.DistanceXZ(p, at) > radius || Mathf.Abs(p.y - at.y) > vertical) continue;
                if (IsWanted(c, player)) tames.Add(c);
            }

            tames.Sort((a, b) => Utils.DistanceXZ(a.transform.position, at).CompareTo(Utils.DistanceXZ(b.transform.position, at)));
            int max = Settings.TameMaxCount.Value;
            if (tames.Count > max) tames.RemoveRange(max, tames.Count - max);
            return tames;
        }

        private static bool IsWanted(Character tame, Player player)
        {
            // Not saved with the world: the game deletes it when it unloads on the way, so it can't travel.
            ZNetView nview = tame.GetComponent<ZNetView>();
            if (!nview || !nview.IsValid() || !nview.GetZDO().Persistent) return false;

            Sadle saddle = tame.GetComponentInChildren<Sadle>();
            if (saddle && saddle.HaveValidUser()) return false; // someone else is riding it
            // Pulling a cart (OdinHorse): moving it would drag the hitched cart across the world. Stays behind.
            if (CartPulledBy(tame)) return false;

            Tameable tameable = tame.GetComponent<Tameable>();
            if (tameable && tameable.m_unsummonDistance > 0f && !Settings.TameIncludeSummons.Value) return false;

            string prefab = Utils.GetPrefabName(tame.gameObject);
            if (Settings.TameBlock.Contains(prefab)) return false;
            if (Settings.TameAllow.Count > 0 && !Settings.TameAllow.Contains(prefab)) return false;

            bool? follows = FollowsPlayer(tame, nview, player);
            if (follows == false) return false; // following another player
            bool following = follows == true;
            bool named = tameable && !string.IsNullOrEmpty(tameable.GetText());

            switch (Settings.TameMode.Value)
            {
                case TameSelection.FollowingOrNamed: return following || named;
                case TameSelection.Named: return named;
                case TameSelection.AllTamed: return true;
                default: return following;
            }
        }

        /// <summary>true = follows this player, false = follows another player, null = follows nobody.</summary>
        private static bool? FollowsPlayer(Character tame, ZNetView nview, Player player)
        {
            MonsterAI ai = tame.GetComponent<MonsterAI>();
            GameObject target = ai ? ai.GetFollowTarget() : null;
            if (target) return target == player.gameObject;
            // A tame whose AI runs on another player's client only knows who it follows from its saved data.
            string name = nview.GetZDO().GetString(ZDOVars.s_follow, "");
            if (name.Length == 0) return null;
            return name == player.GetPlayerName();
        }

        public struct Trip
        {
            public Character Mount;
            public Vagon Cart;
            public List<Character> Tames;
            public Vector3 Exit;
            public Quaternion ExitRotation;
            // The cart is hitched to the mount, not the player (OdinHorse): it travels in the same pose
            // relative to the mount and is hitched back to it. CartPose = cart relative to the mount.
            public bool CartOnMount;
            public Vector3 CartOffset;
            public Quaternion CartTurn;
            // The mount pulls a cart that BringCart doesn't take along: unhitch it before the mount leaves.
            public Vagon LeaveCart;
        }

        /// <summary>Before Player.TeleportTo: what comes along, if this is the local player at a portal.</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static Trip Plan(Player player, Vector3 pos, Quaternion rot)
        {
            var trip = new Trip { Exit = pos, ExitRotation = rot };
            if (!atPortal || player != Player.m_localPlayer) return trip;
            if (BringsMount) trip.Mount = RiddenMount(player);
            if (BringsCart) trip.Cart = CartPulledBy(player);
            if (BringsTames) trip.Tames = new List<Character>(SelectTames(player));

            Vagon towed = trip.Mount ? CartPulledBy(trip.Mount) : null;
            if (towed && trip.Cart == null && BringsCart)
            {
                Transform mount = trip.Mount.transform;
                trip.Cart = towed;
                trip.CartOnMount = true;
                trip.CartOffset = mount.InverseTransformPoint(towed.transform.position);
                trip.CartTurn = Quaternion.Inverse(mount.rotation) * towed.transform.rotation;
            }
            else if (towed)
            {
                trip.LeaveCart = towed;
            }
            return trip;
        }

        /// <summary>After Player.TeleportTo started a teleport: send the mount, tames and cart ahead.</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void Go(Player player, Trip trip)
        {
            int slot = 0;

            if (trip.Mount)
            {
                // Off the saddle first: while riding, the game pins the player to the saddle every frame,
                // so a mount sent ahead would drag the player along before the destination has loaded.
                player.StopDoodadControl();
                // A cart left behind is unhitched first: OdinHorse's hitch never breaks, so it would drag
                // the cart after the mount (or the mount back to the cart).
                if (trip.LeaveCart) VagonDetach.Invoke(trip.LeaveCart, null);
                ZDOID mount = Move(trip.Mount.gameObject, Slot(trip, slot++));
                if (!mount.IsNone())
                {
                    player.StartCoroutine(AfterArrival(player, mount, "mount", Remount));
                    if (trip.CartOnMount)
                    {
                        // Moved in the same frame and the same pose relative to the mount, so the hitch between
                        // them never stretches.
                        Transform m = trip.Mount.transform;
                        ZDOID cart = Move(trip.Cart.gameObject, m.TransformPoint(trip.CartOffset), m.rotation * trip.CartTurn);
                        if (!cart.IsNone())
                            player.StartCoroutine(RehitchToMount(player, mount, cart, trip.CartOffset, trip.CartTurn));
                    }
                }
            }

            if (trip.Tames != null)
            {
                foreach (Character tame in trip.Tames)
                {
                    if (!tame || tame.IsDead()) continue;
                    // A summon vanishes once it's too far from whoever it follows, and it gets here 2 s before
                    // the player does. Only this copy changes: the one spawned at the destination is normal again.
                    Tameable tameable = tame.GetComponent<Tameable>();
                    if (tameable) tameable.m_unsummonDistance = 0f;
                    ZDOID id = Move(tame.gameObject, Slot(trip, slot++));
                    if (!id.IsNone())
                        player.StartCoroutine(AfterArrival(player, id, "tame", null));
                }
            }

            if (trip.Cart && !trip.CartOnMount)
            {
                Vector3 park = Ground(trip.Exit + trip.ExitRotation * Vector3.forward * CartParkDistance, trip.Exit.y);
                player.StartCoroutine(SendCart(player, trip.Cart, park));
            }
        }

        /// <summary>The n-th parking spot: arcs in front of the exit, each arc further out.</summary>
        private static Vector3 Slot(Trip trip, int n)
        {
            float angle = SlotAngles[n % SlotAngles.Length];
            float radius = RingSpacing * (1 + n / SlotAngles.Length);
            Vector3 spot = trip.Exit + trip.ExitRotation * (Quaternion.Euler(0f, angle, 0f) * Vector3.forward * radius);
            return Ground(spot, trip.Exit.y);
        }

        /// <summary>
        /// The spot moved onto the ground, if the destination is already loaded; else at fallbackY
        /// (the exit's height). Whatever lands there is set on the ground again on arrival (Settle).
        /// </summary>
        private static Vector3 Ground(Vector3 spot, float fallbackY)
        {
            spot.y = fallbackY;
            if (ZoneSystem.instance.IsZoneLoaded(spot) && FloorAt(spot, out float floor))
                spot.y = floor + 0.2f;
            return spot;
        }

        /// <summary>
        /// The ground at p: first looking down from just above p (so a roof overhead is never "the ground"),
        /// then, if p is below the ground, from FloorSearchHeight above it.
        /// </summary>
        private static bool FloorAt(Vector3 p, out float floor) =>
            ZoneSystem.instance.FindFloor(p, out floor) ||
            ZoneSystem.instance.GetSolidHeight(p, out floor, FloorSearchHeight);

        private static IEnumerator SendCart(Player player, Vagon cart, Vector3 park)
        {
            // The cart is still hitched this frame; the game unhitches it on its next update because the
            // player is teleporting. Moving it before then would let the hitch yank the player across the world.
            float deadline = Time.time + UnhitchTimeoutSeconds;
            while (cart && player && IsPulling(cart, player) && Time.time < deadline)
                yield return null;
            if (!cart || !player || IsPulling(cart, player)) yield break;

            ZDOID id = Move(cart.gameObject, park);
            if (!id.IsNone())
                yield return AfterArrival(player, id, "cart", Rehitch);
        }

        /// <summary>
        /// A cart that travelled hitched to the mount: once both have spawned at the destination and the mount
        /// has been set on the ground, put the cart back in its place behind the mount and hitch it to the mount.
        /// </summary>
        private static IEnumerator RehitchToMount(Player player, ZDOID mountId, ZDOID cartId, Vector3 offset, Quaternion turn)
        {
            float deadline = Time.time + ArrivalTimeoutSeconds;
            while (player && player.IsTeleporting() && Time.time < deadline)
                yield return null;

            GameObject mount = null, cart = null;
            while (player && ZNetScene.instance && Time.time < deadline &&
                   (!(mount = ZNetScene.instance.FindInstance(mountId)) || !(cart = ZNetScene.instance.FindInstance(cartId))))
                yield return null;
            // The mount's own arrival sets it on the ground this frame; place the cart the frame after.
            yield return null;
            if (!player || !mount || !cart)
            {
                if (player)
                    Jotunn.Logger.LogInfo("BringMount/BringCart: the mount's cart didn't load at the destination in " +
                        "time; it's at the portal exit.");
                yield break;
            }

            try
            {
                Vagon vagon = cart.GetComponent<Vagon>();
                Character puller = mount.GetComponent<Character>();
                if (!vagon || !puller || puller.IsDead() || IsPulling(vagon, puller)) yield break;
                Move(cart, mount.transform.TransformPoint(offset), mount.transform.rotation * turn);
                Hitch(vagon, puller);
            }
            catch (Exception e)
            {
                Failsafe.Report("Rehitching the mount's cart after a portal", e);
            }
        }

        /// <summary>
        /// Moves a loaded object and its saved position. Returns its ZDOID, or ZDOID.None if it can't move
        /// (not networked, or not saved with the world: the game would delete it while it's unloaded).
        /// Every rigidbody is moved too (a cart's wheels are their own bodies), or physics pulls it back.
        /// </summary>
        private static ZDOID Move(GameObject go, Vector3 position, Quaternion? rotation = null)
        {
            ZNetView nview = go.GetComponent<ZNetView>();
            if (!nview || !nview.IsValid() || !nview.GetZDO().Persistent) return ZDOID.None;
            nview.ClaimOwnership();
            go.transform.position = position;
            if (rotation.HasValue) go.transform.rotation = rotation.Value;
            nview.GetZDO().SetPosition(position);
            nview.GetZDO().SetRotation(go.transform.rotation);
            foreach (Rigidbody body in go.GetComponentsInChildren<Rigidbody>())
            {
                body.position = body.transform.position;
                body.rotation = body.transform.rotation;
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
            }
            return nview.GetZDO().m_uid;
        }

        /// <summary>
        /// Waits until the player has landed and the object has been spawned again at the destination
        /// (the old one was unloaded on the way, so only its ZDOID survives), sets it on the ground, then
        /// runs arrived (if any).
        /// </summary>
        private static IEnumerator AfterArrival(Player player, ZDOID id, string what, Action<Player, GameObject> arrived)
        {
            float deadline = Time.time + ArrivalTimeoutSeconds;
            while (player && player.IsTeleporting() && Time.time < deadline)
                yield return null;

            GameObject go = null;
            // ZNetScene goes away when the player leaves the world.
            while (player && ZNetScene.instance && Time.time < deadline && !(go = ZNetScene.instance.FindInstance(id)))
                yield return null;

            if (!player || !go)
            {
                if (player)
                    Jotunn.Logger.LogInfo($"BringMount/BringCart/BringTames: a {what} didn't load at the destination " +
                        "in time; it's at the portal exit.");
                yield break;
            }

            // The player arrives a metre above the exit's floor and drops: hitch or mount once they've landed.
            if (arrived != null)
            {
                float landed = Time.time + LandingTimeoutSeconds;
                while (player && go && !player.IsOnGround() && Time.time < landed)
                    yield return null;
                if (!player || !go) yield break;
            }

            try
            {
                Settle(go);
                arrived?.Invoke(player, go);
            }
            catch (Exception e)
            {
                Failsafe.Report($"Settling a {what} after a portal", e);
            }
        }

        /// <summary>
        /// Lifts the object onto the ground if it ended up under it: it was parked at the exit's height
        /// before the destination's ground existed, and the ground there may be higher.
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void Settle(GameObject go)
        {
            Vector3 p = go.transform.position;
            if (ZoneSystem.instance.FindFloor(p, out _)) return; // ground under it: fine where it is
            if (ZoneSystem.instance.GetSolidHeight(p, out float floor, FloorSearchHeight) && floor > p.y)
                Move(go, new Vector3(p.x, floor + 0.2f, p.z));
        }

        /// <summary>The player did something else after landing (sat down, took a helm, died): leave them be.</summary>
        private static bool Busy(Player player) =>
            player.IsDead() || player.IsAttached() || player.GetDoodadController() != null;

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void Remount(Player player, GameObject go)
        {
            if (Busy(player)) return;
            // The saddle's own "ride" request: the mount's owner grants it, as when the player presses E.
            Sadle saddle = go.GetComponentInChildren<Sadle>();
            if (saddle) saddle.Interact(player, false, false);
        }

        /// <summary>
        /// Lays the cart out behind the player with its handle in their hands, then hitches it to them.
        /// Behind a freshly arrived player is the portal, so the player first steps forward by the cart's
        /// length when the way ahead is clear; otherwise the cart goes to their right or left, wherever
        /// there's room (it swings in behind them once they walk).
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void Rehitch(Player player, GameObject go)
        {
            Vagon cart = go.GetComponent<Vagon>();
            if (!cart || Busy(player) || IsPulling(cart, player)) return;

            Vector3 forward = Vector3.ProjectOnPlane(player.transform.forward, Vector3.up).normalized;
            if (forward == Vector3.zero) forward = Vector3.forward;
            Vector3 right = Vector3.Cross(Vector3.up, forward);

            // Direction from the handle to the cart's back end, in the cart's own frame, and the cart's length.
            Vector3 handle = go.transform.InverseTransformPoint(cart.m_attachPoint.position);
            Vector3 backLocal = new Vector3(-handle.x, 0f, -handle.z);
            if (backLocal.sqrMagnitude < 0.01f) backLocal = Vector3.back;
            float length = backLocal.magnitude * 2f + 0.5f;

            Vector3 back;
            if (Clear(player.transform.position, forward, length, go) && StepForward(player, forward * length))
                back = -forward;
            else if (Clear(player.transform.position, right, length, go))
                back = right;
            else if (Clear(player.transform.position, -right, length, go))
                back = -right;
            else
                back = -forward; // no room anywhere: behind, the way it faced when pulled

            Quaternion rotation = Quaternion.LookRotation(back) * Quaternion.Inverse(Quaternion.LookRotation(backLocal));
            go.transform.rotation = rotation;
            Vector3 handleOffset = cart.m_attachPoint.position - go.transform.position;
            Vector3 hands = player.transform.TransformPoint(HitchOffset(cart, player));
            Move(go, hands - handleOffset, rotation);
            Hitch(cart, player);
        }

        // The game's own hitch (Vagon.AttachTo, private): hitches to exactly the object given.
        private static readonly MethodInfo VagonAttachTo =
            AccessTools.Method(typeof(Vagon), "AttachTo", new[] { typeof(GameObject) });

        // OdinHorse lets tamed animals pull carts. It replaces the cart's "use" with "hitch to the nearest
        // animal, else the player", so pressing "use" for the player can hand the cart to an animal standing
        // nearby. Its own hitch and hitch point are used instead, aimed at whoever pulled the cart before.
        private const string OdinHorseGUID = "Raelaziel.OdinHorse";
        private static bool odinResolved;
        private static MethodInfo odinAttach;   // OdinHorse.AttachCartTo(Character, Vagon), private
        private static MethodInfo odinOffset;   // OdinHorse.GetCartOffsetVectorForCharacter(Character)

        private static void ResolveOdinHorse()
        {
            if (odinResolved) return;
            odinResolved = true;
            Type type = Tames.PluginType(OdinHorseGUID, "OdinHorse.OdinHorse", out bool installed);
            if (!installed) return;
            odinAttach = type == null ? null : AccessTools.Method(type, "AttachCartTo", new[] { typeof(Character), typeof(Vagon) });
            odinOffset = type == null ? null : AccessTools.Method(type, "GetCartOffsetVectorForCharacter", new[] { typeof(Character) });
            if (odinAttach == null || odinOffset == null || odinOffset.ReturnType != typeof(Vector3))
            {
                odinAttach = odinOffset = null;
                Jotunn.Logger.LogWarning("BringCart: OdinHorse's cart code changed (OdinHorse update?). Carts are " +
                    "hitched the game's way after a portal; it may pick a nearby animal instead of you.");
            }
        }

        /// <summary>Where the cart's handle goes, in the puller's own frame.</summary>
        private static Vector3 HitchOffset(Vagon cart, Character puller)
        {
            ResolveOdinHorse();
            return odinOffset != null ? (Vector3)odinOffset.Invoke(null, new object[] { puller }) : cart.m_attachOffset;
        }

        /// <summary>
        /// Hitches the cart to this character: the one that pulled it before the portal. The cart must be owned
        /// here (Move claims it) and its handle already at the puller (HitchOffset), or the game unhitches it
        /// again on its next update.
        /// </summary>
        private static void Hitch(Vagon cart, Character puller)
        {
            ResolveOdinHorse();
            if (odinAttach != null) odinAttach.Invoke(null, new object[] { puller, cart });
            else VagonAttachTo.Invoke(cart, new object[] { puller.gameObject });
        }

        /// <summary>Nothing solid within distance along direction, at knee and chest height, except ignore.</summary>
        private static bool Clear(Vector3 from, Vector3 direction, float distance, GameObject ignore) =>
            ClearAt(from + Vector3.up * 0.4f, direction, distance, ignore) &&
            ClearAt(from + Vector3.up * 1.2f, direction, distance, ignore);

        private static bool ClearAt(Vector3 from, Vector3 direction, float distance, GameObject ignore)
        {
            foreach (RaycastHit hit in Physics.RaycastAll(from, direction, distance, SolidMask, QueryTriggerInteraction.Ignore))
            {
                if (!hit.collider.transform.IsChildOf(ignore.transform)) return false;
            }
            return true;
        }

        /// <summary>Moves the player by offset if there's ground there about as high as where they stand.</summary>
        private static bool StepForward(Player player, Vector3 offset)
        {
            Vector3 to = player.transform.position + offset;
            // Looks down from 1.5 m above the player's feet: under any ceiling the player fits beneath.
            to.y += 0.5f;
            if (!ZoneSystem.instance.FindFloor(to, out float floor)) return false;
            if (Mathf.Abs(floor - player.transform.position.y) > MaxStepHeight) return false;
            to.y = floor;
            player.transform.position = to;
            Rigidbody body = player.GetComponent<Rigidbody>();
            if (body)
            {
                body.position = to;
                body.linearVelocity = Vector3.zero;
            }
            return true;
        }
    }

    /// <summary>
    /// Sets Travel.atPortal while a portal checks the player. UpdatePortal is private and named as text: if a
    /// game update renames it, only the glow stops counting travel cargo (stepping in still does).
    /// Nothing here can fail.
    /// </summary>
    [HarmonyPatch]
    internal static class PortalCheckPatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(TeleportWorld), nameof(TeleportWorld.Teleport));
            MethodBase glow = AccessTools.Method(typeof(TeleportWorld), "UpdatePortal");
            if (glow != null) yield return glow;
            else Jotunn.Logger.LogWarning("Portal glow check (TeleportWorld.UpdatePortal) not found (game update?). " +
                "Portals may glow while a cart or saddlebag holds a locked item; stepping in still refuses.");
        }

        private static void Prefix() => Travel.atPortal = true;

        private static void Finalizer() => Travel.atPortal = false;
    }

    /// <summary>Takes the mount, cart and tames along when a portal teleport starts (see Travel).</summary>
    [HarmonyPatch(typeof(Player), nameof(Player.TeleportTo))]
    internal static class PlayerTeleportToPatch
    {
        private static void Prefix(Player __instance, Vector3 pos, Quaternion rot, out Travel.Trip __state)
        {
            __state = default;
            try
            {
                // Before the original: once it runs, the player counts as teleporting and the cart lets go.
                __state = Travel.Plan(__instance, pos, rot);
            }
            catch (Exception e)
            {
                Failsafe.Report("Mount/cart/tame portal travel (start)", e);
            }
        }

        private static void Postfix(Player __instance, bool __result, Travel.Trip __state)
        {
            // false = no teleport started (cooldown, already teleporting, or handed to the owning client).
            if (!__result) return;
            try
            {
                Travel.Go(__instance, __state);
            }
            catch (Exception e)
            {
                Failsafe.Report("Mount/cart/tame portal travel", e);
            }
        }
    }
}
