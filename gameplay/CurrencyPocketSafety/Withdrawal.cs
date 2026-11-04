using System.Globalization;
using System.Reflection;
using HarmonyLib;
using ItemData = ItemDrop.ItemData;

namespace AdventureRpg.CurrencyPocketSafety;

internal static class Withdrawal
{
    private static FieldInfo splitItem = null!, splitInventory = null!, previewInventory = null!, legacyPending = null!;
    private static Func<Inventory, bool, Vector2i> findEmpty = null!;
    private static Func<Inventory, ItemData, int, int, int, bool, bool> addToCell = null!;
    private static Func<int> ordinaryHeight = null!;
    private static Player? owner;
    private static InventoryGui? previewGui;
    private static Inventory? preview;
    private static ItemData? previewItem;

    internal static void Initialize()
    {
        splitItem = RequiredField(typeof(InventoryGui), "m_splitItem", typeof(ItemData), false);
        splitInventory = RequiredField(typeof(InventoryGui), "m_splitInventory", typeof(Inventory), false);
        previewInventory = RequiredField(AccessTools.TypeByName("CurrencyPocket.InventoryGuiOnSplitOkPatch"), "throwAwayInventory", typeof(Inventory), true);
        legacyPending = RequiredField(AccessTools.TypeByName("CurrencyPocket.CurrencyPocket"), "CoinExtractionInProgress", typeof(bool), true);
        var find = AccessTools.DeclaredMethod(typeof(Inventory), "FindEmptySlot", new[] { typeof(bool) });
        var add = AccessTools.DeclaredMethod(typeof(Inventory), "AddItem", new[] { typeof(ItemData), typeof(int), typeof(int), typeof(int), typeof(bool) });
        if (find == null || find.ReturnType != typeof(Vector2i) || add == null || add.ReturnType != typeof(bool))
            throw new NotSupportedException("Unsupported native inventory cell placement API.");
        findEmpty = (Func<Inventory, bool, Vector2i>)find.CreateDelegate(typeof(Func<Inventory, bool, Vector2i>));
        addToCell = (Func<Inventory, ItemData, int, int, int, bool, bool>)add.CreateDelegate(typeof(Func<Inventory, ItemData, int, int, int, bool, bool>));
        var height = AccessTools.TypeByName("ExtraSlots.Slots")?.GetProperty("InventoryHeightPlayer", BindingFlags.Public | BindingFlags.Static)?.GetGetMethod();
        if (height == null || height.ReturnType != typeof(int)) throw new NotSupportedException("Unsupported ExtraSlots ordinary inventory geometry API.");
        ordinaryHeight = (Func<int>)height.CreateDelegate(typeof(Func<int>));
    }

    private static FieldInfo RequiredField(Type? type, string name, Type expected, bool isStatic)
    {
        var field = type == null ? null : AccessTools.Field(type, name);
        if (field == null || field.FieldType != expected || field.IsStatic != isStatic)
            throw new NotSupportedException("Unsupported wallet field " + name);
        return field;
    }

    internal static void CapturePreview()
    {
        var gui = InventoryGui.instance;
        var temp = previewInventory.GetValue(null) as Inventory;
        var item = gui == null ? null : splitItem.GetValue(gui) as ItemData;
        owner = Player.m_localPlayer;
        previewGui = gui;
        preview = temp;
        previewItem = item;
    }

    internal static void ConfirmPreview(InventoryGui gui)
    {
        var temp = previewInventory.GetValue(null) as Inventory;
        var item = splitItem.GetValue(gui) as ItemData;
        var input = splitInventory.GetValue(gui) as Inventory;
        legacyPending.SetValue(null, false);
        // A cancelled wallet dialog must never hijack a later regular split.
        if (temp == null || input != temp || item?.m_shared?.m_name != Plugin.Coins || !temp.ContainsItem(item)) return;
        try
        {
            float slider = gui.m_splitDialog.SliderValue;
            if (owner == null || owner != Player.m_localPlayer || gui != previewGui || temp != preview || item != previewItem ||
                float.IsNaN(slider) || float.IsInfinity(slider) || slider < 1 || slider > int.MaxValue) return;
            int amount = (int)slider;
            if (amount <= 0 || amount > item.m_stack || amount > Plugin.Balance(owner)) return;
            PlaceInRealInventory(owner, item, amount);
        }
        finally
        {
            // Native OnSplitOk receives no virtual drag. Issued money is already
            // in the real inventory; Cancel/Hide/logout need no refund or journal.
            splitItem.SetValue(gui, null);
            splitInventory.SetValue(gui, null);
            previewInventory.SetValue(null, null);
            owner = null; previewGui = null; preview = null; previewItem = null;
        }
    }

    internal static int PlaceInRealInventory(Player player, ItemData template, int amount)
    {
        if (player == null || player != Player.m_localPlayer || amount <= 0 || template?.m_shared?.m_name != Plugin.Coins ||
            amount > template.m_shared.m_maxStackSize || amount > Plugin.Balance(player)) return 0;
        var inventory = player.GetInventory();
        int height = ordinaryHeight(), width = inventory.GetWidth();
        var stack = inventory.GetAllItems().FirstOrDefault(i => i.m_gridPos.x >= 0 && i.m_gridPos.x < width && i.m_gridPos.y >= 0 && i.m_gridPos.y < height &&
            i.IsSameType(template) && (long)i.m_shared.m_maxStackSize - i.m_stack >= amount);
        Vector2i cell = stack != null ? stack.m_gridPos : findEmpty(inventory, true);
        // ExtraSlots may return an equipment/food/quick slot as a fallback.
        // Withdrawals use ordinary inventory cells only.
        if (cell.x < 0 || cell.x >= width || cell.y < 0 || cell.y >= height) return 0;
        var transfer = template.Clone(); transfer.m_stack = amount;
        int before = inventory.CountItems(Plugin.Coins, -1, false), wallet = Plugin.Balance(player);
        // Reserve the exact request before native Changed callbacks can observe
        // the new physical coins. Refund only an unplaced remainder, including
        // an exceptional native return; never replay the transfer automatically.
        player.m_customData[Plugin.WalletKey] = (wallet - amount).ToString(CultureInfo.InvariantCulture);
        int placed = 0;
        try { addToCell(inventory, transfer, amount, cell.x, cell.y, false); }
        finally
        {
            placed = (int)Math.Min(amount, Math.Max(0L, (long)inventory.CountItems(Plugin.Coins, -1, false) - before));
            int refund = amount - placed;
            if (refund > 0) player.m_customData[Plugin.WalletKey] = Math.Min(int.MaxValue, (long)Plugin.Balance(player) + refund).ToString(CultureInfo.InvariantCulture);
            Plugin.RefreshUi();
        }
        return placed;
    }
}
