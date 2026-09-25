using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using ShopManager.Desktop.Services;
using ShopManager.Domain.Entities;
using ShopManager.Domain.Enums;

using Color = Avalonia.Media.Color;

namespace ShopManager.Desktop.Views;

public partial class UserEditWindow : Window
{
    private User? _editingUser;

    public UserEditWindow(User? userData)
    {
        InitializeComponent();

        _editingUser = userData;

        if (_editingUser == null)
        {
            HeaderText.Text = "افزودن کاربر جدید";
            PasswordGrid.IsVisible = true;
            RoleComboBox.SelectedIndex = 1;
            IsActiveCheckBox.IsChecked = true;
            CanViewFinanceCheckBox.IsChecked = false;
        }
        else
        {
            HeaderText.Text = $"ویرایش کاربر: {_editingUser.Username}";
            PasswordGrid.IsVisible = false;

            UsernameTextBox.Text = _editingUser.Username;
            FullNameTextBox.Text = _editingUser.FullName;
            RoleComboBox.SelectedIndex = _editingUser.Role == UserRole.Admin ? 0 : 1;
            IsActiveCheckBox.IsChecked = _editingUser.IsActive;

            CanDashboardCheckBox.IsChecked = _editingUser.CanDashboard;
            CanItemsCheckBox.IsChecked = _editingUser.CanItems;
            CanPurchaseCheckBox.IsChecked = _editingUser.CanPurchase;
            CanTransferCheckBox.IsChecked = _editingUser.CanTransfer;
            CanPOSCheckBox.IsChecked = _editingUser.CanPOS;
            CanCustomersCheckBox.IsChecked = _editingUser.CanCustomers;
            CanStatsCheckBox.IsChecked = _editingUser.CanStats;
            CanCashboxCheckBox.IsChecked = _editingUser.CanCashbox;
            CanSettingsCheckBox.IsChecked = _editingUser.CanSettings;
            CanPrintCheckBox.IsChecked = _editingUser.CanPrint;
            CanExportExcelCheckBox.IsChecked = _editingUser.CanExportExcel;
            CanUserManagementCheckBox.IsChecked = _editingUser.CanUserManagement;
            CanViewFinanceCheckBox.IsChecked = _editingUser.CanViewFinance;
        }
    }

    private void OnSaveClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            StatusText.Text = "";

            var username = UsernameTextBox.Text?.Trim() ?? "";
            var fullName = FullNameTextBox.Text?.Trim() ?? "";
            var role = RoleComboBox.SelectedIndex == 0 ? UserRole.Admin : UserRole.User;
            var isActive = IsActiveCheckBox.IsChecked == true;

            if (string.IsNullOrWhiteSpace(username) || username.Length < 3)
            {
                StatusText.Text = "نام کاربری باید حداقل ۳ کاراکتر باشد";
                return;
            }

            if (username.Contains(" "))
            {
                StatusText.Text = "نام کاربری نباید فاصله داشته باشد";
                return;
            }

            if (string.IsNullOrWhiteSpace(fullName))
            {
                StatusText.Text = "نام کامل را وارد کنید";
                return;
            }

            using var db = DatabaseService.CreateContext();

            if (_editingUser == null)
            {
                var password = PasswordTextBox.Text ?? "";
                var confirmPass = ConfirmPasswordTextBox.Text ?? "";

                if (string.IsNullOrWhiteSpace(password) || password.Length < 4)
                {
                    StatusText.Text = "رمز عبور باید حداقل ۴ کاراکتر باشد";
                    return;
                }

                if (password != confirmPass)
                {
                    StatusText.Text = "رمز و تکرارش یکسان نیستند";
                    return;
                }

                var normalizedUsername = username.ToLower();
                if (db.Users.Any(u => u.Username.ToLower() == normalizedUsername))
                {
                    StatusText.Text = $"نام کاربری «{username}» قبلاً استفاده شده";
                    return;
                }

                var salt = PasswordHasher.GenerateSalt();
                var hash = PasswordHasher.HashPassword(password, salt);

                var newUser = new User
                {
                    Username = username,
                    PasswordHash = hash,
                    PasswordSalt = salt,
                    FullName = fullName,
                    Role = role,
                    IsActive = isActive,
                    CreatedAt = DateTime.UtcNow,

                    CanDashboard = CanDashboardCheckBox.IsChecked == true,
                    CanItems = CanItemsCheckBox.IsChecked == true,
                    CanPurchase = CanPurchaseCheckBox.IsChecked == true,
                    CanTransfer = CanTransferCheckBox.IsChecked == true,
                    CanPOS = CanPOSCheckBox.IsChecked == true,
                    CanCustomers = CanCustomersCheckBox.IsChecked == true,
                    CanStats = CanStatsCheckBox.IsChecked == true,
                    CanCashbox = CanCashboxCheckBox.IsChecked == true,
                    CanSettings = CanSettingsCheckBox.IsChecked == true,
                    CanPrint = CanPrintCheckBox.IsChecked == true,
                    CanExportExcel = CanExportExcelCheckBox.IsChecked == true,
                    CanUserManagement = CanUserManagementCheckBox.IsChecked == true,
                    CanViewFinance = CanViewFinanceCheckBox.IsChecked == true
                };

                db.Users.Add(newUser);
                db.SaveChanges();
                Close(true);
            }
            else
            {
                var user = db.Users.FirstOrDefault(u => u.Id == _editingUser.Id);
                if (user == null)
                {
                    StatusText.Text = "کاربر پیدا نشد";
                    return;
                }

                var normalizedUsername = username.ToLower();
                if (db.Users.Any(u => u.Id != user.Id && u.Username.ToLower() == normalizedUsername))
                {
                    StatusText.Text = $"نام کاربری «{username}» قبلاً استفاده شده";
                    return;
                }

                if (user.Role == UserRole.Admin && role != UserRole.Admin)
                {
                    var adminCount = db.Users.Count(u => u.Role == UserRole.Admin && u.IsActive);
                    if (adminCount <= 1)
                    {
                        StatusText.Text = "نمی‌تونی نقش آخرین ادمین رو تغییر بدی";
                        return;
                    }
                }

                if (user.Role == UserRole.Admin && !isActive)
                {
                    var adminCount = db.Users.Count(u => u.Role == UserRole.Admin && u.IsActive);
                    if (adminCount <= 1)
                    {
                        StatusText.Text = "نمی‌تونی آخرین ادمین فعال رو غیرفعال کنی";
                        return;
                    }
                }

                user.Username = username;
                user.FullName = fullName;
                user.Role = role;
                user.IsActive = isActive;

                user.CanDashboard = CanDashboardCheckBox.IsChecked == true;
                user.CanItems = CanItemsCheckBox.IsChecked == true;
                user.CanPurchase = CanPurchaseCheckBox.IsChecked == true;
                user.CanTransfer = CanTransferCheckBox.IsChecked == true;
                user.CanPOS = CanPOSCheckBox.IsChecked == true;
                user.CanCustomers = CanCustomersCheckBox.IsChecked == true;
                user.CanStats = CanStatsCheckBox.IsChecked == true;
                user.CanCashbox = CanCashboxCheckBox.IsChecked == true;
                user.CanSettings = CanSettingsCheckBox.IsChecked == true;
                user.CanPrint = CanPrintCheckBox.IsChecked == true;
                user.CanExportExcel = CanExportExcelCheckBox.IsChecked == true;
                user.CanUserManagement = CanUserManagementCheckBox.IsChecked == true;
                user.CanViewFinance = CanViewFinanceCheckBox.IsChecked == true;

                db.SaveChanges();
                Close(true);
            }
        }
        catch (Exception ex)
        {
            StatusText.Foreground = new SolidColorBrush(Color.Parse("#EF4444"));
            StatusText.Text = $"خطا: {ex.Message}";
        }
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        Close(false);
    }
}