using System.Reflection;
namespace BepInEx {
 public sealed class BepInPlugin:Attribute {public BepInPlugin(string a,string b,string c){}}
 [AttributeUsage(AttributeTargets.Class,AllowMultiple=true)] public sealed class BepInDependency:Attribute {public BepInDependency(string a){} public BepInDependency(string a,string b){}}
 public class BaseUnityPlugin {public Log Logger=new();public sealed class Log {public void LogInfo(string s){}}}
}
namespace BepInEx.Bootstrap {public static class Chainloader {public static Dictionary<string,Info> PluginInfos=new();public sealed class Info {public Meta Metadata=new();}public sealed class Meta {public System.Version Version=new(1,21,4);}}}
namespace HarmonyLib {
 public sealed class HarmonyPatch:Attribute {}
 public sealed class HarmonyPriority:Attribute {public HarmonyPriority(int p){}}
 public static class Priority {public const int Last=0;}
 public sealed class Harmony {public Harmony(string s){} public void PatchAll(Assembly a){}public void UnpatchSelf(){}}
 public static class AccessTools {
 const BindingFlags F=BindingFlags.Static|BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
 public static Type? TypeByName(string n)=>typeof(AccessTools).Assembly.GetType(n);
 public static FieldInfo? Field(Type t,string n)=>t.GetField(n,F);
 public static PropertyInfo? Property(Type t,string n)=>t.GetProperty(n,F);
 public static MethodInfo? Method(Type t,string n)=>t.GetMethod(n,F);
 }
}
namespace Jotunn.Utils {
 public enum CompatibilityLevel {NotEnforced,EveryoneMustHaveMod}public enum VersionStrictness {Patch}
 public sealed class NetworkCompatibilityAttribute:Attribute {public NetworkCompatibilityAttribute(CompatibilityLevel c,VersionStrictness v){}}
}
namespace UnityEngine {public sealed class GameObject {public Dictionary<Type,object> Components=new();public T? GetComponent<T>() where T:class=>Components.GetValueOrDefault(typeof(T)) as T;}}
public class Heightmap {[Flags]public enum Biome {None=0,Meadows=1,Swamp=2,Plains=16,Mistlands=512,All=895}}
public class Plant {public Heightmap.Biome m_biome;public float m_growTime=4300,m_growTimeMax=5200,m_growRadius=.8f;public bool m_needCultivatedGround=true,m_tolerateCold=false,m_tolerateHeat=false;}
public class Piece {public Heightmap.Biome m_onlyInBiome; public bool m_cultivatedGroundOnly=true;}
namespace Advize_PlantEverything {
 public class ModConfig {public bool EnforceBiomesVanilla {get;set;}public bool EnableCropOverrides,OverrideModdedCrops;}
 public class PrefabDB {public UnityEngine.GameObject? Prefab {get;set;}}
 public class ModdedPlantDB:PrefabDB {}
 public static class StaticMembers {public static ModConfig config=new();public static List<ModdedPlantDB> moddedCropRefs=new(); public static Heightmap.Biome AllBiomes=Heightmap.Biome.All;}
 public static class PluginInitUtils {public static void InitCrops(){}}
}
