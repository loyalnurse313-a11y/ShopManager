using System;
using System.Globalization;
using System.Linq;
using ShopManager.Domain.Helpers;
using ShopManager.Infrastructure.Persistence;

namespace ShopManager.Desktop.Services;

internal static class SaleInvoiceNumberGenerator
{
    // This is a proposal, not a reservation. Persistence still checks it inside the sale transaction.
    internal static string Create(Func<AppDbContext> createContext, string? rejectedInvoice = null)
    {
        using var db = createContext();
        var prefix = $"POS-{JalaliDate.TodayShamsi().Split('/')[0]}-";
        var used = db.Sales
            .Where(s => s.InvoiceNumber != null && s.InvoiceNumber.StartsWith(prefix))
            .Select(s => s.InvoiceNumber!)
            .Union(db.SaleOperations.Where(o => o.InvoiceNumber.StartsWith(prefix))
                .Select(o => o.InvoiceNumber))
            .ToList();
        if (rejectedInvoice != null) used.Add(rejectedInvoice);

        // Include operation-only invoices and do not assume insertion order equals numeric order.
        var lastNumber = used.Where(number => number.StartsWith(prefix, StringComparison.Ordinal))
            .Select(number => long.TryParse(number.AsSpan(prefix.Length), NumberStyles.None,
                CultureInfo.InvariantCulture, out var value) ? value : 0)
            .DefaultIfEmpty(0).Max();
        var nextNumber = checked(lastNumber + 1);
        return prefix + nextNumber.ToString("D4", CultureInfo.InvariantCulture);
    }
}
