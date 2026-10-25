# Adventure RPG - No Dummy XP

Small fixed-policy BepInEx plugin for the pinned Adventure RPG collection.
Training-dummy faction interactions grant no weapon, magic, block/parry or perfect-dodge bonus XP. Scoped skill awards from other mods inside these damage/block calls are also suppressed. Ordinary enemies, resource gathering, and unrelated running/jumping/rolling XP are unchanged. Existing skill values and saves are never edited.

The dummy still takes damage, attacks, and permits parry/dodge practice. A mixed hit against a dummy and a real eligible target retains the original XP award for the real target. Area spells do not consume their one-XP flag on the dummy.

Install the same version on the dedicated server and every client; Jotunn enforces compatibility. No global skill multiplier, permanent character flag, proximity rule or network timeout change is used. Tested against the collection's pinned game 1.0.20; unsupported damage-call layouts refuse to patch.

Build with the local SDK, providing `-p:GameRoot=...` to a Valheim dedicated-server directory containing the collection's BepInEx and Jotunn. Game assemblies are referenced, never bundled. Copyright 2026 Adventure RPG contributors. MIT License.
