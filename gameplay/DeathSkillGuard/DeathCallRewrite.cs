using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace AdventureRpg.DeathSkillGuard;

internal static class DeathCallRewrite
{
    internal static IEnumerable<CodeInstruction> Rewrite(IEnumerable<CodeInstruction> instructions,
        MethodInfo skillDeath, MethodInfo skillReset, MethodInfo preserve)
    {
        var code = instructions.Select(i => new CodeInstruction(i)).ToList();
        // Validate the entire method before emitting anything. A changed upstream death path
        // must stop patching rather than silently leave one kind of death skill loss active.
        var deathCount = code.Count(i => i.Calls(skillDeath));
        var resetCount = code.Count(i => i.Calls(skillReset));
        if (deathCount != 1 || resetCount != 1)
            throw new NotSupportedException($"Death Skill Guard expected one Skills.OnDeath and one Skills.Clear call in Player.OnDeath; found {deathCount} and {resetCount}.");
        foreach (var instruction in code)
        {
            if (!instruction.Calls(skillDeath) && !instruction.Calls(skillReset)) continue;
            // Static wrapper consumes the same Skills instance; labels/exception blocks remain.
            instruction.opcode = OpCodes.Call;
            instruction.operand = preserve;
        }
        return code;
    }
}
