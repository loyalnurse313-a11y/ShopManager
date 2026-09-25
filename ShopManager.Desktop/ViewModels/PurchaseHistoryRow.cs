using System;

namespace ShopManager.Desktop.ViewModels;

/// <summary>
/// ردیف سابقه خرید برای نمایش توی جدول
/// </summary>
public class PurchaseHistoryRow
{
    public int Id { get; set; }
    public int ItemId { get; set; }
    public string DateShamsi { get; set; } = "";
    public string ItemCode { get; set; } = "";
    public string ItemName { get; set; } = "";
    public string Unit { get; set; } = "";
    public string Qty { get; set; } = "";
    public string UnitCost { get; set; } = "";
    public string TotalCost { get; set; } = "";
    public string PaymentType { get; set; } = "";
    public string SupplierNote { get; set; } = "";

    public decimal RawTotalCost { get; set; }
    public bool RawIsCash { get; set; }
    public DateTime RawDateGregorian { get; set; }
}