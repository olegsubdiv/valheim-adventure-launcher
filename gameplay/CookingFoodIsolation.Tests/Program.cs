using AdventureRpg.CookingFoodIsolation;
using HarmonyLib;
using Mono.Cecil;
using System.Reflection;
using System.Reflection.Emit;
using System.Security.Cryptography;
using System.Text.Json;
using Food=ItemDrop.ItemData;
using Skill=Cooking.Cooking.CookingSkill;
if(args.Length!=2)throw new Exception("Usage: <workspace root> <client Managed folder>");
int checks=0;void Check(bool x,string m){if(!x)throw new Exception(m);checks++;}
void Near(float a,float b,string m)=>Check(Math.Abs(a-b)<.002f,m);
void Throws(Action action,string m){bool threw=false;try{action();}catch{threw=true;}Check(threw,m);}
void Load(Skill skill)
{
 var item=Plugin.PrepareLoad(skill);
 // Preserve native parsing; use the production replacement for its one gate.
 Skill.active[item.m_shared]=skill.skill;if(skill.Value.Length>0){skill.happy=skill.Value[0]=='1';int.TryParse(skill.Value[1..],out skill.skill);}
 if(!Plugin.ShouldSkipNativeMultiplier(skill))skill.Multiply();Plugin.FinishLoad(skill,item);
}
void Unload(Skill skill){var before=Plugin.PrepareUnload(skill);skill.Unload();Plugin.FinishUnload(before);}
UnityEngine.GameObject Prefab()=>new();
Food Item(UnityEngine.GameObject p)=>new(){m_shared=p.Drop.m_itemData.m_shared,m_dropPrefab=p};
var polluted=new Food();var original=new Skill{Item=polluted};for(int n=0;n<140;n++)original.Load();Check(polluted.m_shared.m_food>50000&&polluted.m_shared.m_foodRegen>5000,"Repeated native loads reproduce screenshot-scale food and healing inflation");
var template=Prefab();var clean=template.Drop.m_itemData.m_shared;var food=Item(template);var mark=new Skill{Item=food};
for(int n=0;n<1000;n++)Load(mark);
Check(!ReferenceEquals(food.m_shared,clean),"Food owns an isolated shared definition");Near(clean.m_food,36,"Canonical health never changes");Near(clean.m_foodStamina,40,"Canonical stamina never changes");Near(clean.m_foodEitr,39,"Canonical eitr never changes");Near(clean.m_foodRegen,4,"Canonical regen never changes");
Near(food.m_shared.m_food,36*1.055f,"Exactly one health bonus after1000loads");Near(food.m_shared.m_foodStamina,40*1.055f,"Exactly one stamina bonus");Near(food.m_shared.m_foodEitr,39*1.055f,"Exactly one eitr bonus");Near(food.m_shared.m_foodRegen,4*1.055f,"Exactly one regen bonus");
Check(Skill.active[food.m_shared]==55,"Author live-config registry uses parsed skill");Check(food.m_stack==8&&food.m_quality==1&&food.m_shared.m_weight==.5f&&food.m_shared.m_maxStackSize==100&&food.m_shared.m_foodBurnTime==1800,"Stack/quality/weight/duration untouched");
var owned=food.m_shared;for(int n=0;n<50;n++)Load(mark);Check(ReferenceEquals(owned,food.m_shared),"Repeated loads reuse the private shared definition");
foreach(int level in new[]{0,5,20,55,100})
{
 var other=Item(template);var m=new Skill{Item=other,Value="0"+level};Load(m);Near(other.m_shared.m_food,36*(1+level*(1.1f-1)/100),"Independent cooking levels");Near(food.m_shared.m_food,36*1.055f,"Another stack cannot change the first stack");
 Unload(m);Near(other.m_shared.m_food,36,"Unload restores raw food");Check(!Skill.active.ContainsKey(owned)||Skill.active[owned]==55,"Unload cannot remove another stack registry");
}
var clone=new Food{m_shared=food.m_shared,m_dropPrefab=template};var clonedMark=new Skill{Item=clone,IsCloned=true,Value="155"};Load(clonedMark);
Check(!ReferenceEquals(clone.m_shared,food.m_shared)&&clonedMark.happy,"Inherited clone isolated and perfect-food flag retained");Near(clone.m_shared.m_food,36*1.055f,"Inherited bonus not doubled or lost");
Check(clonedMark.IsCloned,"Native clone metadata remains unchanged for other consumers");
mark.Value="0100";Load(mark);Near(food.m_shared.m_food,36*1.1f,"Skill metadata change recomputes from base");Near(clone.m_shared.m_food,36*1.055f,"Clone remains independent when source skill changes");
for(int n=0;n<200;n++){Load(mark);Unload(mark);}Near(food.m_shared.m_food,36,"Load/unload cycles have no multiplier/division drift");
Check(!Skill.active.ContainsKey(food.m_shared),"Raw unloaded item is not in active registry");
var prepared=new Skill{Item=template.Drop.m_itemData,Value="0100"};Load(prepared);var duringCraft=Item(template);var cook=new Skill{Item=duringCraft,Value="020"};Load(cook);Near(duringCraft.m_shared.m_food,36*1.02f,"Temporary boosted crafting prefab never becomes a baseline");Unload(prepared);Near(template.Drop.m_itemData.m_shared.m_food,36,"Crafting template reset exactly");
var variable=Prefab();var varItem=Item(variable);var vm=new Skill{Item=varItem};Load(vm);variable.Drop.m_itemData.m_shared.m_food=42;Load(vm);Near(varItem.m_shared.m_food,42*1.055f,"Current authored prefab changes retained");
var rawPrefab=Prefab();var rawMark=new Skill{Item=rawPrefab.Drop.m_itemData};Load(rawMark);Unload(rawMark);rawPrefab.Drop.m_itemData.m_shared.m_food=44;Load(rawMark);Near(rawPrefab.Drop.m_itemData.m_shared.m_food,44*1.055f,"Authored change to an unloaded crafting prefab retained");Unload(rawMark);Near(rawPrefab.Drop.m_itemData.m_shared.m_food,44,"Changed crafting prefab baseline remains after unload");
float oldFactor=Skill.Health;Skill.Health=1.3f;foreach(var(s,level)in Skill.active)s.m_food*=(1+level*(Skill.Health-1)/100)/(1+level*(oldFactor-1)/100);Load(vm);Near(varItem.m_shared.m_food,42*(1+55*(1.3f-1)/100),"Native live-config multiplier changes remain idempotent");Skill.Health=oldFactor;
var nonEitr=Prefab();nonEitr.Drop.m_itemData.m_shared.m_foodEitr=0;var nm=new Skill{Item=Item(nonEitr)};Load(nm);Check(nm.Item.m_shared.m_foodEitr==0,"Non-eitr food never gains eitr");
var root=Path.GetFullPath(args[0]);var cookingPath=Path.Combine(root,"management/BakaTrial/runtime/BepInEx/plugins/Smoothbrain-Cooking/Cooking.dll");using var resolver=new DefaultAssemblyResolver();foreach(var p in new[]{"management/BakaTrial/runtime/valheim_server_Data/Managed","management/BakaTrial/runtime/BepInEx/core","management/BakaTrial/runtime/BepInEx/plugins/ValheimModding-Jotunn"})resolver.AddSearchDirectory(Path.Combine(root,p));
using var cooking=AssemblyDefinition.ReadAssembly(cookingPath,new(){AssemblyResolver=resolver});var type=cooking.MainModule.Types.Single(t=>t.FullName=="Cooking.Cooking").NestedTypes.Single(t=>t.Name=="CookingSkill");var load=type.Methods.Single(m=>m.Name=="Load");var opmap=typeof(OpCodes).GetFields(BindingFlags.Public|BindingFlags.Static).Select(f=>(OpCode)f.GetValue(null)!).ToDictionary(o=>o.Value);var getter=typeof(Skill).GetProperty("IsCloned")!.GetGetMethod()!;
var source=load.Body.Instructions.Select(i=>new CodeInstruction(opmap[i.OpCode.Value],i.Operand is MethodReference m&&m.Name=="get_IsCloned"?getter:i.Operand)).ToArray();var before=source.Select(i=>(i.opcode,i.operand)).ToArray();var rewritten=Plugin.LoadPatch.Transpiler(source).ToArray();Check(rewritten.Length==before.Length,"Actual native Load instruction count retained");int changed=0;for(int n=0;n<source.Length;n++)if((source[n].opcode,source[n].operand)!=before[n]){changed++;Check(Equals(before[n].operand,getter)&&source[n].opcode==OpCodes.Call,"Only clone gate rewritten");}Check(changed==1,"Exactly one actual Cooking IL change");
Check(load.Body.Instructions.Count(i=>i.OpCode==Mono.Cecil.Cil.OpCodes.Stfld&&i.Operand is FieldReference f&&new[]{"m_food","m_foodStamina","m_foodRegen","m_foodEitr"}.Contains(f.Name))==4,"Four original author multiplier writes retained");
Throws(()=>Plugin.LoadPatch.Transpiler([]).ToArray(),"Unknown Cooking IL fails closed");Check(Plugin.LoadPatch.TargetMethod().Name=="Load"&&Plugin.UnloadPatch.TargetMethod().Name=="Unload","Exact private metadata targets resolve");
Check(type.BaseType.Resolve().Properties.Single(p=>p.Name=="Item").PropertyType.FullName=="ItemDrop/ItemData","Native metadata item getter signature");Check(type.Fields.Single(f=>f.Name=="active").FieldType.FullName.Contains("ItemDrop/ItemData/SharedData"),"Native active-registry shared-data key type");
var dll=Path.Combine(root,"launcher/gameplay/CookingFoodIsolation/bin/Release/netstandard2.1/CookingFoodIsolation.dll");
IEnumerable<TypeDefinition> Types(IEnumerable<TypeDefinition> ts){foreach(var t in ts){yield return t;foreach(var n in Types(t.NestedTypes))yield return n;}}
foreach(string folder in new[]{Path.Combine(root,"management/BakaTrial/runtime/valheim_server_Data/Managed"),Path.GetFullPath(args[1])})
{
 using var game=AssemblyDefinition.ReadAssembly(Path.Combine(folder,"assembly_valheim.dll"),new(){AssemblyResolver=resolver});using var plugin=AssemblyDefinition.ReadAssembly(dll,new(){AssemblyResolver=resolver});
 var shared=game.MainModule.Types.Single(t=>t.Name=="ItemDrop").NestedTypes.Single(t=>t.Name=="ItemData").NestedTypes.Single(t=>t.Name=="SharedData");foreach(string name in new[]{"m_food","m_foodStamina","m_foodRegen","m_foodEitr"})Check(shared.Fields.Single(f=>f.Name==name).FieldType.FullName=="System.Single","Pinned native food field "+name);
 var itemType=shared.DeclaringType;Check(!itemType.Methods.Single(m=>m.Name=="Save").Body.Instructions.Any(i=>i.Operand is FieldReference f&&new[]{"m_food","m_foodStamina","m_foodRegen","m_foodEitr"}.Contains(f.Name)),"Shared food stats are not serialized in item saves");
 var updateFood=game.MainModule.Types.Single(t=>t.Name=="Player").Methods.Single(m=>m.Name=="UpdateFood");Check(new[]{"m_food","m_foodStamina","m_foodEitr","m_foodRegen"}.All(n=>updateFood.Body.Instructions.Any(i=>i.Operand is FieldReference f&&f.Name==n)),"Native consumed-food update reads corrected shared values");
 foreach(var member in plugin.MainModule.GetMemberReferences())if(member.DeclaringType.Scope is AssemblyNameReference a&&new[]{"assembly_valheim","UnityEngine.CoreModule","BepInEx","0Harmony","Jotunn"}.Contains(a.Name))Check(member switch{MethodReference m=>m.Resolve()!=null,FieldReference f=>f.Resolve()!=null,_=>true},"Pinned native API resolves "+member.FullName);
 var methods=Types(plugin.MainModule.Types).SelectMany(t=>t.Methods).ToArray();Check(!methods.Any(m=>m.Name is "Update" or "FixedUpdate" or "Finalizer"),"No polling/exception suppression");var calls=methods.Where(m=>m.HasBody).SelectMany(m=>m.Body.Instructions).Where(i=>i.Operand is MethodReference).Select(i=>(MethodReference)i.Operand);Check(!calls.Any(m=>m.Name is "Save" or "Destroy"||m.DeclaringType.Name is "ZDO" or "ZNetView"),"No save/world/network mutation");
}
using var assets=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"research/20261101-food-overflow/wizardry-food.json")));
var cake=assets.RootElement.EnumerateArray().Single(x=>x.GetProperty("prefab").GetString()=="BerryCakePlains_TW");Check(cake.GetProperty("m_food").GetSingle()==36&&cake.GetProperty("m_foodStamina").GetSingle()==40&&cake.GetProperty("m_foodEitr").GetSingle()==39&&cake.GetProperty("m_foodRegen").GetSingle()==4,"Actual cake authored baseline confirmed");
Console.WriteLine(JsonSerializer.Serialize(new{status="PASS",checks,pluginSha256=Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(dll))),cookingSha256=Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(cookingPath))),repeatedLoadAndSharedMutationReproduced=true,liveFoodAndMpTested=false,gameStarted=false,savesEdited=false}));
