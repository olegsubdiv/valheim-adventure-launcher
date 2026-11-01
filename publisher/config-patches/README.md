# Rare Mythic equipment

`rare-mythic-4-5-stars.json` records the rare Mythic overlay in the existing
AdventureRPG loot patch. It is a source record, not another installed patch file.

For EpicLoot 0.15.5, append six Loot-only level5/6 anchors to Tier4/5/6Mob.
Their item weights and normal rarity distribution are computed with the native
0.15.5 sparse-anchor/star-scaling rules. Transfer 0.125 percentage points from
Legendary to Mythic on 4-star enemies and 0.25 on 5-star enemies. These are
conditional equipment rarity chances, not chances per kill. Elite tables use
the same equipment rows. Native Drops anchors and drop scaling stay intact.

Earlier tiers, fewer stars, bosses, chests, shard drops, overall drop counts,
equipment progression and later tiers with existing Mythic weights are unchanged.
The first common examples are wolves/cultists (Tier4), fulings/fenrings (Tier5),
and fuling shamans/berserkers (Tier6). This adds rarity, not higher-biome items.

Keep this source record aligned with the signed configuration snapshot. Do not
install both this record and the combined AdventureRPG patch, or its operations
will be redundant.
