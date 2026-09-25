namespace ShopManager.Desktop.ViewModels;

/// <summary>
/// یه ردیف از اقلام فاکتور
/// </summary>
public class InvoiceRow
{
    public string ItemName { get; set; } = "";
    public string Unit { get; set; } = "";
    public string Qty { get; set; } = "";
    public string UnitPrice { get; set; } = "";
    public string Total { get; set; } = "";
}