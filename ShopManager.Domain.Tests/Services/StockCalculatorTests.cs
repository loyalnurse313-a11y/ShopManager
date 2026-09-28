using ShopManager.Domain.Entities;
using ShopManager.Domain.Enums;
using ShopManager.Domain.Services;
using ShopManager.Domain.Tests.Helpers;
using Xunit;

namespace ShopManager.Domain.Tests.Services;

public class StockCalculatorTests
{
    // T1: موجودی انبار ساده
    [Fact]
    public void GetWarehouseStock_BasicFormula()
    {
        var item = TestDataFactory.Item(id: 1, openingWarehouseQty: 100);
        var purchases = new[] { TestDataFactory.Purchase(itemId: 1, qty: 50) };
        var transfers = new[] { TestDataFactory.Transfer(itemId: 1, qty: 30) };

        var result = StockCalculator.GetWarehouseStock(item, purchases, transfers);

        // 100 + 50 - 30 = 120
        Assert.Equal(120m, result);
    }

    // T2: موجودی مغازه ساده
    [Fact]
    public void GetShopStock_BasicFormula()
    {
        var item = TestDataFactory.Item(id: 1, openingShopQty: 20);
        var transfers = new[] { TestDataFactory.Transfer(itemId: 1, qty: 50) };
        var sales = new[] { TestDataFactory.Sale(itemId: 1, qty: 30) };

        var result = StockCalculator.GetShopStock(item, transfers, sales);

        // 20 + 50 - 30 = 40
        Assert.Equal(40m, result);
    }

    // T3: Purchase Reversal → کاهش خرید
    [Fact]
    public void GetWarehouseStock_PurchaseReversal_Reduces()
    {
        var item = TestDataFactory.Item(id: 1, openingWarehouseQty: 0);
        var purchases = new[]
        {
            TestDataFactory.Purchase(itemId: 1, qty: 10, entryType: EntryType.Normal),
            TestDataFactory.Purchase(itemId: 1, qty: 5, entryType: EntryType.Reversal),
        };

        var result = StockCalculator.GetWarehouseStock(item, purchases, Array.Empty<Transfer>());

        // 10 - 5 = 5
        Assert.Equal(5m, result);
    }

    // T4: Transfer Reversal → بازگشت به انبار
    [Fact]
    public void GetWarehouseStock_TransferReversal_Increases()
    {
        var item = TestDataFactory.Item(id: 1, openingWarehouseQty: 100);
        var transfers = new[]
        {
            TestDataFactory.Transfer(itemId: 1, qty: 30, entryType: EntryType.Normal),
            TestDataFactory.Transfer(itemId: 1, qty: 10, entryType: EntryType.Reversal),
        };

        var result = StockCalculator.GetWarehouseStock(item, Array.Empty<Purchase>(), transfers);

        // 100 - 30 + 10 = 80
        Assert.Equal(80m, result);
    }

    // T5: Sale Reversal → بازگشت به مغازه
    [Fact]
    public void GetShopStock_SaleReversal_Increases()
    {
        var item = TestDataFactory.Item(id: 1, openingShopQty: 50);
        var sales = new[]
        {
            TestDataFactory.Sale(itemId: 1, qty: 20, entryType: EntryType.Normal),
            TestDataFactory.Sale(itemId: 1, qty: 5, entryType: EntryType.Reversal),
        };

        var result = StockCalculator.GetShopStock(item, Array.Empty<Transfer>(), sales);

        // 50 - 20 + 5 = 35
        Assert.Equal(35m, result);
    }

    // T6: موجودی منفی ممکن است (بدون گارد)
    [Fact]
    public void GetShopStock_NegativeAllowed()
    {
        var item = TestDataFactory.Item(id: 1, openingShopQty: 5);
        var sales = new[] { TestDataFactory.Sale(itemId: 1, qty: 10) };

        var result = StockCalculator.GetShopStock(item, Array.Empty<Transfer>(), sales);

        // 5 - 10 = -5 → گارد ندارد
        Assert.Equal(-5m, result);
    }
}
