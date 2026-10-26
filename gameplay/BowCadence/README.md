# Bow Cadence 1.0.0

For the pinned Valheim 1.0.20 / NorseDemigods 1.8.1 collection. The local player's
standard `bow_fire` state runs at `1 + 0.005 * clamp(Bows skill, 0, 100)`: 1x at 0,
1.25x at 50 and 1.5x at 100. This accelerates release/recovery in that animation,
not the separate bow draw. Damage, projectile setup, stamina, XP, crossbows,
melee animations and NPCs are unchanged. Existing animation speed modifiers
compose through the AnimationSpeedManager already bundled with NorseDemigods;
no second copy of that DLL is distributed.

Windstride's Bow Animations 1.1.1 keeps the ordinary fire state and animator speed,
and drives its cosmetic pull pose from GetAttackDrawPercentage. Offline code
review therefore finds no overlapping speed write. Mounted poses and live
multiplayer appearance still require observation; no gameplay/saves are loaded
by these tests.

Server and every client require this version through Jotunn. Restart closed
game/server processes to install. No hot-loading or auto-updating is supported.
No new configurable multipliers or per-frame MonoBehaviour loop are introduced.

```powershell
dotnet build gameplay/BowCadence -c Release
dotnet run --project gameplay/BowCadence/Tests -c Release -- <project-root>
```
