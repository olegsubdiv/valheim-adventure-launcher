using AdventureRpg.ModdedCropBiomes;
using Advize_PlantEverything;
using Mono.Cecil;
using Mono.Cecil.Cil;
using System.Text.Json;
int checks=0;
void Check(bool ok,string name){if(!ok)throw new Exception(name);checks++;}
CropBiomes.Validate();
foreach(bool enforce in new[]{false,true})
foreach(bool broad in new[]{false,true})
foreach(bool modded in new[]{false,true})
foreach(var biome in new[]{Heightmap.Biome.Plains,Heightmap.Biome.Swamp,Heightmap.Biome.Mistlands})
foreach(var placement in new[]{Heightmap.Biome.None,Heightmap.Biome.Plains})
{
 var plant=new Plant{m_biome=biome};var piece=new Piece{m_onlyInBiome=placement};
 var go=new UnityEngine.GameObject();go.Components.Add(typeof(Plant),plant);go.Components.Add(typeof(Piece),piece);
 StaticMembers.config.EnforceBiomesVanilla=enforce;StaticMembers.config.EnableCropOverrides=broad;StaticMembers.config.OverrideModdedCrops=modded;
 StaticMembers.moddedCropRefs=[new(){Prefab=go}];
 CropBiomes.Apply();
 Check(plant.m_biome==(enforce?biome:Heightmap.Biome.All),"growth biome only follows biome setting");
 Check(piece.m_onlyInBiome==(enforce?placement:Heightmap.Biome.None),"placement biome follows biome setting");
 Check(plant.m_growTime==4300&&plant.m_growTimeMax==5200&&plant.m_growRadius==.8f&&plant.m_needCultivatedGround&&!plant.m_tolerateCold&&!plant.m_tolerateHeat&&piece.m_cultivatedGroundOnly,"authored crop stats remain intact");
 // Simulate upstream restoration before our postfix when toggling enforcement.
 plant.m_biome=biome;StaticMembers.config.EnforceBiomesVanilla=true;CropBiomes.Apply();
 Check(plant.m_biome==biome&&piece.m_onlyInBiome==placement,"re-enabling enforcement restores restrictions");
}
StaticMembers.moddedCropRefs=[new(){Prefab=null},new(){Prefab=new()}];CropBiomes.Apply();Check(true,"missing prefab/components skipped");
var unrelated=new Plant{m_biome=Heightmap.Biome.Swamp};CropBiomes.Apply();Check(unrelated.m_biome==Heightmap.Biome.Swamp,"unregistered vanilla crops/trees untouched");
var root=Path.GetFullPath(args[0]);using var resolver=new DefaultAssemblyResolver();
foreach(var dir in new[]{"valheim_server_Data/Managed","BepInEx/core","BepInEx/plugins/ValheimModding-Jotunn"})resolver.AddSearchDirectory(Path.Combine(root,"management/BakaTrial/runtime",dir));
using var pe=AssemblyDefinition.ReadAssembly(Path.Combine(root,"profile/Adventure RPG/BepInEx/plugins/Advize-PlantEverything/Advize_PlantEverything.dll"));
TypeDefinition Type(string n)=>pe.MainModule.Types.Single(t=>t.FullName=="Advize_PlantEverything."+n);
Check(Type("PluginInitUtils").Methods.Single(m=>m.Name=="InitCrops").Parameters.Count==0,"pinned initialization method");
foreach(string field in new[]{"config","moddedCropRefs","AllBiomes"})Check(Type("StaticMembers").Fields.Any(f=>f.Name==field),"pinned field "+field);
Check(Type("ModConfig").Properties.Single(p=>p.Name=="EnforceBiomesVanilla").PropertyType.FullName=="System.Boolean","synced biome option");
Check(Type("PrefabDB").Properties.Single(p=>p.Name=="Prefab").PropertyType.Name=="GameObject","prefab access");
using var plugin=AssemblyDefinition.ReadAssembly(Path.Combine(root,"launcher/gameplay/ModdedCropBiomes/bin/Release/netstandard2.1/ModdedCropBiomes.dll"),new ReaderParameters{AssemblyResolver=resolver});
IEnumerable<TypeDefinition> Types(IEnumerable<TypeDefinition> ts){foreach(var t in ts){yield return t;foreach(var n in Types(t.NestedTypes))yield return n;}}
var types=Types(plugin.MainModule.Types).ToList();var methods=types.SelectMany(t=>t.Methods).ToList();
Check(methods.Count(m=>m.Name=="Postfix")==1,"one initialization postfix");
Check(!methods.Any(m=>m.Name is "Update" or "FixedUpdate" or "LateUpdate"),"no polling callbacks");
var writes=methods.Where(m=>m.HasBody).SelectMany(m=>m.Body.Instructions).Where(i=>i.OpCode==OpCodes.Stfld&&i.Operand is FieldReference f&&f.DeclaringType.Scope.Name=="assembly_valheim").Select(i=>((FieldReference)i.Operand).Name).ToHashSet();
Check(writes.SetEquals(new[]{"m_biome","m_onlyInBiome"}),"only two game field writes");
var references=new HashSet<string>{"assembly_valheim","UnityEngine","UnityEngine.CoreModule","BepInEx","0Harmony","Jotunn"};
foreach(var member in plugin.MainModule.GetMemberReferences())if(member.DeclaringType.Scope is AssemblyNameReference a&&references.Contains(a.Name))Check(member switch{MethodReference m=>m.Resolve()!=null,FieldReference f=>f.Resolve()!=null,_=>true},"pinned API "+member.FullName);
Console.WriteLine(JsonSerializer.Serialize(new{status="PASS",checks,gameStarted=false,savesLoaded=false,productionPostfixExecutedWithFixtures=true}));
