using System.Globalization;
using ShopManager.Desktop.Services;
using ShopManager.Desktop.Views;
using ShopManager.Domain.Enums;

namespace ShopManager.Domain.Tests.Integration;

public class SaleRequestTests
{
    private static readonly SaleLine[] Lines = [new(1, 1m, 100m, 40m), new(2, 2m, 80m, 30m)];

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    public void EveryMeaningfulInputAndLineOrderAffectsFingerprint(int change)
    {
        var lines = Lines.ToArray();
        var name = "Customer";
        var phone = "123";
        var discount = 1m;
        var payment = PaymentStatus.Card;
        var terminal = "Bank";
        switch (change)
        {
            case 0: lines[0] = lines[0] with { ItemId = 3 }; break;
            case 1: lines[0] = lines[0] with { Qty = 1.01m }; break;
            case 2: lines[0] = lines[0] with { SaleUnitPrice = 101m }; break;
            case 3: lines[0] = lines[0] with { LockedCost = 41m }; break;
            case 4: Array.Reverse(lines); break;
            case 5: name = "Other"; break;
            case 6: phone = "124"; break;
            case 7: discount = 2m; break;
            case 8: payment = PaymentStatus.Cash; break;
            case 9: terminal = "Other"; break;
        }
        var original = new SaleRequest(Lines, "Customer", "123", 1m, PaymentStatus.Card, "Bank");
        var changed = new SaleRequest(lines, name, phone, discount, payment, terminal);
        Assert.NotEqual(original.Fingerprint(), changed.Fingerprint());
    }

    [Fact]
    public void FingerprintIsCultureIndependentAndNormalizesEquivalentInput()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fa-IR");
            var first = new SaleRequest(Lines, " Customer ", " ۱۲۳ ", 1.00m, PaymentStatus.Card, "Bank");
            var fingerprint = first.Fingerprint();
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            var equivalent = new SaleRequest([new(1, 1.00m, 100.00m, 40.00m), Lines[1]],
                "Customer", "123", 1m, PaymentStatus.Card, "Bank");
            Assert.Equal(fingerprint, equivalent.Fingerprint());
            Assert.Matches("^v1:sha256:[0-9a-f]{64}$", fingerprint);
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    [Fact]
    public void TerminalCanonicalizationPreservesCurrentSemantics()
    {
        SaleRequest Request(PaymentStatus payment, string? terminal) => new(Lines, "", "", 0, payment, terminal);
        Assert.Equal(Request(PaymentStatus.Cash, "Bank").Fingerprint(), Request(PaymentStatus.Cash, null).Fingerprint());
        Assert.Equal(Request(PaymentStatus.Card, "  ").Fingerprint(), Request(PaymentStatus.Card, null).Fingerprint());
        Assert.NotEqual(Request(PaymentStatus.Card, " Bank ").Fingerprint(), Request(PaymentStatus.Card, "Bank").Fingerprint());
    }

    [Fact]
    public void SnapshotDefensivelyCopiesLines()
    {
        var source = Lines.ToList();
        var request = new SaleRequest(source, "", "", 0, PaymentStatus.Cash, null);
        var before = request.Fingerprint();
        source[0] = source[0] with { Qty = 100m };
        source.Clear();
        Assert.Equal(before, request.Fingerprint());
        Assert.Throws<NotSupportedException>(() => ((IList<SaleLine>)request.Lines).Clear());
    }

    [Fact]
    public void UncertainAttemptCannotBeDiscardedAfterDefinitiveRetryFailure()
    {
        var pending = new PendingSale(new SaleRequest(Lines, "", "", 0, PaymentStatus.Cash, null), "PROPOSED");
        var id = pending.OperationId;
        var definitive = new InvalidOperationException("Failure before COMMIT");
        SaleFailure.MarkNotCommitted(definitive);
        var collision = new SaleInvoiceCollisionException("PROPOSED");
        SaleFailure.MarkNotCommitted(collision);
        foreach (var error in new Exception[] { new InvalidOperationException("Unknown outcome"), definitive, collision })
        {
            POSWindow.ExecutePendingSale(pending, (_, _, _) => throw error,
                () => Assert.Fail("No reset on unresolved outcome"),
                () => Assert.Fail("Must retain unresolved request"),
                _ => Assert.Fail("Must not replace an unresolved invoice"),
                _ => Assert.Fail("No post-commit action"), actual => Assert.Same(error, actual),
                _ => Assert.Fail("No post-commit failure"));
            Assert.False(pending.CanDiscard);
            Assert.Equal(id, pending.OperationId);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CompletedSaleReleasesPendingBeforeFallibleUi(bool failReset)
    {
        var request = new SaleRequest(Lines, "", "", 0, PaymentStatus.Cash, null);
        PendingSale? active = new(request, "FIRST");
        var original = active;
        var writes = 0;
        var cartCount = 2;
        var resetError = new InvalidOperationException("Reset failed");
        var uiError = new InvalidOperationException("Refresh or print failed");
        var errors = new List<Exception>();
        void Run(bool resetFails) => POSWindow.ExecutePendingSale(active!, (_, _, invoice) =>
        {
            writes++;
            return new SaleSaveResult(invoice, false, []);
        }, () =>
        {
            if (resetFails) throw resetError;
            cartCount = 0;
        }, () => active = null, _ => Assert.Fail("No collision"), result =>
        {
            Assert.Null(active);
            Assert.Equal(0, cartCount);
            Assert.Equal("FIRST", result.InvoiceNumber);
            throw uiError;
        }, _ => Assert.Fail("Persistence succeeded"), errors.Add);
        Run(failReset);
        if (failReset)
        {
            Assert.Same(original, active);
            Assert.Equal(2, cartCount);
            Assert.Same(resetError, Assert.Single(errors));
            Run(false);
        }
        Assert.Null(active);
        Assert.Equal(1, writes);
        Assert.Same(uiError, errors.Last());
    }

    [Fact]
    public void PendingRequestRetainsIdentityOnFailureAndSuccessBeforeUiFailure()
    {
        var request = new SaleRequest(Lines, "", "", 0, PaymentStatus.Cash, null);
        var pending = new PendingSale(request, "PROPOSED");
        var id = pending.OperationId;
        Assert.Throws<InvalidOperationException>(() => pending.Persist((key, snapshot, invoice) =>
        {
            Assert.Equal(id, key);
            Assert.Same(request, snapshot);
            throw new InvalidOperationException("Uncertain result");
        }));
        Assert.Null(pending.Result);
        Assert.Equal(id, pending.OperationId);
        var expected = new SaleSaveResult("STORED", true, []);
        var uiErrors = new List<Exception>();
        POSWindow.ExecuteSale(() => pending.Persist((key, snapshot, invoice) =>
        {
            Assert.Equal(id, key);
            Assert.Same(request, snapshot);
            Assert.Equal("PROPOSED", invoice);
            return expected;
        }), result =>
        {
            Assert.Same(expected, pending.Result);
            Assert.Equal("STORED", result.InvoiceNumber);
            throw new InvalidOperationException("Print failed");
        }, _ => Assert.Fail("Persistence was successful"), uiErrors.Add);
        Assert.Single(uiErrors);
        Assert.Same(expected, pending.Persist((_, _, _) => throw new Exception("Must not persist again")));
        // Identity allocation is independent of the invoice proposal; number generation is tested against SQLite.
        Assert.NotEqual(id, new PendingSale(request, "PROPOSED").OperationId);
    }
}
