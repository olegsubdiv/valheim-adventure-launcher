using AdventureRpg.NorseDemigodsUICompat;
using DemigodPanel.Logic;
using Mono.Cecil;
using Mono.Cecil.Cil;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

var root=Path.GetFullPath(args[0]);int checks=0;
void Check(bool ok,string message){if(!ok)throw new Exception(message);checks++;}
string Sha(string p)=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p))).ToLowerInvariant();
var settings=new Dictionary<string,double>();
using(var d=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"updates/20261104-mod-migration/norse-data/new-defaults.json"))))
foreach(var x in d.RootElement.EnumerateArray()){
    var value=x.GetProperty("value").GetString()!;
    if(double.TryParse(value,NumberStyles.Float,CultureInfo.InvariantCulture,out var n))settings[x.GetProperty("field").GetString()!]=n;
    else if(bool.TryParse(value,out var b))settings[x.GetProperty("field").GetString()!]=b?1:0;
}
var source=new Source(settings);
var original=Values.All.ToDictionary(x=>x.Id);
foreach(var level in new[]{0d,10d,50d,100d})foreach(var skill in new[]{0d,50d,100d})foreach(var infused in new[]{false,true}){
    var context=new ValueContext(level,skill,infused);var v=new ValueInputs();v.Reset(source,context);
    Check(Policy.HolySunRadius(v)==(infused?4:3),"Holy Sun radius matches native1.9.0");
    Check(!v.Missing,"Holy Sun settings exist");
    var raw=20d-(infused?5d:0d);var expected=raw*(1d-.33d*skill/100d);
    v.Reset(source,context);
    Check(Math.Abs(Policy.TorpedoEitr(v)-expected)<1e-9,"torpedo formula charges once with native skill");
    var use=Policy.TorpedoUse(raw,skill);
    Check(Math.Abs(use.Needed-expected)<1e-9&&Math.Abs(use.Most-expected)<1e-9&&use.ByAttack&&!use.TakesMore,"torpedo HUD has one actual charge");
}
foreach(var cost in new[]{-5d,0d,1d,20d})foreach(var skill in new[]{-5d,0d,100d,500d}){
    var u=Policy.TorpedoUse(cost,skill);Check(u.Needed>=0&&u.Most==u.Needed,"zero and clamped skill cost");
}
Check(Values.TryEvaluate("eir.holy_sun_radius",source,new ValueContext(10,0,false),out var oldRadius)&&oldRadius==4,"actual UI old radius defect reproduced");
Check(Values.TryEvaluate("brokkr.torpedo_eitr",source,new ValueContext(10,100,false),out var oldCost)&&oldCost==20,"actual UI old torpedo full-cost formula reproduced");
// Execute all original public formula definitions with the new native settings.
var formulaFailures=new List<string>();
foreach(var def in original.Values)foreach(var level in new[]{0d,10d,50d,100d})foreach(var infused in new[]{false,true}){
    bool ok=Values.TryEvaluate(def.Id,source,new ValueContext(level,50,infused),out var value);
    if(!ok)formulaFailures.Add(def.Id);
    Check(ok&&double.IsFinite(value),"native UI formula resolves with1.9 fields: "+def.Id);
}
foreach(var pair in Descriptions.Entries){
    Check(Descriptions.TryGet("$"+pair.Key,"English",out var en)&&en==pair.Value.English,"English corrected token "+pair.Key);
    Check(Descriptions.TryGet(pair.Key,"Russian",out var ru)&&ru==pair.Value.Russian,"Russian corrected token "+pair.Key);
    Check(Descriptions.TryGet("$"+pair.Key,"German",out var fallback)&&fallback==en,"safe corrected-English fallback "+pair.Key);
    foreach(var text in new[]{en,ru})foreach(Match m in Regex.Matches(text,@"\{\??([A-Za-z][A-Za-z0-9_]*\.[A-Za-z0-9_]+)(?:\|[^}]+)?\}")){
        var id=m.Groups[1].Value;Check(settings.ContainsKey(id)||original.ContainsKey(id),"replacement token resolves "+id);
    }
}
Check(!Descriptions.TryGet("$dgp_tab_abilities","Russian",out _),"unrelated translations use original localizer");
Check(Descriptions.TryGet("$dgp_ab_AbilityRestoration_thor","English",out var restoration)&&restoration.Contains("displayed base excludes")&&restoration.Contains("EnergyBlastBonusDamagePerPoint"),"Restoration reports base plus variable energy bonus");
Check(Descriptions.TryGet("$dgp_ab_AbilityVortex","English",out var vortex)&&vortex.Contains("does not pull or knock back creatures"),"Vortex description matches new creatures scope");
var dll=Path.Combine(root,"launcher/gameplay/NorseDemigodsUICompat/bin/Release/netstandard2.1/NorseDemigodsUICompat.dll");
using var own=AssemblyDefinition.ReadAssembly(dll);var plugin=own.MainModule.Types.Single(t=>t.Name=="Plugin");
IEnumerable<MethodReference> Calls(MethodDefinition m)=>m.Body.Instructions.Select(i=>i.Operand).OfType<MethodReference>();
var calls=plugin.Methods.Where(m=>m.HasBody).SelectMany(Calls).ToArray();
Check(plugin.Methods.All(m=>m.Name is not("Update" or "FixedUpdate" or "LateUpdate")),"no own per-frame hook");
Check(!calls.Any(m=>m.Name is "Save" or "SavePlayer" or "Damage" or "InvokeRPC" or "RaiseSkill" or "WriteAllText" or "WriteAllBytes"),"no gameplay/RPC/save/file writes");
Check(calls.Count(m=>m.DeclaringType.FullName=="HarmonyLib.Harmony"&&m.Name=="Patch")==3,"exactly three UI-only prefix targets");
Check(!calls.Any(m=>m.Name=="set_Value"),"no config writes");
Check(plugin.Methods.Single(m=>m.Name=="FromAttackPrefix").Body.Instructions.Any(i=>Equals(i.Operand,"AbilityLightningTorpedo")),"only torpedo replaces FromAttack");
Check(plugin.Methods.Single(m=>m.Name=="EitrPrefix").Body.Instructions.Any(i=>i.OpCode==OpCodes.Isinst&&i.Operand is TypeReference t&&t.Name=="AbilityMeteor"),"only Meteor replaces Eitr");
Check(plugin.Methods.Single(m=>m.Name=="EitrPrefix").Body.Instructions.Any(i=>i.Operand is MethodReference m&&m.Name=="GetAbilityEitrCost"),"Meteor invokes exact native cost API");
Check(plugin.Fields.Any(f=>f.Name=="UiSha"&&Equals(f.Constant,Sha(Path.Combine(root,"updates/20261101-food-overflow/prepared/client/BepInEx/plugins/RDMods-NorseDemigodsUI/DemigodPanel.dll")))),"exact packaged UI DLL guard");
Check(plugin.Fields.Any(f=>f.Name=="NdSha"&&Equals(f.Constant,Sha(Path.Combine(root,"updates/20261104-mod-migration/extracted/Alpus-NorseDemigods-1.9.0/plugins/NorseDemigods.dll")))),"exact native ND DLL guard");
Check(plugin.Methods.Any(m=>m.Name=="OnDestroy")&&calls.Any(m=>m.Name=="UnpatchSelf"),"unload restores owned definitions and patches");
var result=new{status="PASS",checks,originalUiDefinitionsExecuted=original.Count,descriptionTokens=Descriptions.Entries.Count,dllSha256=Sha(dll),actualUiLogicExecuted=true,nativeGameLaunched=false,liveHarmonyTested=false};
File.WriteAllText(Path.Combine(root,"research/20261104-norse190/ui-compat-checks.json"),JsonSerializer.Serialize(result,new JsonSerializerOptions{WriteIndented=true}));Console.WriteLine(JsonSerializer.Serialize(result));
sealed class Source(Dictionary<string,double> values):ISettingSource{
    public bool TryGet(string id,out double value)=>values.TryGetValue(id,out value);
}
