using System;

namespace ShopManager.Desktop.ViewModels;

/// <summary>
/// ردیف سابقه فروش برای نمایش توی جدول
/// </summary>
public class SaleHistoryRow
{
    public int Id { get; set; }
    public int ItemId { get; set; }
    public string DateShamsi { get; set; } = "";
    public string ItemCode { get; set; } = "";
    public string ItemName { get; set; } = "";
    public string Unit { get; set; } = "";
    public string Qty { get; set; } = "";
    public string SaleUnitPrice { get; set; } = "";
    public string Revenue { get; set; } = "";
    public string LockedCost { get; set; } = "";
    public string Profit { get; set; } = "";
    public string PaymentType { get; set; } = "";

    // مقادیر عددی برای محاسبات
    public decimal RawRevenue { get; set; }
    public decimal RawProfit { get; set; }
    public decimal RawQty { get; set; }
    public bool RawIsCash { get; set; }
    public DateTime RawDateGregorian { get; set; }
}