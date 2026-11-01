using AdventureRpg.SafeReclaim;
using HarmonyLib;
using Mono.Cecil;
using System.Reflection;
using System.Reflection.Emit;
using System.Security.Cryptography;
using System.Text.Json;
using Recycle_N_Reclaim.GamePatches.Recycling;
if(args.Length!=2)throw new Exception("Usage: <workspace root> <client Managed folder>");
int checks=0;void Check(bool x,string m){if(!x)throw new Exception(m);checks++;}
void Throws(Action a,string message){bool thrown=false;try{a();}catch{thrown=true;}Check(thrown,message);}
var player=new Player();
void Tick(RecyclingAnalysisContext c,Inventory i,bool patched)
{
 var gui=InventoryGui.instance!;if(gui.m_craftTimer<0)return;gui.m_craftTimer+=.1f;
 if(gui.m_craftTimer<1)return;
 if(!patched||Plugin.BeginReclaim(c,i,true))Reclaimer.DoInventoryChanges(c,i,player,true);
 gui.m_craftTimer=-1;
}
var source=new ItemDrop.ItemData();var old=new Inventory();old.Items.Add(source);var oldContext=new RecyclingAnalysisContext(source);oldContext.Entries.AddRange([("Wood",4,false),("FenringTrophy",0,false)]);
InventoryGui.instance=new(){m_craftTimer=1};Reclaimer.Patched=false;
Throws(()=>Tick(oldContext,old,false),"Old zero add triggers Blacksmithing null dereference");
Check(old.Counts["Wood"]==4&&old.ContainsItem(source)&&InventoryGui.instance.m_craftTimer>=1,"Old partial payout, retained source and expired timer reproduced");
Throws(()=>Tick(oldContext,old,false),"Old next frame replays same request");Check(old.Counts["Wood"]==8,"Historical unlimited material growth mechanism reproduced");
foreach(bool crafted in new[]{false,true})foreach(var entries in new[]{new[]{("Wood",4,false),("FenringTrophy",0,false)},new[]{("FenringTrophy",0,false),("Wood",4,false)},new[]{("A",0,false),("B",0,false)},new[]{("A",0,true),("B",0,false),("Wood",4,false)}})
{
 var item=new ItemDrop.ItemData();var inv=new Inventory{GuardBlacksmithing=true};inv.Items.Add(item);var c=new RecyclingAnalysisContext(item){ApplyCraftedBy=crafted};c.Entries.AddRange(entries);
 InventoryGui.instance=new(){m_craftTimer=1};Reclaimer.Patched=true;Tick(c,inv,true);
 Check(!inv.ContainsItem(item),"Source removed once, including all-zero yields");Check(inv.Calls.All(x=>x.Amount>0),"Zero amounts never reach native Inventory.AddItem or addon hooks");
 Check(c.UndoRecorded&&InventoryGui.instance.m_craftTimer<0,"Native undo record path and stopped timer");
 var counts=inv.Counts.ToDictionary();Tick(c,inv,true);Check(inv.Counts.OrderBy(x=>x.Key).SequenceEqual(counts.OrderBy(x=>x.Key)),"Next frame cannot award again");
 Check(!Plugin.BeginReclaim(c,inv,true),"Stale source reference cannot pay again");
}
Plugin.RestoreDurabilityIfPresent(null);Check(true,"Blacksmithing null result safely ignored");
var durable=new ItemDrop.ItemData();Plugin.RestoreDurabilityIfPresent(durable);Check(durable.m_durability==2300&&durable.DurabilityReads==1,"Non-null native Blacksmithing durability preserved exactly");
var meta=new Inventory{GuardBlacksmithing=true};var returned=Plugin.AddPositiveResource(meta,"Wood",4,3,2,99,"Crafter",true,true)!;
Check(returned.m_stack==4&&returned.m_quality==3&&returned.m_variant==2&&returned.Crafter==99&&returned.CrafterName=="Crafter"&&returned.Cheated&&returned.Picked,"Positive native AddItem metadata passed unchanged");
Check(Plugin.AddPositiveResource(meta,"Missing",0,1,0,0,"",false,false)==null&&!meta.Calls.Any(x=>x.Name=="Missing"),"Zero does not access an unavailable prefab");
Throws(()=>Plugin.AddPositiveResource(meta,"Wood",-1,1,0,0,"",false,false),"Negative request rejected before native add");
var failItem=new ItemDrop.ItemData();var fail=new Inventory{GuardBlacksmithing=true,Refuse="Refused"};fail.Items.Add(failItem);var failContext=new RecyclingAnalysisContext(failItem);failContext.Entries.AddRange([("Wood",4,false),("Refused",2,false)]);InventoryGui.instance=new(){m_craftTimer=1};Tick(failContext,fail,true);
Check(fail.ContainsItem(failItem)&&fail.Counts["Wood"]==0&&failContext.RecyclingImpediments.Count==1&&!failContext.UndoRecorded,"Native refused-add rollback/input retention preserved");
Check(!Plugin.BeginReclaim(failContext,fail,true),"Known failed analysis cannot pay");
var exceptionItem=new ItemDrop.ItemData();var exceptionInv=new Inventory{GuardBlacksmithing=true,ThrowOn="Broken"};exceptionInv.Items.Add(exceptionItem);var exceptionContext=new RecyclingAnalysisContext(exceptionItem);exceptionContext.Entries.AddRange([("Wood",4,false),("Broken",2,false)]);InventoryGui.instance=new(){m_craftTimer=1};
Throws(()=>Tick(exceptionContext,exceptionInv,true),"Unrelated addon exception remains visible");Check(InventoryGui.instance.m_craftTimer<0,"Timer stopped even when unrelated addon throws");Tick(exceptionContext,exceptionInv,true);Check(exceptionInv.Counts["Wood"]==4,"Unrelated exception cannot automatically produce unlimited rewards");
InventoryGui.instance.m_craftTimer=7;Check(Plugin.BeginReclaim(exceptionContext,exceptionInv,false)&&InventoryGui.instance.m_craftTimer==7,"Container/non-UI reclaim never changes a crafting timer");
InventoryGui.instance=null;Check(Plugin.BeginReclaim(exceptionContext,exceptionInv,true),"No GUI tolerated");
var root=Path.GetFullPath(args[0]);var opmap=typeof(OpCodes).GetFields(BindingFlags.Static|BindingFlags.Public).Select(f=>(OpCode)f.GetValue(null)!).ToDictionary(x=>x.Value);
using var resolver=new DefaultAssemblyResolver();foreach(var p in new[]{"management/BakaTrial/runtime/valheim_server_Data/Managed","management/BakaTrial/runtime/BepInEx/core","management/BakaTrial/runtime/BepInEx/plugins/Azumatt-Recycle_N_Reclaim","management/BakaTrial/runtime/BepInEx/plugins/ValheimModding-Jotunn"})resolver.AddSearchDirectory(Path.Combine(root,p));
var rnrPath=Path.Combine(root,"management/BakaTrial/runtime/BepInEx/plugins/Azumatt-Recycle_N_Reclaim/Recycle_N_Reclaim.dll");var blackPath=Path.Combine(root,"management/BakaTrial/runtime/BepInEx/plugins/Smoothbrain-Blacksmithing/Blacksmithing.dll");
using var rnr=AssemblyDefinition.ReadAssembly(rnrPath,new(){AssemblyResolver=resolver});using var black=AssemblyDefinition.ReadAssembly(blackPath,new(){AssemblyResolver=resolver});
var reclaim=rnr.MainModule.Types.Single(t=>t.Name=="Reclaimer").Methods.Single(m=>m.Name=="DoInventoryChanges");
var actual=reclaim.Body.Instructions.Select(i=>new CodeInstruction(opmap[i.OpCode.Value],i.Operand is MethodReference m&&m.DeclaringType.Name=="Inventory"&&m.Name=="AddItem"&&m.Parameters.Count==8?Plugin.NativeAdd:i.Operand)).ToArray();
var oldInstructions=actual.Select(x=>(x.opcode,x.operand)).ToArray();var rewritten=Plugin.ReclaimPatch.Transpiler(actual).ToArray();
Check(rewritten.Length==oldInstructions.Length,"Actual RNR IL count preserved");int changed=0;
for(int i=0;i<actual.Length;i++)if((rewritten[i].opcode,rewritten[i].operand)!=oldInstructions[i]){changed++;Check(Equals(oldInstructions[i].operand,Plugin.NativeAdd)&&rewritten[i].opcode==OpCodes.Call&&Equals(rewritten[i].operand,Plugin.SafeAdd),"Only reviewed AddItem call-site changed");}
Check(changed==2,"Both ApplyCraftedBy native branches rewritten");
Throws(()=>Plugin.ReclaimPatch.Transpiler([]).ToArray(),"Unsupported RNR IL rejected");
var blackMethod=black.MainModule.Types.Single(t=>t.FullName=="Blacksmithing.Blacksmithing").NestedTypes.Single(t=>t.Name=="IncreaseDurabilityToMax").Methods.Single(m=>m.Name=="Postfix");
var unsafeBody=blackMethod.Body.Instructions.Select(i=>new CodeInstruction(opmap[i.OpCode.Value],i.Operand switch{MethodReference m when m.Name=="GetMaxDurability"=>typeof(ItemDrop.ItemData).GetMethod(m.Name),FieldReference f when f.Name=="m_durability"=>typeof(ItemDrop.ItemData).GetField(f.Name),_=>i.Operand})).ToArray();
Action<ItemDrop.ItemData?> Emit(IEnumerable<CodeInstruction> body){var dm=new DynamicMethod("DurabilityFixture",typeof(void),[typeof(ItemDrop.ItemData)],typeof(Plugin),true);var il=dm.GetILGenerator();foreach(var i in body){if(i.operand is MethodInfo m)il.Emit(i.opcode,m);else if(i.operand is FieldInfo f)il.Emit(i.opcode,f);else if(i.operand==null)il.Emit(i.opcode);else throw new Exception("Unmapped IL operand");}return dm.CreateDelegate<Action<ItemDrop.ItemData?>>();}
Throws(()=>Emit(unsafeBody)(null),"Exact installed Blacksmithing IL reproduces null exception");
var safeBody=Plugin.BlacksmithingPatch.Transpiler(unsafeBody).ToArray();Check(safeBody.Count(i=>i.opcode!=OpCodes.Nop)==3,"Blacksmithing rewritten to guarded helper");var safeDurability=Emit(safeBody);safeDurability(null);Check(true,"Rewritten actual Blacksmithing IL accepts null");var good=new ItemDrop.ItemData();safeDurability(good);Check(good.m_durability==2300&&good.DurabilityReads==1,"Rewritten actual IL retains non-null durability");
Throws(()=>Plugin.BlacksmithingPatch.Transpiler([]).ToArray(),"Unsupported Blacksmithing IL rejected");Check(Plugin.BlacksmithingPatch.TargetMethod().Name=="Postfix","Exact dynamic target signature resolves");
IEnumerable<TypeDefinition> Types(IEnumerable<TypeDefinition> ts){foreach(var t in ts){yield return t;foreach(var n in Types(t.NestedTypes))yield return n;}}
var dll=Path.Combine(root,"launcher/gameplay/SafeReclaim/bin/Release/netstandard2.1/SafeReclaim.dll");
foreach(var managed in new[]{Path.Combine(root,"management/BakaTrial/runtime/valheim_server_Data/Managed"),Path.GetFullPath(args[1])})
{
 resolver.AddSearchDirectory(managed);using var game=AssemblyDefinition.ReadAssembly(Path.Combine(managed,"assembly_valheim.dll"),new(){AssemblyResolver=resolver});using var plugin=AssemblyDefinition.ReadAssembly(dll,new(){AssemblyResolver=resolver});
 var timer=game.MainModule.Types.Single(t=>t.Name=="InventoryGui").Fields.Single(f=>f.Name=="m_craftTimer");Check(timer.FieldType.FullName=="System.Single"&&!timer.IsStatic,"Native instance timer field pinned");
 var inv=game.MainModule.Types.Single(t=>t.Name=="Inventory");Check(inv.Methods.Any(m=>m.Name=="ContainsItem"&&m.Parameters.Count==1&&m.Parameters[0].ParameterType.FullName=="ItemDrop/ItemData"),"Exact stale-source membership API");
 foreach(var member in plugin.MainModule.GetMemberReferences())if(member.DeclaringType.Scope is AssemblyNameReference a&&new[]{"assembly_valheim","assembly_utils","UnityEngine.CoreModule","BepInEx","0Harmony","Jotunn","Recycle_N_Reclaim"}.Contains(a.Name))Check(member switch{MethodReference m=>m.Resolve()!=null,FieldReference f=>f.Resolve()!=null,_=>true},"Pinned client/server API resolves "+member.FullName);
 var methods=Types(plugin.MainModule.Types).SelectMany(t=>t.Methods).ToArray();var il=methods.Where(m=>m.HasBody).SelectMany(m=>m.Body.Instructions).ToArray();Check(!methods.Any(m=>m.Name is "Update" or "FixedUpdate" or "Finalizer"),"No own polling or exception suppression");
 Check(!il.Any(i=>i.Operand is MethodReference m&&(m.Name is "Save" or "Load" or "Destroy"||m.DeclaringType.Name is "ZDO" or "ZNetView")),"No save/world/network mutation");
 Check(methods.Count(m=>m.Name=="Transpiler")==2&&methods.Count(m=>m.Name=="Prefix")==1,"Only two pinned transpilers and one reclaim entry prefix");
}
Console.WriteLine(JsonSerializer.Serialize(new{status="PASS",checks,pluginSha256=Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(dll))),rnrSha256=Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(rnrPath))),blacksmithingSha256=Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(blackPath))),actualBlacksmithIlReproduced=true,historicalLoopMechanismReproduced=true,unrelatedExceptionsNotMadeTransactional=true,liveReclaimOrMpTested=false,gameStarted=false}));
