using System.Reflection;
using System.Security.Cryptography;
using BepInEx;
using BepInEx.Bootstrap;
using DemigodPanel.Logic;
using HarmonyLib;
using NorseDemigods;
using NorseDemigods.Abilities;

namespace AdventureRpg.NorseDemigodsUICompat;

[BepInPlugin(Guid, "Adventure RPG - Norse Demigods UI Compatibility", "1.0.0")]
[BepInDependency("NorseDemigods", "1.9.0")]
[BepInDependency("com.rdmods.norsedemigodsui", "0.11.5")]
public sealed class Plugin : BaseUnityPlugin
{
    public const string Guid = "arpg.norsedemigodsuicompat";
    // Thunderstore package0.11.6 embeds plugin/assembly0.11.5. These exact DLL
    // pins identify the reviewed package content without editing vendor metadata.
    private const string UiSha = "dd3efb169a90de7d070586d2f01136c8a4a8321434bfdb83f139d764f5f03caf";
    private const string NdSha = "8a1b9252baba7fbf940ea1664f2098be1940ef6edd75ecb28bf0e976f58fdee0";
    private readonly Harmony harmony = new(Guid);
    private Dictionary<string, ValueDef>? definitions;
    private readonly Dictionary<string, (ValueDef Original, ValueDef Replacement)> originals = new();

    private void Awake()
    {
        var nd = Chainloader.PluginInfos["NorseDemigods"];
        var ui = Chainloader.PluginInfos["com.rdmods.norsedemigodsui"];
        if (nd.Metadata.Version != new System.Version(1, 9, 0) ||
            ui.Metadata.Version != new System.Version(0, 11, 5) ||
            !HasSha(nd.Instance.GetType().Assembly, NdSha) || !HasSha(typeof(Values).Assembly, UiSha))
            throw new NotSupportedException("UI compatibility1.0.0 requires the reviewed NorseDemigods1.9.0 and NorseDemigodsUI package0.11.6 DLLs.");

        var uiAssembly = typeof(Values).Assembly;
        var fromAttack = AccessTools.Method(uiAssembly.GetType("DemigodPanel.Nd.AbilityView", true), "FromAttack",
            new[] { typeof(string), typeof(double), typeof(Player) });
        var translate = AccessTools.Method(uiAssembly.GetType("DemigodPanel.Loc", true), "T", new[] { typeof(string) });
        var eitr = AccessTools.Method(uiAssembly.GetType("DemigodPanel.Nd.AbilityView", true), "Eitr",
            new[] { typeof(DemigodAbility), typeof(Player), typeof(bool) });
        // Touch the existing public registry once, allowing its native static
        // initialization to run, then replace only the two reviewed entries.
        _ = Values.All;
        definitions = (Dictionary<string, ValueDef>)AccessTools.Field(typeof(Values), "Defs").GetValue(null);
        if (fromAttack == null || translate == null || eitr == null || definitions == null ||
            !definitions.ContainsKey("eir.holy_sun_radius") || !definitions.ContainsKey("brokkr.torpedo_eitr"))
            throw new NotSupportedException("Reviewed UI methods/definitions are missing.");

        try
        {
            Replace("eir.holy_sun_radius", Policy.HolySunRadius, "NorseDemigods1.9.0 AbilityHolySun.Update");
            Replace("brokkr.torpedo_eitr", Policy.TorpedoEitr, "NorseDemigods1.9.0 native attack charge");
            harmony.Patch(fromAttack, prefix: new HarmonyMethod(typeof(Plugin), nameof(FromAttackPrefix)));
            harmony.Patch(translate, prefix: new HarmonyMethod(typeof(Plugin), nameof(TranslatePrefix)));
            harmony.Patch(eitr, prefix: new HarmonyMethod(typeof(Plugin), nameof(EitrPrefix)));
        }
        catch
        {
            Restore();
            throw;
        }
        Logger.LogInfo("UI adapted to NorseDemigods1.9.0: Holy Sun radius, single torpedo charge and corrected descriptions. Gameplay is unchanged.");
    }

    private static bool HasSha(Assembly assembly, string expected)
    {
        using var sha = SHA256.Create();
        using var file = File.OpenRead(assembly.Location);
        return string.Equals(BitConverter.ToString(sha.ComputeHash(file)).Replace("-", ""), expected, StringComparison.OrdinalIgnoreCase);
    }

    private void Replace(string id, Func<ValueInputs, double> formula, string source)
    {
        var original = definitions![id];
        var replacement = new ValueDef(id, original.Unit, formula, source);
        originals.Add(id, (original, replacement));
        definitions[id] = replacement;
    }

    private static bool FromAttackPrefix(string typeName, double cost, Player player, ref EitrUse __result)
    {
        if (typeName != "AbilityLightningTorpedo") return true;
        var unarmed = player != null ? player.GetSkillLevel(Skills.SkillType.Unarmed) : 0;
        __result = Policy.TorpedoUse(cost, unarmed);
        return false;
    }

    private static bool TranslatePrefix(string token, ref string __result)
    {
        if (Localization.instance == null || !Descriptions.TryGet(token,
                Localization.instance.GetSelectedLanguage(), out var text)) return true;
        __result = text;
        return false;
    }

    private static bool EitrPrefix(DemigodAbility ability, bool infused, ref EitrUse __result)
    {
        if (ability is not AbilityMeteor meteor) return true;
        __result = EitrUse.ByNd(meteor.GetAbilityEitrCost(infused));
        return false;
    }

    private void OnDestroy() => Restore();

    private void Restore()
    {
        harmony.UnpatchSelf();
        if (definitions == null) return;
        foreach (var pair in originals)
            if (definitions.TryGetValue(pair.Key, out var current) && ReferenceEquals(current, pair.Value.Replacement))
                definitions[pair.Key] = pair.Value.Original;
        originals.Clear();
    }
}
