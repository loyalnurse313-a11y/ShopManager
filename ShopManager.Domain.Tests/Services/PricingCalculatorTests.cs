using ShopManager.Domain.Entities;
using ShopManager.Domain.Services;
using ShopManager.Domain.Tests.Helpers;
using Xunit;

namespace ShopManager.Domain.Tests.Services;

public class PricingCalculatorTests
{
    // T1: SalePrice > 0 → Override
    [Fact]
    public void GetSuggestedSalePrice_Override_Used()
    {
        var item = TestDataFactory.Item(id: 1);
        item.SalePrice = 500;
        var purchases = new[] { TestDataFactory.Purchase(itemId: 1, qty: 10, unitCost: 100) };
        var result = PricingCalculator.GetSuggestedSalePrice(item, purchases);
        Assert.Equal(500m, result);
    }

    // T2: SalePrice = 0 → باید Ignore شود (انتظار Fail در گام ۴)
    [Fact]
    public void GetSuggestedSalePrice_ZeroSalePrice_Ignored()
    {
        var item = TestDataFactory.Item(id: 1);
        item.SalePrice = 0;
        item.MarkupPct = 30;
        var purchases = new[] { TestDataFactory.Purchase(itemId: 1, qty: 10, unitCost: 100) };
        var result = PricingCalculator.GetSuggestedSalePrice(item, purchases);
        Assert.Equal(130m, result);
    }

    // T3: Markup
    [Fact]
    public void GetSuggestedSalePrice_MarkupApplied()
    {
        var item = TestDataFactory.Item(id: 1);
        item.MarkupPct = 50;
        var purchases = new[] { TestDataFactory.Purchase(itemId: 1, qty: 10, unitCost: 200) };
        var result = PricingCalculator.GetSuggestedSalePrice(item, purchases);
        Assert.Equal(300m, result);
    }

    // T4: بدون خرید → Opening
    [Fact]
    public void GetSuggestedSalePrice_NoPurchases_FallsBackToOpeningCost()
    {
        var item = TestDataFactory.Item(id: 1, openingWarehouseQty: 10, openingWarehouseUnitCost: 100);
        item.MarkupPct = 20;
        var result = PricingCalculator.GetSuggestedSalePrice(item, Array.Empty<Purchase>());
        Assert.Equal(120m, result);
    }

    // T5: Rounding AwayFromZero (انتظار Fail در گام ۴)
    [Fact]
    public void GetSuggestedSalePrice_RoundingHalfAwayFromZero()
    {
        var item = TestDataFactory.Item(id: 1);
        item.MarkupPct = 25;
        var purchases = new[] { TestDataFactory.Purchase(itemId: 1, qty: 10, unitCost: 10) };
        var result = PricingCalculator.GetSuggestedSalePrice(item, purchases);
        // 12.5 → باید 13 (AwayFromZero) نه 12 (ToEven)
        Assert.Equal(13m, result);
    }
}
