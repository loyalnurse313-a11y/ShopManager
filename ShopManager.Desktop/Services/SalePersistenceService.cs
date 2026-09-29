using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using ShopManager.Domain.Entities;
using ShopManager.Domain.Enums;
using ShopManager.Domain.Helpers;
using ShopManager.Domain.Services;
using ShopManager.Infrastructure.Persistence;

[assembly: InternalsVisibleTo("ShopManager.Domain.Tests")]

namespace ShopManager.Desktop.Services;

internal sealed record SaleLine(int ItemId, decimal Qty, decimal SaleUnitPrice, decimal LockedCost)
{
    public decimal Revenue => Qty * SaleUnitPrice;
}

internal static class SalePersistenceService
{
    // The caller owns the context; this method owns the sale's single transaction.
    internal static void Save(
        AppDbContext db,
        IReadOnlyList<SaleLine> items,
        string invoiceNumber,
        string customerName,
        string customerPhone,
        decimal discountAmount,
        PaymentStatus paymentStatus,
        string cardTerminal)
    {
        using var tx = db.Database.BeginTransaction();
        try
        {
            var today = DateTime.Today;

            // ── ۱. مشتری (بدون SaveChanges، فقط track) ──
            Customer? saleCustomer = null;
            customerName = customerName.Trim();
            customerPhone = PersianNumber.ToEnglishDigits(customerPhone).Trim();

            if (!string.IsNullOrWhiteSpace(customerPhone))
            {
                var totalCartRevenue = items.Sum(c => c.Revenue) - discountAmount;
                var existingCustomer = db.Customers.FirstOrDefault(c => c.Phone == customerPhone);

                if (existingCustomer != null)
                {
                    if (!string.IsNullOrWhiteSpace(customerName))
                        existingCustomer.Name = customerName;

                    existingCustomer.LastPurchaseAt = DateTime.UtcNow;
                    existingCustomer.TotalPurchasedAmount += totalCartRevenue;
                    existingCustomer.PurchaseCount += 1;

                    saleCustomer = existingCustomer;
                }
                else if (!string.IsNullOrWhiteSpace(customerName))
                {
                    var newCustomer = new Customer
                    {
                        Name = customerName,
                        Phone = customerPhone,
                        FirstPurchaseAt = DateTime.UtcNow,
                        LastPurchaseAt = DateTime.UtcNow,
                        TotalPurchasedAmount = totalCartRevenue,
                        PurchaseCount = 1
                    };
                    db.Customers.Add(newCustomer);
                    saleCustomer = newCustomer;
                }
            }

            // ── ۲. بررسی موجودی — authoritative، داخل تراکنش ──
            var cartItemIds = items.Select(c => c.ItemId).Distinct().ToList();

            var transfers = db.Transfers
                .Where(t => cartItemIds.Contains(t.ItemId))
                .ToList();

            var sales = db.Sales
                .Where(s => cartItemIds.Contains(s.ItemId))
                .ToList();

            foreach (var cartItem in items)
            {
                var item = db.Items.FirstOrDefault(i => i.Id == cartItem.ItemId);
                if (item == null)
                    throw new InvalidOperationException(
                        $"کالا با شناسه {cartItem.ItemId} پیدا نشد");

                var shopStock = StockCalculator.GetShopStock(item, transfers, sales);
                if (shopStock < cartItem.Qty)
                    throw new InvalidOperationException(
                        $"موجودی {item.Name} کافی نیست (موجودی: {PersianNumber.ToPersian(shopStock)})");
            }

            // ── ۳. ثبت اقلام فروش (خالص‌سازی تخفیف + rounding AwayFromZero) ──
            decimal totalCartRev = items.Sum(c => c.Revenue);
            decimal accumulatedDiscount = 0;
            var cartList = items.ToList();

            for (int i = 0; i < cartList.Count; i++)
            {
                var cartItem = cartList[i];
                var revenueRaw = cartItem.Revenue;
                var costRaw = cartItem.Qty * cartItem.LockedCost;

                // ── سهم تخفیف این قلم ──
                decimal discountShare = 0;
                if (discountAmount > 0 && totalCartRev > 0)
                {
                    if (i == cartList.Count - 1)
                    {
                        discountShare = discountAmount - accumulatedDiscount;
                    }
                    else
                    {
                        discountShare = decimal.Round(
                            (revenueRaw / totalCartRev) * discountAmount,
                            0, MidpointRounding.AwayFromZero);
                        accumulatedDiscount += discountShare;
                    }
                }

                // R2: اول round discountShare، بعد revenueNet
                discountShare = decimal.Round(discountShare, 0, MidpointRounding.AwayFromZero);
                var revenueNet = decimal.Round(revenueRaw - discountShare, 0, MidpointRounding.AwayFromZero);
                var cost = decimal.Round(costRaw, 0, MidpointRounding.AwayFromZero);
                var profit = revenueNet - cost;

                var sale = new Sale
                {
                    ItemId = cartItem.ItemId,
                    InvoiceNumber = invoiceNumber,
                    Customer = saleCustomer,
                    DateShamsi = JalaliDate.TodayShamsi(),
                    DateGregorian = today,
                    Qty = cartItem.Qty,
                    SaleUnitPrice = cartItem.SaleUnitPrice,
                    LockedUnitCost = cartItem.LockedCost,
                    Revenue = revenueNet,
                    DiscountAmount = discountShare,
                    Cost = cost,
                    Profit = profit,
                    PaymentStatus = paymentStatus,
                    CardTerminal = paymentStatus == PaymentStatus.Card
                                   && !string.IsNullOrWhiteSpace(cardTerminal)
                        ? cardTerminal
                        : null,
                    EntryType = EntryType.Normal,
                    CreatedAt = DateTime.UtcNow
                };

                db.Sales.Add(sale);
            }

            // ── ۴. کامیت اتمی ──
            db.SaveChanges();
            tx.Commit();
        }
        catch (Exception originalException)
        {
            try
            {
                tx.Rollback();
            }
            catch (Exception rollbackException)
            {
                originalException.Data["RollbackException"] = rollbackException;
            }
            throw;
        }

        // SavedChanges is not a commit notification when a transaction is explicit.
        DatabaseService.NotifyDataChanged();
    }
}
