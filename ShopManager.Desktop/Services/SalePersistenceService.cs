using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore.Storage;
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
    // Own both resources so cleanup cannot change the outcome of a successful commit.
    internal static SaleSaveResult Save(
        Func<AppDbContext> createContext,
        string operationId,
        SaleRequest request,
        string invoiceNumber)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operationId);
        ArgumentNullException.ThrowIfNull(request);
        var fingerprint = request.Fingerprint();
        var items = request.Lines;
        var customerName = request.CustomerName;
        var customerPhone = request.CustomerPhone;
        var discountAmount = request.DiscountAmount;
        var paymentStatus = request.PaymentStatus;
        var cardTerminal = request.CardTerminal;
        AppDbContext? db = null;
        IDbContextTransaction? tx = null;
        Exception? persistenceError = null;
        var committed = false;
        var establishedReplay = false;
        var commitAttempted = false;
        var postCommitErrors = new List<Exception>();
        try
        {
            db = createContext();
            tx = db.Database.BeginTransaction();
            var existing = db.SaleOperations.AsNoTracking().SingleOrDefault(o => o.OperationId == operationId);
            if (existing != null)
            {
                if (existing.RequestFingerprint != fingerprint)
                    throw new SaleOperationConflictException(operationId);
                invoiceNumber = existing.InvoiceNumber;
                establishedReplay = true;
            }
            else
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(invoiceNumber);
                if (db.SaleOperations.Any(o => o.InvoiceNumber == invoiceNumber)
                    || db.Sales.Any(s => s.InvoiceNumber == invoiceNumber))
                    throw new InvalidOperationException("Invoice number is already in use.");
                PersistNewSale();
            }

            void PersistNewSale()
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
                db.SaleOperations.Add(new SaleOperation
                {
                    OperationId = operationId,
                    InvoiceNumber = invoiceNumber,
                    RequestFingerprint = fingerprint
                });
                db.SaveChanges();
                commitAttempted = true;
                tx.Commit();
                committed = true;
            }
        }
        catch (Exception originalException)
        {
            persistenceError = originalException;
            try
            {
                tx?.Rollback();
            }
            catch (Exception rollbackException)
            {
                originalException.Data["RollbackException"] = rollbackException;
            }
        }
        finally
        {
            DisposeResource(tx, "TransactionDisposeException");
            DisposeResource(db, "ContextDisposeException");
        }

        if (persistenceError != null)
        {
            // Only ambiguous COMMIT or an OperationId constraint race can resolve as replay.
            var keyRace = persistenceError is DbUpdateException
            {
                InnerException: SqliteException { SqliteErrorCode: 19 } sqlite
            } && sqlite.Message.Contains("SaleOperations.OperationId", StringComparison.Ordinal);
            if (commitAttempted || keyRace)
            {
                AppDbContext? verification = null;
                SaleOperation? stored = null;
                try
                {
                    verification = createContext();
                    stored = verification.SaleOperations.AsNoTracking()
                        .SingleOrDefault(o => o.OperationId == operationId);
                }
                catch (Exception readError)
                {
                    persistenceError.Data["ReplayVerificationException"] = readError;
                }
                finally
                {
                    try { verification?.Dispose(); }
                    catch (Exception cleanupError)
                    {
                        if (stored != null && stored.RequestFingerprint == fingerprint)
                            postCommitErrors.Add(new InvalidOperationException("ContextDisposeException", cleanupError));
                        else
                            persistenceError.Data["ReplayVerificationDisposeException"] = cleanupError;
                    }
                }
                if (stored != null)
                {
                    if (stored.RequestFingerprint != fingerprint)
                        throw new SaleOperationConflictException(operationId) { Data = { ["PersistenceException"] = persistenceError } };
                    postCommitErrors.Add(persistenceError);
                    return new SaleSaveResult(stored.InvoiceNumber, true, postCommitErrors);
                }
            }
            ExceptionDispatchInfo.Capture(persistenceError).Throw();
        }

        // SavedChanges is not a commit notification when a transaction is explicit.
        if (!establishedReplay)
        {
            var notificationError = DatabaseService.NotifyDataChanged();
            if (notificationError != null)
                postCommitErrors.Add(notificationError);
        }
        return new SaleSaveResult(invoiceNumber, establishedReplay, postCommitErrors);

        void DisposeResource(IDisposable? resource, string stage)
        {
            try
            {
                resource?.Dispose();
            }
            catch (Exception cleanupError)
            {
                if (committed || establishedReplay)
                    postCommitErrors.Add(new InvalidOperationException(stage, cleanupError));
                else
                    persistenceError!.Data[stage] = cleanupError;
            }
        }
    }
}
