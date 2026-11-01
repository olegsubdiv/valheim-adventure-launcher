# AutoAttack Jotunn compatibility patch

This offline Mono.Cecil tool adds only HarmonyAfter(com.jotunn.jotunn) and HarmonyPriority(0) to the existing AutoAttack0.1.0 held-attack postfix. All 62 original method bodies must remain identical. It accepts only the pinned upstream release DLL. No game or plugin is executed.

Run with .NET10: `dotnet run -c Release -- original-AutoAttack.dll 0Harmony.dll output-AutoAttack.dll review.json`.

The canonical shared-pack binary is the preserved, previously installed patched DLL with SHA2565320f11a763db54351face089773731a94c84fe8beeed2a1fc2d12a63a9b57f1.

Upstream AutoAttack and this patch source are distributed under MIT-0; see LICENSE.
