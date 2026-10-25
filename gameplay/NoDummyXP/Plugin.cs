using System.Reflection;
using System.Reflection.Emit;
using BepInEx;
using HarmonyLib;
using Jotunn.Utils;
using UnityEngine;

namespace AdventureRpg.NoDummyXp;

[BepInPlugin(Guid, "Adventure RPG - No Dummy XP", "1.0.0")]
[BepInDependency("com.jotunn.jotunn")]
[BepInDependency("NorseDemigods", BepInDependency.DependencyFlags.SoftDependency)]
[NetworkCompatibility(CompatibilityLevel.EveryoneMustHaveMod, VersionStrictness.Patch)]
public sealed class Plugin : BaseUnityPlugin
{
    public const string Guid = "arpg.nodummyxp";
    private const string DodgeRpc = "ARPG_NoDummyXP_Dodge";
    [ThreadStatic] private static List<Frame>? frames;
    private static readonly AccessTools.FieldRef<Attack, ItemDrop.ItemData> Weapon = AccessTools.FieldRefAccess<Attack, ItemDrop.ItemData>("m_weapon");
    private static readonly AccessTools.FieldRef<Attack, Humanoid> Attacker = AccessTools.FieldRefAccess<Attack, Humanoid>("m_character");
    private static readonly AccessTools.FieldRef<Projectile, Character> ProjectileOwner = AccessTools.FieldRefAccess<Projectile, Character>("m_owner");
    private static readonly AccessTools.FieldRef<Aoe, Character> AoeOwner = AccessTools.FieldRefAccess<Aoe, Character>("m_owner");
    private static readonly MethodInfo DodgeOriginal = AccessTools.Method(typeof(Player), "RPC_HitWhileDodging");
    private Harmony? harmony;

    private void Awake()
    {
        harmony = new Harmony(Guid);
        try
        {
            harmony.PatchAll(typeof(Plugin).Assembly);
            // Norse's percentage award directly edits its accumulator instead of calling Skills.
            var norse = AccessTools.TypeByName("NorseDemigods.SE_Demigod");
            if (norse != null)
                foreach (var name in new[] { "RaiseSkill", "RaiseSkillPct" })
                    harmony.Patch(AccessTools.Method(norse, name), prefix: new HarmonyMethod(AccessTools.Method(typeof(Plugin), nameof(NorseSkillPrefix))) { priority = Priority.First + 100 });
        }
        catch { harmony.UnpatchSelf(); throw; }
        Logger.LogInfo("No Dummy XP active: training-dummy weapon, magic, block/parry, perfect-dodge and scoped mod skill awards disabled.");
    }
    private void OnDestroy() => harmony?.UnpatchSelf();
    private static bool NorseSkillPrefix(Skills.SkillType skillType) => Allowed(skillType);
    internal static bool Dummy(Character? c) => c != null && c.GetFaction() == Character.Faction.TrainingDummy;
    private sealed class Frame : IDisposable
    {
        internal readonly XpPolicy Policy;
        internal readonly Attack? Attack;
        internal readonly bool Area, SourceDummy;
        internal Frame(bool strict = false, Attack? attack = null, bool area = false, bool sourceDummy = false)
        {
            Policy = new XpPolicy(strict); Attack = attack; Area = area; SourceDummy = sourceDummy;
            (frames ??= new List<Frame>()).Add(this);
        }
        public void Dispose() { frames?.Remove(this); }
    }
    private static bool Allowed(Skills.SkillType skill) => frames == null || !frames.Any(f => f.Policy.Blocks((int)skill));
    // Replace only the original damage-call sites. Damage and target selection are unchanged.
    public static void DamageAndRecord(IDestructible target, HitData hit)
    {
        if (frames != null && hit.m_hitType != HitData.HitType.Self)
        {
            var character = target as Character;
            bool dummy = Dummy(character);
            foreach (var frame in frames)
            {
                if (frame.Attack != null)
                {
                    var attack = frame.Attack;
                    var type = target.GetDestructibleType();
                    var weapon = Weapon(attack);
                    if (!frame.Area && attack.m_specialHitSkill != Skills.SkillType.None && (type & attack.m_specialHitType) != 0)
                        frame.Policy.Record((int)attack.m_specialHitSkill, dummy);
                    else if ((type & attack.m_skillHitType) != 0 && weapon != null)
                        frame.Policy.Record((int)weapon.m_shared.m_skillType, dummy);
                }
                else if (character != null && !frame.Policy.Strict) frame.Policy.Record(-1, dummy);
            }
        }
        target.Damage(hit);
    }

    [HarmonyPatch(typeof(Skills), nameof(Skills.RaiseSkill))]
    private static class SkillGuard
    {
        [HarmonyPriority(Priority.First + 100)]
        private static bool Prefix(Skills.SkillType skillType) => Allowed(skillType);
    }
    [HarmonyPatch]
    private static class AttackScope
    {
        private static IEnumerable<MethodBase> TargetMethods() => new[] { AccessTools.Method(typeof(Attack), "DoMeleeAttack"), AccessTools.Method(typeof(Attack), "DoAreaAttack") };
        [HarmonyPriority(Priority.First + 100)]
        private static void Prefix(Attack __instance, MethodBase __originalMethod, out IDisposable __state) =>
            __state = new Frame(attack: __instance, area: __originalMethod.Name == "DoAreaAttack", sourceDummy: Dummy(Attacker(__instance)));
        [HarmonyFinalizer, HarmonyPriority(Priority.Last)]
        private static void Finalizer(IDisposable __state) => __state?.Dispose();
    }
    [HarmonyPatch(typeof(Projectile), nameof(Projectile.OnHit))]
    private static class ProjectileScope
    {
        [HarmonyPriority(Priority.First + 100)]
        private static void Prefix(Projectile __instance, out IDisposable __state) => __state = new Frame(sourceDummy: Dummy(ProjectileOwner(__instance)));
        [HarmonyFinalizer, HarmonyPriority(Priority.Last)]
        private static void Finalizer(IDisposable __state) => __state?.Dispose();
    }
    [HarmonyPatch(typeof(Aoe), "OnHit")]
    private static class AoeScope
    {
        private sealed class State : IDisposable
        {
            internal Aoe Aoe = null!;
            internal bool Original;
            internal Frame Scope = null!;
            public void Dispose() { Aoe.m_canRaiseSkill = Original; Scope.Dispose(); }
        }
        [HarmonyPriority(Priority.First + 100)]
        private static void Prefix(Aoe __instance, Collider collider, out IDisposable __state)
        {
            var obj = collider != null ? Projectile.FindHitObject(collider) : null;
            bool dummy = obj != null && Dummy(obj.GetComponent<Character>());
            __state = new State { Aoe = __instance, Original = __instance.m_canRaiseSkill, Scope = new Frame(dummy, sourceDummy: Dummy(AoeOwner(__instance))) };
            // Avoid consuming the AoE's one-XP-award flag on a dummy before it reaches a real enemy.
            if (dummy) __instance.m_canRaiseSkill = false;
        }
        [HarmonyFinalizer, HarmonyPriority(Priority.Last)]
        private static void Finalizer(IDisposable __state) => __state?.Dispose();
    }
    [HarmonyPatch]
    private static class DamageScope
    {
        private static IEnumerable<MethodBase> TargetMethods() => new[] { AccessTools.Method(typeof(Character), "Damage"), AccessTools.Method(typeof(Character), "RPC_Damage"), AccessTools.Method(typeof(Character), "ApplyDamage") };
        [HarmonyPriority(Priority.First + 100)]
        private static void Prefix(Character __instance, HitData hit, out IDisposable? __state) =>
            __state = Dummy(__instance) || Dummy(hit.GetAttacker()) ? new Frame(true) : null;
        [HarmonyFinalizer, HarmonyPriority(Priority.Last)]
        private static void Finalizer(IDisposable? __state) => __state?.Dispose();
    }
    [HarmonyPatch(typeof(Humanoid), "BlockAttack")]
    private static class BlockScope
    {
        [HarmonyPriority(Priority.First + 100)]
        private static void Prefix(Character attacker, out IDisposable? __state) => __state = Dummy(attacker) ? new Frame(true) : null;
        [HarmonyFinalizer, HarmonyPriority(Priority.Last)]
        private static void Finalizer(IDisposable? __state) => __state?.Dispose();
    }
    [HarmonyPatch]
    private static class RecordDamageCalls
    {
        internal static IEnumerable<MethodBase> Methods()
        {
            yield return AccessTools.Method(typeof(Attack), "DoMeleeAttack");
            // The area attack's target loop is a compiler-generated local function.
            var local = typeof(Attack).GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic)
                .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static))
                .Concat(typeof(Attack).GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static))
                .Single(m => m.Name.StartsWith("<DoAreaAttack>g__checkHits|", StringComparison.Ordinal));
            yield return local;
            yield return AccessTools.Method(typeof(Projectile), "OnHit");
            yield return AccessTools.Method(typeof(Projectile), "DoAOE");
            yield return AccessTools.Method(typeof(Aoe), "OnHit");
        }
        private static IEnumerable<MethodBase> TargetMethods() => Methods();
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
        {
            var original = AccessTools.Method(typeof(IDestructible), nameof(IDestructible.Damage));
            var replacement = AccessTools.Method(typeof(Plugin), nameof(DamageAndRecord));
            var output = instructions.ToList(); int count = 0;
            foreach (var code in output) if (code.Calls(original)) { code.opcode = OpCodes.Call; code.operand = replacement; count++; }
            int expected = __originalMethod.DeclaringType == typeof(Aoe) ? 2 : 1;
            if (count != expected) throw new InvalidOperationException("Unsupported damage-call layout: " + __originalMethod + ", count=" + count);
            return output;
        }
    }
    [HarmonyPatch(typeof(Player), "Awake")]
    private static class DodgeRegistration
    {
        private static void Postfix(Player __instance, ZNetView ___m_nview)
        {
            ___m_nview.Register(DodgeRpc, sender =>
            {
                using var scope = new Frame(true);
                DodgeOriginal.Invoke(__instance, new object[] { sender });
            });
        }
    }
    [HarmonyPatch(typeof(Player), nameof(Player.HitWhileDodging))]
    private static class DummyDodge
    {
        private static bool Prefix(Player __instance, ZNetView ___m_nview)
        {
            if (frames == null || !frames.Any(f => f.SourceDummy)) return true;
            // Preserve feedback/stamina/adrenaline and the one-dodge flag on the owning peer;
            // suppress only its skill award. Jotunn requires this plugin on all peers.
            ___m_nview.InvokeRPC(DodgeRpc);
            return false;
        }
    }
}
