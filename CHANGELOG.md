# Changelog

## 1.0.0

Now **BossGatedPortals Plus**: still a portal progression mod first, with optional extras that are off by
default. Same Thunderstore package, so it updates as usual.

**Updating**
- Server owners: update the server and every player (players need 1.0.x).
- The config file is now `BarryWhite.BossGatedPortals.cfg`. Your old file's settings are copied into it on
  first start, then carried over to the new setting names below; the old file is kept as
  `BarryWhite.BossGatedPortals.cfg.before-1.0.0`.

**Progression**
- New installs unlock a tier for everyone once its boss is killed on the world (`UnlockWhen = BossKilled`).
  Existing servers keep their current rule.
- The hint now says where the blocking item is when it isn't in your inventory: "in your Cart",
  "in Lox's saddlebags", "in your Explorer's Backpack" (new `{where}` in `HintFormat`; an unchanged hint
  text is updated automatically).
- `LogItemChecks` now also warns about a tier missing the world key or boss item it needs (it would never
  unlock).
- Inventory icons and tooltips are about three times faster: item names and unlocked tiers are looked up
  once instead of for every item, and Backpacks bag checks are a direct call instead of a slow lookup.
- Saddlebags on tames the TeleportEverything 1.0 fork brings along are gated too. New startup warnings: the
  fork's `Transport Ores` (lets every item through), and ServersideQoL TameAssist's follower teleport
  (following tames' saddlebags skip the gate).

**Optional extras (off by default)**
- `[5 - Travel]`, each switched on separately:
  - `BringMount`: ride into a portal and your mount comes with you; you're put back in the saddle.
  - `BringCart`: a cart you're pulling comes with you and is hitched back to you.
  - `BringTames`: tames near you come with you. Choose which with the `Tame…` settings right under it:
    following you, named, or all tames, how far away, how many, which creatures, and whether summons come
    too. Tames following another player, or pulling a cart, never come.
  - A cart your horse pulls (OdinHorse) comes along with BringMount and BringCart both on, and is hitched
    back to the horse; with BringCart off it's unhitched and left behind.
  - Carts are always hitched back to whoever pulled them, even with other animals nearby.
  - Cart cargo and saddlebags follow the tiers like your own inventory, and a portal doesn't glow while
    they hold something locked.
  - Only through portals you walk into (vanilla and XPortal).
- Steps aside for mods that already do the same job, with a line in the log: Travel while either
  TeleportEverything (Zenox's, or the 1.0 fork) is on; Portal Speed while QuickTeleport (OdinPlus or
  Muindor), FastTeleport or Proper Portals is installed.
- `[4 - Portal Speed]`, set to the game's own timings: `FadeSeconds` (default 1) sets how fast the screen
  fades when you step into a portal, down to 0 for instant; `MinimumLoadingSeconds` (default 8) sets the
  shortest loading screen, down to 0. Real loading time is never cut.

**Shorter, regrouped config** (your settings carry over automatically; the old lines are removed)
- Sections: 1 General, 2 Item Rules, 3 Messages, 4 Portal Speed, 5 Travel, 6 Compatibility, 7 Admin,
  8 Client.
- `RequireWorldKey` and `RequirePlayerBossItem` became one choice, `UnlockWhen`: `BossKilled`,
  `BossKilledAndDropHeld` or `DropHeld`.
- `ShowUnlockHint` is gone: an empty `HintFormat` turns the hint off.
- `GateAttachedCartCargo` and `GateTameCargo` became `GateCompanionCargo`.
- `GateListedVanillaItems` is now `LockListedItemsAlways`, with a clearer description.
- `ValidateItemIds` and `LogUnmappedItems` became `LogItemChecks`; `EnableStatusCommand` is now
  `StatusCommand`.
- `LogPortalModDetection` is gone: the XPortal version is always logged, and the AnyPortal warning is part
  of `WarnOnConflictingMods`.
- `RespectVanillaAllowAll` moved to Item Rules; `HintPosition` and `HintCooldownSeconds`, the settings
  each player chooses, have their own Client section.

## 0.2.1

- New `GateTameCargo` setting (on by default): when TeleportEverything, or Waypoints with "Teleport Tames"
  on, brings tames through with you, items in their saddlebags follow the tiers too (LoxSaddleBags,
  OdinHorse, or any container on a tame). Normal portals without those mods are unaffected.

## 0.2.0

- Server owners: update the server and every player. Players need 0.2.x to join a 0.2.0 server, so
  nobody can keep using the backpack exploit below on an older version.
- Fixed: ore in an AdventureBackpacks bag could go through portals before its boss was defeated.
  Bag contents now follow the tiers, and the hint names the right boss area.
- Smoothbrain's Backpacks and RustyBags: items in their bags now unlock with their tiers. Before, they
  stayed blocked until the final tier.
- The ServersideQoL warning now only appears for its PortalProgression add-on, not for every ServersideQoL part.
- New startup warnings when another mod is set to let every item through: TeleportEverything (on by
  default), AzuMiscPatches, ValheimPlus, TargetPortal, Unified Target Portal, Portal Stations, Waypoints,
  Waystones, ReturnScroll, PortalRules (paid fares) and TeleportationMeads.
- New log warning if a tier-listed item is teleportable anyway because another mod changed it.

## 0.1.4

- Server owners: update the server first. From this version, players only need the same major.minor
  version as the server (any 0.1.x), so future bug-fix releases won't lock anyone out.
- Safer against future Valheim updates: if an update makes part of the game private (what broke
  portals in 0.1.2), the mod keeps working instead of throwing errors.
- Every hook now falls back to vanilla behaviour if something goes wrong, including the inventory
  icon, the unlock hint and the unlock announcement.
- Better compatibility with other mods: the portal item check now runs after the game's own check
  instead of replacing it. No change in-game.
- Small fixes from Valheim modding guidance: the config file is written once at startup, and the
  mod no longer unhooks itself when the game closes.

## 0.1.3

- Fixed portals not working at all after the September 2026 Valheim update (the cart cargo check crashed).
- Safer against future game updates: if a game change breaks one of the mod's hooks, that feature falls back
  to vanilla with an error in the log, instead of breaking portals, tooltips or the inventory.
- Requires BepInExPack Valheim 5.4.2351.

## 0.1.2

- Source code on GitHub (linked from the mod page and README). No gameplay changes.

## 0.1.1

- Iron Pit unlocks in Swamp, Dvergr Extractor in Mistlands; Petrified Tissue and Bloodgold listed in Deep North.
- New `HildirChestsByTier` setting (off by default): Hildir's chests never teleport unless enabled.

## 0.1.0

First release.

- Teleport-blocked items unlock one tier per boss (world key + the player has held the boss drop).
- The last tier (Deep North) unlocks everything; NeverTeleport list for permanent blocks.
- Zone hints when a portal refuses you (never name a boss), with a per-player cooldown and position.
- Tooltips and inventory icons show the real per-player teleport result.
- Server-wide unlock announcements, admin-only `portalgate status`, optional AdminBypass.
- Cart cargo gate for cart-teleport mods; conflict and XPortal/AnyPortal detection in the log.
- Server-synced, admin-only settings; strict version check.
