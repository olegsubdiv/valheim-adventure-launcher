# Death Skill Guard

Version 1.0.0, plugin `arpg.deathskillguard`.

Preserves all existing skill levels and partial XP progress when the player dies.
Both ordinary skill reduction and the world modifier that resets all skills are
handled. Inventory/tombstones, food, respawn, death statistics, death cooldowns,
and other death consequences retain their existing behavior.

The patch replaces exactly two call sites **inside `Player.OnDeath`**:
`Skills.OnDeath()` and `Skills.Clear()`. Each calls an empty static wrapper that
consumes the same Skills argument. Every other instruction, branch label and
exception block is retained. Startup fails visibly if either reviewed call site
is absent or duplicated, and rolls back our Harmony patches.

`Skills.LowerAllSkills`, `Skills.Clear`, experience gain, and class switching
outside player death remain untouched. In particular, the NorseDemigods class
switch cost remains one level. No configuration file, per-frame callback, network
RPC, player-save edit or world-save edit is introduced.

Reviewed against the pinned Valheim 1.0.20 assembly and NorseDemigods 1.8.1.
Norse's `Player.OnDeath` postfix retains ability cleanup; its skill-loss override
only runs from `Skills.LowerAllSkills`, which is no longer called by death.
Installed SkillManager versions wrap `Skills.OnDeath` or its reduction path;
the death call no longer enters either hook, so their custom skills are preserved.

Requires BepInEx/Harmony and Jotunn. Install the same patch version on the server
and every client (Jotunn `EveryoneMustHaveMod`, patch version strictness).
Offline checks exercise the actual rewrite and pinned call sites. They do not
replace a live multiplayer death/respawn check.
