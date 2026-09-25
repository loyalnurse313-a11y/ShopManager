using ShopManager.Domain.Entities;
using ShopManager.Domain.Enums;

namespace ShopManager.Domain.Services;

/// <summary>
/// محاسبه وضعیت هشدار موجودی (خوب / هشدار / بحرانی)
/// </summary>
public static class StockAlertCalculator
{
    /// <summary>
    /// کل ورودی تاریخی کالا = موجودی اولیه انبار + موجودی اولیه مغازه + همه خریدها
    /// این مبنا برای محاسبه درصد هشداره
    /// </summary>
    public static decimal GetTotalHistoricalInput(
        Item item,
        IEnumerable<Purchase> purchases)
    {
        decimal purchased = purchases
            .Where(p => p.ItemId == item.Id)
            .Where(p => p.EntryType == EntryType.Normal)
            .Sum(p => p.Qty);

        return item.OpeningWarehouseQty + item.OpeningShopQty + purchased;
    }

    /// <summary>
    /// وضعیت موجودی رو برمی‌گردونه: Ok / Warning / Critical
    /// 
    /// - اگه حد ثابت تعریف شده باشه، از اون استفاده می‌شه
    /// - وگرنه از درصد استفاده می‌شه
    /// - اگه کالا هیچ ورودی تاریخی نداره و حد ثابت هم نیست، همیشه Ok برمی‌گردونه
    /// </summary>
    public static StockStatus GetStatus(
        Item item,
        decimal currentQty,
        decimal totalHistoricalInput)
    {
        // ─── اگه هیچ ورودی تاریخی نداریم و حد ثابت هم نیست → هیچ‌وقت بحرانی نشون نده ───
        if (totalHistoricalInput <= 0 && !item.LowStockCriticalFixed.HasValue)
        {
            return StockStatus.Ok;
        }

        decimal criticalThreshold;
        decimal warningThreshold;

        // ─── اولویت با حد ثابت ───
        if (item.LowStockCriticalFixed.HasValue)
        {
            criticalThreshold = item.LowStockCriticalFixed.Value;
            warningThreshold = item.LowStockWarningFixed ?? item.LowStockCriticalFixed.Value;
        }
        else
        {
            criticalThreshold = totalHistoricalInput * item.LowStockCriticalPct;
            warningThreshold = totalHistoricalInput * item.LowStockWarningPct;
        }

        if (currentQty <= criticalThreshold) return StockStatus.Critical;
        if (currentQty <= warningThreshold) return StockStatus.Warning;
        return StockStatus.Ok;
    }
}