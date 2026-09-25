using ShopManager.Domain.Entities;
using ShopManager.Domain.Enums;

namespace ShopManager.Domain.Services;

/// <summary>
/// محاسبه موجودی صندوق و مانده‌های نسیه
/// </summary>
public static class CashboxCalculator
{
    /// <summary>
    /// موجودی صندوق = سرمایه اولیه
    ///                 + فروش‌های نقدی − برگشت فروش نقدی
    ///                 − خریدهای نقدی + برگشت خرید نقدی
    ///                 + ورودی‌های دفتر − خروجی‌های دفتر
    /// </summary>
    public static decimal CalculateCashbox(
        decimal initialCapital,
        IEnumerable<Sale> allSales,
        IEnumerable<Purchase> allPurchases,
        IEnumerable<CashLedger> ledger)
    {
        // فروش نقدی خالص
        decimal cashSalesNet = allSales
            .Where(s => s.PaymentStatus == PaymentStatus.Cash)
            .Sum(s => s.EntryType == EntryType.Normal ? s.Revenue : -s.Revenue);

        // خرید نقدی خالص
        decimal cashPurchasesNet = allPurchases
            .Where(p => p.PaymentStatus == PaymentStatus.Cash)
            .Sum(p => p.EntryType == EntryType.Normal ? p.TotalCost : -p.TotalCost);

        // ورود و خروج دفتر صندوق
        decimal ledgerIn = ledger.Sum(l => l.AmountIn ?? 0);
        decimal ledgerOut = ledger.Sum(l => l.AmountOut ?? 0);

        return initialCapital + cashSalesNet - cashPurchasesNet + ledgerIn - ledgerOut;
    }

    /// <summary>جمع طلب از مشتری‌ها (فروش نسیه)</summary>
    public static decimal CalculateReceivables(IEnumerable<Sale> allSales)
    {
        return allSales
            .Where(s => s.PaymentStatus == PaymentStatus.Credit)
            .Sum(s => s.EntryType == EntryType.Normal ? s.Revenue : -s.Revenue);
    }

    /// <summary>جمع بدهی به تأمین‌کننده‌ها (خرید نسیه)</summary>
    public static decimal CalculatePayables(IEnumerable<Purchase> allPurchases)
    {
        return allPurchases
            .Where(p => p.PaymentStatus == PaymentStatus.Credit)
            .Sum(p => p.EntryType == EntryType.Normal ? p.TotalCost : -p.TotalCost);
    }
}