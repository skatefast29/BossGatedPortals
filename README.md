# BossGatedPortals Plus

A progression mod for Valheim portals: ores and metals can go through a portal once that biome's boss is
beaten. Each boss unlocks its own biome's goods, so portals open up as your world progresses instead of
never letting metal through.

That's all it does out of the box. The **optional extras** further down (companions that travel with
you, faster portals) are **off by default**: until a server admin turns them on, the game plays exactly
like vanilla apart from the progression gate.

Made to run with **[XPortal](https://thunderstore.io/c/valheim/p/SpikeHimself/XPortal/)** (which chooses
where portals go); also works with vanilla portals. All settings are server-synced and editable live with F1.

Source: [github.com/skatefast29/BossGatedPortals](https://github.com/skatefast29/BossGatedPortals)

## Progression (on by default)

- **One tier per boss**, each unlocking its biome's items. Item lists are configurable.
- **Unlock rule** (`UnlockWhen`): the boss killed on this world (default), or also each player having
  picked up the boss drop, or the drop alone. `CumulativeTiers` lets a later tier unlock earlier ones.
- **Final tier unlocks everything**, including items no tier lists. A **NeverTeleport** list keeps items
  blocked forever.
- **Everything you carry counts:** your inventory, backpacks (AdventureBackpacks, Backpacks, RustyBags),
  and anything another mod brings through with you (a cart, tames' saddlebags), so they can't be used to
  sneak locked items through.
- **Hints** when a portal refuses you, saying where the item is ("in your Cart", "in your backpack") and
  which part of the world still has to be conquered. Never names a boss.
- **Portals only glow** when you can actually go through, and **tooltips and inventory icons** show what you
  can teleport right now.
- **Unlock announcements** to everyone on a boss's first kill.
- **For admins:** `portalgate status` console command, optional `AdminBypass`, and startup checks that log
  item typos, duplicate items, unlisted blocked items and conflicting mods.

## Optional extras (off by default)

These change nothing until you switch them on in F1.

**Travel with your companions** (`5 - Travel`; each off)
- **Mount:** ride into a portal and arrive in the saddle.
- **Cart:** arrives hitched to you, or to your horse (OdinHorse).
- **Tames:** following, named, or all tames nearby, with radius, height, headcount and creature lists.
- Their cargo follows the same progression rules as your own inventory.
- Steps aside automatically while TeleportEverything (either version) is on, since it does the same job.

**Faster portals** (`4 - Portal Speed`; set to the game's own timings)
- `FadeSeconds` starts at 1, the game's usual fade into the teleport screen. Lower it, down to 0 for instant.
- `MinimumLoadingSeconds` starts at 8, the game's usual minimum loading screen. Lower it, down to 0 for no
  built-in delay. Real loading is never skipped.
- Steps aside automatically while QuickTeleport, FastTeleport or Proper Portals is installed.

Server and clients need the same major.minor version (e.g. any 1.0.x).

## Installation

**Requires:** BepInExPack_Valheim, Jotunn.

- Install on the server and every client (same major.minor version, e.g. any 1.0.x; the newest is recommended).
- **Gale / r2modman:** install or update *BossGatedPortals* as usual, or *Import → local mod* and pick the zip.
- **Manual:** copy `BossGatedPortals.dll` to `BepInEx/plugins/BossGatedPortals/`.

Config: `BepInEx/config/BarryWhite.BossGatedPortals.cfg` (created on first launch). Updating from 0.2.x
carries your settings over automatically, from the old file and to the new setting names.

## Tiers (defaults)

| Tier | Biome | Unlocks |
|---|---|---|
| T0 | Meadows | nothing |
| T1 | Black Forest | CopperOre, Copper, CopperScrap, TinOre, Tin, Bronze, BronzeScrap |
| T2 | Swamp | IronOre, IronScrap, Iron, Ironpit |
| T3 | Mountains | SilverOre, Silver, DragonEgg |
| T4 | Plains | BlackMetalScrap, BlackMetal |
| T5 | Mistlands | MechanicalSpring, DvergrNeedle |
| T6 | Ashlands | FlametalOreNew, FlametalNew, FlametalOre, Flametal, CharredCogwheel |
| T7 | Deep North | GoldOre, Gold, and everything else |

Hildir's chests never teleport unless `HildirChestsByTier` is on. Other unlisted blocked items unlock at T7.

Per-tier settings:

| Setting | Meaning |
|---|---|
| `GlobalKey` | World key set by the boss kill |
| `BossItem` | Boss drop the player must have held (when `UnlockWhen` asks for it) |
| `Items` | Items unlocked, comma-separated |
| `Hint` | Shown when blocked |
| `UnlockMessage` | Announced on first kill |
| `AllowEverything` | T7 only: unlock all blocked items |

## Settings

All admin-only and server-synced, except **8 - Client**, which each player sets for themselves.

| Section | Setting | Default | Description |
|---|---|---|---|
| 1 - General | `Enabled` | `true` | Master switch |
| | `UnlockWhen` | `BossKilled` | `BossKilled` (on this world, for everyone), `BossKilledAndDropHeld` (each player must also have held the boss drop) or `DropHeld` |
| | `CumulativeTiers` | `false` | A later tier unlocks all earlier tiers |
| 2 - Item Rules | `NeverTeleport` | *(empty)* | Items never teleportable |
| | `HildirChestsByTier` | `false` | Hildir's chests unlock with their biome's tier (Brass T1, Silver T3, Bronze T4) |
| | `LockListedItemsAlways` | `false` | Also lock listed items the game (or another mod) already lets through, e.g. Coal in the Black Forest tier |
| | `FinalTierIncludesCheatTier` | `false` | T7 also unlocks tool-tier 1000+ items |
| | `RespectVanillaAllowAll` | `true` | Don't block Stone Portals or the allow-all *Portals* world modifier |
| 3 - Messages | `HintFormat` | `An item{where} blocks the portal. {hint}` | `{hint}`, `{where}` (e.g. " in your Cart", " in Lox's saddlebags") and `{item}`. Empty = the game's usual message |
| | `AnnounceTierUnlock` | `true` | Announce first boss kills |
| | `AccurateTooltips` | `true` | Per-player tooltips and icons |
| 4 - Portal Speed | `FadeSeconds` | `1` | Screen fade into and out of a teleport. 0 = the teleport screen appears the moment you touch the portal |
| | `MinimumLoadingSeconds` | `8` | Shortest loading screen (8 = the game's usual). 0 = no built-in delay; real loading still shows |
| 5 - Travel | `BringMount` | `false` | Your mount comes through portals and you're remounted |
| | `BringCart` | `false` | Your cart comes through portals and is hitched back up |
| | `BringTames` | `false` | Tames near you come through portals. The `Tame…` settings below only apply while this is on |
| | ↳ `TameMode` | `Following` | `Following`, `FollowingOrNamed`, `Named` or `AllTamed`. Tames following another player never come |
| | ↳ `TameRadius` | `10` | Metres around you (sideways) |
| | ↳ `TameHeightRange` | `3` | Metres above/below you; keeps other floors' animals behind |
| | ↳ `TameMaxCount` | `5` | Most tames per trip, nearest first |
| | ↳ `TameAllowList` | *(empty)* | If set, only these creatures (prefab names, e.g. `Wolf, Lox`) |
| | ↳ `TameBlockList` | *(empty)* | These creatures never come |
| | ↳ `TameIncludeSummons` | `false` | Also staff summons that follow you |
| 6 - Compatibility | `GateCompanionCargo` | `true` | Gate cart cargo and tame saddlebags when another mod brings them along |
| | `WarnOnConflictingMods` | `true` | Warn about conflicting mods and settings |
| 7 - Admin | `AdminBypass` | `false` | Admins ignore the gate |
| | `StatusCommand` | `true` | Enable `portalgate status` |
| | `LogItemChecks` | `true` | Log unknown item names, items in two tiers, and blocked items no tier lists |
| 8 - Client | `HintPosition` | `Centered` | `Centered` or `TopLeft` |
| | `HintCooldownSeconds` | `5` | Seconds between hints (0–60) |

## Compatibility

Tested in game with XPortal 1.2.25, alongside AdventureBackpacks 2.2.5, Backpacks 1.3.10, OdinHorse 1.7.5
and LoxSaddleBags 2.0.2. The other mods below were checked by reading their code:

- **Portal mods:** TargetPortal, Unified Target Portal, XPortalNetworks, PortalRules, PotalMap, Better
  Portal, Portal Stations, Waypoints and Waystones all ask the game's own item check, so the tiers and
  unlock hints apply to them too. Their own "ignore item restrictions" settings are warned about at startup
  when they're on.
- **Portal speed mods** (QuickTeleport by OdinPlus or by Muindor, FastTeleport, Proper Portals): they set
  the same fade and loading times as `[4 - Portal Speed]`, so Portal Speed steps aside automatically while
  one is installed (the log says which). Use that mod's settings.
- **Bags:** items in AdventureBackpacks, Smoothbrain's Backpacks and RustyBags bags are gated like items in
  your inventory, and unlock with their tiers. Jewelcrafting's bags only hold gems and jewelry.
  ExtraSlots and AzuExtendedPlayerInventory slots are part of your inventory.
- **Tames:** when TeleportEverything (Zenox's, or the 1.0 fork) or Waypoints with "Teleport Tames" on
  brings tames along, items in their saddlebags are gated too: LoxSaddleBags, OdinHorse, or any container
  on a tame. Set TeleportEverything's `TransportRestrictedItems` (the fork: `Transport Ores`) to false, or
  it lets everything through. ServersideQoL TameAssist moves following tames to you after a trip, so their
  saddlebags aren't checked (warned at startup on the server).
- **Carts and ships:** a cart a mod pulls through a portal with you is gated (`GateCompanionCargo`).
  BottleShips bottles arrive empty, since the game drops a cart's or ship's cargo when it's taken apart.
  ValheimRAFT's portals on ships only move the player.
- **Mods that change items:** if another mod makes a tier-listed item teleportable (for example the item
  file of Creature Level & Loot Control), the log says so once. Set `LockListedItemsAlways = true` to lock
  it anyway.
- **Mounts, carts and tames:** `BringMount`, `BringCart` and `BringTames` work with vanilla and XPortal portals.
  They do nothing while either TeleportEverything is on, since it moves creatures and carts itself.
  OdinHorse: a cart your horse pulls comes along (and is hitched back to it) when both `BringMount` and
  `BringCart` are on. A following tame that's pulling a cart stays behind.
- **Warned at startup:** mods with their own item gate (ServersideQoL's PortalProgression add-on,
  AdvancedPortals), ServersideQoL TameAssist's follower teleport, and settings that let every item
  through: TeleportEverything's `TransportRestrictedItems` and the fork's `Transport Ores` (both on by
  default), AzuMiscPatches, ValheimPlus, TargetPortal, Unified Target
  Portal, Portal Stations, Waypoints, Waystones, ReturnScroll, PortalRules' paid fares, and
  TeleportationMeads.
- **Proper Portals:** its faster loading screens work (Portal Speed steps aside); its "carry anything"
  doesn't, because this mod's check still runs.
- **Not gated:** ways to move items that don't use portals (shipping mods, cross-server portals, admin
  commands).

## Known limits

- Checks run client-side; a modified client can bypass them.
- Mounts, carts and tames travel only through portals you walk into, not with map-teleport mods
  (TargetPortal and the like). A cart you're pulling is still checked there (`GateCompanionCargo`).
- After Valheim updates, check the log for `LogItemChecks` warnings.

## Source and bug reports

Source code and bug reports are on [GitHub](https://github.com/skatefast29/BossGatedPortals). MIT licensed.

## Credits

- [Jotunn](https://github.com/Valheim-Modding/Jotunn) and [JotunnModStub](https://github.com/Valheim-Modding/JotunnModStub) (MIT No Attribution)
- [VentureValheim](https://github.com/OrianaVenture/VentureValheim) (MIT), for the per-boss unlock approach
- [XPortal](https://github.com/SpikeHimself/XPortal) by SpikeHimself (no code used)
