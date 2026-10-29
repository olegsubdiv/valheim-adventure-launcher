# Bow Eitr Regen 1.0.0

Allows normal eitr regeneration while attacking with a bow, including vanilla
and modded equipment using the existing Bows skill. Merely attacking with a bow
no longer activates the native eitr-regeneration attack lockout.

Only the eitr-specific `InAttack()` call in `Player.UpdateStats(float)` is replaced.
The separate stamina attack gate and all resource costs stay intact. The native
dodge lockout, blocking modifier, regeneration rate, food maximum, equipment and
status effects (including EpicLoot/Darting Thoughts/NorseDemigods), eitr-spending
delay, clamping and synchronization remain in place.

Holding a bow does not bypass the delay after actually spending eitr: spells,
eitr-consuming bows or other effects can still pause regeneration. Melee weapons,
staves, crossbows and unarmed attacks retain their original attack lockout.
The patch adds no regeneration tick, independent Update loop, RPC or resource
refund. It checks the current weapon only when the existing attack check is true.

Required on server and all clients (Jotunn exact patch compatibility). No new
configuration, item/save changes, bow timing, damage or stamina changes. Removing
the plugin restores native behavior after restarting.

Verified offline with production policy/rewrite fixtures and pinned Valheim
1.0.20 client/server IL. Live regeneration, UI and multiplayer remain untested.
