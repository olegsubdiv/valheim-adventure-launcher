namespace AdventureRpg.CurrencyPocketSafety;

internal static class PaymentPolicy
{
    internal static int RemainingCharge(int amount, int carriedBefore, int carriedAfter, int walletBefore, int walletNow)
    {
        if (amount <= 0 || walletNow <= 0) return 0;
        long carriedPaid = Math.Max(0L, (long)carriedBefore - carriedAfter);
        long walletAlreadyPaid = Math.Max(0L, (long)walletBefore - walletNow);
        long remaining = Math.Max(0L, (long)amount - carriedPaid - walletAlreadyPaid);
        return (int)Math.Min(walletNow, remaining);
    }
}
