using ShopManager.Domain.Entities;
using ShopManager.Domain.Enums;

namespace ShopManager.Domain.Tests.Helpers;

/// <summary>
/// سازنده‌های آماده برای ساخت سریع موجودیت‌های تست
/// </summary>
internal static class TestDataFactory
{
    public static Item Item(
        int id = 1,
        decimal openingWarehouseQty = 0,
        decimal? openingWarehouseUnitCost = null,
        decimal openingShopQty = 0)
        => new()
        {
            Id = id,
            ItemCode = id,
            Name = $"Item{id}",
            Unit = "عدد",
            OpeningWarehouseQty = openingWarehouseQty,
            OpeningWarehouseUnitCost = openingWarehouseUnitCost,
            OpeningShopQty = openingShopQty,
        };

    public static Purchase Purchase(
        int itemId = 1,
        decimal qty = 1,
        decimal unitCost = 0,
        DateTime? date = null,
        EntryType entryType = EntryType.Normal,
        PaymentStatus payment = PaymentStatus.Cash)
        => new()
        {
            ItemId = itemId,
            Qty = qty,
            UnitCost = unitCost,
            TotalCost = qty * unitCost,
            DateGregorian = date ?? DateTime.Today,
            EntryType = entryType,
            PaymentStatus = payment,
        };

    public static Transfer Transfer(
        int itemId = 1,
        decimal qty = 1,
        DateTime? date = null,
        EntryType entryType = EntryType.Normal)
        => new()
        {
            ItemId = itemId,
            Qty = qty,
            DateGregorian = date ?? DateTime.Today,
            EntryType = entryType,
        };

    public static Sale Sale(
        int itemId = 1,
        decimal qty = 1,
        decimal unitPrice = 0,
        decimal lockedCost = 0,
        DateTime? date = null,
        EntryType entryType = EntryType.Normal,
        PaymentStatus payment = PaymentStatus.Cash)
        => new()
        {
            ItemId = itemId,
            Qty = qty,
            SaleUnitPrice = unitPrice,
            LockedUnitCost = lockedCost,
            Revenue = qty * unitPrice,
            Cost = qty * lockedCost,
            Profit = (qty * unitPrice) - (qty * lockedCost),
            DateGregorian = date ?? DateTime.Today,
            EntryType = entryType,
            PaymentStatus = payment,
        };

    public static CashLedger Ledger(
        decimal? amountIn = null,
        decimal? amountOut = null)
        => new()
        {
            DateGregorian = DateTime.Today,
            AmountIn = amountIn,
            AmountOut = amountOut,
        };
}
