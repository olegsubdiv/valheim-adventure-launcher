using AdventureRpg.DartingThoughtsTuning;
using HarmonyLib;
using System.Reflection;
using System.Reflection.Emit;
using Mono.Cecil;
using System.Text.Json;
int checks=0;void Check(bool ok,string n){if(!ok)throw new Exception(n);checks++;}
void Near(float a,float b,string n)=>Check(Math.Abs(a-b)<.0001f,n);
foreach(float value in new[]{0f,1f,2f,3f,4f,5f,6f,7f,10f,15f,20f,25f,40f,50f,60f,80f,100f,150f,200f,250f,300f}) {
 var values=Plugin.DisplayValues(value);
 Check(values.Length==2,"translation placeholder count preserved");Near((float)values[0],value,"regeneration display unchanged");Near((float)values[1],value*.5f*.8f,"penalty magnitude reduced by20 percent for rune/rarity/stack values");
}
Near((float)Plugin.DisplayValues(100)[1],40,"minus50 becomesminus40");
Near((float)Plugin.DisplayValues(50)[1],20,"minus25 becomesminus20");
var unrelated=new Func<float,object[]>(v=>["unrelated",v]);EpicLoot.MagicItem.Providers["Other"]=unrelated;
EpicLoot.Magic.MagicItemEffects.DartingThoughts.RegisterDisplayValues();
Plugin.RegisterDisplay();Near((float)EpicLoot.MagicItem.Providers["DartingThoughts"](100)[1],40,"existing registration replaced on plugin load");
EpicLoot.Magic.MagicItemEffects.DartingThoughts.RegisterDisplayValues();
var post=typeof(Plugin).GetNestedType("DisplayRegistration",BindingFlags.NonPublic)!.GetMethod("Postfix",BindingFlags.Static|BindingFlags.NonPublic)!;post.Invoke(null,null);
Near((float)EpicLoot.MagicItem.Providers["DartingThoughts"](100)[1],40,"provider restored after upstream registration");Check(ReferenceEquals(unrelated,EpicLoot.MagicItem.Providers["Other"]),"unrelated effect provider unchanged");
var root=Path.GetFullPath(args[0]);var epicPath=Directory.EnumerateFiles(Path.Combine(root,"management/BakaTrial/runtime/BepInEx/plugins"),"EpicLoot.dll",SearchOption.AllDirectories).Single();
using var resolver=new DefaultAssemblyResolver();foreach(var p in new[]{Path.GetDirectoryName(epicPath)!,Path.Combine(root,"management/BakaTrial/runtime/BepInEx/core"),Path.Combine(root,"management/BakaTrial/runtime/valheim_server_Data/Managed"),Path.Combine(root,"management/BakaTrial/runtime/BepInEx/plugins/ValheimModding-Jotunn")})resolver.AddSearchDirectory(p);
using var epic=AssemblyDefinition.ReadAssembly(epicPath,new ReaderParameters{AssemblyResolver=resolver});
var dtype=epic.MainModule.Types.Single(t=>t.FullName=="EpicLoot.Magic.MagicItemEffects.DartingThoughts");var method=dtype.NestedTypes.Single(t=>t.Name=="ModifyMaxEitr").Methods.Single(m=>m.Name=="Postfix");
var opmap=typeof(OpCodes).GetFields(BindingFlags.Static|BindingFlags.Public).Select(f=>(OpCode)f.GetValue(null)!).ToDictionary(o=>o.Value);
var input=method.Body.Instructions.Select(i=>new CodeInstruction(opmap[i.OpCode.Value],i.Operand)).ToList();
input.First(i=>i.opcode==OpCodes.Ldc_R4&&Equals(i.operand,.5f)).labels.Add("literal-label");
var output=Plugin.RewritePenalty(input).ToList();Check(output.Count==input.Count,"instruction count unchanged");
int changes=0;for(int i=0;i<input.Count;i++) {
 Check(input[i].opcode==output[i].opcode,"original opcode retained");
 Check(input[i].labels.SequenceEqual(output[i].labels)&&input[i].blocks.SequenceEqual(output[i].blocks),"branch labels/exception regions retained");
 if(!Equals(input[i].operand,output[i].operand)){changes++;Check(Equals(input[i].operand,.5f)&&Equals(output[i].operand,.4f),"only the penalty literal changes");}
}
Check(changes==1,"exactly one operand changed");Check(input.Count(i=>i.opcode==OpCodes.Ldc_R4&&Equals(i.operand,.5f))==1,"source IL not mutated");
Check(method.Body.Instructions.Any(i=>i.Operand is MethodReference m&&m.Name=="HasActiveMagicEffect"),"native active-effect/stack lookup retained");
Check(method.Body.Instructions.Count(i=>i.OpCode==Mono.Cecil.Cil.OpCodes.Stind_R4)==2,"native update and zero clamp retained");
foreach(int count in new[]{0,2}){bool rejected=false;try{_ =Plugin.RewritePenalty(Enumerable.Range(0,count).Select(_=>new CodeInstruction(OpCodes.Ldc_R4,.5f))).ToList();}catch(NotSupportedException){rejected=true;}Check(rejected,"unexpected upstream ratio count fails closed");}
foreach(string type in new[]{"MaximumEitrPenalty","DisplayRegistration"})Check(typeof(Plugin).GetNestedType(type,BindingFlags.NonPublic)!.GetMethod("TargetMethod",BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,null) is MethodInfo,"reflected target resolves");
using var plugin=AssemblyDefinition.ReadAssembly(Path.Combine(root,"launcher/gameplay/DartingThoughtsTuning/bin/Release/netstandard2.1/DartingThoughtsTuning.dll"),new ReaderParameters{AssemblyResolver=resolver});
IEnumerable<TypeDefinition> Types(IEnumerable<TypeDefinition> ts){foreach(var t in ts){yield return t;foreach(var n in Types(t.NestedTypes))yield return n;}}
var methods=Types(plugin.MainModule.Types).SelectMany(t=>t.Methods).ToList();
Check(methods.Count(m=>m.Name=="Transpiler")==1&&methods.Count(m=>m.Name=="Postfix")==1,"one existing-penalty patch and one display-registration patch");
Check(!methods.Any(m=>m.Name is "Update" or "FixedUpdate" or "ModifyEitrRegen" or "GetTotalFoodValue"),"no added recurring/stat/regen callbacks");
Check(!methods.Where(m=>m.HasBody).SelectMany(m=>m.Body.Instructions).Any(i=>i.OpCode==Mono.Cecil.Cil.OpCodes.Stfld&&i.Operand is FieldReference f&&f.DeclaringType.Scope.Name is "assembly_valheim" or "EpicLoot"),"no item/game data writes");
var scopes=new HashSet<string>{"BepInEx","0Harmony","Jotunn","EpicLoot"};foreach(var m in plugin.MainModule.GetMemberReferences())if(m.DeclaringType.Scope is AssemblyNameReference a&&scopes.Contains(a.Name))Check(m switch{MethodReference x=>x.Resolve()!=null,FieldReference x=>x.Resolve()!=null,_=>true},"pinned API resolves "+m.FullName);
Console.WriteLine(JsonSerializer.Serialize(new{status="PASS",checks,gameStarted=false,savesLoaded=false,productionRewriteAndProviderExecuted=true,exactPinnedEpicLootDeltaVerified=true}));
