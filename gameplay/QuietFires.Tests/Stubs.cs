namespace BepInEx
{
 public sealed class BepInPlugin:Attribute{public BepInPlugin(string id,string name,string version){}}
 public class BaseUnityPlugin{public Log Logger=new();public sealed class Log{public void LogInfo(string s){}}}
}
namespace HarmonyLib
{
 public sealed class HarmonyPatch:Attribute{public HarmonyPatch(Type t,string name){}}
 public sealed class HarmonyPriority:Attribute{public HarmonyPriority(int p){}}
 public static class Priority{public const int First=800;}
 public sealed class Harmony{public Harmony(string id){}public void PatchAll(System.Reflection.Assembly a){}public void UnpatchSelf(){}}
}
namespace UnityEngine
{
 public sealed class GameObject{public string name="";}
 public sealed class AudioSource{public bool loop;public bool mute;public bool active;public float volume=.57f;}
}
public sealed class Fireplace
{
 public UnityEngine.GameObject gameObject=new();public UnityEngine.AudioSource[] Sources=[];public int Lookups;public bool IncludedInactive;
 public void Awake(){}
 public T[] GetComponentsInChildren<T>(bool inactive){Lookups++;IncludedInactive=inactive;return Sources.Where(x=>inactive||x.active).Cast<T>().ToArray();}
}
