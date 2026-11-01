using AdventureRpg.QuietFires;
using Mono.Cecil;
using Mono.Cecil.Cil;
using System.Security.Cryptography;
using System.Text.Json;
if(args.Length!=2)throw new Exception("Usage: <workspace root> <client Managed directory>");
int checks=0;void Check(bool x,string m){if(!x)throw new Exception(m);checks++;}
string[] selected=["hearth","rk_hearth","rk_brazier","piece_brazierceiling01","piece_brazierfloor01","piece_brazierfloor02"];
string[] other=["fire_pit","rk_campfire","bonfire","piece_groundtorch","CastleKit_brazier","MountainKit_brazier","hearth_extra","rk_hearth2","other_hearth","HEARTH","hearth(Clone)(Clone)",""];
foreach(string n in selected.Concat(selected.Select(n=>n+"(Clone)")).Concat(other))
{
 bool wanted=selected.Any(s=>n==s||n==s+"(Clone)");Check(Plugin.IsSelected(n)==wanted,"Exact name boundary "+n);
 var a=new UnityEngine.AudioSource{loop=true,active=true};var b=new UnityEngine.AudioSource{loop=true,active=false};var c=new UnityEngine.AudioSource{loop=false,active=true};
 var fire=new Fireplace{gameObject=new(){name=n},Sources=[a,b,c]};Plugin.MuteLoops(fire);
 Check(a.mute==wanted&&b.mute==wanted&&!c.mute,"Active/inactive loop-only selection "+n);
 Check(a.volume==.57f&&b.volume==.57f&&c.volume==.57f,"Native volume preserved");
 Check(fire.Lookups==(wanted?1:0)&&(!wanted||fire.IncludedInactive),"One lookup only on selected sources");
 Plugin.MuteLoops(fire);Check(a.mute==wanted&&b.mute==wanted&&!c.mute,"Idempotent reload");
}
var empty=new Fireplace{gameObject=new(){name="hearth"}};Plugin.MuteLoops(empty);Check(empty.Lookups==1,"Empty audio hierarchy tolerated");
var root=Path.GetFullPath(args[0]);var dll=Path.Combine(root,"launcher/gameplay/QuietFires/bin/Release/netstandard2.1/QuietFires.dll");
IEnumerable<TypeDefinition> Types(IEnumerable<TypeDefinition> ts){foreach(var t in ts){yield return t;foreach(var n in Types(t.NestedTypes))yield return n;}}
foreach(string folder in new[]{Path.Combine(root,"management/BakaTrial/runtime/valheim_server_Data/Managed"),Path.GetFullPath(args[1])})
{
 using var resolver=new DefaultAssemblyResolver();resolver.AddSearchDirectory(folder);resolver.AddSearchDirectory(Path.Combine(root,"management/BakaTrial/runtime/BepInEx/core"));
 using var game=AssemblyDefinition.ReadAssembly(Path.Combine(folder,"assembly_valheim.dll"),new(){AssemblyResolver=resolver});
 using var plugin=AssemblyDefinition.ReadAssembly(dll,new(){AssemblyResolver=resolver});
 var fire=game.MainModule.Types.Single(t=>t.Name=="Fireplace");Check(fire.Methods.Single(m=>m.Name=="Awake").Parameters.Count==0,"Native Awake target");
 var zsfx=game.MainModule.Types.Single(t=>t.Name=="ZSFX");Check(!zsfx.Methods.Where(m=>m.HasBody).SelectMany(m=>m.Body.Instructions).Any(i=>i.Operand is MethodReference m&&m.Name=="set_mute"),"Native ZSFX never resets mute");
 var methods=Types(plugin.MainModule.Types).SelectMany(t=>t.Methods).ToArray();var instructions=methods.Where(m=>m.HasBody).SelectMany(m=>m.Body.Instructions).ToArray();
 Check(!instructions.Any(i=>i.OpCode==OpCodes.Stfld&&i.Operand is FieldReference f&&f.DeclaringType.Scope.Name=="assembly_valheim"),"No game field writes");
 Check(instructions.Count(i=>i.Operand is MethodReference m&&m.Name=="set_mute")==1,"Only audio mute setter");
 Check(methods.Count(m=>m.Name=="Prefix")==1&&!methods.Any(m=>m.Name is "Update" or "FixedUpdate" or "Postfix" or "Transpiler"),"One initialization hook; no polling");
 Check(!instructions.Any(i=>i.Operand is MethodReference m&&(m.Name is "Destroy" or "SetActive" or "InvokeRepeating" or "Stop"||m.DeclaringType.Name is "ZDO" or "ZNetView")),"No object/gameplay/network mutation");
 foreach(var member in plugin.MainModule.GetMemberReferences())if(member.DeclaringType.Scope is AssemblyNameReference a&&new[]{"assembly_valheim","UnityEngine.CoreModule","UnityEngine.AudioModule","BepInEx","0Harmony"}.Contains(a.Name))
  Check(member switch{MethodReference m=>m.Resolve()!=null,FieldReference f=>f.Resolve()!=null,_=>true},"Pinned API resolves "+member.FullName);
}
using var assets=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"research/20261101-quiet-fires/assets.json")));
foreach(string n in selected){var row=assets.RootElement.EnumerateArray().Single(x=>x.GetProperty("prefab").GetString()==n);Check(row.GetProperty("fireplace").GetBoolean(),"Selected asset has Fireplace "+n);Check(row.GetProperty("audio").GetArrayLength()==1&&row.GetProperty("audio")[0].GetProperty("loop").GetBoolean(),"Selected asset has continuous audio "+n);}
Console.WriteLine(JsonSerializer.Serialize(new{status="PASS",checks,pluginSha256=Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(dll))),clientOnly=true,liveAudioTested=false,gameStarted=false,savesEdited=false}));
