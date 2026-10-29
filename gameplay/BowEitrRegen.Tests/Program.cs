using AdventureRpg.BowEitrRegen;
using HarmonyLib;
using System.Reflection;
using System.Reflection.Emit;
using Mono.Cecil;
using System.Text.Json;
int checks=0;void Check(bool ok,string name){if(!ok)throw new Exception(name);checks++;}
foreach(bool attacking in new[]{false,true})
foreach(var skill in new[]{Skills.SkillType.None,Skills.SkillType.Bows,Skills.SkillType.Swords,Skills.SkillType.Crossbows,Skills.SkillType.ElementalMagic,Skills.SkillType.BloodMagic})
{
 var player=new Player{Attacking=attacking,Weapon=new()};player.Weapon.m_shared.m_skillType=skill;
 Check(Plugin.AttackStopsEitrRegen(player)==(attacking&&skill!=Skills.SkillType.Bows),"only bow attack exemption");
 Check(player.AttackReads==1,"native attack check executed once");
 Check(player.WeaponReads==(attacking?1:0),"no extra weapon lookup while not attacking");
}
foreach(bool attacking in new[]{false,true})Check(Plugin.AttackStopsEitrRegen(new Player{Attacking=attacking})==attacking,"unarmed/null weapon retains native gate");
var root=Path.GetFullPath(args[0]);using var resolver=new DefaultAssemblyResolver();
foreach(var d in new[]{"valheim_server_Data/Managed","BepInEx/core","BepInEx/plugins/ValheimModding-Jotunn"})resolver.AddSearchDirectory(Path.Combine(root,"management/BakaTrial/runtime",d));
var opmap=typeof(OpCodes).GetFields(BindingFlags.Static|BindingFlags.Public).Select(f=>(OpCode)f.GetValue(null)!).ToDictionary(o=>o.Value);
var methodsMap=new[]{nameof(Character.InAttack),nameof(Character.InDodge),nameof(Character.GetMaxEitr)}.ToDictionary(n=>n,n=>AccessTools.Method(typeof(Character),n,Type.EmptyTypes));
var regen=AccessTools.Field(typeof(Player),"m_eiterRegen");
foreach(var path in new[]{Path.Combine(root,"management/BakaTrial/runtime/valheim_server_Data/Managed/assembly_valheim.dll"),args[1]})
{
 using var game=AssemblyDefinition.ReadAssembly(path,new ReaderParameters{AssemblyResolver=resolver});
 var type=game.MainModule.Types.Single(t=>t.Name=="Player");var method=type.Methods.Single(m=>m.Name=="UpdateStats"&&m.Parameters.Count==1);
 var input=new List<CodeInstruction>();
 foreach(var il in method.Body.Instructions)
 {
  object? operand=il.Operand;
  if(operand is MethodReference mr&&methodsMap.TryGetValue(mr.Name,out var mapped)){Check(mr.DeclaringType.Name=="Character","pinned virtual-call declaring type");operand=mapped;}
  if(operand is FieldReference fr&&fr.Name=="m_eiterRegen"){Check(fr.DeclaringType.Name=="Player","pinned regen field owner");operand=regen;}
  var code=new CodeInstruction(opmap[il.OpCode.Value],operand);if(code.Calls(methodsMap["InAttack"]))code.labels.Add("attack-label");input.Add(code);
 }
 var output=Plugin.Rewrite(input).ToList();Check(input.Count==output.Count,"no added instructions in tick body");int changes=0;
 for(int i=0;i<input.Count;i++)
 {
  Check(input[i].labels.SequenceEqual(output[i].labels)&&input[i].blocks.SequenceEqual(output[i].blocks),"branch/EH metadata preserved");
  if(input[i].opcode!=output[i].opcode||!Equals(input[i].operand,output[i].operand))
  {changes++;Check(input[i].Calls(methodsMap["InAttack"])&&output[i].opcode==OpCodes.Call&&output[i].operand is MethodInfo m&&m.Name=="AttackStopsEitrRegen","only the eitr attack call replaced");}
 }
 Check(changes==1,"exactly one call-site changed in pinned client/server");
 Check(output.Count(i=>i.Calls(methodsMap["InAttack"]))==1,"separate stamina attack lockout retained");
 Check(output.Count(i=>i.Calls(methodsMap["InDodge"]))==2,"both native dodge gates retained");
 Check(output.Any(i=>i.operand is FieldReference f&&f.Name=="m_eitrRegenTimer"),"real eitr spending delay read retained");
 Check(output.Any(i=>i.operand is MethodReference m&&m.Name=="ModifyEitrRegen"),"original equipment/status/Darting regeneration pipeline retained");
 Check(input.Count(i=>i.Calls(methodsMap["InAttack"]))==2,"original IL not mutated");
 foreach(string missing in new[]{"GetMaxEitr","InAttack","InDodge"})
 {
  bool rejected=false;try{_ =Plugin.Rewrite(input.Where(i=>!i.Calls(methodsMap[missing]))).ToList();}catch(NotSupportedException){rejected=true;}Check(rejected,"upstream gate/anchor drift fails closed");
 }
}
using var plugin=AssemblyDefinition.ReadAssembly(Path.Combine(root,"launcher/gameplay/BowEitrRegen/bin/Release/netstandard2.1/BowEitrRegen.dll"),new ReaderParameters{AssemblyResolver=resolver});
IEnumerable<TypeDefinition> Types(IEnumerable<TypeDefinition> ts){foreach(var t in ts){yield return t;foreach(var n in Types(t.NestedTypes))yield return n;}}
var all=Types(plugin.MainModule.Types).SelectMany(t=>t.Methods).ToList();
Check(all.Count(m=>m.Name=="Transpiler")==1&&!all.Any(m=>m.Name is "Update" or "FixedUpdate" or "Postfix" or "UseEitr" or "RPC_UseEitr"),"one existing gate hook, no added polling/stat tick or spending hook");
Check(!all.Where(m=>m.HasBody).SelectMany(m=>m.Body.Instructions).Any(i=>i.OpCode==Mono.Cecil.Cil.OpCodes.Stfld&&i.Operand is FieldReference f&&f.DeclaringType.Scope.Name=="assembly_valheim"),"no direct game/resource field writes");
Check(!all.Where(m=>m.HasBody).SelectMany(m=>m.Body.Instructions).Any(i=>i.Operand is MethodReference m&&m.Name is "AddEitr" or "AddStamina" or "UseEitr" or "UseStamina"),"no bonus tick, resource refund or cost bypass");
var scopes=new HashSet<string>{"assembly_valheim","BepInEx","0Harmony","Jotunn"};foreach(var m in plugin.MainModule.GetMemberReferences())if(m.DeclaringType.Scope is AssemblyNameReference a&&scopes.Contains(a.Name))Check(m switch{MethodReference x=>x.Resolve()!=null,FieldReference x=>x.Resolve()!=null,_=>true},"pinned API resolves "+m.FullName);
Console.WriteLine(JsonSerializer.Serialize(new{status="PASS",checks,gameStarted=false,savesLoaded=false,productionPolicyAndRewriteExecuted=true,pinnedClientAndServerExactDeltaVerified=true}));
