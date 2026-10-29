# Free Boss Resummon

After a boss has been defeated in the current world, interact with its altar
to summon it again without an offering. The first summon keeps its normal cost.
Already defeated bosses are recognized through the existing world progression
key on the boss prefab; no additional defeat history or saved data is created.

The normal summon request, owner checks, queue, spawn-position search, effects
and delay remain in use. Offering items on the hotbar are not consumed either.
Item-stand offerings are preserved on repeat summons. Non-boss offering bowls,
item exchanges, boss loot, difficulty and progression keys are unchanged.

English/Russian altar hint. No config, polling loop or new network messages.
Required on the server and all clients (Jotunn exact patch compatibility).
Live multiplayer and altar interactions must still be checked during normal play.

Build: `dotnet build gameplay/FreeBossResummon -c Release`.
