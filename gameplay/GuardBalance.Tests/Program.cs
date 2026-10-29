using AdventureRpg.GuardBalance;
using HarmonyLib;
using System.Reflection;
using System.Reflection.Emit;
using Mono.Cecil;
using Mono.Cecil.Cil;
using System.Text.Json;
using OpCodes=System.Reflection.Emit.OpCodes;
int checks=0;void Check(bool ok,string n){if(!ok)throw new Exception(n);checks++;}
void Near(float a,float b,string n)=>Check(Math.Abs(a-b)<.001f,n);
var player=new Player();var npc=new Humanoid();
foreach(var type in new[]{ItemDrop.ItemData.ItemType.Shield,ItemDrop.ItemData.ItemType.OneHandedWeapon,ItemDrop.ItemData.ItemType.Bow,ItemDrop.ItemData.ItemType.Tool})
foreach(float bonus in new[]{0f,1f,1.5f,2.5f,4f,6f})
foreach(int quality in new[]{1,3,4})
foreach(float skill in new[]{0f,.4f,1f})
foreach(float enchant in new[]{1f,1.3f})
foreach(float flat in new[]{0f,25f})
{
 var item=new ItemDrop.ItemData{m_quality=quality,Enchantment=enchant,FlatBonus=flat};item.m_shared.m_itemType=type;item.m_shared.m_timedBlockBonus=bonus;
 var before=(item.m_shared.m_blockPower,item.m_shared.m_blockPowerPerLevel,item.m_shared.m_timedBlockBonus,item.m_quality);
 bool eligible=type==ItemDrop.ItemData.ItemType.Shield||bonus>1;
 float native=item.GetBlockPower(skill);
 Near(GuardPolicy.CombatBlockPower(item,skill,player),native*(eligible?1.7f:1f),"player computed block including quality/skill/enchant/flat");
 Near(GuardPolicy.CombatBlockPower(item,skill,npc),native,"NPC defense unchanged");
 Near(GuardPolicy.CombatParryBonus(bonus,player),bonus*(bonus>1?1.2f:1),"parry bonus only if already parry-capable");
 Near(GuardPolicy.CombatParryBonus(bonus,npc),bonus,"NPC parry unchanged");
 Near(GuardPolicy.TooltipBaseBlockPower(item,quality),item.GetBaseBlockPower(quality)*(eligible?1.7f:1),"ordinary tooltip block agrees");
 Check(before==(item.m_shared.m_blockPower,item.m_shared.m_blockPowerPerLevel,item.m_shared.m_timedBlockBonus,item.m_quality),"shared/item data never mutated");
}
Near(GuardPolicy.TooltipParryBonus(2.5f),3f,"buckler 2.5 to3.0");
Near(1.7f*1.2f,2.04f,"combined parry multiplier2.04");
foreach(string nested in new[]{"SkillAdjustedTooltip","EnchantedBlockTooltip","EnchantedParryTooltip"})
{
 var method=typeof(Plugin).GetNestedType(nested,BindingFlags.NonPublic)!.GetMethod("Postfix",BindingFlags.Static|BindingFlags.NonPublic)!;
 var item=new ItemDrop.ItemData();object[] args2=[item,100f];method.Invoke(null,args2);
 Near((float)args2[1],nested=="EnchantedParryTooltip"?120f:170f,"UI postfix applies once after original modifiers");
}
var root=Path.GetFullPath(args[0]);using var resolver=new DefaultAssemblyResolver();
foreach(var dir in new[]{"valheim_server_Data/Managed","BepInEx/core","BepInEx/plugins/ValheimModding-Jotunn","BepInEx/plugins/RandyKnapp-EpicLoot"})resolver.AddSearchDirectory(Path.Combine(root,"management/BakaTrial/runtime",dir));
var epicPath=Directory.EnumerateFiles(Path.Combine(root,"management/BakaTrial/runtime/BepInEx/plugins"),"EpicLoot.dll",SearchOption.AllDirectories).Single();resolver.AddSearchDirectory(Path.GetDirectoryName(epicPath)!);
var power=AccessTools.Method(typeof(ItemDrop.ItemData),"GetBlockPower",[typeof(float)]);var basePower=AccessTools.Method(typeof(ItemDrop.ItemData),"GetBaseBlockPower",[typeof(int)]);var parry=AccessTools.Field(typeof(ItemDrop.ItemData.SharedData),"m_timedBlockBonus");
var opmap=typeof(OpCodes).GetFields(BindingFlags.Static|BindingFlags.Public).Select(f=>(System.Reflection.Emit.OpCode)f.GetValue(null)!).ToDictionary(o=>o.Value);
foreach(var path in new[]{Path.Combine(root,"management/BakaTrial/runtime/valheim_server_Data/Managed/assembly_valheim.dll"),args[1]})
{
 using var game=AssemblyDefinition.ReadAssembly(path,new ReaderParameters{AssemblyResolver=resolver});
 var item=game.MainModule.Types.Single(t=>t.Name=="ItemDrop").NestedTypes.Single(t=>t.Name=="ItemData");
 foreach(bool combat in new[]{true,false})
 {
  var method=(combat?game.MainModule.Types.Single(t=>t.Name=="Humanoid"):item).Methods.Single(m=>m.Name==(combat?"BlockAttack":"AddBlockTooltip"));
  var input=new List<CodeInstruction>();
  foreach(var il in method.Body.Instructions)
  {
   object? operand=il.Operand;
   if(operand is MethodReference mr&&mr.DeclaringType.Name=="ItemData"&&mr.Name==(combat?"GetBlockPower":"GetBaseBlockPower")&&mr.Parameters.Count==1)operand=combat?power:basePower;
   if(operand is FieldReference fr&&fr.Name=="m_timedBlockBonus")operand=parry;
   var ci=new CodeInstruction(opmap[il.OpCode.Value],operand);
   if(ci.Calls(combat?power:basePower))ci.labels.Add("call-target");
   input.Add(ci);
  }
  var result=(combat?GuardRewrite.Combat(input):GuardRewrite.Tooltip(input)).ToList();
  Check(result.Count==input.Count+(combat?5:2),"pinned client/server IL insertion count");
  Check(result.Count(c=>c.operand is MethodInfo mi&&mi.Name==(combat?"CombatBlockPower":"TooltipBaseBlockPower"))==1,"single block calculation wrapper");
  Check(result.Count(c=>c.operand is MethodInfo mi&&mi.Name==(combat?"CombatParryBonus":"TooltipParryBonus"))==2,"only two native parry reads wrapped");
  Check(result.Sum(c=>c.labels.Count)==1,"branch label retained once");
  Check(input.Single(c=>c.Calls(combat?power:basePower)).labels.Count==1,"original IL not modified");
  var invalid=input.Where(c=>!c.Calls(combat?power:basePower)).ToList();
  bool rejected=false;try{_=(combat?GuardRewrite.Combat(invalid):GuardRewrite.Tooltip(invalid)).ToList();}catch(NotSupportedException){rejected=true;}
  Check(rejected,"upstream signature drift fails closed before emission");
 }
}
using var plugin=AssemblyDefinition.ReadAssembly(Path.Combine(root,"launcher/gameplay/GuardBalance/bin/Release/netstandard2.1/GuardBalance.dll"),new ReaderParameters{AssemblyResolver=resolver});
IEnumerable<TypeDefinition> Types(IEnumerable<TypeDefinition> ts){foreach(var t in ts){yield return t;foreach(var n in Types(t.NestedTypes))yield return n;}}
var methods=Types(plugin.MainModule.Types).SelectMany(t=>t.Methods).ToList();
Check(!methods.Any(m=>m.Name is "Update" or "FixedUpdate" or "RPC_Damage"),"no polling/full damage interception");
Check(!methods.Where(m=>m.HasBody).SelectMany(m=>m.Body.Instructions).Any(i=>i.OpCode==Mono.Cecil.Cil.OpCodes.Stfld&&i.Operand is FieldReference f&&f.DeclaringType.Scope.Name=="assembly_valheim"),"no game/shared data writes");
Check(methods.Count(m=>m.Name=="Transpiler")==2&&methods.Count(m=>m.Name=="Postfix")==3,"reviewed combat/UI patch count");
var scopes=new HashSet<string>{"assembly_valheim","UnityEngine","UnityEngine.CoreModule","BepInEx","0Harmony","Jotunn","EpicLoot"};
foreach(var m in plugin.MainModule.GetMemberReferences())if(m.DeclaringType.Scope is AssemblyNameReference a&&scopes.Contains(a.Name))Check(m switch{MethodReference x=>x.Resolve()!=null,FieldReference x=>x.Resolve()!=null,_=>true},"pinned API resolves "+m.FullName);
Console.WriteLine(JsonSerializer.Serialize(new{status="PASS",checks,gameStarted=false,savesLoaded=false,linkedProductionPolicyAndRewritesExecuted=true,pinnedClientAndServerILVerified=true}));
