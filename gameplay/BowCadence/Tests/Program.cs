using AdventureRpg.BowCadence;
using Mono.Cecil;
using Mono.Cecil.Cil;
using System.Text.Json;

int checks = 0;
void Check(bool ok, string description) { if (!ok) throw new Exception(description); checks++; }
foreach (var (skill, expected) in new[] { (-20f, 1d), (0f, 1d), (25f, 1.125d), (50f, 1.25d), (75f, 1.375d), (100f, 1.5d), (1000f, 1.5d), (float.NaN, 1d) })
    Check(Math.Abs(CadencePolicy.Factor(true, true, true, true, skill) - expected) < 1e-9, "clamped skill scaling " + skill);
foreach (float skill in new[] { 0f, 50f, 100f })
foreach (var (on, local, bow, fire) in new[] { (false,true,true,true), (true,false,true,true), (true,true,false,true), (true,true,true,false) })
    Check(CadencePolicy.Factor(on, local, bow, fire, skill) == 1d, "unrelated scope unchanged");
Check(0.8 / CadencePolicy.Factor(true,true,true,true,100f) > 0.5, "release cannot become instantaneous at maximum skill");
Check(Math.Abs(2d * CadencePolicy.Factor(true,true,true,true,100f) - 3d) < 1e-9, "compose upstream speed multiplicatively");

var root = Path.GetFullPath(args[0]);
using var game = AssemblyDefinition.ReadAssembly(Path.Combine(root,"management/BakaTrial/runtime/valheim_server_Data/Managed/assembly_valheim.dll"));
using var asm = AssemblyDefinition.ReadAssembly(Path.Combine(root,"management/BakaTrial/runtime/BepInEx/plugins/Alpus-NorseDemigods/AnimationSpeedManager.dll"));
using var own = AssemblyDefinition.ReadAssembly(Path.Combine(root,"launcher/gameplay/BowCadence/bin/Release/netstandard2.1/BowCadence.dll"));
TypeDefinition Type(AssemblyDefinition a,string n) => a.MainModule.Types.Single(t=>t.Name==n);
IEnumerable<MethodReference> Calls(MethodDefinition m) => m.Body.Instructions.Select(i=>i.Operand).OfType<MethodReference>();
var manager=Type(asm,"AnimationSpeedManager");
var add=manager.Methods.Single(m=>m.Name=="Add");
Check(manager.IsPublic && add.IsPublic && add.IsStatic && add.Parameters.Count==2,"public shared animation manager registration");
Check(add.Parameters[1].IsOptional,"registration priority optional");
var handler=manager.NestedTypes.Single(t=>t.Name=="Handler").Methods.Single(m=>m.Name=="Invoke");
Check(handler.ReturnType.FullName=="System.Double" && handler.Parameters[0].ParameterType.Name=="Character" && handler.Parameters[1].ParameterType.FullName=="System.Double","shared manager callback signature");
var character=Type(game,"Character");
Check(character.Fields.Any(f=>f.Name=="m_animator" && f.FieldType.FullName=="UnityEngine.Animator"),"cached animator field exists");
Check(character.Methods.Any(m=>m.Name=="GetSkillLevel" && m.IsPublic && m.Parameters.Count==1),"Bows skill API exists");
Check(Type(game,"Humanoid").Methods.Any(m=>m.Name=="GetCurrentWeapon" && m.IsPublic && m.Parameters.Count==0),"equipped weapon API exists");
Check(Type(game,"Player").Fields.Any(f=>f.Name=="m_localPlayer" && f.IsPublic && f.IsStatic),"local player guard API exists");
var plugin=Type(own,"Plugin");
var adjust=plugin.Methods.Single(m=>m.Name=="AdjustSpeed");
var calls=Calls(adjust).ToArray();
Check(calls.Any(m=>m.Name=="GetCurrentAnimatorStateInfo"),"only real animator state inspected");
Check(calls.Any(m=>m.Name=="get_shortNameHash"),"exact bow fire short state guard");
Check(calls.Any(m=>m.Name=="GetSkillLevel"),"effective Bows skill read from character");
Check(calls.Any(m=>m.DeclaringType.Name=="CadencePolicy" && m.Name=="Factor"),"production uses tested cadence policy");
Check(!calls.Any(m=>m.Name=="set_speed"),"speed composed through manager rather than direct animator writes");
Check(!plugin.CustomAttributes.Any(a=>a.AttributeType.Name=="HarmonyPatch"),"no independent Harmony attack/draw patches");
Check(plugin.Methods.All(m=>m.Name!="Update" && m.Name!="FixedUpdate" && m.Name!="LateUpdate"),"no extra per-frame MonoBehaviour loop");
var allcalls=plugin.Methods.Where(m=>m.HasBody).SelectMany(Calls).ToArray();
Check(!allcalls.Any(m=>m.Name.Contains("RaiseSkill") || m.Name.Contains("Damage") || m.Name.Contains("DrawPercentage")),"no XP/damage/draw modifications");
Check(allcalls.Count(m=>m.DeclaringType.Name=="AnimationSpeedManager" && m.Name=="Add")==1,"one shared manager handler only");
Check(plugin.Methods.Single(m=>m.Name==".cctor").Body.Instructions.Any(i=>i.OpCode==OpCodes.Ldstr && Equals(i.Operand,"bow_fire")),"only standard bow release animation selected");
Console.WriteLine(JsonSerializer.Serialize(new { status="PASS",checks,gameStarted=false,savesLoaded=false,engineFreeRegression=true,pinnedAssemblyContracts=true }));
