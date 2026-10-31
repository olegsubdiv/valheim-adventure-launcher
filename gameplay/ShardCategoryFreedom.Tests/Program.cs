using AdventureRpg.ShardCategoryFreedom;
using EpicLoot.ShardStones;
using BepInEx.Bootstrap;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Mono.Cecil;
using Mono.Cecil.Cil;
int checks=0;void Check(bool ok,string name){if(!ok)throw new Exception(name);checks++;}
for(int mask=0;mask<16;mask++)
{
 var original=Enum.GetValues<ShardCategory>().Where(x=>(mask&(1<<(int)x))!=0).ToHashSet();
 var set=new HashSet<ShardCategory>(original);var change=new CategoryOverride(set);
 Check(!set.Contains(ShardCategory.Boss)&&!set.Contains(ShardCategory.Unique),"both requested categories unrestricted");
 Check(set.SetEquals(original.Except(new[]{ShardCategory.Boss,ShardCategory.Unique})),"other categories untouched");
 var nested=new CategoryOverride(set);nested.Restore();Check(!set.Contains(ShardCategory.Boss)&&!set.Contains(ShardCategory.Unique),"second instance does not restore another instance's entries");
 change.Restore();Check(set.SetEquals(original),"original entries restored exactly");
 change.Restore();Check(set.SetEquals(original),"restore is idempotent");
}
var plugin=new Plugin();var awake=typeof(Plugin).GetMethod("Awake",BindingFlags.Instance|BindingFlags.NonPublic)!;var destroy=typeof(Plugin).GetMethod("OnDestroy",BindingFlags.Instance|BindingFlags.NonPublic)!;
Chainloader.PluginInfos["randyknapp.mods.epicloot"]=new(){Metadata=new(){Version=new System.Version(0,14,13)}};
awake.Invoke(plugin,null);Check(Shards.ExclusiveCategories.Count==0,"production startup edits shared registry");destroy.Invoke(plugin,null);Check(Shards.ExclusiveCategories.SetEquals(new[]{ShardCategory.Boss,ShardCategory.Unique}),"production destruction restores registry");
Chainloader.PluginInfos["randyknapp.mods.epicloot"].Metadata.Version=new System.Version(0,14,14);
try{awake.Invoke(new Plugin(),null);throw new Exception("wrong version accepted");}catch(TargetInvocationException e)when(e.InnerException is NotSupportedException){Check(Shards.ExclusiveCategories.Count==2,"wrong version fails without edits");}
var root=Path.GetFullPath(args[0]);string dll=Path.Combine(root,"launcher/gameplay/ShardCategoryFreedom/bin/Release/netstandard2.1/ShardCategoryFreedom.dll");
using var resolver=new DefaultAssemblyResolver();foreach(string path in new[]{"BepInEx/core","BepInEx/plugins/RandyKnapp-EpicLoot","BepInEx/plugins/ValheimModding-Jotunn","valheim_server_Data/Managed"})resolver.AddSearchDirectory(Path.Combine(root,"management/BakaTrial/runtime",path));
string epic=Path.Combine(root,"management/BakaTrial/runtime/BepInEx/plugins/RandyKnapp-EpicLoot/EpicLoot.dll");
string Hash(string path)=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
Check(Hash(epic)=="802790e0cbd8a6f951627619a488b84e6a406e8770e2687b2fff8b63d2adf9ea","exact reviewed EpicLoot binary");
using var el=AssemblyDefinition.ReadAssembly(epic,new(){AssemblyResolver=resolver});
IEnumerable<TypeDefinition> Types(IEnumerable<TypeDefinition> ts){foreach(var t in ts){yield return t;foreach(var n in Types(t.NestedTypes))yield return n;}}
var types=Types(el.MainModule.Types).ToArray();var shards=types.Single(t=>t.FullName=="EpicLoot.ShardStones.Shards");var field=shards.Fields.Single(f=>f.Name=="ExclusiveCategories");
Check(field.IsStatic&&field.IsInitOnly&&field.FieldType.FullName=="System.Collections.Generic.HashSet`1<EpicLoot.ShardStones.ShardCategory>","exact registry field type and lifetime");
Check(shards.Methods.Single(m=>m.Name=="IsExclusive").Body.Instructions.Any(i=>i.Operand is FieldReference f&&f.FullName==field.FullName),"exclusivity check uses registry");
var reads=types.SelectMany(t=>t.Methods).Where(m=>m.HasBody&&m.Body.Instructions.Any(i=>i.OpCode==OpCodes.Ldsfld&&i.Operand is FieldReference f&&f.FullName==field.FullName)).ToArray();
Check(reads.Select(m=>m.FullName).Order().SequenceEqual(new[]{"System.Boolean EpicLoot.ShardStones.Shards::IsExclusive(EpicLoot.ShardStones.ShardCategory)","System.Boolean EpicLoot.ShardStones.UniqueShardGuard::Prefix(Humanoid,ItemDrop/ItemData,System.Boolean&)"}.Order()),"only native category and equipment guards consume registry");
Check(types.SelectMany(t=>t.Methods).Where(m=>m.HasBody&&m.Body.Instructions.Any(i=>i.OpCode==OpCodes.Stsfld&&i.Operand is FieldReference f&&f.FullName==field.FullName)).All(m=>m.Name==".cctor"),"config reload cannot reset registry");
var manager=types.Single(t=>t.FullName=="EpicLoot.ShardStones.ShardSocketManager");
foreach(string name in new[]{"CanSocket","CanCoexist"})Check(manager.Methods.Single(m=>m.Name==name).Body.Instructions.Any(i=>i.Operand is MethodReference m&&m.Name=="CheckExclusiveCategory"),"both socket insertion paths use native category guard");
Check(manager.Methods.Single(m=>m.Name=="CheckExclusiveCategory").Body.Instructions.Any(i=>i.Operand is MethodReference m&&m.Name=="IsExclusive"),"socket guard reads registry predicate");
Check(manager.Methods.Single(m=>m.Name=="CanSocket").Body.Instructions.Any(i=>i.Operand is MethodReference m&&m.Name=="CheckDuplicateEffect"),"separate duplicate-effect rule remains");
foreach(string name in new[]{"EpicLoot.MagicTooltip","EpicLoot.Compendium.ShardStoneTextInfo"})Check(types.Single(t=>t.FullName==name).Methods.Where(m=>m.HasBody).SelectMany(m=>m.Body.Instructions).Any(i=>i.Operand is MethodReference m&&m.Name=="IsExclusive"),"UI uses same native predicate: "+name);
using var prod=AssemblyDefinition.ReadAssembly(dll,new(){AssemblyResolver=resolver});
var methods=Types(prod.MainModule.Types).SelectMany(t=>t.Methods).ToArray();
Check(!methods.Any(m=>m.Name is "Update" or "FixedUpdate" or "Prefix" or "Postfix" or "Transpiler"),"no runtime hook or polling");
Check(!methods.Where(m=>m.HasBody).SelectMany(m=>m.Body.Instructions).Any(i=>(i.OpCode==OpCodes.Stfld||i.OpCode==OpCodes.Stsfld)&&i.Operand is FieldReference f&&f.DeclaringType.Scope is AssemblyNameReference a&&a.Name is "EpicLoot" or "assembly_valheim"),"no game or item fields written");
Check(methods.Where(m=>m.HasBody).SelectMany(m=>m.Body.Instructions).Count(i=>i.Operand is MethodReference m&&m.Name=="Remove"&&m.DeclaringType.Name=="HashSet`1")==2,"only two startup removals");
foreach(var m in prod.MainModule.GetMemberReferences())if(m.DeclaringType.Scope is AssemblyNameReference a&&new[]{"EpicLoot","BepInEx","Jotunn","UnityEngine.CoreModule"}.Contains(a.Name))Check(m switch{MethodReference x=>x.Resolve()!=null,FieldReference x=>x.Resolve()!=null,_=>true},"pinned API resolves "+m.FullName);
Console.WriteLine(JsonSerializer.Serialize(new{status="PASS",checks,pluginSha256=Hash(dll),epicLootSha256=Hash(epic),gameStarted=false,savesLoaded=false,liveGameplayTested=false}));
