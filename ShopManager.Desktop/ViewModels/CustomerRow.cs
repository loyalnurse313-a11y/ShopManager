namespace ShopManager.Desktop.ViewModels;

/// <summary>
/// ردیف مشتری برای نمایش توی جدول
/// </summary>
public class CustomerRow
{
    public int Id { get; set; }
    public string RowNumber { get; set; } = "";
    public string Name { get; set; } = "";
    public string Phone { get; set; } = "";
    public string PurchaseCount { get; set; } = "";
    public string LastPurchaseDate { get; set; } = "";
    public string TotalAmount { get; set; } = "";

    // برای جستجو
    public string RawName { get; set; } = "";
    public string RawPhone { get; set; } = "";
}