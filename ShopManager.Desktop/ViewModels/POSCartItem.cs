namespace ShopManager.Desktop.ViewModels;

/// <summary>
/// یه قلم کالا توی سبد POS
/// </summary>
public class POSCartItem
{
    public int ItemId { get; set; }
    public int ItemCode { get; set; }
    public string ItemName { get; set; } = "";
    public string Unit { get; set; } = "";
    public decimal Qty { get; set; }
    public decimal SaleUnitPrice { get; set; }
    public decimal LockedCost { get; set; }

    public decimal Revenue => Qty * SaleUnitPrice;
    public decimal Profit => Revenue - (Qty * LockedCost);

    public string QtyDisplay => Domain.Helpers.PersianNumber.ToPersian(Qty);
    public string PriceDisplay => Domain.Helpers.PersianNumber.ToToman(SaleUnitPrice);
    public string RevenueDisplay => Domain.Helpers.PersianNumber.ToToman(Revenue);

    public bool HasDiscount { get; set; }
    public decimal DiscountPct { get; set; }
}