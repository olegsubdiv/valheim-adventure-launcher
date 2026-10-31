# Shard Category Freedom 1.0.0

Removes EpicLoot 0.14.13's one-per-category restriction for Boss and Unique
shardstones. Multiple different Boss and Unique shardstones can be socketed in
the same item or worn across equipped items, subject to the remaining native
socket and effect requirements. Their Boss/Unique categories are retained.

At startup removes only Boss and Unique from EpicLoot's ExclusiveCategories
registry. Native socket checks, equipment checks, tooltips and compendium all
read this same registry, so the obsolete category-limit hints disappear too.
No Harmony patches, per-frame work, damage hooks, new RPC or configuration.
The exact EpicLoot version is checked. On plugin destruction only the categories
actually removed by this instance are restored.

Socket counts, item eligibility, duplicate-effect restrictions, matching rolled
effects, diminishing returns, shard effect values, drops, rarity, extraction and
removal costs retain their original rules. This does not permit arbitrary copies
of the same effect or add sockets. No existing item or saved data is rewritten.

Requires matching versions on the server and all clients through Jotunn.
Install with the game and server closed. Removing the plugin restores native
category restrictions on the next normal load; do not assume a multi-category
loadout will remain equippable after removal. Live socketing/equipment and
multiplayer use have not been tested.
