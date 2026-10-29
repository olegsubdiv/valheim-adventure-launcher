# Darting Thoughts Tuning 1.0.0

Reduces only the **magnitude of Darting Thoughts' maximum-eitr penalty by 20%**.
For example, -50% becomes -40%, -25% becomes -20%, and -10% becomes -8%.
The eitr-regeneration bonus keeps its original value.

EpicLoot 0.14.13 couples both effects to the same stored enchantment value:
regeneration uses that value, while maximum eitr uses half of it. This plugin
changes the latter coefficient from 0.5 to 0.4 in the existing Darting Thoughts
postfix. Its other instructions, stacking, active-effect lookup, maximum-food-eitr
calculation and zero floor stay in place. No generic player-stat hook is added.

The change applies to all rarities, existing items, new drops, rune/shard effects
and combined active Darting Thoughts values. Item rolls, rarity ranges, rune
strength, selection weights, other effects and saved item data are unchanged.

The effect's display-value provider is registered with the new penalty ratio,
so existing translated tooltips and descriptions show the reduced penalty while
retaining the original regeneration value. Only this effect's provider changes.

Requires reviewed EpicLoot 0.14.13 on the server and all clients (Jotunn exact
patch compatibility). No new config, polling, RPC, damage hook or save format.
The runtime change replaces a float literal; it adds no recurring checks.
Removing the plugin restores the original behavior after restarting.

Verified offline with the production rewrite/provider and the pinned EpicLoot
IL. Live UI, character stats and multiplayer remain untested.
