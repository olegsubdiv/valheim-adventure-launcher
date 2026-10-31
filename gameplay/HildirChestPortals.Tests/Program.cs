using AdventureRpg.HildirChestPortals;
using Mono.Cecil;
using Mono.Cecil.Cil;
using System.Security.Cryptography;
using System.Text.Json;
int checks=0;void Check(bool ok,string reason){if(!ok)throw new Exception(reason);checks++;}
string[] names={"chest_hildir1","chest_hildir2","chest_hildir3"};
foreach(bool original in new[]{false,true})
{
 var db=new ObjectDB();
 foreach(string n in names.Concat(new[]{"Iron","CopperOre","Silver","chest_hildir4","chest_hildir1_extra","TreasureChest_forestcrypt_hildir"}))
  db.Items[n]=new(){Drop=new(){m_itemData=new(){m_shared=new(){m_teleportable=original}}}};
 var old=db.Items.ToDictionary(x=>x.Key,x=>x.Value.Drop!.m_itemData!.m_shared!);
 Plugin.EnableChests(db);
 Check(db.Lookups.SequenceEqual(names),"exactly three direct prefab lookups");
 foreach(var(n,data) in old)
 {
  Check(ReferenceEquals(data,db.Items[n].Drop!.m_itemData!.m_shared),"shared definition retained");
  Check(data.m_teleportable==(original||names.Contains(n)),"only quest chest portal flag changes: "+n);
  Check(data.m_weight==200&&data.m_toolTier==0&&data.m_name=="original","other item properties preserved");
 }
 Plugin.EnableChests(db);Check(old.Where(x=>names.Contains(x.Key)).All(x=>x.Value.m_teleportable),"idempotent initialization");
}
foreach(int missing in new[]{0,1,2,3})
{
 var db=new ObjectDB();
 if(missing<3)db.Items[names[missing]]=new(){Drop=missing==0?null:new(){m_itemData=missing==1?null:new(){m_shared=null}}};
 Plugin.EnableChests(db);Check(db.Lookups.SequenceEqual(names),"missing components and missing prefab tolerated");
}
var root=Path.GetFullPath(args[0]);var dll=Path.Combine(root,"launcher/gameplay/HildirChestPortals/bin/Release/netstandard2.1/HildirChestPortals.dll");
IEnumerable<TypeDefinition> Types(IEnumerable<TypeDefinition> ts){foreach(var t in ts){yield return t;foreach(var n in Types(t.NestedTypes))yield return n;}}
int targets=0;
foreach(string managed in new[]{"management/BakaTrial/runtime/valheim_server_Data/Managed","F:/SteamLibrary/steamapps/common/Valheim/valheim_Data/Managed"})
{
 using var resolver=new DefaultAssemblyResolver();resolver.AddSearchDirectory(Path.Combine(root,managed));
 foreach(var path in new[]{"BepInEx/core","BepInEx/plugins/ValheimModding-Jotunn"})resolver.AddSearchDirectory(Path.Combine(root,"management/BakaTrial/runtime",path));
 using var game=AssemblyDefinition.ReadAssembly(Path.Combine(root,managed,"assembly_valheim.dll"),new(){AssemblyResolver=resolver});
 using var plugin=AssemblyDefinition.ReadAssembly(dll,new(){AssemblyResolver=resolver});
 var db=game.MainModule.Types.Single(t=>t.Name=="ObjectDB");var register=db.Methods.Single(m=>m.Name=="UpdateRegisters");
 Check(register.Parameters.Count==0&&register.ReturnType.FullName=="System.Void","exact register target");
 foreach(string n in new[]{"Awake","CopyOtherDB"})Check(db.Methods.Single(m=>m.Name==n).Body.Instructions.Any(i=>i.Operand is MethodReference m&&m.Name=="UpdateRegisters"),n+" registers item definitions");
 var inventory=game.MainModule.Types.Single(t=>t.Name=="Inventory");
 var tele=inventory.Methods.Single(m=>m.Name=="IsTeleportable");
 Check(tele.Body.Instructions.Any(i=>i.Operand is FieldReference f&&f.Name=="m_teleportable"),"native inventory reads the edited flag");
 Check(tele.Body.Instructions.Any(i=>i.Operand is FieldReference f&&f.Name=="m_toolTier"),"other native teleport gates exist and are unpatched");
 var item=game.MainModule.Types.Single(t=>t.Name=="ItemDrop").NestedTypes.Single(t=>t.Name=="ItemData");
 Check(item.Methods.Single(m=>m.Name=="Clone").Body.Instructions.Any(i=>i.Operand is MethodReference m&&m.Name=="MemberwiseClone"),"inventory items retain native shared definitions through cloning");
 var methods=Types(plugin.MainModule.Types).SelectMany(t=>t.Methods).ToArray();
 var writes=methods.Where(m=>m.HasBody).SelectMany(m=>m.Body.Instructions).Where(i=>i.OpCode==OpCodes.Stfld&&i.Operand is FieldReference f&&f.DeclaringType.Scope.Name=="assembly_valheim").Select(i=>((FieldReference)i.Operand).Name).ToArray();
 Check(writes.SequenceEqual(new[]{"m_teleportable"}),"only one game field is written");
 Check(methods.Count(m=>m.Name=="Postfix")==1&&!methods.Any(m=>m.Name is "Update" or "FixedUpdate" or "Prefix" or "Transpiler"),"one initialization hook without polling or teleport bypass");
 var scopes=new HashSet<string>{"assembly_valheim","UnityEngine","UnityEngine.CoreModule","BepInEx","0Harmony","Jotunn"};
 foreach(var member in plugin.MainModule.GetMemberReferences())if(member.DeclaringType.Scope is AssemblyNameReference a&&scopes.Contains(a.Name))Check(member switch{MethodReference x=>x.Resolve()!=null,FieldReference x=>x.Resolve()!=null,_=>true},"pinned API resolves "+member.FullName);
 targets++;
}
using var assets=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"research/20261031-hildir-portals/items.json")));
Check(assets.RootElement.GetArrayLength()==3,"three pinned assets");
foreach(var row in assets.RootElement.EnumerateArray())Check(names.Contains(row.GetProperty("prefab").GetString())&&row.GetProperty("teleportable").GetInt32()==0&&row.GetProperty("toolTier").GetInt32()==0,"native restriction is solely teleport flag");
Console.WriteLine(JsonSerializer.Serialize(new{status="PASS",checks,pinnedAssemblies=targets,pluginSha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(dll))).ToLowerInvariant(),gameStarted=false,savesLoaded=false,liveGameplayTested=false}));
