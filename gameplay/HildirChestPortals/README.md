# Hildir Chest Portals 1.0.0

Allows the three vanilla Hildir quest items `chest_hildir1`, `chest_hildir2`
and `chest_hildir3` through portals. Sets only their shared item definition's
`m_teleportable` flag during `ObjectDB.UpdateRegisters`, which runs when the
item database starts and when another database is copied.

Existing and newly obtained chests use the updated definitions on normal game
reload. Native inventory checks and item tooltips see the same flag. Weight,
contents, trading, quest keys, recipes and all other item properties are unchanged.
Ore, metal and other non-teleportable items retain their existing restrictions.
World portal settings and TargetPortal behavior are unchanged.

No configuration, new RPC, per-frame update, inventory scan during play, or save
editing. Removing the plugin restores the native flag on the next game load.
Requires the server and all clients to have matching patch versions through
Jotunn. Install with games and server closed. Verified offline against pinned
client/server APIs and item assets; live portal/multiplayer use is not tested.
