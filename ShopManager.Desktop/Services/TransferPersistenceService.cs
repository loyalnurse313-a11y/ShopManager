using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.ExceptionServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using ShopManager.Domain.Entities;
using ShopManager.Domain.Enums;
using ShopManager.Domain.Helpers;
using ShopManager.Domain.Services;
using ShopManager.Infrastructure.Persistence;

namespace ShopManager.Desktop.Services;

internal sealed record TransferSaveResult(
    string ItemName, string ItemUnit, IReadOnlyList<Exception> PostCommitErrors);

internal sealed class TransferCommitUncertainException(Exception innerException)
    : InvalidOperationException("The transfer commit outcome is unknown. Check transfer history before retrying.", innerException);

internal static class TransferPersistenceService
{
    internal static TransferSaveResult Save(Func<AppDbContext> createContext, int itemCode, decimal qty,
        string dateShamsi, DateTime dateGregorian, string? note)
    {
        if (qty <= 0) throw new ArgumentOutOfRangeException(nameof(qty));
        if (note is { Length: > 500 }) throw new ArgumentException("Note must not exceed 500 characters.", nameof(note));

        AppDbContext? db = null;
        IDbContextTransaction? tx = null;
        Exception? persistenceError = null;
        var commitAttempted = false;
        var committed = false;
        var postCommitErrors = new List<Exception>();
        Item? item = null;
        try
        {
            db = createContext();
            // The SQLite provider starts an immediate writer transaction before authoritative reads.
            tx = db.Database.BeginTransaction();
            item = db.Items.AsNoTracking().FirstOrDefault(i => i.ItemCode == itemCode && i.IsActive)
                ?? throw new InvalidOperationException($"کالایی با کد {itemCode} پیدا نشد");
            var purchases = db.Purchases.AsNoTracking().Where(p => p.ItemId == item.Id).ToList();
            var transfers = db.Transfers.AsNoTracking().Where(t => t.ItemId == item.Id).ToList();
            var stock = StockCalculator.GetWarehouseStock(item, purchases, transfers);
            if (stock < qty)
                throw new InvalidOperationException(
                    $"موجودی انبار کافی نیست. موجودی فعلی: {PersianNumber.ToPersian(stock)}");

            db.Transfers.Add(new Transfer
            {
                ItemId = item.Id,
                DateShamsi = dateShamsi,
                DateGregorian = dateGregorian,
                Qty = qty,
                Note = string.IsNullOrWhiteSpace(note) ? null : note,
                EntryType = EntryType.Normal,
                CreatedAt = DateTime.UtcNow
            });
            db.SaveChanges();
            commitAttempted = true;
            tx.Commit();
            committed = true;
        }
        catch (Exception error)
        {
            persistenceError = error;
            try { tx?.Rollback(); }
            catch (Exception rollbackError) { error.Data["RollbackException"] = rollbackError; }
        }
        finally
        {
            DisposeResource(tx, "TransactionDisposeException");
            DisposeResource(db, "ContextDisposeException");
        }

        if (persistenceError != null)
        {
            // Without an idempotency record, a thrown COMMIT cannot safely be retried automatically.
            if (commitAttempted) throw new TransferCommitUncertainException(persistenceError);
            ExceptionDispatchInfo.Capture(persistenceError).Throw();
        }

        var notificationError = DatabaseService.NotifyDataChanged();
        if (notificationError != null) postCommitErrors.Add(notificationError);
        return new TransferSaveResult(item!.Name, item.Unit, postCommitErrors);

        void DisposeResource(IDisposable? resource, string stage)
        {
            try { resource?.Dispose(); }
            catch (Exception cleanupError)
            {
                if (committed)
                    postCommitErrors.Add(new InvalidOperationException(stage, cleanupError));
                else
                    persistenceError!.Data[stage] = cleanupError;
            }
        }
    }
}
