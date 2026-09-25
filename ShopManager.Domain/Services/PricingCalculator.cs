using ShopManager.Domain.Entities;
using ShopManager.Domain.Enums;

namespace ShopManager.Domain.Services;

/// <summary>
/// محاسبه قیمت خرید مبنا و قیمت فروش پیشنهادی
/// 
/// قانون: با تورم، قیمت خرید بالا می‌ره → قیمت فروش هم خودکار بالا می‌ره
/// مبنای قیمت خرید = آخرین خرید کالا (نه میانگین)
/// </summary>
public static class PricingCalculator
{
    /// <summary>
    /// آخرین قیمت خرید کالا (مبنای قیمت فروش)
    /// اگه خرید نباشه، بهای موجودی اولیه رو برمی‌گردونه
    /// </summary>
    public static decimal GetLatestPurchasePrice(
        Item item,
        IEnumerable<Purchase> purchases)
    {
        var lastPurchase = purchases
            .Where(p => p.ItemId == item.Id)
            .Where(p => p.EntryType == EntryType.Normal)
            .OrderByDescending(p => p.DateGregorian)
            .ThenByDescending(p => p.Id)
            .FirstOrDefault();

        if (lastPurchase != null)
            return lastPurchase.UnitCost;

        // اگه خرید نبود، بهای موجودی اولیه
        return item.OpeningWarehouseUnitCost ?? 0;
    }

    /// <summary>
    /// قیمت فروش پیشنهادی
    /// اگه SalePrice (Override) پر باشه، همون رو برمی‌گردونه
    /// وگرنه: آخرین قیمت خرید × (1 + MarkupPct/100)
    /// </summary>
    public static decimal GetSuggestedSalePrice(
        Item item,
        IEnumerable<Purchase> purchases)
    {
        // اگه کاربر دستی قیمت فروش رو تنظیم کرده، همون ملاکه
        if (item.SalePrice.HasValue)
            return item.SalePrice.Value;

        var purchasePrice = GetLatestPurchasePrice(item, purchases);
        if (purchasePrice <= 0)
            return 0;

        var salePrice = purchasePrice * (1 + item.MarkupPct / 100m);
        return decimal.Round(salePrice, 0);   // گرد کردن به تومان
    }

    /// <summary>
    /// آیا قیمت فروش دستی قفل شده؟ (Override فعاله)
    /// </summary>
    public static bool IsPriceOverridden(Item item)
    {
        return item.SalePrice.HasValue;
    }
}