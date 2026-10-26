# NPC Equipment Isolation 1.0.0

Compatibility fix for the pinned RenegadeVikings 1.4.2 and Valheim 1.0.20 collection.

RenegadeVikings assigns equipment quality directly to shared ObjectDB item prefabs.
Its neutral NPC armor default is quality 10. EpicLoot can then instantiate those
prefabs as player loot with the same unintended quality (for example, troll pants
with 24 armor instead of the ordinary quality 1–4 range of 6–12).

This plugin suppresses EquipmentAdjustment.Awake, including its prefab-writing
config callbacks. After Humanoid.GiveDefaultItems, it applies the configured
weapon and armor qualities to the affected NPC's private inventory ItemData.
Neutral, hostile, boss and tamed NPCs retain their configured loadout quality.
Shared item prefabs, ordinary NPCs and player inventories are untouched.
No update loop, damage hooks, RPCs, new settings or save migrations are added.

Install on the server and every client, with the game/server stopped. Jotunn
requires the same patch version on peers. Only RenegadeVikings 1.4.2 is supported;
other versions require review. Do not hot-load this into an already polluted
session. Existing quality-10 items are deliberately left unchanged.

Build and synthetic regression / pinned assembly checks:

```powershell
dotnet build gameplay/NpcEquipmentIsolation -c Release
dotnet run --project gameplay/NpcEquipmentIsolation.Tests -c Release
```

The checks do not launch Valheim or load saves. A live multiplayer session has
not been tested. Upstream mod and game binaries are not included in this source.
