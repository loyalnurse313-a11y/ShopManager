using System;

namespace ShopManager.Desktop.ViewModels;

/// <summary>
/// ردیف دفتر صندوق برای نمایش
/// </summary>
public class CashLedgerRow
{
    public int Id { get; set; }
    public string DateShamsi { get; set; } = "";
    public string TypeDisplay { get; set; } = "";
    public string Counterparty { get; set; } = "";
    public string AmountDisplay { get; set; } = "";
    public string DirectionDisplay { get; set; } = "";
    public string Note { get; set; } = "";

    // برای رنگ‌بندی
    public bool IsIncome { get; set; }
}