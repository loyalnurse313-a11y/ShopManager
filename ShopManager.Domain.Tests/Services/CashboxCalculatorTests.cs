using ShopManager.Domain.Entities;
using ShopManager.Domain.Enums;
using ShopManager.Domain.Services;
using ShopManager.Domain.Tests.Helpers;
using Xunit;

namespace ShopManager.Domain.Tests.Services;

public class CashboxCalculatorTests
{
    // T1: فقط فروش نقدی وارد صندوق می‌شود (کارتی و نسیه حذف)
    [Fact]
    public void CalculateCashbox_OnlyCashSalesIncluded()
    {
        var sales = new[]
        {
            TestDataFactory.Sale(qty: 1, unitPrice: 100, payment: PaymentStatus.Cash),
            TestDataFactory.Sale(qty: 1, unitPrice: 500, payment: PaymentStatus.Card),
            TestDataFactory.Sale(qty: 1, unitPrice: 200, payment: PaymentStatus.Credit),
        };

        var result = CashboxCalculator.CalculateCashbox(
            initialCapital: 1000, sales, Array.Empty<Purchase>(), Array.Empty<CashLedger>());

        Assert.Equal(1100m, result);
    }

    // T2: خرید نقدی از صندوق کم می‌شود (نسیه نادیده)
    [Fact]
    public void CalculateCashbox_CashPurchasesDeducted()
    {
        var purchases = new[]
        {
            TestDataFactory.Purchase(qty: 10, unitCost: 50, payment: PaymentStatus.Cash),   // 500
            TestDataFactory.Purchase(qty: 10, unitCost: 999, payment: PaymentStatus.Credit), // نادیده
        };

        var result = CashboxCalculator.CalculateCashbox(
            initialCapital: 1000, Array.Empty<Sale>(), purchases, Array.Empty<CashLedger>());

        Assert.Equal(500m, result);
    }

    // T3: Reversal فروش نقدی، موجودی را کاهش می‌دهد
    [Fact]
    public void CalculateCashbox_ReversalSaleReducesCashbox()
    {
        var sales = new[]
        {
            TestDataFactory.Sale(qty: 1, unitPrice: 100, payment: PaymentStatus.Cash),
            TestDataFactory.Sale(qty: 1, unitPrice: 100, payment: PaymentStatus.Cash, entryType: EntryType.Reversal),
        };

        var result = CashboxCalculator.CalculateCashbox(
            initialCapital: 0, sales, Array.Empty<Purchase>(), Array.Empty<CashLedger>());

        Assert.Equal(0m, result);
    }

    // T4: ورودی/خروجی دفتر صندوق
    [Fact]
    public void CalculateCashbox_LedgerInOut()
    {
        var ledger = new[]
        {
            TestDataFactory.Ledger(amountIn: 500),
            TestDataFactory.Ledger(amountOut: 200),
        };

        var result = CashboxCalculator.CalculateCashbox(
            initialCapital: 1000, Array.Empty<Sale>(), Array.Empty<Purchase>(), ledger);

        Assert.Equal(1300m, result);
    }

    // T5: AmountIn = null + AmountOut = 100 → خروجی 100
    [Fact]
    public void CalculateCashbox_NullAmountIn_TreatedAsZero()
    {
        var ledger = new[]
        {
            TestDataFactory.Ledger(amountIn: null, amountOut: 100),
        };

        var result = CashboxCalculator.CalculateCashbox(
            initialCapital: 1000, Array.Empty<Sale>(), Array.Empty<Purchase>(), ledger);

        Assert.Equal(900m, result);
    }

    // T6: صندوق خالی → صفر
    [Fact]
    public void CalculateCashbox_EmptyState_Zero()
    {
        var result = CashboxCalculator.CalculateCashbox(
            initialCapital: 0, Array.Empty<Sale>(), Array.Empty<Purchase>(), Array.Empty<CashLedger>());

        Assert.Equal(0m, result);
    }
}
