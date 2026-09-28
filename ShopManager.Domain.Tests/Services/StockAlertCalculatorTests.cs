using ShopManager.Domain.Enums;
using ShopManager.Domain.Services;
using ShopManager.Domain.Tests.Helpers;
using Xunit;

namespace ShopManager.Domain.Tests.Services;

public class StockAlertCalculatorTests
{
    // T1: کافی → Ok
    [Fact]
    public void GetStatus_EnoughStock_Ok()
    {
        var item = TestDataFactory.Item(id: 1, openingWarehouseQty: 100);
        item.LowStockCriticalPct = 0.10m;
        item.LowStockWarningPct = 0.25m;
        var status = StockAlertCalculator.GetStatus(item, currentQty: 80, totalHistoricalInput: 100);
        Assert.Equal(StockStatus.Ok, status);
    }

    // T2: بحرانی
    [Fact]
    public void GetStatus_Critical_WhenBelowPct()
    {
        var item = TestDataFactory.Item(id: 1);
        item.LowStockCriticalPct = 0.10m;
        item.LowStockWarningPct = 0.25m;
        var status = StockAlertCalculator.GetStatus(item, currentQty: 5, totalHistoricalInput: 100);
        Assert.Equal(StockStatus.Critical, status);
    }

    // T3: هشدار
    [Fact]
    public void GetStatus_Warning_WhenBetweenPcts()
    {
        var item = TestDataFactory.Item(id: 1);
        item.LowStockCriticalPct = 0.10m;
        item.LowStockWarningPct = 0.25m;
        var status = StockAlertCalculator.GetStatus(item, currentQty: 20, totalHistoricalInput: 100);
        Assert.Equal(StockStatus.Warning, status);
    }

    // T4: Fixed اولویت
    [Fact]
    public void GetStatus_FixedTakesPriorityOverPct()
    {
        var item = TestDataFactory.Item(id: 1);
        item.LowStockCriticalPct = 0.01m;
        item.LowStockWarningPct = 0.02m;
        item.LowStockCriticalFixed = 5;
        var status = StockAlertCalculator.GetStatus(item, currentQty: 50, totalHistoricalInput: 1000);
        Assert.Equal(StockStatus.Ok, status);
    }

    // T5: totalHistoricalInput=0 → Ok
    [Fact]
    public void GetStatus_ZeroInput_ReturnsOk()
    {
        var item = TestDataFactory.Item(id: 1);
        var status = StockAlertCalculator.GetStatus(item, currentQty: 0, totalHistoricalInput: 0);
        Assert.Equal(StockStatus.Ok, status);
    }
}
