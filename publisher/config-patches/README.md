# Rare Mythic equipment

`rare-mythic-4-5-stars.json` records the six Epic Loot patch operations added to
the existing AdventureRPG loot patch in collection 2026.10.29.6. It is a source
record, not a second installed patch file.

For Epic Loot 0.14.13, transfer 0.5 rarity weight from Legendary to Mythic on
4-star Tier4/5/6 creatures, and 1 weight on 5-star creatures. These rows total
100, so the weights equal percentages among equipment rarity rolls. They are
not chances per kill. Elite loot tables reference the same equipment rows.

Earlier tiers, fewer stars, bosses, chests, shard drops, overall drop counts,
equipment progression and later tiers with existing Mythic weights are unchanged.
The first common examples are wolves/cultists (Tier4), fulings/fenrings (Tier5),
and fuling shamans/berserkers (Tier6). This adds rarity, not higher-biome items.

Keep this source record aligned with the signed configuration snapshot. Do not
install both this record and the combined AdventureRPG patch, or its operations
will be redundant.
