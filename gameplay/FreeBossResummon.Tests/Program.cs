using AdventureRpg.FreeBossResummon;
using Mono.Cecil;
using System.Text.Json;
using System.Security.Cryptography;

int checks=0;
void Check(bool ok,string label){if(!ok)throw new Exception(label);checks++;}
OfferingBowl Bowl(bool boss=true,string key="defeated_bonemass")=>new(){
    m_bossPrefab=new(){Component=new Character{m_boss=boss,m_defeatSetGlobalKey=key}},
    m_bossItem=new(){m_itemData=new(){m_shared=new(){m_name="$bone"}}}};
var user=new Humanoid();Humanoid actor=new();
foreach(bool defeated in new[]{false,true})
foreach(bool boss in new[]{false,true})
foreach(bool hold in new[]{false,true})
foreach(bool queued in new[]{false,true})
foreach(bool stands in new[]{false,true})
{
    ZoneSystem.instance=new();if(defeated)ZoneSystem.instance.Keys.Add("defeated_bonemass");
    var b=Bowl(boss);b.Queued=queued;b.m_useItemStands=stands;
    bool result=false;actor=new();var originalActor=actor;
    bool vanilla=Plugin.InteractPatch.Prefix(b,user,hold,ref actor,ref result);
    bool free=defeated&&boss,spawn=free&&!hold&&!queued;
    Check(vanilla==!free,"first summon/nonboss stay vanilla");
    Check(b.Requests==(spawn?1:0)&&b.Positions==b.Requests,"native queue and hold respected");
    Check(result==spawn,"interaction result");
    Check(ReferenceEquals(actor,spawn?user:originalActor),"native notification actor");
    Check(b.RemoveInventory==(spawn?false:null),"free spawn never requests inventory consumption");
    Check(Plugin.AltarRemovalPatch.Prefix(b)==!free,"free altar never destroys stand offerings");
    Check(Plugin.InventoryRemovalPatch.Prefix(b)==!free,"first summon inventory removal retained");
    bool remove=true;Plugin.SpawnRequestPatch.Prefix(b,ref remove);
    Check(remove==!free,"owner preserves all native logic except free offering flag");
    remove=false;Plugin.SpawnRequestPatch.Prefix(b,ref remove);Check(!remove,"false flag stays false");
    string text="vanilla";Plugin.HoverPatch.Postfix(b,ref text);
    Check(free?text.Contains("$KEY_Use")&&text.Contains("free"):text=="vanilla","hover matches free eligibility");
}
foreach(bool defeated in new[]{false,true})
foreach(bool correct in new[]{false,true})
foreach(bool queued in new[]{false,true})
foreach(bool stands in new[]{false,true})
{
    ZoneSystem.instance=new();if(defeated)ZoneSystem.instance.Keys.Add("defeated_bonemass");
    var b=Bowl();b.Queued=queued;b.m_useItemStands=stands;
    var item=new ItemDrop.ItemData{m_shared=new(){m_name=correct?"$bone":"$wood"}};
    bool result=false;actor=new();
    bool vanilla=Plugin.UseItemPatch.Prefix(b,user,item,ref actor,ref result);
    bool free=defeated&&correct&&!stands;
    Check(vanilla==!free&&result==free,"wrong items/stand usage/first summon keep original behavior");
    Check(b.Requests==(free&&!queued?1:0),"hotbar queued summon not duplicated");
    Check(b.RemoveInventory==(b.Requests>0?false:null),"hotbar offering preserved");
}
ZoneSystem.instance=new();ZoneSystem.instance.Keys.Add("defeated_bonemass");
Check(!Plugin.IsFree(new()),"item exchange/nonboss bowl is not free");
Check(!Plugin.IsFree(Bowl(key:"")),"boss without defeat key unchanged");
Check(!Plugin.IsFree(Bowl(key:"defeated_dragon")),"defeating a different boss does not unlock free summon");
Check(!Plugin.IsFree(new(){m_bossPrefab=new()}),"non-character prefab unchanged");
var freeBowl=Bowl();string hover="";Localization.instance.Language="Russian";
Plugin.HoverPatch.Postfix(freeBowl,ref hover);Check(hover.Contains("бесплатно"),"Russian action hint");
ZoneSystem.instance=null!;Check(!Plugin.IsFree(freeBowl),"no world fails closed");

var root=Path.GetFullPath(args[0]);using var resolver=new DefaultAssemblyResolver();
foreach(var t in new[]{typeof(Plugin.InteractPatch),typeof(Plugin.UseItemPatch)})
    Check(((HarmonyLib.HarmonyAfter)Attribute.GetCustomAttribute(t,typeof(HarmonyLib.HarmonyAfter))!).Owner=="org.bepinex.plugins.creaturelevelcontrol","CLLC state prefix must run before a skipping prefix");
foreach(var d in new[]{"valheim_server_Data/Managed","BepInEx/core","BepInEx/plugins/ValheimModding-Jotunn"})resolver.AddSearchDirectory(Path.Combine(root,"management/BakaTrial/runtime",d));
foreach(string path in new[]{Path.Combine(root,"management/BakaTrial/runtime/valheim_server_Data/Managed/assembly_valheim.dll"),args[1]})
{
    using var game=AssemblyDefinition.ReadAssembly(path);
    var bowl=game.MainModule.Types.Single(t=>t.Name=="OfferingBowl");
    foreach(var (name,parameters) in new[]{("Interact",new[]{"Humanoid","System.Boolean","System.Boolean"}),
        ("UseItem",new[]{"Humanoid","ItemDrop/ItemData"}),("GetHoverText",Array.Empty<string>()),
        ("RPC_SpawnBoss",new[]{"System.Int64","UnityEngine.Vector3","System.Boolean"}),
        ("RemoveAltarItems",Array.Empty<string>()),("RPC_RemoveBossSpawnInventoryItems",new[]{"System.Int64"}),
        ("IsBossSpawnQueued",Array.Empty<string>()),("GetSpawnPosition",Array.Empty<string>()),
        ("InitiateSpawnBoss",new[]{"UnityEngine.Vector3","System.Boolean"})})
        Check(bowl.Methods.Count(m=>m.Name==name&&m.Parameters.Select(p=>p.ParameterType.FullName).SequenceEqual(parameters))==1,"pinned target/delegate signature "+name);
    Check(bowl.Fields.Single(f=>f.Name=="m_interactUser").FieldType.Name=="Humanoid","Harmony private field injection");
    var rpc=bowl.Methods.Single(m=>m.Name=="RPC_SpawnBoss");
    foreach(string name in new[]{"IsOwner","IsBossSpawnQueued","CanSpawnBoss","SpawnBoss","RemoveAltarItems"})
        Check(rpc.Body.Instructions.Any(i=>i.Operand is MethodReference m&&m.Name==name),"native owner/queue/spawn/offer path "+name);
    Check(bowl.Methods.Single(m=>m.Name=="InitiateSpawnBoss").Body.Instructions.Any(i=>i.Operand as string=="RPC_SpawnBoss"),"native RPC reused");
    var death=game.MainModule.Types.Single(t=>t.Name=="Character").Methods.Single(m=>m.Name=="OnDeath");
    Check(death.Body.Instructions.Any(i=>i.Operand is FieldReference f&&f.Name=="m_defeatSetGlobalKey")&&death.Body.Instructions.Any(i=>i.Operand is MethodReference m&&m.Name=="SetGlobalKey"),"death already writes world boss key");
}
using var plugin=AssemblyDefinition.ReadAssembly(Path.Combine(root,"launcher/gameplay/FreeBossResummon/bin/Release/netstandard2.1/FreeBossResummon.dll"),new ReaderParameters{AssemblyResolver=resolver});
IEnumerable<TypeDefinition> Types(IEnumerable<TypeDefinition> ts){foreach(var t in ts){yield return t;foreach(var n in Types(t.NestedTypes))yield return n;}}
var methods=Types(plugin.MainModule.Types).SelectMany(t=>t.Methods).ToList();
Check(!methods.Any(m=>m.Name is "Update" or "FixedUpdate" or "Transpiler"),"no polling or IL rewrites");
var calls=methods.Where(m=>m.HasBody).SelectMany(m=>m.Body.Instructions).Where(i=>i.Operand is MethodReference).Select(i=>(MethodReference)i.Operand).ToList();
Check(!calls.Any(m=>m.Name is "SetGlobalKey" or "RemoveGlobalKey" or "DestroyAttachment" or "RemoveItem" or "Instantiate" or "InvokeRPC" or "Register"),"no progression writes, item destruction, custom spawn or RPC");
Check(!methods.Where(m=>m.HasBody).SelectMany(m=>m.Body.Instructions).Any(i=>i.OpCode==Mono.Cecil.Cil.OpCodes.Stfld&&i.Operand is FieldReference f&&f.DeclaringType.Scope.Name=="assembly_valheim"),"no prefab or saved game field writes");
var scopes=new HashSet<string>{"assembly_valheim","assembly_guiutils","BepInEx","0Harmony","Jotunn"};
foreach(var m in plugin.MainModule.GetMemberReferences())
    if(m.DeclaringType.Scope is AssemblyNameReference a&&scopes.Contains(a.Name))
        Check(m switch{MethodReference x=>x.Resolve()!=null,FieldReference x=>x.Resolve()!=null,_=>true},"pinned API resolves "+m.FullName);
Console.WriteLine(JsonSerializer.Serialize(new{status="PASS",checks,gameStarted=false,savesLoaded=false,
    productionPolicyExecuted=true,pinnedClientAndServerApiVerified=true}));
