using DemigodPanel.Logic;

namespace AdventureRpg.NorseDemigodsUICompat;

public static class Policy
{
    public static double HolySunRadius(ValueInputs v) => v.S(v.Infused
        ? "AbilityHolySun.InfusedAoeDamageRadius" : "AbilityHolySun.AoeDamageRadius");

    public static double TorpedoEitr(ValueInputs v) => EitrMath.AttackCharge(Math.Max(0,
        v.S("AbilityLightningTorpedo.AbilityEitrCost") - (v.Infused
            ? v.S("AbilityLightningTorpedo.InfusedAbilityEitrCostReduction") : 0)), v.U);

    public static EitrUse TorpedoUse(double cost, double unarmed)
    {
        var charged = EitrMath.AttackCharge(Math.Max(0, cost), unarmed);
        return new EitrUse(charged, charged, true);
    }
}
