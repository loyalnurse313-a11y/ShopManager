using System;

namespace ShopManager.Desktop.ViewModels;

/// <summary>
/// ردیف کاربر برای نمایش توی جدول
/// </summary>
public class UserRow
{
    public int Id { get; set; }
    public string RowNumber { get; set; } = "";
    public string Username { get; set; } = "";
    public string FullName { get; set; } = "";
    public string RoleDisplay { get; set; } = "";
    public string StatusDisplay { get; set; } = "";
    public bool IsActive { get; set; }
    public string LastLoginDisplay { get; set; } = "";
    public string CreatedAtDisplay { get; set; } = "";

    // برای جستجو
    public string RawUsername { get; set; } = "";
    public string RawFullName { get; set; } = "";
}