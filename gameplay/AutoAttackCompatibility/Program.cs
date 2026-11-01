// SPDX-License-Identifier: MIT-0
using Mono.Cecil;
using Mono.Cecil.Cil;
using System.Security.Cryptography;
using System.Text.Json;
if(args.Length < 3) throw new ArgumentException("Usage: original-AutoAttack.dll 0Harmony.dll output-AutoAttack.dll [review.json]");
string source=Path.GetFullPath(args[0]); string harmonyPath=Path.GetFullPath(args[1]); string output=Path.GetFullPath(args[2]);
string Hash(string p)=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p))).ToLowerInvariant();
if(Hash(source)!="8c0c5933807ccac6128d1d9b8fcda35c435e64ae18b067f63b1b4bbd69b7bfcc")throw new Exception("Original pin differs");
using var resolver=new DefaultAssemblyResolver();
resolver.AddSearchDirectory(Path.GetDirectoryName(harmonyPath)!);
var parameters=new ReaderParameters{AssemblyResolver=resolver};
using var original=AssemblyDefinition.ReadAssembly(source,parameters);
using var harmony=AssemblyDefinition.ReadAssembly(harmonyPath,parameters);
if(original.Name.HasPublicKey)throw new Exception("Would invalidate an author strong-name");
var type=original.MainModule.Types.Single(t=>t.FullName=="AutoAttack.ZInput_GetButton_ForceAttackHold");
var method=type.Methods.Single(m=>m.Name=="Postfix");
if(method.CustomAttributes.Any(a=>a.AttributeType.FullName is "HarmonyLib.HarmonyPriority" or "HarmonyLib.HarmonyAfter"))throw new Exception("Original already has ordering attributes");
var afterType=harmony.MainModule.Types.Single(t=>t.FullName=="HarmonyLib.HarmonyAfter");
var afterCtor=afterType.Methods.Single(m=>m.IsConstructor&&m.Parameters.Count==1&&m.Parameters[0].ParameterType.FullName=="System.String[]");
var after=new CustomAttribute(original.MainModule.ImportReference(afterCtor));
after.ConstructorArguments.Add(new CustomAttributeArgument(new ArrayType(original.MainModule.TypeSystem.String),new[]{new CustomAttributeArgument(original.MainModule.TypeSystem.String,"com.jotunn.jotunn")}));
var priorityType=harmony.MainModule.Types.Single(t=>t.FullName=="HarmonyLib.HarmonyPriority");
var priorityCtor=priorityType.Methods.Single(m=>m.IsConstructor&&m.Parameters.Count==1&&m.Parameters[0].ParameterType.FullName=="System.Int32");
var priority=new CustomAttribute(original.MainModule.ImportReference(priorityCtor));
priority.ConstructorArguments.Add(new CustomAttributeArgument(original.MainModule.TypeSystem.Int32,0));
method.CustomAttributes.Add(after);method.CustomAttributes.Add(priority);
Directory.CreateDirectory(Path.GetDirectoryName(output)!);original.Write(output);
using var patched=AssemblyDefinition.ReadAssembly(output,parameters);
using var pristine=AssemblyDefinition.ReadAssembly(source,parameters);
IEnumerable<TypeDefinition> Types(IEnumerable<TypeDefinition> ts){foreach(var t in ts){yield return t;foreach(var n in Types(t.NestedTypes))yield return n;}}
string Fingerprint(MethodDefinition m)
{
 if(!m.HasBody)return "no body";
 var il=m.Body.Instructions;
 string Operand(object? o)=>o switch{Instruction i=>"IL#"+il.IndexOf(i),Instruction[] xs=>string.Join(",",xs.Select(x=>il.IndexOf(x))),MemberReference r=>r.FullName,_=>o?.ToString()??""};
 return string.Join("\n",il.Select(i=>i.OpCode.Code+" "+Operand(i.Operand)))+"\nlocals:"+string.Join(",",m.Body.Variables.Select(v=>v.VariableType.FullName));
}
var before=Types(pristine.MainModule.Types).SelectMany(t=>t.Methods).ToDictionary(m=>m.FullName);
var changed=Types(patched.MainModule.Types).SelectMany(t=>t.Methods).ToDictionary(m=>m.FullName);
if(!before.Keys.Order().SequenceEqual(changed.Keys.Order()))throw new Exception("Method inventory changed");
foreach(var name in before.Keys)if(Fingerprint(before[name])!=Fingerprint(changed[name]))throw new Exception("Method body changed: "+name);
var fixedMethod=patched.MainModule.Types.Single(t=>t.FullName==type.FullName).Methods.Single(m=>m.Name=="Postfix");
if(fixedMethod.CustomAttributes.Count!=method.CustomAttributes.Count||fixedMethod.CustomAttributes.Single(a=>a.AttributeType.Name=="HarmonyPriority").ConstructorArguments[0].Value is not 0)throw new Exception("Priority mismatch");
var ownerArgs=(CustomAttributeArgument[])fixedMethod.CustomAttributes.Single(a=>a.AttributeType.Name=="HarmonyAfter").ConstructorArguments[0].Value;
if(ownerArgs.Length!=1||(string)ownerArgs[0].Value!="com.jotunn.jotunn")throw new Exception("Dependency order mismatch");
foreach(var attr in fixedMethod.CustomAttributes.Where(a=>a.AttributeType.Namespace=="HarmonyLib"))if(attr.Constructor.Resolve()==null)throw new Exception("Attribute API missing");
var report=new{status="PASS",originalSha256=Hash(source),patchedSha256=Hash(output),originalVersion=pristine.Name.Version.ToString(),patchedVersion=patched.Name.Version.ToString(),
    unchangedMethodBodies=before.Count,changedMethodBodies=0,addedAttributes=2,affectedHook="ZInput.GetButton(string) / AutoAttack postfix",
    afterOwner="com.jotunn.jotunn",priority=0,newRuntimeMethods=0,newPollingLoops=0,serverChanges=0,liveGameplayTested=false};
if(args.Length > 3) File.WriteAllText(args[3],JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));
Console.WriteLine(JsonSerializer.Serialize(report));
