namespace ShopManager.Desktop.ViewModels;

/// <summary>
/// ردیف کالا برای نمایش توی جدول
/// </summary>
public class ItemRow
{
    public int Id { get; set; }
    public string ItemCode { get; set; } = "";
    public string Name { get; set; } = "";
    public string Category { get; set; } = "";
    public string Unit { get; set; } = "";
    public string WarehouseStock { get; set; } = "";
    public string ShopStock { get; set; } = "";
    public string PurchasePrice { get; set; } = "";
    public string MarkupPct { get; set; } = "";
    public string SalePrice { get; set; } = "";
    public string PriceType { get; set; } = "";
}