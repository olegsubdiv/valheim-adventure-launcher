# Guard Balance 1.0.0

Player block armor **x1.7** for all shields (including tower shields) and other
equipment whose existing parry bonus is greater than 1. Parry bonus **x1.2**:
an ordinary buckler's 2.5 becomes 3.0. Combined parry armor is x2.04 before
the game's existing timed-block status-effect handling. These replace the earlier
unapproved x2/x3 proposal.

Includes vanilla and modded weapons/tools/bows that can already parry. Items
without a parry bonus gain no new parry ability. Tower shields receive the ordinary
block multiplier and retain their existing inability to parry. NPC defense is
unchanged.

The combat hook wraps one `GetBlockPower(float)` call and two parry-bonus reads
inside `Humanoid.BlockAttack`. The wrappers apply only for a player defender;
the original APIs run with their skill, quality, NorseDemigods and EpicLoot effects.
No shared item prefabs, inventories or saved item data are modified. The native
parry window and enchantments, stamina rules, stagger calculations, resistances,
armor, durability, damage and retaliation rules remain in place. Guard breaks,
residual damage and self-stagger are still possible; x2.04 is not immunity.

Ordinary and EpicLoot item tooltips display the adjusted block armor and parry
bonus, including the skill-adjusted block value. Block force, attack damage and
attack speed are unchanged. UI adjustments do not feed back into combat.

Requires reviewed EpicLoot 0.14.13. Install on server and all clients (Jotunn exact
patch compatibility). No new config, RPC, polling or full damage interception.
Removing the plugin restores native calculations on restart without save edits.

Built and checked offline against pinned Valheim 1.0.20/EpicLoot APIs, executable
policy fixtures and original client/server IL. Live combat/MP is not implied.
