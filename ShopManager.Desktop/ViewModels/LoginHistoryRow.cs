using System;

namespace ShopManager.Desktop.ViewModels;

/// <summary>
/// ردیف سابقه ورود برای نمایش توی جدول
/// </summary>
public class LoginHistoryRow
{
    public int Id { get; set; }
    public string RowNumber { get; set; } = "";
    public string Username { get; set; } = "";
    public string FullName { get; set; } = "";
    public string LoginAt { get; set; } = "";
    public string LogoutAt { get; set; } = "";
    public string Duration { get; set; } = "";
    public string Status { get; set; } = "";
    public bool IsSuccess { get; set; }
    public string Note { get; set; } = "";

    // برای فیلتر
    public DateTime RawLoginAt { get; set; }
    public string RawUsername { get; set; } = "";
    public string RawFullName { get; set; } = "";
}