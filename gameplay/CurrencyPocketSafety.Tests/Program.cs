using AdventureRpg.CurrencyPocketSafety;
using BepInEx.Bootstrap;
using Mono.Cecil;
using System.Reflection;
using System.Reflection.Emit;
using System.Security.Cryptography;
using System.Text.Json;
using System.IO.Compression;
using CP = CurrencyPocket.CurrencyPocket;
if(args.Length!=2)throw new Exception("Usage: <workspace root> <client Managed folder>");
int checks=0;void Check(bool ok,string name){if(!ok)throw new Exception(name);checks++;}
var root=Path.GetFullPath(args[0]);var research=Path.Combine(root,"research/20261104-qol-wallet");
var cpPath=Path.Combine(research,"extracted/Azumatt-CurrencyPocket-1.0.15/CurrencyPocket.dll");
using var cp=AssemblyDefinition.ReadAssembly(cpPath);
IEnumerable<TypeDefinition> Types(IEnumerable<TypeDefinition> ts){foreach(var t in ts){yield return t;foreach(var n in Types(t.NestedTypes))yield return n;}}
var unsafeDebit=Types(cp.MainModule.Types).Single(t=>t.FullName=="CurrencyPocket.CurrencyPocket/Inventory_RemoveItem_Patch").Methods.Single(m=>m.Name=="Postfix");
var extractCoins=Types(cp.MainModule.Types).Single(t=>t.FullName=="CurrencyPocket.MiscFunctions").Methods.Single(m=>m.Name=="ExtractCoins");
Check(extractCoins.Body.Instructions.Any(i=>i.Operand is MethodReference m&&m.DeclaringType.FullName=="Inventory"&&m.Name=="AddItem"&&m.Parameters.Select(p=>p.ParameterType.FullName).SequenceEqual(new[]{"UnityEngine.GameObject","System.Int32"})),"Actual CP preview uses the native prefab AddItem overload that clamps to one stack");
Check(unsafeDebit.Parameters.Select(p=>p.ParameterType.FullName).SequenceEqual(new[]{"Inventory","System.String","System.Int32","System.Int32","System.Boolean"}),"Actual CurrencyPocket removal hook signature pinned");
Check(!unsafeDebit.Body.Instructions.Any(i=>i.Operand is MethodReference m&&m.Name=="CountItems"),"Actual vendor debit does not measure physically paid coins");
Check(unsafeDebit.Body.Instructions.Count(i=>i.Operand is MethodReference m&&m.Name=="GetPlayerCoinsFromCustomData")==2,"Actual vendor debit rechecks full wallet amount");

// Execute the installed vendor postfix IL against engine-free boundary stubs.
// Instructions and branch topology come from the real DLL, not a rewritten copy.
var opmap=typeof(OpCodes).GetFields(BindingFlags.Public|BindingFlags.Static).Select(f=>(OpCode)f.GetValue(null)!).ToDictionary(v=>v.Value);
var dm=new DynamicMethod("ActualCurrencyPocketPostfix",typeof(void),new[]{typeof(Inventory),typeof(string),typeof(int),typeof(int),typeof(bool)},typeof(Plugin),true);
var il=dm.GetILGenerator();var labels=unsafeDebit.Body.Instructions.ToDictionary(i=>i,_=>il.DefineLabel());
Type MapType(TypeReference t)=>t.FullName switch{"System.Int32"=>typeof(int),"System.Boolean"=>typeof(bool),_=>throw new Exception("Unexpected vendor local "+t.FullName)};
var locals=unsafeDebit.Body.Variables.Select(v=>il.DeclareLocal(MapType(v.VariableType))).ToArray();
MethodInfo MapMethod(MethodReference m)=>m.DeclaringType.FullName switch
{
 "UnityEngine.Object" when m.Name=="op_Equality"=>typeof(ReferencePolicy).GetMethod(nameof(ReferencePolicy.Equal))!,
 "Humanoid" when m.Name=="GetInventory"=>typeof(Humanoid).GetMethod(nameof(Humanoid.GetInventory))!,
 "System.String" when m.Name=="op_Equality"=>typeof(string).GetMethod(m.Name,new[]{typeof(string),typeof(string)})!,
 "CurrencyPocket.MiscFunctions"=>typeof(CurrencyPocket.MiscFunctions).GetMethod(m.Name)!,
 "CurrencyPocket.CurrencyPocket"=>typeof(CP).GetMethod(m.Name)!,
 _=>throw new Exception("Unexpected vendor call "+m.FullName)
};
foreach(var i in unsafeDebit.Body.Instructions)
{
 il.MarkLabel(labels[i]);var op=opmap[i.OpCode.Value];
 switch(i.Operand)
 {
  case null:il.Emit(op);break;
  case string s:il.Emit(op,s);break;
  case int v:il.Emit(op,v);break;
  case sbyte v:il.Emit(op,v);break;
  case Mono.Cecil.Cil.Instruction target:il.Emit(op,labels[target]);break;
  case Mono.Cecil.Cil.VariableDefinition local:il.Emit(op,locals[local.Index]);break;
  case MethodReference m:il.Emit(op,MapMethod(m));break;
  case FieldReference f when f.DeclaringType.FullName=="Player"&&f.Name=="m_localPlayer":il.Emit(op,typeof(Player).GetField(f.Name)!);break;
  default:throw new Exception("Unexpected actual vendor operand "+i);
 }
}
var actualLegacy=dm.CreateDelegate<Action<Inventory,string,int,int,bool>>();
Player PlayerWith(int carried,int wallet)
{
 var p=new Player();p.Inventory.Add(Plugin.Coins,carried);p.m_customData[Plugin.WalletKey]=wallet.ToString();Player.m_localPlayer=p;return p;
}
var old=PlayerWith(30,200);old.Inventory.RemoveItem(Plugin.Coins,100,-1,true);actualLegacy(old.Inventory,Plugin.Coins,100,-1,true);
Check(old.Inventory.CountItems(Plugin.Coins)==0&&Plugin.Balance(old)==100,"Real vendor IL reproduces 130-coin charge for 100 price (30 physical +100 wallet)");
Plugin.RefreshUi=CP.UpdatePocketUI;
Check(!Plugin.OriginalWalletDebitPatch.Prefix(),"Only old wallet debit body disabled");
foreach(int carried in new[]{0,1,30,100,200})foreach(int wallet in new[]{0,1,70,100,200})foreach(int cost in new[]{0,1,30,100,201})
{
 var p=PlayerWith(carried,wallet);var before=Plugin.BeforeRemoval(p.Inventory,Plugin.Coins,cost,-1,true);
 p.Inventory.RemoveItem(Plugin.Coins,cost,-1,true);Plugin.AfterRemoval(p.Inventory,Plugin.Coins,cost,-1,true,before,true);
 int expectedPaid=Math.Min(Math.Max(cost,0),carried+wallet);
 Check(p.Inventory.CountItems(Plugin.Coins)+Plugin.Balance(p)==carried+wallet-expectedPaid,"Physical+wallet total falls by requested available amount exactly once");
 Check(p.Inventory.Changes==1,"Native removal still executes once");
}
var mixed=PlayerWith(30,200);var state=Plugin.BeforeRemoval(mixed.Inventory,Plugin.Coins,100,-1,true);mixed.Inventory.RemoveItem(Plugin.Coins,100,-1,true);Plugin.AfterRemoval(mixed.Inventory,Plugin.Coins,100,-1,true,state,true);
Check(Plugin.Balance(mixed)==130&&mixed.Inventory.CountItems(Plugin.Coins)==0,"Mixed debit charges only remaining70 coins");
var physical=PlayerWith(200,200);state=Plugin.BeforeRemoval(physical.Inventory,Plugin.Coins,100,-1,true);physical.Inventory.RemoveItem(Plugin.Coins,100,-1,true);Plugin.AfterRemoval(physical.Inventory,Plugin.Coins,100,-1,true,state,true);Check(Plugin.Balance(physical)==200,"Fully carried payment leaves wallet unchanged");
var other=new Inventory();other.Add(Plugin.Coins,30);state=Plugin.BeforeRemoval(other,Plugin.Coins,50,-1,true);other.RemoveItem(Plugin.Coins,50,-1,true);Plugin.AfterRemoval(other,Plugin.Coins,50,-1,true,state,true);Check(Plugin.Balance(physical)==200,"Container removal cannot debit local wallet");
physical.Inventory.Add("$item_iron",10);state=Plugin.BeforeRemoval(physical.Inventory,"$item_iron",5,-1,true);physical.Inventory.RemoveItem("$item_iron",5,-1,true);Plugin.AfterRemoval(physical.Inventory,"$item_iron",5,-1,true,state,true);Check(Plugin.Balance(physical)==200,"Other materials untouched");
state=Plugin.BeforeRemoval(physical.Inventory,Plugin.Coins,50,-1,true);Plugin.AfterRemoval(physical.Inventory,Plugin.Coins,50,-1,true,state,false);Check(Plugin.Balance(physical)==200,"Skipped native call never charged");
state=Plugin.BeforeRemoval(physical.Inventory,Plugin.Coins,150,-1,true);Player.m_localPlayer=new Player();Plugin.AfterRemoval(physical.Inventory,Plugin.Coins,150,-1,true,state,true);Check(Plugin.Balance(physical)==200,"Switching characters cannot charge previous or next wallet");
var q=PlayerWith(0,100);q.Inventory.Add(Plugin.Coins,20,2,1);q.Inventory.Add(Plugin.Coins,30,1,0);Game.m_worldLevel=1;
state=Plugin.BeforeRemoval(q.Inventory,Plugin.Coins,50,2,true);q.Inventory.RemoveItem(Plugin.Coins,50,2,true);Plugin.AfterRemoval(q.Inventory,Plugin.Coins,50,2,true,state,true);Check(Plugin.Balance(q)==70&&q.Inventory.CountItems(Plugin.Coins,-1,false)==30,"Quality and world-level filters preserved");Game.m_worldLevel=0;
var foreign=PlayerWith(0,100);state=Plugin.BeforeRemoval(foreign.Inventory,Plugin.Coins,50,-1,true);foreign.m_customData[Plugin.WalletKey]="70";Plugin.AfterRemoval(foreign.Inventory,Plugin.Coins,50,-1,true,state,true);Check(Plugin.Balance(foreign)==50,"Already-paid wallet amount from another hook not charged twice");
foreach(var bad in new[]{"bad","-3","2147483648"}){foreign.m_customData[Plugin.WalletKey]=bad;Check(Plugin.Balance(foreign)==0&&foreign.m_customData[Plugin.WalletKey]==bad,"Invalid wallet is read defensively without rewriting save data");}
Check(PaymentPolicy.RemainingCharge(int.MaxValue,0,0,int.MaxValue,int.MaxValue)==int.MaxValue,"No arithmetic overflow at int boundary");

// Exercise exact public provider registration shape and the production delegates.
Chainloader.PluginInfos["Azumatt.CurrencyPocket"]=new(){Metadata=new(){Version=new System.Version(1,0,15)}};
Chainloader.PluginInfos["randyknapp.mods.epicloot"]=new(){Metadata=new(){Version=new System.Version(0,15,6)}};
Chainloader.PluginInfos["shudnal.ExtraSlots"]=new(){Metadata=new(){Version=new System.Version(1,2,23)}};
var pluginInstance=new Plugin();typeof(Plugin).GetMethod("Awake",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(pluginInstance,null);
Check(EpicLoot.API.Id==Plugin.Guid&&EpicLoot.API.Items==null&&EpicLoot.API.Exact==null,"Provider has no virtual items or exact-item/save handling");
var buyer=PlayerWith(30,200);Check(EpicLoot.API.Count!(Plugin.Coins)==200&&EpicLoot.API.Count("$item_iron")==0,"Provider counts only this character's coin balance");
int EpicPay(int price,int stash,Action? afterNative=null)
{
 Plugin.EpicRemovalPatch.Prefix(Plugin.Coins,price,out bool scope);
 try
 {
  int before=buyer.Inventory.CountItems(Plugin.Coins);var s=Plugin.BeforeRemoval(buyer.Inventory,Plugin.Coins,price,-1,true);buyer.Inventory.RemoveItem(Plugin.Coins,price,-1,true);Plugin.AfterRemoval(buyer.Inventory,Plugin.Coins,price,-1,true,s,true);
  int paid=before-buyer.Inventory.CountItems(Plugin.Coins);afterNative?.Invoke();paid+=Math.Min(stash,price-paid);
  return paid+EpicLoot.API.Remove!(Plugin.Coins,price-paid);
 }
 finally{Plugin.EpicRemovalPatch.Finalizer(scope);}
}
Check(EpicPay(100,20)==100&&Plugin.Balance(buyer)==150,"Epic keeps physical30 then stash20 then wallet50 payment order");Check(Plugin.EpicRemovalDepth==0,"Epic scope restored after payment");
buyer=PlayerWith(0,100);try{EpicPay(50,0,()=>throw new InvalidOperationException("provider failure"));throw new Exception("Expected propagated failure");}catch(InvalidOperationException){}
Check(Plugin.EpicRemovalDepth==0&&Plugin.Balance(buyer)==100,"Epic exception preserved, scope released, no premature wallet debit");
Plugin.EpicRemovalPatch.Prefix(Plugin.Coins,1,out bool outer);Plugin.EpicRemovalPatch.Prefix(Plugin.Coins,1,out bool inner);Check(Plugin.EpicRemovalDepth==2,"Nested Epic coin removal tracked");Plugin.EpicRemovalPatch.Finalizer(inner);Plugin.EpicRemovalPatch.Finalizer(outer);Check(Plugin.EpicRemovalDepth==0,"Nested scopes restored");
Plugin.EpicRemovalPatch.Prefix("$item_irontoken",1,out bool noScope);Check(!noScope&&Plugin.EpicRemovalDepth==0,"Epic tokens do not open coin scope");
Check(EpicLoot.API.Remove!("$item_irontoken",50)==0&&Plugin.Balance(buyer)==100,"Provider cannot spend tokens or other items");
Check(EpicLoot.API.Remove!(Plugin.Coins,0)==0&&EpicLoot.API.Remove!(Plugin.Coins,-10)==0,"Nonpositive provider requests ignored");

InventoryGui OpenWallet(Player p,int amount)
{
 Player.m_localPlayer=p;var temp=new Inventory();temp.Add(Plugin.Coins,Math.Min(Plugin.Balance(p),1000));
 var gui=new InventoryGui{m_splitItem=temp.Items[0],m_splitInventory=temp,m_splitDialog=new(){SliderValue=amount}};
 InventoryGui.instance=gui;CurrencyPocket.InventoryGuiOnSplitOkPatch.throwAwayInventory=temp;CP.CoinExtractionInProgress=true;
 Plugin.WithdrawalOpenPatch.Postfix();return gui;
}
void ConfirmWallet(InventoryGui gui)=>Check(!Plugin.WithdrawalConfirmPatch.Prefix(gui),"Only legacy wallet prefix replaced; native split continuation kept");
var withdrawing=PlayerWith(0,200);withdrawing.Inventory.Items.Clear();var withdrawGui=OpenWallet(withdrawing,80);ConfirmWallet(withdrawGui);
Check(Plugin.Balance(withdrawing)==120&&withdrawing.Inventory.CountItems(Plugin.Coins)==80,"Confirmed withdrawal immediately materializes coins in real inventory");
Check(withdrawGui.m_splitItem==null&&withdrawGui.m_splitInventory==null&&CurrencyPocket.InventoryGuiOnSplitOkPatch.throwAwayInventory==null,"Native continuation cannot start a phantom temporary drag");
Check(withdrawing.Inventory.NormalAddCalls==0&&withdrawing.Inventory.CellAddCalls==1,"Withdrawal uses only reviewed explicit-cell native add; no backpack auto-store entry");
Check(withdrawing.Inventory.LastFindTopFirst==true&&withdrawing.Inventory.Items.All(i=>i.m_gridPos.y<7),"Top-first selector uses ordinary free cells even before ExtraSlots has narrowed native height");
// Clearing/hiding a GUI or saving a character can no longer discard issued coins:
// the only authoritative state is native inventory + existing player customData.
InventoryGui.instance=null;Check(Plugin.Balance(withdrawing)+withdrawing.Inventory.CountItems(Plugin.Coins)==200,"Hide/logout/save boundary has no pending money outside serialized character state");
var cancel=PlayerWith(0,200);var cancelledGui=OpenWallet(cancel,50);cancelledGui.m_splitInventory=cancel.Inventory;cancel.Inventory.Add(Plugin.Coins,10);cancelledGui.m_splitItem=cancel.Inventory.Items.Last();
var ordinaryItem=cancelledGui.m_splitItem;ConfirmWallet(cancelledGui);Check(cancelledGui.m_splitItem==ordinaryItem&&cancelledGui.m_splitInventory==cancel.Inventory&&Plugin.Balance(cancel)==200,"Cancelled wallet preview cannot hijack a later ordinary coin split");
var changing=PlayerWith(0,200);var changingGui=OpenWallet(changing,50);var nextCharacter=PlayerWith(0,300);ConfirmWallet(changingGui);Check(Plugin.Balance(changing)==200&&Plugin.Balance(nextCharacter)==300&&nextCharacter.Inventory.CountItems(Plugin.Coins)==0,"Stale preview cannot withdraw another character's money");
var full=PlayerWith(0,5000);full.Inventory.Items.Clear();for(int i=0;i<56;i++)full.Inventory.Add("$item_wood",1);var fullGui=OpenWallet(full,1000);ConfirmWallet(fullGui);
Check(Plugin.Balance(full)==5000&&full.Inventory.CountItems(Plugin.Coins)==0&&full.Inventory.CellAddCalls==0,"Full56 ordinary slots with free equipment/food row: no withdrawal, debit or reserved-slot placement");
var funded=PlayerWith(0,5000);funded.Inventory.Items.Clear();for(int i=0;i<49;i++)funded.Inventory.Add("$item_wood",1);
for(int i=0;i<5;i++)ConfirmWallet(OpenWallet(funded,1000));
Check(Plugin.Balance(funded)==0&&funded.Inventory.CountItems(Plugin.Coins)==5000&&funded.Inventory.Items.Where(i=>i.m_shared.m_name==Plugin.Coins).All(i=>i.m_gridPos.y<7),"5000 coins withdraw in five stack-sized operations into seven free ordinary slots");
var topped=PlayerWith(30,200);for(int i=1;i<56;i++)topped.Inventory.Add("$item_wood",1);ConfirmWallet(OpenWallet(topped,100));Check(topped.Inventory.CountItems(Plugin.Coins)==130&&Plugin.Balance(topped)==100&&topped.Inventory.Items.Count==56,"Existing normal coin stack can be topped up even with no free ordinary cell");
var reject=PlayerWith(0,200);reject.Inventory.Items.Clear();reject.Inventory.RejectCellAdd=true;ConfirmWallet(OpenWallet(reject,50));Check(Plugin.Balance(reject)==200&&reject.Inventory.CountItems(Plugin.Coins)==0,"Native rejected placement refunds entire reservation");
var partial=PlayerWith(0,200);partial.Inventory.Items.Clear();partial.Inventory.PartialLimit=30;ConfirmWallet(OpenWallet(partial,100));Check(Plugin.Balance(partial)==170&&partial.Inventory.CountItems(Plugin.Coins)==30,"Unexpected partial native placement charges actual30 only");
var exceptional=PlayerWith(0,200);exceptional.Inventory.Items.Clear();exceptional.Inventory.PartialLimit=20;exceptional.Inventory.ThrowAfterCellAdd=true;var exceptionGui=OpenWallet(exceptional,100);
try{Plugin.WithdrawalConfirmPatch.Prefix(exceptionGui);throw new Exception("Expected native error");}catch(InvalidOperationException){}
Check(Plugin.Balance(exceptional)==180&&exceptional.Inventory.CountItems(Plugin.Coins)==20&&exceptionGui.m_splitItem==null,"Native callback error remains visible; granted20 charged once and preview cleared");
exceptional.Inventory.ThrowAfterCellAdd=false;ConfirmWallet(exceptionGui);Check(Plugin.Balance(exceptional)==180&&exceptional.Inventory.CountItems(Plugin.Coins)==20,"Stale confirmation after failure cannot mint or debit again");
var bankOnly=PlayerWith(0,100);bankOnly.Inventory.Items.Clear();var impossibleGui=OpenWallet(bankOnly,5000);ConfirmWallet(impossibleGui);Check(Plugin.Balance(bankOnly)==100&&bankOnly.Inventory.CellAddCalls==0,"Amounts above preview or wallet are rejected without AddItem(0)");
Player.m_localPlayer=null;Check(EpicLoot.API.Count!(Plugin.Coins)==0&&EpicLoot.API.Remove!(Plugin.Coins,50)==0,"Dedicated server with no local player no-ops");
typeof(Plugin).GetMethod("OnDestroy",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(pluginInstance,null);Check(EpicLoot.API.Id==null,"Provider unregisters on unload");

// TradersExtended pays physical item references first and writes only the
// planned pocket remainder. Its real IL below proves that the string hook is
// never entered on this path; the fixture includes both overloads and rollback.
using var te=AssemblyDefinition.ReadAssembly(Path.Combine(research,"extracted/shudnal-TradersExtended-2.0.6/TradersExtended.dll"));
var trade=Types(te.MainModule.Types).Single(t=>t.FullName=="TradersExtended.TradeInventory");
var teRemove=trade.Methods.Single(m=>m.Name=="Remove");
var teRemoveCalls=teRemove.Body.Instructions.Where(i=>i.Operand is MethodReference m&&m.DeclaringType.FullName=="Inventory"&&m.Name=="RemoveItem").Select(i=>(MethodReference)i.Operand).ToArray();
Check(teRemoveCalls.Length==1&&teRemoveCalls[0].Parameters.Select(p=>p.ParameterType.FullName).SequenceEqual(new[]{"ItemDrop/ItemData","System.Int32"}),"Actual TE removal uses only ItemData,int overload; no CurrencyPocket string hook");
var teSpend=trade.Methods.Single(m=>m.Name=="SpendPayment");var spendCalls=teSpend.Body.Instructions.Where(i=>i.Operand is MethodReference).Select(i=>(MethodReference)i.Operand).ToArray();
Check(Array.FindIndex(spendCalls,m=>m.Name=="Remove")<Array.FindIndex(spendCalls,m=>m.Name=="TrySpend"),"Actual TE physical removal precedes explicit pocket remainder");
var snapshot=Types(te.MainModule.Types).Single(t=>t.FullName=="TradersExtended.TradeInventory/Snapshot");
Check(snapshot.Methods.Single(m=>m.Name==".ctor").Body.Instructions.Any(i=>i.Operand is string s&&s==Plugin.WalletKey)&&snapshot.Methods.Single(m=>m.Name=="Dispose").Body.Instructions.Any(i=>i.Operand is string s&&s==Plugin.WalletKey),"Actual TE snapshot captures/restores pocket balance for rollback");
bool TePayment(Player p,int price,bool failDelivery)
{
 Player.m_localPlayer=p;var inv=p.Inventory;var items=inv.Items.ToArray();var stacks=items.Select(x=>x.m_stack).ToArray();int wallet=Plugin.Balance(p),physical=Math.Min(price,inv.CountItems(Plugin.Coins)),pocket=price-physical;
 if(wallet<pocket)return false;
 int left=physical;foreach(var item in items.Where(i=>i.m_shared.m_name==Plugin.Coins)) {int take=Math.Min(left,item.m_stack);if(take>0&&!inv.RemoveItem(item,take))throw new Exception("Fixture native payment rejected");left-=take;}
 if(pocket>0)p.m_customData[Plugin.WalletKey]=(wallet-pocket).ToString();
 if(failDelivery){inv.Items.Clear();inv.Items.AddRange(items);for(int i=0;i<items.Length;i++)items[i].m_stack=stacks[i];p.m_customData[Plugin.WalletKey]=wallet.ToString();return false;}
 return true;
}
var teBuyer=PlayerWith(30,200);Check(TePayment(teBuyer,100,false)&&Plugin.Balance(teBuyer)==130&&teBuyer.Inventory.CountItems(Plugin.Coins)==0,"TE mixed payment with our installed overload hook routing charges100 exactly once");
Check(teBuyer.Inventory.StringRemoveCalls==0&&teBuyer.Inventory.ExactRemoveCalls==1,"TE payment never dispatches our native string prefix/postfix");
teBuyer=PlayerWith(30,200);Check(!TePayment(teBuyer,100,true)&&Plugin.Balance(teBuyer)==200&&teBuyer.Inventory.CountItems(Plugin.Coins)==30,"TE failed delivery rolls back physical and pocket funds");
teBuyer=PlayerWith(30,20);Check(!TePayment(teBuyer,100,false)&&Plugin.Balance(teBuyer)==20&&teBuyer.Inventory.CountItems(Plugin.Coins)==30,"TE insufficient mixed funds leave both sources untouched");

using(var zip=ZipFile.OpenRead(Path.Combine(root,"updates/20261104-mod-migration/downloads/RandyKnapp-EpicLoot-0.15.6.zip")))
using(var buffer=new MemoryStream())
{
 using(var input=zip.Entries.Single(e=>e.FullName.EndsWith("EpicLoot.dll",StringComparison.Ordinal)).Open())input.CopyTo(buffer);buffer.Position=0;
 using var epic=AssemblyDefinition.ReadAssembly(buffer);var api=Types(epic.MainModule.Types).Single(t=>t.FullName=="EpicLoot.API");
 var register=api.Methods.Single(m=>m.Name=="RegisterInventoryProvider");Check(register.IsPublic&&register.IsStatic&&register.Parameters.Count==5&&register.ReturnType.FullName=="System.Boolean","Actual public Epic provider registration contract");
 Check(register.Parameters[1].ParameterType.FullName.Contains("System.Func`1<System.Collections.Generic.List`1<ItemDrop/ItemData>>")&&register.Parameters[2].ParameterType.FullName=="System.Func`2<System.String,System.Int32>"&&register.Parameters[3].ParameterType.FullName=="System.Func`3<System.String,System.Int32,System.Int32>","Actual Epic count/remove delegate shapes match production reflection registration");
 var epicRemove=Types(epic.MainModule.Types).Single(t=>t.FullName=="EpicLoot_UnityLib.InventoryManagement").Methods.Single(m=>m.Name=="RemoveItem"&&m.Parameters[0].ParameterType.FullName=="System.String");
 var calls=epicRemove.Body.Instructions.Where(i=>i.Operand is MethodReference).Select(i=>(MethodReference)i.Operand).ToArray();
 Check(calls.Count(m=>m.DeclaringType.FullName=="Inventory"&&m.Name=="CountItems")==2&&calls.Count(m=>m.DeclaringType.FullName=="Inventory"&&m.Name=="RemoveItem")==1,"Actual Epic measures carried coins around one native string removal");
 Check(Array.FindIndex(calls,m=>m.Name=="TakeBySharedName")<Array.FindIndex(calls,m=>m.Name=="RemoveProviderItems"),"Actual Epic stash-before-provider order preserved by scope");
}

// Inspect compiled adapter against the real client and dedicated-server APIs.
var dll=Path.Combine(root,"launcher/gameplay/CurrencyPocketSafety/bin/Release/netstandard2.1/CurrencyPocketSafety.dll");
foreach(var managed in new[]{Path.Combine(root,"management/BakaTrial/runtime/valheim_server_Data/Managed"),Path.GetFullPath(args[1])})
{
 using var resolver=new DefaultAssemblyResolver();resolver.AddSearchDirectory(managed);resolver.AddSearchDirectory(Path.Combine(root,"management/BakaTrial/runtime/BepInEx/core"));resolver.AddSearchDirectory(Path.Combine(root,"management/BakaTrial/runtime/BepInEx/plugins/ValheimModding-Jotunn"));
 using var game=AssemblyDefinition.ReadAssembly(Path.Combine(managed,"assembly_valheim.dll"),new(){AssemblyResolver=resolver});using var production=AssemblyDefinition.ReadAssembly(dll,new(){AssemblyResolver=resolver});
 var inv=game.MainModule.Types.Single(t=>t.Name=="Inventory");Check(inv.Methods.Any(m=>m.Name=="RemoveItem"&&m.Parameters.Select(p=>p.ParameterType.FullName).SequenceEqual(new[]{"System.String","System.Int32","System.Int32","System.Boolean"})&&m.ReturnType.FullName=="System.Void"),"Exact native RemoveItem target exists");
 Check(inv.Methods.Any(m=>m.Name=="AddItem"&&m.Parameters.Select(p=>p.ParameterType.FullName).SequenceEqual(new[]{"ItemDrop/ItemData","System.Int32","System.Int32","System.Int32","System.Boolean"})&&m.ReturnType.FullName=="System.Boolean"),"Exact private explicit-cell AddItem target exists");
 Check(inv.Methods.Any(m=>m.Name=="FindEmptySlot"&&m.Parameters.Count==1&&m.Parameters[0].ParameterType.FullName=="System.Boolean"&&m.ReturnType.FullName=="Vector2i"),"Exact private native empty-cell selector exists");
 var gui=game.MainModule.Types.Single(t=>t.Name=="InventoryGui");Check(gui.Fields.Any(f=>f.Name=="m_splitItem"&&f.FieldType.FullName=="ItemDrop/ItemData")&&gui.Fields.Any(f=>f.Name=="m_splitInventory"&&f.FieldType.FullName=="Inventory"),"Native split input fields pinned");
 var prefabAdd=inv.Methods.Single(m=>m.Name=="AddItem"&&m.Parameters.Select(p=>p.ParameterType.FullName).SequenceEqual(new[]{"UnityEngine.GameObject","System.Int32"}));
 Check(prefabAdd.Body.Instructions.Any(i=>i.Operand is FieldReference f&&f.Name=="m_maxStackSize")&&prefabAdd.Body.Instructions.Any(i=>i.Operand is MethodReference m&&m.DeclaringType.FullName=="UnityEngine.Mathf"&&m.Name=="Min")&&prefabAdd.Body.Instructions.Any(i=>i.OpCode.Code==Mono.Cecil.Cil.Code.Stfld&&i.Operand is FieldReference f&&f.Name=="m_stack"),"Native prefab preview clamps requested balance to current max stack before opening split");
 var showSplit=gui.Methods.Single(m=>m.Name=="ShowSplitDialog");Check(showSplit.Body.Instructions.Any(i=>i.Operand is FieldReference f&&f.Name=="m_stack")&&showSplit.Body.Instructions.Any(i=>i.Operand is MethodReference m&&m.DeclaringType.FullName=="SplitDialog"&&m.Name=="UpdateLimits"),"Native split limits read the already stack-clamped preview amount");
 foreach(var member in production.MainModule.GetMemberReferences().Where(m=>m.DeclaringType.Scope.Name is "assembly_valheim" or "BepInEx" or "0Harmony" or "Jotunn" or "UnityEngine.CoreModule"))
 {
  try{Check(member switch{MethodReference m=>m.Resolve()!=null,FieldReference f=>f.Resolve()!=null,_=>true},"Production dependency resolves: "+member.FullName);}catch(Exception e){throw new Exception(member.FullName,e);}
 }
 var methods=Types(production.MainModule.Types).SelectMany(t=>t.Methods).ToArray();Check(!methods.Any(m=>m.Name is "Update" or "FixedUpdate" or "LateUpdate"),"No polling loop in adapter");
 Check(Types(production.MainModule.Types).Single(t=>t.Name=="WithdrawalConfirmPatch").Methods.Single(m=>m.Name=="Prefix").Parameters.Single().Name=="__0","Static vendor prefix argument uses positional Harmony binding instead of target-instance injection");
 Check(!methods.Where(m=>m.HasBody).SelectMany(m=>m.Body.Instructions).Any(i=>i.Operand is MethodReference m&&(m.DeclaringType.FullName.Contains("ZNet")||m.Name is "Save" or "Load" or "Destroy")),"No RPC, game destruction or save hooks");
}
var result=new{status="PASS",checks,actualVendorIlExecuted=true,legacyDoubleChargeReproduced=true,pluginSha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(dll))).ToLowerInvariant(),currencyPocketSha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(cpPath))).ToLowerInvariant(),gameStarted=false,liveHarmonyTested=false};
File.WriteAllText(Path.Combine(research,"safety-checks.json"),JsonSerializer.Serialize(result,new JsonSerializerOptions{WriteIndented=true})+"\n");Console.WriteLine(JsonSerializer.Serialize(result));
public static class ReferencePolicy{public static bool Equal(object? a,object? b)=>ReferenceEquals(a,b);}
