using System.Collections;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using BepInEx;
using BepInEx.Bootstrap;
using HarmonyLib;
using Jotunn.Utils;
using ItemData = ItemDrop.ItemData;
using SharedData = ItemDrop.ItemData.SharedData;

namespace AdventureRpg.CookingFoodIsolation;

[BepInPlugin(Guid, "Adventure RPG - Cooking Food Isolation", "1.0.0")]
[BepInDependency("com.jotunn.jotunn")]
[BepInDependency("org.bepinex.plugins.cooking", "1.2.5")]
[NetworkCompatibility(CompatibilityLevel.EveryoneMustHaveMod, VersionStrictness.Patch)]
public sealed class Plugin : BaseUnityPlugin
{
    public const string Guid = "arpg.cookingfoodisolation";
    private Harmony? harmony;
    private static readonly ConditionalWeakTable<ItemData, Owner> Owners = new();
    private static readonly ConditionalWeakTable<SharedData, FoodValues> Bases = new();
    private static readonly MethodInfo CloneShared = typeof(object).GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic)!;
    private static readonly Type SkillType = AccessTools.TypeByName("Cooking.Cooking+CookingSkill") ?? throw new InvalidOperationException("CookingSkill type missing.");
    private static readonly PropertyInfo ItemProperty = SkillType.GetProperty("Item", BindingFlags.Instance | BindingFlags.Public) ?? throw new InvalidOperationException("Cooking item property missing.");
    private static readonly MethodInfo IsCloned = AccessTools.PropertyGetter(SkillType, "IsCloned");
    private static readonly FieldInfo SkillField = AccessTools.Field(SkillType, "skill");
    private static IDictionary Active => (IDictionary)AccessTools.Field(SkillType, "active").GetValue(null)!;

    private void Awake()
    {
        if (!Chainloader.PluginInfos.TryGetValue("org.bepinex.plugins.cooking", out var info) || info.Metadata.Version != new System.Version(1, 2, 5))
            throw new InvalidOperationException("Cooking Food Isolation requires reviewed Cooking1.2.5.");
        if (ItemProperty.PropertyType != typeof(ItemData) || SkillField.FieldType != typeof(int) || IsCloned.ReturnType != typeof(bool))
            throw new InvalidOperationException("Cooking metadata contracts changed.");
        harmony = new Harmony(Guid);
        try { harmony.PatchAll(typeof(Plugin).Assembly); }
        catch { harmony.UnpatchSelf(); throw; }
        Logger.LogInfo("Cooking food values isolated per item; native skill bonus is applied once from the current food baseline.");
    }
    private void OnDestroy() => harmony?.UnpatchSelf();

    internal sealed class FoodValues
    {
        internal readonly float Health, Stamina, Regen, Eitr;
        internal FoodValues(SharedData s) { Health = s.m_food; Stamina = s.m_foodStamina; Regen = s.m_foodRegen; Eitr = s.m_foodEitr; }
        internal void Write(SharedData s) { s.m_food = Health; s.m_foodStamina = Stamina; s.m_foodRegen = Regen; s.m_foodEitr = Eitr; }
    }
    private sealed class Owner
    {
        internal SharedData Shared = null!;
        internal FoodValues Base = null!;
        internal bool Applied;
    }
    internal sealed class UnloadState
    {
        internal ItemData Item = null!;
        internal SharedData Before = null!;
        internal FoodValues Base = null!;
    }
    private static ItemData Item(object skill) => (ItemData?)ItemProperty.GetValue(skill) ?? throw new InvalidOperationException("Cooking item unavailable.");
    private static FoodValues Baseline(ItemData item)
    {
        // Use the current authored prefab when it is not temporarily carrying a
        // crafting bonus. This retains author/mod configuration changes on reload.
        var canonical = item.m_dropPrefab?.GetComponent<ItemDrop>()?.m_itemData;
        if (canonical?.m_shared != null && !ReferenceEquals(canonical, item))
        {
            if (Owners.TryGetValue(canonical, out var prefabOwner) && ReferenceEquals(prefabOwner.Shared, canonical.m_shared) && prefabOwner.Applied)
                return prefabOwner.Base;
            return new FoodValues(canonical.m_shared);
        }
        if (Owners.TryGetValue(item, out var owner) && ReferenceEquals(owner.Shared, item.m_shared))
            return owner.Applied ? owner.Base : new FoodValues(item.m_shared);
        return Bases.TryGetValue(item.m_shared, out var known) ? known : new FoodValues(item.m_shared);
    }
    private static void Remember(SharedData shared, FoodValues values) { Bases.Remove(shared); Bases.Add(shared, values); }

    internal static ItemData PrepareLoad(object skill)
    {
        var item = Item(skill);
        if (item.m_shared == null) throw new InvalidOperationException("Cooking food definition unavailable.");
        var baseline = Baseline(item);
        var owner = Owners.GetValue(item, _ => new Owner());
        if (!ReferenceEquals(owner.Shared, item.m_shared))
        {
            if (owner.Shared != null) Active.Remove(owner.Shared);
            // Copy all other shared fields as-is; only this item's four food
            // values are reset. Never multiply the source prefab/shared object.
            item.m_shared = (SharedData)CloneShared.Invoke(item.m_shared, null)!;
            owner.Shared = item.m_shared;
        }
        owner.Base = baseline; owner.Applied = false;
        baseline.Write(item.m_shared); Remember(item.m_shared, baseline);
        return item;
    }
    internal static void FinishLoad(object skill, ItemData item)
    {
        var owner = Owners.GetValue(item, _ => throw new InvalidOperationException("Cooking load state missing."));
        owner.Applied = true;
        // Cooking registers the old skill before parsing saved metadata. Keep
        // its own live-config update registry in sync with the parsed skill.
        Active[item.m_shared] = (int)SkillField.GetValue(skill)!;
    }
    internal static bool ShouldSkipNativeMultiplier(object skill) => false;
    internal static UnloadState PrepareUnload(object skill)
    {
        var item = Item(skill); return new UnloadState { Item = item, Before = item.m_shared, Base = Baseline(item) };
    }
    internal static void FinishUnload(UnloadState state)
    {
        // Native Unload already detaches its SharedData. Restore the exact base
        // instead of accumulating float roundoff through multiply/divide cycles.
        state.Base.Write(state.Item.m_shared);
        Active.Remove(state.Before);
        var owner = Owners.GetValue(state.Item, _ => new Owner());
        owner.Shared = state.Item.m_shared; owner.Base = state.Base; owner.Applied = false;
        Remember(owner.Shared, owner.Base);
    }

    [HarmonyPatch]
    internal static class LoadPatch
    {
        internal static MethodBase TargetMethod() => AccessTools.DeclaredMethod(SkillType, "Load", Type.EmptyTypes);
        [HarmonyPriority(Priority.First)]
        internal static void Prefix(object __instance, out ItemData __state) => __state = PrepareLoad(__instance);
        internal static void Postfix(object __instance, ItemData __state) => FinishLoad(__instance, __state);
        internal static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var code = instructions.ToList();
            var gates = code.Where(i => (i.opcode == OpCodes.Call || i.opcode == OpCodes.Callvirt) && Equals(i.operand, IsCloned)).ToArray();
            if (gates.Length != 1) throw new InvalidOperationException("Expected one inherited Cooking multiplier gate.");
            // Even clones now have a private, unmodified baseline. Keep native
            // metadata parsing and all four author multiplier formulas intact.
            gates[0].opcode = OpCodes.Call;
            gates[0].operand = AccessTools.DeclaredMethod(typeof(Plugin), nameof(ShouldSkipNativeMultiplier));
            return code;
        }
    }
    [HarmonyPatch]
    internal static class UnloadPatch
    {
        internal static MethodBase TargetMethod() => AccessTools.DeclaredMethod(SkillType, "Unload", Type.EmptyTypes);
        internal static void Prefix(object __instance, out UnloadState __state) => __state = PrepareUnload(__instance);
        internal static void Postfix(UnloadState __state) => FinishUnload(__state);
    }
}
