using System;

namespace ShopManager.Desktop.ViewModels;

/// <summary>
/// ردیف سابقه انتقال برای نمایش
/// </summary>
public class TransferHistoryRow
{
    public int Id { get; set; }
    public int ItemId { get; set; }
    public string DateShamsi { get; set; } = "";
    public string ItemCode { get; set; } = "";
    public string ItemName { get; set; } = "";
    public string Unit { get; set; } = "";
    public string Qty { get; set; } = "";
    public string Note { get; set; } = "";

    // برای فیلتر
    public decimal RawQty { get; set; }
    public DateTime RawDateGregorian { get; set; }
}