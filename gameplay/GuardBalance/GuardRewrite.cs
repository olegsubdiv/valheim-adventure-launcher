using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace AdventureRpg.GuardBalance;

internal static class GuardRewrite
{
    private static readonly FieldInfo Parry = AccessTools.Field(typeof(ItemDrop.ItemData.SharedData), "m_timedBlockBonus");
    private static readonly MethodInfo CombatPower = AccessTools.Method(typeof(ItemDrop.ItemData), "GetBlockPower", new[] { typeof(float) });
    private static readonly MethodInfo BasePower = AccessTools.Method(typeof(ItemDrop.ItemData), "GetBaseBlockPower", new[] { typeof(int) });
    private static MethodInfo Helper(string name) => AccessTools.Method(typeof(GuardPolicy), name);

    internal static IEnumerable<CodeInstruction> Combat(IEnumerable<CodeInstruction> instructions) => Rewrite(instructions, true);
    internal static IEnumerable<CodeInstruction> Tooltip(IEnumerable<CodeInstruction> instructions) => Rewrite(instructions, false);

    private static IEnumerable<CodeInstruction> Rewrite(IEnumerable<CodeInstruction> instructions, bool combat)
    {
        var input = instructions.Select(i => new CodeInstruction(i)).ToList();
        var sourceCall = combat ? CombatPower : BasePower;
        int powers = input.Count(i => i.Calls(sourceCall));
        int parries = input.Count(i => i.opcode == OpCodes.Ldfld && Equals(i.operand, Parry));
        if (powers != 1 || parries != 2 || input.Any(i => i.blocks.Count != 0))
            throw new NotSupportedException($"Guard Balance expected one block-power call and two parry reads without exception regions ({(combat ? "combat" : "tooltip")}); got {powers}/{parries}.");
        var output = new List<CodeInstruction>();
        foreach (var instruction in input)
        {
            if (instruction.Calls(sourceCall))
            {
                if (combat)
                {
                    var defender = new CodeInstruction(OpCodes.Ldarg_0);
                    instruction.MoveLabelsTo(defender);
                    output.Add(defender);
                }
                instruction.opcode = OpCodes.Call;
                instruction.operand = Helper(combat ? nameof(GuardPolicy.CombatBlockPower) : nameof(GuardPolicy.TooltipBaseBlockPower));
                output.Add(instruction);
            }
            else
            {
                output.Add(instruction);
                if (instruction.opcode != OpCodes.Ldfld || !Equals(instruction.operand, Parry)) continue;
                if (combat) output.Add(new CodeInstruction(OpCodes.Ldarg_0));
                output.Add(new CodeInstruction(OpCodes.Call, Helper(combat ? nameof(GuardPolicy.CombatParryBonus) : nameof(GuardPolicy.TooltipParryBonus))));
            }
        }
        return output;
    }
}
