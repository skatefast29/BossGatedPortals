# Changelog

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
