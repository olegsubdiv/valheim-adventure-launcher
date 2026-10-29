# Modded Crop Biomes 1.0.0

Compatibility fix for PlantEverything **1.21.4**. Install on the dedicated server
and every client. The existing server-synced `EnforceBiomesVanilla` option controls
both vanilla and third-party crops, without enabling `EnableCropOverrides` or
`OverrideModdedCrops`.

PlantEverything's original modded-crop initialization gates its biome setting
behind both broad crop override switches. This plugin runs once after each
`InitCrops` call and changes only the registered modded crop prefab's `Plant.m_biome`
and `Piece.m_onlyInBiome`. Enabling enforcement restores the original placement
restriction; upstream restores the original growth biome.

Growth times, harvest amounts, scale, spacing, cultivation, roof and temperature
requirements stay as configured by the original mods. Trees, ordinary pieces,
damage, skills and world generation are untouched. Existing planted crops use
the corrected prefab fields when normally loaded again. No save editing, new
configuration, per-frame loop, damage hook or network RPC.

Removing this plugin restores upstream biome behavior after restarting. A crop
outside its authored biome can then fail to grow under the original rules.

Validated offline against pinned game/PlantEverything APIs and synthetic prefab
fixtures. No live world or multiplayer verification is implied.
