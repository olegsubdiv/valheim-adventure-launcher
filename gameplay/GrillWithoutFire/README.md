# Grill Without Fire 1.0.0

Removes only the separate-fire requirement from the two Stone Grill prefabs
in this collection: Valharvest `piece_cooking_pot` (currently selected) and
BoneAppetit `rk_grill` (existing older pieces and the alternative station mode).
Their normal Unity clone names are matched exactly.

A prefix on `CraftingStation.Start` sets only `m_craftRequireFire=false`, before
vanilla schedules fire detection. Valharvest's existing `CheckFire` patch therefore
does not need to run for these grill instances. No per-frame or per-damage hooks,
new physics scans, configuration or network RPCs are added.

Roof/shelter, station level, upgrades, distance, recipes, ingredient costs and
crafting skills keep their original rules. Cauldrons, forges, griddles, ovens,
preparation tables, cooking racks, smelters, fireplaces and warmth are unchanged.

Install on the server and every client; Jotunn requires matching patch versions.
Already built grills receive the change when normally loaded after installation.
No saved world data is modified. Removing the plugin restores the original fire
requirement on the next normal load.

Validated offline against the pinned Valheim 1.0.20 API and fixture stations.
Live crafting and multiplayer remain untested.
