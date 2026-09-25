using ShopManager.Domain.Entities;
using ShopManager.Domain.Enums;

namespace ShopManager.Domain.Services;

/// <summary>
/// محاسبه موجودی انبار و مغازه
/// همه توابع Pure هستن (فقط ورودی می‌گیرن، خروجی می‌دن)
/// </summary>
public static class StockCalculator
{
    /// <summary>
    /// موجودی انبار = موجودی اولیه
    ///                 + خریدهای عادی − خریدهای برگشتی
    ///                 − انتقال‌های عادی + انتقال‌های برگشتی
    /// </summary>
    public static decimal GetWarehouseStock(
        Item item,
        IEnumerable<Purchase> purchases,
        IEnumerable<Transfer> transfers)
    {
        // خریدها: عادی مثبت، برگشت منفی
        decimal purchaseNet = purchases
            .Where(p => p.ItemId == item.Id)
            .Sum(p => p.EntryType == EntryType.Normal ? p.Qty : -p.Qty);

        // انتقال‌ها: عادی مثبت، برگشت منفی
        decimal transferNet = transfers
            .Where(t => t.ItemId == item.Id)
            .Sum(t => t.EntryType == EntryType.Normal ? t.Qty : -t.Qty);

        return item.OpeningWarehouseQty + purchaseNet - transferNet;
    }

    /// <summary>
    /// موجودی مغازه = موجودی اولیه مغازه
    ///                + انتقال‌های عادی − انتقال‌های برگشتی
    ///                − فروش‌های عادی + فروش‌های برگشتی
    /// </summary>
    public static decimal GetShopStock(
        Item item,
        IEnumerable<Transfer> transfers,
        IEnumerable<Sale> sales)
    {
        decimal transferNet = transfers
            .Where(t => t.ItemId == item.Id)
            .Sum(t => t.EntryType == EntryType.Normal ? t.Qty : -t.Qty);

        decimal salesNet = sales
            .Where(s => s.ItemId == item.Id)
            .Sum(s => s.EntryType == EntryType.Normal ? s.Qty : -s.Qty);

        return item.OpeningShopQty + transferNet - salesNet;
    }
}