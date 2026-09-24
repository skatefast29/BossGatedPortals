# BossGatedPortals

Ores and metals can be teleported once you have defeated that biome's boss. Fully configurable. Made to run with
**[XPortal](https://thunderstore.io/c/valheim/p/SpikeHimself/XPortal/)**; also works with vanilla portals.

## Features

- **One tier per boss**, each unlocking its biome's items. Item lists are configurable.
- **Two unlock conditions** (each switchable):
  - boss killed on this world
  - this player has picked up the boss drop
- **Final tier unlocks everything**, including items no tier lists.
- **Zone hints** when a portal refuses you. Never names a boss.
- **Accurate tooltips** and inventory icons, per player.
- **Unlock announcements** to all players on a boss's first kill.
- **NeverTeleport list** for items that stay blocked forever.
- **Cart cargo gated** when a cart-teleport mod is used.
- **Admin tools:** `portalgate status` command and optional `AdminBypass`.
- **Server-synced, admin-only settings**, editable live with F1. Strict version check.
- **Startup checks** log item typos, duplicate items, unlisted blocked items and conflicting mods.

## Installation

**Requires:** BepInExPack_Valheim, Jotunn.

- Install on the server and every client (same version).
- **Gale / r2modman:** *Import → local mod*, pick the zip.
- **Manual:** copy `BossGatedPortals.dll` to `BepInEx/plugins/BossGatedPortals/`.

Config: `BepInEx/config/com.jtmill01.bossgatedportals.cfg` (created on first launch).

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
| `BossItem` | Boss drop the player must have held |
| `Items` | Items unlocked, comma-separated |
| `Hint` | Shown when blocked |
| `UnlockMessage` | Announced on first kill |
| `AllowEverything` | T7 only: unlock all blocked items |

## Settings

All admin-only and server-synced, except those marked *client*.

| Section | Setting | Default | Description |
|---|---|---|---|
| General | `Enabled` | `true` | Master switch |
| General | `RequireWorldKey` | `true` | Boss killed on this world |
| General | `RequirePlayerBossItem` | `true` | Player has held the boss drop. If both are `false`, world key is required. |
| General | `CumulativeTiers` | `false` | A later tier unlocks all earlier tiers |
| Item Rules | `NeverTeleport` | *(empty)* | Items never teleportable |
| Item Rules | `HildirChestsByTier` | `false` | Hildir's chests unlock with their biome's tier (Brass T1, Silver T3, Bronze T4) |
| Item Rules | `GateListedVanillaItems` | `false` | Also gate listed items vanilla allows |
| Item Rules | `FinalTierIncludesCheatTier` | `false` | T7 also unlocks tool-tier 1000+ items |
| Item Rules | `LogUnmappedItems` | `true` | Log blocked items not in any tier |
| Item Rules | `ValidateItemIds` | `true` | Warn on unknown item names |
| Messages | `ShowUnlockHint` | `true` | Show the tier hint when blocked |
| Messages | `HintFormat` | `An item blocks the portal. {hint}` | Supports `{hint}` and `{item}` |
| Messages | `HintPosition` | `Centered` | *Client.* `Centered` or `TopLeft` |
| Messages | `HintCooldownSeconds` | `5` | *Client.* Seconds between hints (0–60) |
| Messages | `AccurateTooltips` | `true` | Per-player tooltips and icons |
| Messages | `AnnounceTierUnlock` | `true` | Announce first boss kills |
| Admin | `AdminBypass` | `false` | Admins ignore the gate |
| Admin | `EnableStatusCommand` | `true` | Enable `portalgate status` |
| Compatibility | `RespectVanillaAllowAll` | `true` | Don't block Stone Portals or the allow-all *Portals* world modifier |
| Compatibility | `GateAttachedCartCargo` | `true` | Gate cart cargo |
| Compatibility | `WarnOnConflictingMods` | `true` | Warn about conflicting mods |
| XPortal Compatibility | `LogPortalModDetection` | `true` | Log XPortal/XPortalNetworks versions, warn on AnyPortal |

## Compatibility

Checked against XPortal 1.2.25 only.

## Known limits

- Checks run client-side; a modified client can bypass them.
- After Valheim updates, check the log for `ValidateItemIds` and `LogUnmappedItems` warnings.

## Credits

- [Jotunn](https://github.com/Valheim-Modding/Jotunn) and [JotunnModStub](https://github.com/Valheim-Modding/JotunnModStub) (MIT No Attribution)
- [VentureValheim](https://github.com/OrianaVenture/VentureValheim) (MIT), for the per-boss unlock approach
- [XPortal](https://github.com/SpikeHimself/XPortal) by SpikeHimself (no code used)
