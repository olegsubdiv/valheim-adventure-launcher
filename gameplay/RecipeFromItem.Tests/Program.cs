using Mono.Cecil;
using Mono.Cecil.Cil;
using System.Reflection;
using System.Text.Json;

int checks=0;
void Check(bool ok,string name){if(!ok)throw new Exception(name);checks++;}
var postfix=typeof(AdventureRpg.RecipeFromItem.Plugin).GetNestedType("DiscoverOwnedOutput",BindingFlags.NonPublic)!.GetMethod("Postfix",BindingFlags.Static|BindingFlags.NonPublic)!;
bool Apply(Player p,Recipe? r,bool discover,bool original)
{object?[] a=[p,r,discover,original];postfix.Invoke(null,a);return(bool)a[3]!;}
var player=new Player();
var recipe=new Recipe{m_item=new ItemDrop()};
recipe.m_item.m_itemData!.m_shared!.m_name="$item_sword_iron";
foreach(bool discover in new[]{false,true})
foreach(bool native in new[]{false,true})
foreach(bool known in new[]{false,true})
{
 player.Known.Clear();if(known)player.Known.Add("$item_sword_iron");
 Check(Apply(player,recipe,discover,native)==(native||(discover&&known)),$"discovery={discover},native={native},known={known}");
}
player.Known.Add("$item_sword_iron");
Check(Apply(player,recipe,true,false),"owned output unlocks even if station/material discovery failed");
Check(!Apply(player,recipe,false,false),"actual craft/upgrade with missing requirements remains blocked");
Check(player.Known.SetEquals(new[]{"$item_sword_iron"}),"ingredients and unrelated recipes are not learned recursively");
Check(!Apply(player,null,true,false),"missing recipe ignored");
Check(!Apply(player,new Recipe(),true,false),"missing output ignored");
Check(!Apply(player,new Recipe{m_item=new ItemDrop{m_itemData=null}},true,false),"missing item data ignored");
Check(!Apply(player,new Recipe{m_item=new ItemDrop{m_itemData=new(){m_shared=null}}},true,false),"missing shared data ignored");
recipe.m_item.m_itemData.m_shared.m_dlc="FixtureDLC";
Check(!Apply(player,recipe,true,false),"missing DLC stays unavailable");
DLCMan.instance=null;
Check(!Apply(player,recipe,true,false),"missing DLC manager does not grant DLC content");
DLCMan.instance=new DLCMan();DLCMan.instance.Installed.Add("FixtureDLC");
Check(Apply(player,recipe,true,false),"owned item from installed DLC can be discovered");
Check(!Apply(player,recipe,false,false),"DLC ownership does not bypass actual crafting cost");

var root=Path.GetFullPath(args[0]);
using var resolver=new DefaultAssemblyResolver();
foreach(var dir in new[]{"valheim_server_Data/Managed","BepInEx/core","BepInEx/plugins/ValheimModding-Jotunn"})resolver.AddSearchDirectory(Path.Combine(root,"management/BakaTrial/runtime",dir));
using var game=AssemblyDefinition.ReadAssembly(Path.Combine(root,"management/BakaTrial/runtime/valheim_server_Data/Managed/assembly_valheim.dll"),new ReaderParameters{AssemblyResolver=resolver});
using var plugin=AssemblyDefinition.ReadAssembly(Path.Combine(root,"launcher/gameplay/RecipeFromItem/bin/Release/netstandard2.1/RecipeFromItem.dll"),new ReaderParameters{AssemblyResolver=resolver});
IEnumerable<TypeDefinition> Types(IEnumerable<TypeDefinition> ts){foreach(var t in ts){yield return t;foreach(var n in Types(t.NestedTypes))yield return n;}}
IEnumerable<MethodReference> Calls(MethodDefinition m)=>m.Body.Instructions.Where(i=>i.OpCode==OpCodes.Call||i.OpCode==OpCodes.Callvirt).Select(i=>i.Operand).OfType<MethodReference>();
var ptype=game.MainModule.Types.Single(t=>t.FullName=="Player");
var requirement=ptype.Methods.Single(m=>m.Name=="HaveRequirements"&&m.Parameters.Count==4&&m.Parameters[0].ParameterType.Name=="Recipe");
Check(requirement.Parameters[0].Name=="recipe"&&requirement.Parameters[1].Name=="discover","pinned Harmony argument names and overload");
Check(Calls(requirement).Any(m=>m.Name=="RequiredCraftingStation"),"ordinary crafting still checks station and level");
Check(Calls(requirement).Any(m=>m.Name=="HaveRequirementItems"),"ordinary crafting still checks resource requirements");
Check(Calls(requirement).Any(m=>m.Name=="IsDLCInstalled"),"pinned DLC gate");
var update=ptype.Methods.Single(m=>m.Name=="UpdateKnownRecipesList");
Check(update.Body.Instructions.Any(i=>i.Operand is FieldReference f&&f.DeclaringType.Name=="Recipe"&&f.Name=="m_enabled"),"caller retains enabled recipe filter");
Check(update.Body.Instructions.Any(i=>i.Operand is FieldReference f&&f.Name=="m_currentSeason"),"caller retains seasonal recipe filter");
Check(Calls(update).Any(m=>m.Name=="HaveRequirements"&&m.Parameters.Count==4),"recipe discovery invokes patched overload");
Check(Calls(ptype.Methods.Single(m=>m.Name=="AddKnownItem")).Any(m=>m.Name=="UpdateKnownRecipesList"),"pickup triggers immediate recipe refresh");
Check(Calls(ptype.Methods.Single(m=>m.Name=="OnInventoryChanged")).Any(m=>m.Name=="UpdateKnownRecipesList"),"inventory refresh rechecks all recipes against existing discovered items");
Check(ptype.Methods.Single(m=>m.Name=="IsKnownMaterial").Body.Instructions.Any(i=>i.Operand is FieldReference f&&f.Name=="m_knownMaterial"),"existing character discovery log is used");
var types=Types(plugin.MainModule.Types).ToList();
Check(types.SelectMany(t=>t.Methods).Count(m=>m.Name=="Postfix")==1,"single discovery hook only");
Check(!types.SelectMany(t=>t.Methods).Any(m=>m.Name is "Update" or "FixedUpdate" or "LateUpdate"),"no polling loop");
Check(!types.SelectMany(t=>t.Methods).Where(m=>m.HasBody).SelectMany(Calls).Any(m=>m.Name is "AddKnownItem" or "ConsumeResources" or "AddItem" or "SavePlayerData"),"no recursive ingredient discovery, item mutation or save editing");
var own=types.Single(t=>t.Name=="Plugin");
var compat=own.CustomAttributes.Single(a=>a.AttributeType.Name=="NetworkCompatibilityAttribute").ConstructorArguments[0];
Check(Convert.ToInt32(compat.Value)==Convert.ToInt32(compat.Type.Resolve().Fields.Single(f=>f.Name=="NotEnforced").Constant),"client-only compatibility declared");
var assemblies=new HashSet<string>{"assembly_valheim","UnityEngine","UnityEngine.CoreModule","BepInEx","0Harmony","Jotunn"};
foreach(var member in plugin.MainModule.GetMemberReferences())
 if(member.DeclaringType.Scope is AssemblyNameReference scope&&assemblies.Contains(scope.Name))
  Check(member switch{MethodReference m=>m.Resolve()!=null,FieldReference f=>f.Resolve()!=null,_=>true},"pinned API resolves: "+member.FullName);
Console.WriteLine(JsonSerializer.Serialize(new{status="PASS",checks,gameStarted=false,savesLoaded=false,productionPostfixExecutedWithFixtures=true}));
