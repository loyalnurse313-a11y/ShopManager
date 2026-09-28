using ShopManager.Domain.Entities;
using ShopManager.Domain.Enums;
using ShopManager.Domain.Services;
using ShopManager.Domain.Tests.Helpers;
using Xunit;

namespace ShopManager.Domain.Tests.Services;

public class LockedCostCalculatorTests
{
    // T1: فقط خریدهای Normal لحاظ می‌شوند (Reversal نادیده)
    [Fact]
    public void CalculateLockedCost_IgnoresReversalPurchases()
    {
        var item = TestDataFactory.Item(id: 1);
        var purchases = new[]
        {
            TestDataFactory.Purchase(itemId: 1, qty: 10, unitCost: 100, entryType: EntryType.Normal),
            TestDataFactory.Purchase(itemId: 1, qty: 5, unitCost: 200, entryType: EntryType.Reversal),
        };

        var result = LockedCostCalculator.CalculateLockedCost(item, DateTime.Today, purchases);

        // فقط Normal: 10×100 / 10 = 100
        Assert.Equal(100m, result);
    }

    // T2: خریدهای بعد از تاریخ فروش لحاظ نمی‌شوند
    [Fact]
    public void CalculateLockedCost_ExcludesFuturePurchases()
    {
        var item = TestDataFactory.Item(id: 1);
        var saleDate = new DateTime(2026, 1, 10);
        var purchases = new[]
        {
            TestDataFactory.Purchase(itemId: 1, qty: 10, unitCost: 100, date: new DateTime(2026, 1, 5)),
            TestDataFactory.Purchase(itemId: 1, qty: 10, unitCost: 999, date: new DateTime(2026, 1, 20)), // بعد از فروش
        };

        var result = LockedCostCalculator.CalculateLockedCost(item, saleDate, purchases);

        Assert.Equal(100m, result);
    }

    // T3: میانگین موزون چند خرید
    [Fact]
    public void CalculateLockedCost_WeightedAverageAcrossPurchases()
    {
        var item = TestDataFactory.Item(id: 1);
        var purchases = new[]
        {
            TestDataFactory.Purchase(itemId: 1, qty: 10, unitCost: 100), // 1000
            TestDataFactory.Purchase(itemId: 1, qty: 5, unitCost: 200),  // 1000
            TestDataFactory.Purchase(itemId: 1, qty: 5, unitCost: 300),  // 1500
        };

        var result = LockedCostCalculator.CalculateLockedCost(item, DateTime.Today, purchases);

        // TotalCost=3500, TotalQty=20 → 175
        Assert.Equal(175m, result);
    }

    // T4: Opening بدون UnitCost → حذف از محاسبه
    [Fact]
    public void CalculateLockedCost_OpeningQtyWithoutUnitCost_IsIgnored()
    {
        var item = TestDataFactory.Item(id: 1, openingWarehouseQty: 100, openingWarehouseUnitCost: null);
        var purchases = new[]
        {
            TestDataFactory.Purchase(itemId: 1, qty: 10, unitCost: 100),
        };

        var result = LockedCostCalculator.CalculateLockedCost(item, DateTime.Today, purchases);

        // Opening حذف می‌شود → فقط 10×100/10 = 100
        Assert.Equal(100m, result);
    }

    // T5: Opening با UnitCost → میانگین با خرید
    [Fact]
    public void CalculateLockedCost_OpeningQtyWithUnitCost_Included()
    {
        var item = TestDataFactory.Item(id: 1, openingWarehouseQty: 10, openingWarehouseUnitCost: 50);
        var purchases = new[]
        {
            TestDataFactory.Purchase(itemId: 1, qty: 10, unitCost: 150),
        };

        var result = LockedCostCalculator.CalculateLockedCost(item, DateTime.Today, purchases);

        // (10×50 + 10×150) / 20 = 100
        Assert.Equal(100m, result);
    }

    // T6: بدون خرید → صفر
    [Fact]
    public void CalculateLockedCost_NoPurchases_ReturnsZero()
    {
        var item = TestDataFactory.Item(id: 1);

        var result = LockedCostCalculator.CalculateLockedCost(item, DateTime.Today, Array.Empty<Purchase>());

        Assert.Equal(0m, result);
    }

    // T7: خرید فقط در همان روز (granularity .Date) → لحاظ می‌شود
    [Fact]
    public void CalculateLockedCost_SameDayPurchase_IsIncluded()
    {
        var item = TestDataFactory.Item(id: 1);
        var saleDate = new DateTime(2026, 1, 10, 14, 0, 0); // بعدازظهر
        var purchases = new[]
        {
            TestDataFactory.Purchase(itemId: 1, qty: 10, unitCost: 500, date: new DateTime(2026, 1, 10, 18, 0, 0)), // همان روز، شب
        };

        var result = LockedCostCalculator.CalculateLockedCost(item, saleDate, purchases);

        // .Date یکسان → لحاظ می‌شود
        Assert.Equal(500m, result);
    }

    // T8: خرید کالای دیگر لحاظ نمی‌شود
    [Fact]
    public void CalculateLockedCost_DifferentItem_Excluded()
    {
        var item = TestDataFactory.Item(id: 1);
        var purchases = new[]
        {
            TestDataFactory.Purchase(itemId: 2, qty: 10, unitCost: 999), // Item دیگر
        };

        var result = LockedCostCalculator.CalculateLockedCost(item, DateTime.Today, purchases);

        Assert.Equal(0m, result);
    }
}
