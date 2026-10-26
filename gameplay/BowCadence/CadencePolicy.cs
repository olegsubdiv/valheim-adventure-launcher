namespace AdventureRpg.BowCadence;

internal static class CadencePolicy
{
    // The release animation is independent of the bow's existing charge speed.
    internal static double Factor(bool enabled, bool localPlayer, bool bow, bool firing, float skill)
    {
        if (!enabled || !localPlayer || !bow || !firing || float.IsNaN(skill)) return 1d;
        return 1d + 0.005d * System.Math.Max(0d, System.Math.Min(100d, skill));
    }
}
