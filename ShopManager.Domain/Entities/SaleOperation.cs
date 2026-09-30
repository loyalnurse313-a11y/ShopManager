namespace ShopManager.Domain.Entities;

public class SaleOperation
{
    public required string OperationId { get; set; }
    public required string InvoiceNumber { get; set; }

    // Includes the fingerprint version; computation belongs to the persistence flow.
    public required string RequestFingerprint { get; set; }
}
