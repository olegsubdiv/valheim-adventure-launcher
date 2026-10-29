using AdventureRpg.GrillWithoutFire;
using System.Reflection;
using Mono.Cecil;
using Mono.Cecil.Cil;
using System.Text.Json;
int checks=0;void Check(bool ok,string n){if(!ok)throw new Exception(n);checks++;}
var prefix=typeof(Plugin).GetNestedType("InitializeGrill",BindingFlags.NonPublic)!.GetMethod("Prefix",BindingFlags.NonPublic|BindingFlags.Static)!;
foreach(string name in new[]{"piece_cooking_pot","piece_cooking_pot(Clone)","rk_grill","rk_grill(Clone)","forge","forge(Clone)","piece_cauldron(Clone)","rk_griddle(Clone)","rk_oven(Clone)","rk_prep(Clone)","piece_prep_table(Clone)","piece_cooking_pot_extension(Clone)","rk_grill_extension","unrelated", ""})
foreach(bool fire in new[]{false,true})
{
 var station=new CraftingStation{m_craftRequireFire=fire};station.gameObject.name=name;
 prefix.Invoke(null,[station]);
 bool target=name is "piece_cooking_pot" or "piece_cooking_pot(Clone)" or "rk_grill" or "rk_grill(Clone)";
 Check(station.m_craftRequireFire==(fire&&!target),"exact station/fire scope "+name);
 Check(station.m_craftRequireRoof&&station.m_hasCraftTab&&station.m_canRepair&&station.m_rangeBuild==13&&station.m_useDistance==3,"other station properties untouched");
}
Check(!GrillNames.IsStoneGrill(null),"null name ignored");
var root=Path.GetFullPath(args[0]);using var resolver=new DefaultAssemblyResolver();
foreach(var dir in new[]{"valheim_server_Data/Managed","BepInEx/core","BepInEx/plugins/ValheimModding-Jotunn"})resolver.AddSearchDirectory(Path.Combine(root,"management/BakaTrial/runtime",dir));
using var game=AssemblyDefinition.ReadAssembly(Path.Combine(root,"management/BakaTrial/runtime/valheim_server_Data/Managed/assembly_valheim.dll"),new ReaderParameters{AssemblyResolver=resolver});
using var plugin=AssemblyDefinition.ReadAssembly(Path.Combine(root,"launcher/gameplay/GrillWithoutFire/bin/Release/netstandard2.1/GrillWithoutFire.dll"),new ReaderParameters{AssemblyResolver=resolver});
var stationType=game.MainModule.Types.Single(t=>t.Name=="CraftingStation");
Check(stationType.Methods.Single(m=>m.Name=="Start").Parameters.Count==0,"pinned Start target");
Check(stationType.Methods.Single(m=>m.Name=="Start").Body.Instructions.Any(i=>i.Operand is FieldReference f&&f.Name=="m_craftRequireFire"),"fire scheduling checks same field");
Check(stationType.Methods.Single(m=>m.Name=="CheckUsable").Body.Instructions.Any(i=>i.Operand is FieldReference f&&f.Name=="m_craftRequireFire"),"native usable check uses same field");
Check(stationType.Methods.Single(m=>m.Name=="CheckUsable").Body.Instructions.Any(i=>i.Operand is FieldReference f&&f.Name=="m_craftRequireRoof"),"native roof gate retained");
IEnumerable<TypeDefinition> Types(IEnumerable<TypeDefinition> ts){foreach(var t in ts){yield return t;foreach(var n in Types(t.NestedTypes))yield return n;}}
var methods=Types(plugin.MainModule.Types).SelectMany(t=>t.Methods).ToList();
var writes=methods.Where(m=>m.HasBody).SelectMany(m=>m.Body.Instructions).Where(i=>i.OpCode==OpCodes.Stfld&&i.Operand is FieldReference f&&f.DeclaringType.Scope.Name=="assembly_valheim").Select(i=>((FieldReference)i.Operand).Name).ToHashSet();
Check(writes.SetEquals(["m_craftRequireFire"]),"only fire requirement is written");
Check(methods.Count(m=>m.Name=="Prefix")==1&&!methods.Any(m=>m.Name is "Update" or "FixedUpdate" or "CheckFire" or "CheckUsable"),"one initialization hook, no polling or checks bypass");
var scopes=new HashSet<string>{"assembly_valheim","UnityEngine","UnityEngine.CoreModule","BepInEx","0Harmony","Jotunn"};
foreach(var m in plugin.MainModule.GetMemberReferences())if(m.DeclaringType.Scope is AssemblyNameReference a&&scopes.Contains(a.Name))Check(m switch{MethodReference x=>x.Resolve()!=null,FieldReference x=>x.Resolve()!=null,_=>true},"API resolves "+m.FullName);
Console.WriteLine(JsonSerializer.Serialize(new{status="PASS",checks,gameStarted=false,savesLoaded=false,productionPrefixExecutedWithFixtures=true}));
