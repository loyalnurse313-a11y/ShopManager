using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using ShopManager.Domain.Enums;
using ShopManager.Domain.Helpers;

namespace ShopManager.Desktop.Services;

internal sealed class SaleRequest
{
    public IReadOnlyList<SaleLine> Lines { get; }
    public string CustomerName { get; }
    public string CustomerPhone { get; }
    public decimal DiscountAmount { get; }
    public PaymentStatus PaymentStatus { get; }
    public string? CardTerminal { get; }

    public SaleRequest(IEnumerable<SaleLine> lines, string customerName, string customerPhone,
        decimal discountAmount, PaymentStatus paymentStatus, string? cardTerminal)
    {
        ArgumentNullException.ThrowIfNull(lines);
        var copy = lines.ToArray();
        if (copy.Length == 0 || copy.Any(line => line is null))
            throw new ArgumentException("A sale request requires non-null lines.", nameof(lines));
        if (!Enum.IsDefined(paymentStatus))
            throw new ArgumentOutOfRangeException(nameof(paymentStatus));
        Lines = Array.AsReadOnly(copy);
        CustomerName = customerName.Trim();
        CustomerPhone = PersianNumber.ToEnglishDigits(customerPhone).Trim();
        DiscountAmount = discountAmount;
        PaymentStatus = paymentStatus;
        CardTerminal = paymentStatus == PaymentStatus.Card && !string.IsNullOrWhiteSpace(cardTerminal)
            ? cardTerminal : null;
    }

    public string Fingerprint()
    {
        using var stream = new MemoryStream();
        using (var json = new Utf8JsonWriter(stream))
        {
            json.WriteStartObject();
            json.WriteStartArray("lines");
            foreach (var line in Lines)
            {
                json.WriteStartObject();
                json.WriteNumber("itemId", line.ItemId);
                json.WriteString("qty", Number(line.Qty));
                json.WriteString("saleUnitPrice", Number(line.SaleUnitPrice));
                json.WriteString("lockedCost", Number(line.LockedCost));
                json.WriteEndObject();
            }
            json.WriteEndArray();
            json.WriteString("customerName", CustomerName);
            json.WriteString("customerPhone", CustomerPhone);
            json.WriteString("discountAmount", Number(DiscountAmount));
            json.WriteNumber("paymentStatus", (int)PaymentStatus);
            json.WriteString("cardTerminal", CardTerminal);
            json.WriteEndObject();
        }
        return "v1:sha256:" + Convert.ToHexString(SHA256.HashData(stream.ToArray())).ToLowerInvariant();
    }

    private static string Number(decimal value) =>
        value.ToString("0.############################", CultureInfo.InvariantCulture);
}

internal sealed record SaleSaveResult(
    string InvoiceNumber, bool IsReplay, IReadOnlyList<Exception> PostCommitErrors);

internal sealed class SaleOperationConflictException(string operationId)
    : InvalidOperationException($"Sale operation '{operationId}' was already used for a different request.");

internal sealed class SaleInvoiceCollisionException(string invoiceNumber)
    : InvalidOperationException($"Invoice number '{invoiceNumber}' is already in use.");

// Preserve the original exception and its diagnostics while explicitly marking a definitive failure.
internal static class SaleFailure
{
    private static readonly object OutcomeKey = new();
    internal static void MarkNotCommitted(Exception error) => error.Data[OutcomeKey] = true;
    internal static bool IsNotCommitted(Exception error) => error.Data[OutcomeKey] is true;
}

// An unresolved attempt must retain its identity even if a later retry fails before COMMIT.
internal sealed class PendingSale(SaleRequest request, string proposedInvoiceNumber)
{
    public string OperationId { get; } = Guid.NewGuid().ToString("N");
    public SaleRequest Request { get; } = request;
    public string ProposedInvoiceNumber { get; } = proposedInvoiceNumber;
    public SaleSaveResult? Result { get; private set; }
    public bool CanDiscard { get; private set; }
    private bool _requiresResolution;

    public SaleSaveResult Persist(Func<string, SaleRequest, string, SaleSaveResult> save)
    {
        try
        {
            CanDiscard = false;
            return Result ??= save(OperationId, Request, ProposedInvoiceNumber);
        }
        catch (Exception error)
        {
            _requiresResolution |= !SaleFailure.IsNotCommitted(error);
            CanDiscard = !_requiresResolution;
            throw;
        }
    }
}
