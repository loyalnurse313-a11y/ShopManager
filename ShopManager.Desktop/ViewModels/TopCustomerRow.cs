namespace ShopManager.Desktop.ViewModels;

/// <summary>
/// ردیف مشتری برتر
/// </summary>
public class TopCustomerRow
{
    public int Rank { get; set; }
    public string RankDisplay { get; set; } = "";
    public string Name { get; set; } = "";
    public string Phone { get; set; } = "";
    public string PurchaseCount { get; set; } = "";
    public string TotalAmount { get; set; } = "";
}