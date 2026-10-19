# BossGatedPortals Plus

A progression mod for Valheim portals:

- **Ores and metals** can go through portals once that biome's boss is beaten. Each boss unlocks its own
  biome's goods.
- **A world portal limit** that grows with each boss killed: 2 portals at the start, more after every boss,
  no limit after the last one. On for **new worlds** only.

Optional extras (companions that travel with you, faster portals) are **off by default**.

Works with **XPortal** and **TargetPortal**. All settings are server-synced and editable live with F1.

## Progression (on by default)

- **One tier per boss**, each unlocking its biome's items. Item lists are configurable.
- **Unlock rule** (`UnlockWhen`): the boss killed on this world (default), or also each player having
  picked up the boss drop, or the drop alone. `CumulativeTiers` lets a later tier unlock earlier ones.
- **The final tier unlocks everything.** A **NeverTeleport** list keeps items blocked forever.
- **Everything you carry counts:** your inventory, backpacks (AdventureBackpacks, Backpacks, RustyBags),
  and anything a mod brings through with you (a cart, tames' saddlebags).
- **Hints** when a portal refuses you say where the item is ("in your Cart") and which part of the world
  still has to be conquered. They never name a boss.
- **Portals only glow** when you can go through, and **tooltips and inventory icons** show what you can
  teleport right now.
- **Unlock announcements** to everyone on a boss's first kill.
- **For admins:** `portalgate status` and `portalgate limit on/off` console commands, optional `AdminBypass`,
  and startup checks that log item typos, duplicates and conflicting mods.

## Portal limit (on for new worlds)

- Caps how many portals the whole world can hold (everyone's together). Defaults: 2 before any boss, then 4,
  6, 8, 10, 12, 16, 20, and no limit after the final boss (each tier's `MaxPortals`).
- **New worlds only.** A world that already has portals when the server first loads it with this mod is left
  without a limit, so installing or updating never locks anyone's portals. Admins can switch a world with
  `portalgate limit on/off`.
- Building over the limit is refused before any materials are used.
- `PortalLimitLockWhenOver` (on): while the world is over its limit, no portal works until enough are
  destroyed. The mod never removes portals.
- `PortalLimitAdminsExempt` (off) lets admins build past the limit (their portals still count).
- `PortalLimitCountStonePortals` (on): Stone Portals count like wood ones. Off = they don't count, can always
  be built, and keep working when the world is over its limit.
- Counts vanilla, XPortal and TargetPortal portals. Portal pieces from other mods only count if that mod
  registers them as game portals.

## Optional extras (off by default)

**Travel with your companions** (`5 - Travel`)
- **Mount:** ride into a portal and arrive in the saddle.
- **Cart:** arrives hitched to you, or to your horse (OdinHorse).
- **Tames:** following, named, or all tames nearby, with radius, height, headcount and creature lists.
- Works through vanilla, XPortal and TargetPortal portals. Their cargo follows the same rules as your
  inventory. Steps aside while TeleportEverything is on.

**Faster portals** (`4 - Portal Speed`)
- `FadeSeconds` (default 1) and `MinimumLoadingSeconds` (default 8) match the game. Lower them, down to 0.
  Real loading is never skipped.
- Steps aside while QuickTeleport, FastTeleport or Proper Portals is installed.

## Installation

**Requires:** BepInExPack_Valheim, Jotunn.

- Install on the server and every client, with the same major.minor version (e.g. any 1.3.x).
- **Gale / r2modman:** install *BossGatedPortals* as usual.
- **Manual:** copy `BossGatedPortals.dll` to `BepInEx/plugins/BossGatedPortals/`.

Config: `BepInEx/config/BarryWhite.BossGatedPortals.cfg`. Settings from 0.2.x carry over automatically.

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

Hildir's chests never teleport unless `HildirChestsByTier` is on.

Per-tier settings:

| Setting | Meaning |
|---|---|
| `GlobalKey` | World key set by the boss kill |
| `BossItem` | Boss drop the player must have held (when `UnlockWhen` asks for it) |
| `Items` | Items unlocked, comma-separated |
| `Hint` | Shown when blocked |
| `UnlockMessage` | Announced on first kill |
| `AllowEverything` | T7 only: unlock all blocked items |
| `MaxPortals` | Portal limit once this boss is killed. -1 = no limit |

## Settings

All admin-only and server-synced, except **8 - Client**, which each player sets for themselves.

| Section | Setting | Default | Description |
|---|---|---|---|
| 1 - General | `Enabled` | `true` | Master switch |
| | `UnlockWhen` | `BossKilled` | `BossKilled` (on this world, for everyone), `BossKilledAndDropHeld` (each player must also have held the boss drop) or `DropHeld` |
| | `CumulativeTiers` | `false` | A later tier unlocks all earlier tiers |
| 2 - Item Rules | `NeverTeleport` | *(empty)* | Items never teleportable |
| | `HildirChestsByTier` | `false` | Hildir's chests unlock with their biome's tier (Brass T1, Silver T3, Bronze T4) |
| | `LockListedItemsAlways` | `false` | Also lock listed items the game or another mod already lets through |
| | `FinalTierIncludesCheatTier` | `false` | T7 also unlocks tool-tier 1000+ items |
| | `RespectVanillaAllowAll` | `true` | Stone Portals carry every item, as in vanilla. `false` = they follow the tiers |
| 3 - Messages | `HintFormat` | `An item{where} blocks the portal. {hint}` | Placeholders `{hint}`, `{where}`, `{item}`. Empty = the game's message |
| | `AnnounceTierUnlock` | `true` | Announce first boss kills |
| | `AccurateTooltips` | `true` | Per-player tooltips and icons |
| 4 - Portal Speed | `FadeSeconds` | `1` | Screen fade into and out of a teleport. 0 = instant |
| | `MinimumLoadingSeconds` | `8` | Shortest loading screen. 0 = no built-in delay |
| 5 - Travel | `BringMount` | `false` | Your mount comes through and you're remounted |
| | `BringCart` | `false` | Your cart comes through and is hitched back up |
| | `BringTames` | `false` | Tames near you come through. The `Tame…` settings only apply while this is on |
| | ↳ `TameMode` | `Following` | `Following`, `FollowingOrNamed`, `Named` or `AllTamed`. Tames following another player never come |
| | ↳ `TameRadius` | `10` | Metres around you |
| | ↳ `TameHeightRange` | `3` | Metres above/below you |
| | ↳ `TameMaxCount` | `5` | Most tames per trip, nearest first |
| | ↳ `TameAllowList` | *(empty)* | If set, only these creatures (e.g. `Wolf, Lox`) |
| | ↳ `TameBlockList` | *(empty)* | These creatures never come |
| | ↳ `TameIncludeSummons` | `false` | Also staff summons that follow you |
| 6 - Compatibility | `GateCompanionCargo` | `true` | Gate cart cargo and tame saddlebags when another mod brings them along |
| | `WarnOnConflictingMods` | `true` | Warn about conflicting mods and settings |
| 7 - Admin | `AdminBypass` | `false` | Admins ignore the gate |
| | `StatusCommand` | `true` | Enable `portalgate status` |
| | `LogItemChecks` | `true` | Log unknown item names, items in two tiers, and blocked items no tier lists |
| 8 - Client | `HintPosition` | `Centered` | `Centered` or `TopLeft` |
| | `HintCooldownSeconds` | `5` | Seconds between hints (0–60) |
| 9 - Portal Limit | `PortalLimitEnabled` | `true` | World portal limit, raised by each tier's `MaxPortals`. New worlds only. The settings below only apply while this is on |
| | ↳ `PortalLimitStart` | `2` | Portals allowed before any boss is killed |
| | ↳ `PortalLimitAdminsExempt` | `false` | Admins can build past the limit |
| | ↳ `PortalLimitMessage` | `This world holds {count} of {limit} portals. …` | Shown when a portal can't be built |
| | ↳ `PortalLimitLockWhenOver` | `true` | While over the limit, no portal works |
| | ↳ `PortalLimitOverMessage` | `This world has too many portals ({count} of {limit}). …` | Shown at a locked portal |
| | ↳ `PortalLimitCountStonePortals` | `true` | Stone Portals count toward the limit |

## Compatibility

Tested in game with XPortal 1.2.25, TargetPortal 1.2.7, AdventureBackpacks 2.4.5, Backpacks 1.3.10,
OdinHorse 1.7.5 and LoxSaddleBags 2.0.2. The rest was checked by reading their code.

- **Portal mods:** XPortal, TargetPortal, Unified Target Portal, XPortalNetworks, PortalRules, PotalMap,
  Better Portal, Portal Stations, Waypoints and Waystones all use the game's item check, so the tiers apply.
- **Portal speed mods** (QuickTeleport, FastTeleport, Proper Portals): Portal Speed steps aside; use their
  settings. Proper Portals' "carry anything" doesn't override this mod.
- **Bags:** AdventureBackpacks, Smoothbrain's Backpacks and RustyBags bags are gated like your inventory.
  ExtraSlots and AzuExtendedPlayerInventory slots count as your inventory.
- **Tames:** when TeleportEverything or Waypoints brings tames along, their saddlebags are gated too
  (LoxSaddleBags, OdinHorse, or any container on a tame). Set TeleportEverything's
  `TransportRestrictedItems` (the 1.0 fork: `Transport Ores`) to false, or it lets everything through.
- **Carts and ships:** a cart a mod pulls through with you is gated. BottleShips bottles arrive empty.
  ValheimRAFT's ship portals only move the player.
- **Mods that change items:** if a mod makes a tier-listed item teleportable, the log says so once.
  `LockListedItemsAlways = true` locks it anyway.
- **Warned at startup:** mods or settings that let every item through or add their own gate (e.g.
  TeleportEverything, AzuMiscPatches, ValheimPlus, ServersideQoL's PortalProgression and TameAssist,
  AdvancedPortals, ReturnScroll, TeleportationMeads, and the portal mods' "ignore items" options).
- **Not gated:** ways to move items without portals (shipping mods, cross-server portals, admin commands).

## Known limits

- Checks run client-side; a modified client can bypass them.
- Mounts, carts and tames only travel through vanilla, XPortal and TargetPortal portals. A cart you pull is
  still checked through other map-teleport mods.
- The portal count reaches players every 2 seconds, so two players building at the same moment can go one
  over the limit (`PortalLimitLockWhenOver` makes that pointless).
- A mod that blocks world keys (e.g. Server Devcommands' `DisableGlobalKeys`) can keep the portal limit off.
- With `PortalLimitCountStonePortals` off, a Stone Portal still locks when you jump from TargetPortal's map
  while the world is over its limit.
- After Valheim updates, check the log for `LogItemChecks` warnings.

## Source and bug reports

Source code and bug reports are on [GitHub](https://github.com/skatefast29/BossGatedPortals). MIT licensed.

## Credits

- [Jotunn](https://github.com/Valheim-Modding/Jotunn) and [JotunnModStub](https://github.com/Valheim-Modding/JotunnModStub) (MIT No Attribution)
- [VentureValheim](https://github.com/OrianaVenture/VentureValheim) (MIT), for the per-boss unlock approach
- [XPortal](https://github.com/SpikeHimself/XPortal) by SpikeHimself (no code used)
- [TargetPortal](https://github.com/blaxxun-boop/TargetPortal) by Smoothbrain (no code used)
