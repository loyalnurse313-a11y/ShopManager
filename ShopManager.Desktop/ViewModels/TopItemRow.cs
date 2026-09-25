namespace ShopManager.Desktop.ViewModels;

/// <summary>
/// یه ردیف کالای برتر برای جدول آمار
/// </summary>
public class TopItemRow
{
    public int Rank { get; set; }
    public string RankDisplay { get; set; } = "";
    public string ItemCode { get; set; } = "";
    public string ItemName { get; set; } = "";
    public string Unit { get; set; } = "";
    public string MainValue { get; set; } = "";      // مقدار اصلی (تعداد یا سود)
    public string SecondaryValue { get; set; } = ""; // مقدار فرعی (درآمد یا تعداد)
}