using System.Globalization;
using System.Reflection;
using BepInEx;
using BepInEx.Bootstrap;
using HarmonyLib;
using Jotunn.Utils;

namespace AdventureRpg.CurrencyPocketSafety;

[BepInPlugin(Guid, "Adventure RPG - Currency Pocket Safety", "1.0.0")]
[BepInDependency("com.jotunn.jotunn")]
[BepInDependency("Azumatt.CurrencyPocket", "1.0.15")]
[BepInDependency("randyknapp.mods.epicloot", "0.15.6")]
[BepInDependency("shudnal.ExtraSlots", "1.2.23")]
[NetworkCompatibility(CompatibilityLevel.EveryoneMustHaveMod, VersionStrictness.Patch)]
public sealed class Plugin : BaseUnityPlugin
{
    public const string Guid = "arpg.currencypocketsafety";
    internal const string Coins = "$item_coins";
    internal const string WalletKey = "CoinPocket_CoinCount";
    private Harmony? harmony;
    internal static Action RefreshUi = () => { };
    [ThreadStatic] internal static int EpicRemovalDepth;
    private MethodInfo? unregisterProvider;
    private bool providerRegistered;

    private void Awake()
    {
        if (!Chainloader.PluginInfos.TryGetValue("Azumatt.CurrencyPocket", out var info) || info.Metadata.Version != new System.Version(1, 0, 15))
            throw new NotSupportedException("Currency Pocket Safety requires reviewed CurrencyPocket 1.0.15.");
        if (!Chainloader.PluginInfos.TryGetValue("randyknapp.mods.epicloot", out var epic) || epic.Metadata.Version != new System.Version(0, 15, 6))
            throw new NotSupportedException("Currency Pocket Safety requires reviewed EpicLoot 0.15.6.");
        if (!Chainloader.PluginInfos.TryGetValue("shudnal.ExtraSlots", out var slots) || slots.Metadata.Version != new System.Version(1, 2, 23))
            throw new NotSupportedException("Currency Pocket Safety requires reviewed ExtraSlots 1.2.23.");
        var ui = AccessTools.DeclaredMethod(AccessTools.TypeByName("CurrencyPocket.CurrencyPocket"), "UpdatePocketUI");
        if (ui == null || !ui.IsStatic || ui.ReturnType != typeof(void) || ui.GetParameters().Length != 0)
            throw new NotSupportedException("Unsupported CurrencyPocket UI refresh API.");
        RefreshUi = (Action)ui.CreateDelegate(typeof(Action));
        Withdrawal.Initialize();
        harmony = new Harmony(Guid);
        try
        {
            harmony.PatchAll(typeof(Plugin).Assembly);
            var api = AccessTools.TypeByName("EpicLoot.API");
            var register = api == null ? null : AccessTools.DeclaredMethod(api, "RegisterInventoryProvider", new[]
            {
                typeof(string), typeof(Func<List<ItemDrop.ItemData>>), typeof(Func<string, int>),
                typeof(Func<string, int, int>), typeof(Func<ItemDrop.ItemData, int, int>)
            });
            unregisterProvider = api == null ? null : AccessTools.DeclaredMethod(api, "UnregisterInventoryProvider", new[] { typeof(string) });
            if (register == null || unregisterProvider == null || register.ReturnType != typeof(bool) || unregisterProvider.ReturnType != typeof(bool))
                throw new NotSupportedException("Unsupported EpicLoot inventory provider API.");
            // No virtual items and no save handler: only the existing wallet's
            // coin balance participates in EpicLoot's normal payment order.
            providerRegistered = register.Invoke(null, new object?[] { Guid, null, (Func<string, int>)CountProviderCoins,
                (Func<string, int, int>)SpendProviderCoins, null }) is true;
            if (!providerRegistered) throw new InvalidOperationException("EpicLoot rejected CurrencyPocket coin provider.");
        }
        catch { harmony.UnpatchSelf(); throw; }
        Logger.LogInfo("Currency Pocket Safety: measured coin payment, immediate real-inventory withdrawal and coin-only EpicLoot provider enabled.");
    }

    private void OnDestroy()
    {
        try { if (providerRegistered) unregisterProvider!.Invoke(null, new object[] { Guid }); }
        finally { providerRegistered = false; harmony?.UnpatchSelf(); }
    }

    internal static int Balance(Player? player)
    {
        return player != null && player.m_customData.TryGetValue(WalletKey, out var text) &&
            int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? Math.Max(0, value) : 0;
    }

    internal static int CountProviderCoins(string item) => item == Coins ? Balance(Player.m_localPlayer) : 0;

    internal static int SpendProviderCoins(string item, int amount)
    {
        var player = Player.m_localPlayer;
        if (item != Coins || amount <= 0 || player == null) return 0;
        int balance = Balance(player), paid = Math.Min(balance, amount);
        if (paid == 0) return 0;
        player.m_customData[WalletKey] = (balance - paid).ToString(CultureInfo.InvariantCulture);
        RefreshUi();
        return paid;
    }

    internal readonly struct PaymentState
    {
        internal readonly Player? Player;
        internal readonly int Carried;
        internal readonly int Wallet;
        internal PaymentState(Player player, int carried, int wallet) { Player = player; Carried = carried; Wallet = wallet; }
    }

    internal static PaymentState BeforeRemoval(Inventory inventory, string name, int amount, int quality, bool worldLevelBased)
    {
        var player = Player.m_localPlayer;
        if (EpicRemovalDepth != 0 || amount <= 0 || name != Coins || player == null || inventory != player.GetInventory()) return default;
        return new PaymentState(player, inventory.CountItems(name, quality, worldLevelBased), Balance(player));
    }

    internal static void AfterRemoval(Inventory inventory, string name, int amount, int quality, bool worldLevelBased, PaymentState state, bool ranOriginal)
    {
        var player = state.Player;
        if (!ranOriginal || EpicRemovalDepth != 0 || player == null || player != Player.m_localPlayer || name != Coins || inventory != player.GetInventory()) return;
        int balance = Balance(player);
        int charge = PaymentPolicy.RemainingCharge(amount, state.Carried, inventory.CountItems(name, quality, worldLevelBased), state.Wallet, balance);
        if (charge == 0) return;
        player.m_customData[WalletKey] = (balance - charge).ToString(CultureInfo.InvariantCulture);
        RefreshUi();
    }

    [HarmonyPatch(typeof(Inventory), nameof(Inventory.RemoveItem), new[] { typeof(string), typeof(int), typeof(int), typeof(bool) })]
    internal static class PaymentPatch
    {
        [HarmonyPriority(Priority.First)]
        internal static void Prefix(Inventory __instance, string name, int amount, int itemQuality, bool worldLevelBased, out PaymentState __state)
            => __state = BeforeRemoval(__instance, name, amount, itemQuality, worldLevelBased);

        [HarmonyPriority(Priority.Last)]
        internal static void Postfix(Inventory __instance, string name, int amount, int itemQuality, bool worldLevelBased, PaymentState __state, bool __runOriginal)
            => AfterRemoval(__instance, name, amount, itemQuality, worldLevelBased, __state, __runOriginal);
    }

    [HarmonyPatch]
    internal static class OriginalWalletDebitPatch
    {
        internal static MethodBase TargetMethod()
        {
            var type = AccessTools.TypeByName("CurrencyPocket.CurrencyPocket+Inventory_RemoveItem_Patch");
            var method = type == null ? null : AccessTools.DeclaredMethod(type, "Postfix");
            if (method == null || !method.IsStatic || method.ReturnType != typeof(void) || !method.GetParameters().Select(p => p.ParameterType)
                .SequenceEqual(new[] { typeof(Inventory), typeof(string), typeof(int), typeof(int), typeof(bool) }))
                throw new NotSupportedException("Unsupported CurrencyPocket removal postfix.");
            return method;
        }

        // The native inventory call still runs. Only CurrencyPocket's duplicate
        // full-amount debit is replaced by the measured remaining charge above.
        internal static bool Prefix() => false;
    }

    [HarmonyPatch]
    internal static class EpicRemovalPatch
    {
        internal static MethodBase TargetMethod()
        {
            var type = AccessTools.TypeByName("EpicLoot_UnityLib.InventoryManagement");
            var method = type == null ? null : AccessTools.DeclaredMethod(type, "RemoveItem", new[] { typeof(string), typeof(int) });
            if (method == null || method.IsStatic || method.ReturnType != typeof(int))
                throw new NotSupportedException("Unsupported EpicLoot inventory removal API.");
            return method;
        }

        internal static void Prefix(string item, int amount, out bool __state)
        {
            __state = item == Coins && amount > 0 && Player.m_localPlayer != null;
            if (__state) EpicRemovalDepth++;
        }

        // Void finalizer leaves the original result/exception intact, including
        // failures from other providers. Nested payments restore their own scope.
        internal static void Finalizer(bool __state) { if (__state) EpicRemovalDepth--; }
    }

    [HarmonyPatch]
    internal static class WithdrawalOpenPatch
    {
        internal static MethodBase TargetMethod()
        {
            var type = AccessTools.TypeByName("CurrencyPocket.MiscFunctions");
            var method = type == null ? null : AccessTools.DeclaredMethod(type, "ExtractCoins");
            if (method == null || !method.IsStatic || method.ReturnType != typeof(void) || method.GetParameters().Length != 0)
                throw new NotSupportedException("Unsupported CurrencyPocket withdrawal button API.");
            return method;
        }
        internal static void Postfix() => Withdrawal.CapturePreview();
    }

    [HarmonyPatch]
    internal static class WithdrawalConfirmPatch
    {
        internal static MethodBase TargetMethod()
        {
            var type = AccessTools.TypeByName("CurrencyPocket.InventoryGuiOnSplitOkPatch");
            var method = type == null ? null : AccessTools.DeclaredMethod(type, "Prefix");
            if (method == null || !method.IsStatic || method.ReturnType != typeof(void) || !method.GetParameters()
                .Select(p => p.ParameterType).SequenceEqual(new[] { typeof(InventoryGui) }))
                throw new NotSupportedException("Unsupported CurrencyPocket withdrawal confirmation API.");
            return method;
        }
        internal static bool Prefix(InventoryGui __0)
        {
            // This patches a static Harmony prefix whose argument is itself
            // named __instance. Use positional binding, not Harmony's special
            // target-instance injection (the target here is static).
            Withdrawal.ConfirmPreview(__0);
            // Skip only CurrencyPocket's old debit-to-temporary-drag prefix.
            // The native OnSplitOk still closes its dialog normally.
            return false;
        }
    }
}
