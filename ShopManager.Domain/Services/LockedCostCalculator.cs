using ShopManager.Domain.Entities;
using ShopManager.Domain.Enums;

namespace ShopManager.Domain.Services;

/// <summary>
/// محاسبه بهای تمام‌شده قفل‌شده برای فروش
/// 
/// قانون طلایی: قیمت خرید هر فروش فقط از خریدهای
/// «تا تاریخ همون فروش» محاسبه می‌شه. خریدهای بعدی
/// هرگز روی سود فروش‌های قبلی تأثیر نمی‌ذارن.
/// 
/// این دقیقاً همون باگی بود که توی نسخه اکسل وجود داشت.
/// </summary>
public static class LockedCostCalculator
{
    /// <summary>
    /// محاسبه بهای تمام‌شده قفل‌شده برای فروش در تاریخ مشخص
    /// </summary>
    /// <param name="item">کالای مربوطه</param>
    /// <param name="saleDate">تاریخ فروش (میلادی)</param>
    /// <param name="allPurchases">همه خریدهای این کالا (چه قبل چه بعد از فروش)</param>
    /// <returns>بهای تمام‌شده واحد</returns>
    public static decimal CalculateLockedCost(
        Item item,
        DateTime saleDate,
        IEnumerable<Purchase> allPurchases)
    {
        // فقط خریدهای عادی که تاریخشون <= تاریخ فروش هست
        var eligible = allPurchases
            .Where(p => p.ItemId == item.Id)
            .Where(p => p.EntryType == EntryType.Normal)
            .Where(p => p.DateGregorian.Date <= saleDate.Date)
            .ToList();

        decimal purchaseQty = eligible.Sum(p => p.Qty);
        decimal purchaseCost = eligible.Sum(p => p.TotalCost);

        // اگه بهای موجودی اولیه تنظیم شده، وارد محاسبه می‌شه
        if (item.OpeningWarehouseUnitCost.HasValue)
        {
            decimal openingQty = item.OpeningWarehouseQty;
            decimal openingCost = openingQty * item.OpeningWarehouseUnitCost.Value;

            decimal totalQty = openingQty + purchaseQty;
            decimal totalCost = openingCost + purchaseCost;

            if (totalQty == 0) return 0;
            return totalCost / totalQty;
        }

        // بدون بهای موجودی اولیه
        if (purchaseQty == 0) return 0;
        return purchaseCost / purchaseQty;
    }

    /// <summary>
    /// میانگین قیمت خرید فعلی (فقط برای نمایش در گزارش‌ها)
    /// این تابع در محاسبه سود استفاده نمی‌شه
    /// </summary>
    public static decimal CalculateCurrentAverageCost(
        Item item,
        IEnumerable<Purchase> allPurchases)
    {
        var normal = allPurchases
            .Where(p => p.ItemId == item.Id)
            .Where(p => p.EntryType == EntryType.Normal)
            .ToList();

        decimal purchaseQty = normal.Sum(p => p.Qty);
        decimal purchaseCost = normal.Sum(p => p.TotalCost);

        if (item.OpeningWarehouseUnitCost.HasValue)
        {
            decimal openingQty = item.OpeningWarehouseQty;
            decimal openingCost = openingQty * item.OpeningWarehouseUnitCost.Value;

            decimal totalQty = openingQty + purchaseQty;
            decimal totalCost = openingCost + purchaseCost;

            if (totalQty == 0) return 0;
            return totalCost / totalQty;
        }

        if (purchaseQty == 0) return 0;
        return purchaseCost / purchaseQty;
    }
}