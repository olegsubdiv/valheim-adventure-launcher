using BepInEx;
using BepInEx.Bootstrap;
using HarmonyLib;
using Jotunn.Utils;
using UnityEngine;

namespace AdventureRpg.BowCadence;

[BepInPlugin(Guid, "Adventure RPG - Bow Cadence", "1.0.0")]
[BepInDependency("NorseDemigods", "1.8.1")]
[BepInDependency("com.jotunn.jotunn")]
[NetworkCompatibility(CompatibilityLevel.EveryoneMustHaveMod, VersionStrictness.Patch)]
public sealed class Plugin : BaseUnityPlugin
{
    public const string Guid = "arpg.bowcadence";
    private static readonly AccessTools.FieldRef<Character, Animator> AnimatorOf =
        AccessTools.FieldRefAccess<Character, Animator>("m_animator");
    private static readonly int BowFireState = Animator.StringToHash("bow_fire");
    private static bool registered;
    private static bool active;

    private void Awake()
    {
        if (Chainloader.PluginInfos["NorseDemigods"].Metadata.Version != new System.Version(1, 8, 1))
            throw new NotSupportedException("Bow Cadence 1.0.0 requires reviewed NorseDemigods 1.8.1 and its AnimationSpeedManager.");
        active = true;
        if (!registered)
        {
            global::AnimationSpeedManager.Add(AdjustSpeed);
            registered = true;
        }
        Logger.LogInfo("Bow release animation scales from 1.0x to 1.5x with Bows 0-100. Draw speed, damage and other weapons are unchanged.");
    }

    private void OnDestroy() => active = false;

    private static double AdjustSpeed(Character character, double speed)
    {
        // Network observers keep native speed handling; the owning player emits
        // the real projectile and animation timing. Never accelerate NPC bows.
        if (!active || character is not Player player || player != Player.m_localPlayer) return speed;
        var weapon = player.GetCurrentWeapon();
        if (weapon == null || weapon.m_shared.m_skillType != Skills.SkillType.Bows) return speed;
        var animator = AnimatorOf(character);
        if (animator == null || animator.GetCurrentAnimatorStateInfo(0).shortNameHash != BowFireState) return speed;
        return speed * CadencePolicy.Factor(active, true, true, true, player.GetSkillLevel(Skills.SkillType.Bows));
    }
}
