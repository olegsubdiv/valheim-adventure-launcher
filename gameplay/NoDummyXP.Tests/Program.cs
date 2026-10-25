using AdventureRpg.NoDummyXp;
using Mono.Cecil;
using System.Text.Json;

int cases = 0;
void Check(bool ok, string name) { if (!ok) throw new Exception(name); cases++; }
var weapon = new XpPolicy(); Check(!weapon.Blocks(1), "unrelated award unchanged");
weapon.Record(1, true); Check(weapon.Blocks(1), "dummy melee XP blocked");
Check(!weapon.Blocks(2), "unrelated skill unaffected by dummy weapon vote");
weapon.Record(1, false); Check(!weapon.Blocks(1), "mixed dummy/real enemy preserves real XP");
var ranged = new XpPolicy(); ranged.Record(-1, true); Check(ranged.Blocks(8), "dummy bow XP blocked");
Check(ranged.Blocks(75), "dummy elemental magic XP blocked");
Check(ranged.Blocks(76), "dummy blood magic XP blocked");
ranged.Record(-1, false); Check(!ranged.Blocks(8), "mixed explosion preserves real target XP");
var strict = new XpPolicy(true); Check(strict.Blocks(6), "dummy block/parry XP blocked");
Check(strict.Blocks(998123), "scoped custom demigod XP blocked");
Check(strict.Blocks(102), "dummy perfect dodge bonus XP blocked");
var resources = new XpPolicy(); resources.Record(12, false); Check(!resources.Blocks(12), "ordinary resource XP retained");
resources.Record(12, true); Check(!resources.Blocks(12), "resource+dummy retains resource XP");
Check(!new XpPolicy().Blocks(75), "new AoE hit against real target starts unblocked");
var root = Path.GetFullPath(args[0]);
using var game = AssemblyDefinition.ReadAssembly(Path.Combine(root,"management/BakaTrial/runtime/valheim_server_Data/Managed/assembly_valheim.dll"));
IEnumerable<TypeDefinition> All(IEnumerable<TypeDefinition> ts) { foreach(var t in ts) { yield return t; foreach(var c in All(t.NestedTypes)) yield return c; } }
var types=All(game.MainModule.Types).ToList();
var targets=new List<MethodDefinition>();
MethodDefinition Method(string t,string n)=>types.Single(x=>x.FullName==t).Methods.Single(m=>m.Name==n);
targets.Add(Method("Attack","DoMeleeAttack"));
targets.Add(types.SelectMany(t=>t.Methods).Single(m=>m.Name.StartsWith("<DoAreaAttack>g__checkHits|")));
targets.Add(Method("Projectile","OnHit"));targets.Add(Method("Projectile","DoAOE"));targets.Add(Method("Aoe","OnHit"));
foreach(var method in targets)
    Check(method.Body.Instructions.Count(i=>i.Operand is MethodReference r && r.DeclaringType.FullName=="IDestructible" && r.Name=="Damage")== (method.DeclaringType.FullName=="Aoe" ? 2 : 1),"damage-call layout: "+method.FullName);
foreach(var n in new[]{"Damage","RPC_Damage","ApplyDamage"}) Check(Method("Character",n).Parameters.Any(p=>p.Name=="hit" && p.ParameterType.Name=="HitData"),"damage scope signature "+n);
Check(Method("Humanoid","BlockAttack").Parameters.Any(p=>p.Name=="attacker" && p.ParameterType.Name=="Character"),"block attacker context");
Check(Method("Skills","RaiseSkill").Parameters.Any(p=>p.Name=="skillType"),"skill guard parameter");
Check(Method("Player","RPC_HitWhileDodging").Parameters.Count==1,"perfect dodge RPC layout");
using var nd=AssemblyDefinition.ReadAssembly(Path.Combine(root,"management/BakaTrial/runtime/BepInEx/plugins/Alpus-NorseDemigods/NorseDemigods.dll"));
var se=All(nd.MainModule.Types).Single(t=>t.FullName=="NorseDemigods.SE_Demigod");
foreach(var n in new[]{"RaiseSkill","RaiseSkillPct"}) Check(se.Methods.Single(m=>m.Name==n).Parameters.Any(p=>p.Name=="skillType"),"Norse award signature "+n);
Console.WriteLine(JsonSerializer.Serialize(new { status="PASS", checks=cases, methods=targets.Select(m=>m.FullName),gameStarted=false,worldsOrCharactersLoaded=false }));
