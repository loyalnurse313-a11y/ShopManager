namespace ShopManager.Desktop.ViewModels;

/// <summary>
/// یه قلم کالا توی سبد فروش
/// </summary>
public class SaleCartItem
{
    public int ItemId { get; set; }
    public int ItemCode { get; set; }
    public string ItemName { get; set; } = "";
    public string Unit { get; set; } = "";
    public decimal Qty { get; set; }
    public decimal SaleUnitPrice { get; set; }

    /// <summary>جمع درآمد این قلم</summary>
    public decimal Revenue => Qty * SaleUnitPrice;
}