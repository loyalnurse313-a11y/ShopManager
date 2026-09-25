using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using ShopManager.Desktop.Services;
using ShopManager.Desktop.ViewModels;
using ShopManager.Domain.Entities;
using ShopManager.Domain.Enums;
using ShopManager.Domain.Helpers;

// ═══ رفع تداخل با DocumentFormat.OpenXml ═══
using Color = Avalonia.Media.Color;
using FontFamily = Avalonia.Media.FontFamily;

namespace ShopManager.Desktop.Views;

public partial class UsersWindow : Window
{
    private List<UserRow> _allRows = new();

    public UsersWindow()
    {
        InitializeComponent();

        if (!AuthService.IsAdmin)
        {
            Close();
            return;
        }

        LoadUsers();
    }

    private void LoadUsers()
    {
        try
        {
            using var db = DatabaseService.CreateContext();

            var users = db.Users
                .OrderByDescending(u => u.Role)
                .ThenBy(u => u.Username)
                .ToList();

            _allRows = users.Select((u, index) => new UserRow
            {
                Id = u.Id,
                RowNumber = PersianNumber.ToPersian(index + 1),
                Username = u.Username,
                FullName = u.FullName,
                RoleDisplay = u.Role == UserRole.Admin ? "👑 ادمین" : "👤 کاربر",
                StatusDisplay = u.IsActive ? "✅ فعال" : "⛔ غیرفعال",
                IsActive = u.IsActive,
                LastLoginDisplay = u.LastLoginAt.HasValue
                    ? PersianNumber.ToPersianDigits(JalaliDate.ToShamsi(u.LastLoginAt.Value)) + " " +
                      PersianNumber.ToPersianDigits(u.LastLoginAt.Value.ToLocalTime().ToString("HH:mm"))
                    : "—",
                CreatedAtDisplay = PersianNumber.ToPersianDigits(JalaliDate.ToShamsi(u.CreatedAt)),
                RawUsername = u.Username,
                RawFullName = u.FullName
            }).ToList();

            ApplyFilter();
        }
        catch (Exception ex)
        {
            StatusText.Text = $"خطا: {ex.Message}";
        }
    }

    private void ApplyFilter()
    {
        var query = SearchBox.Text?.Trim() ?? "";
        var normalized = Normalize(query);

        List<UserRow> filtered;
        if (string.IsNullOrWhiteSpace(normalized))
        {
            filtered = _allRows;
        }
        else
        {
            filtered = _allRows
                .Where(r => Normalize(r.RawUsername).Contains(normalized) ||
                            Normalize(r.RawFullName).Contains(normalized))
                .ToList();
        }

        UsersList.ItemsSource = null;
        UsersList.ItemsSource = filtered;

        if (filtered.Count == 0)
        {
            EmptyPanel.IsVisible = true;
            UsersScrollViewer.IsVisible = false;
            EmptyTitle.Text = string.IsNullOrWhiteSpace(normalized)
                ? "هنوز کاربری ثبت نشده"
                : "کاربری با این جستجو یافت نشد";
        }
        else
        {
            EmptyPanel.IsVisible = false;
            UsersScrollViewer.IsVisible = true;
        }

        TotalCountText.Text = $"تعداد: {PersianNumber.ToPersian(filtered.Count)}";
        StatusText.Text = $"تعداد کاربران: {PersianNumber.ToPersian(_allRows.Count)}";
    }

    private static string Normalize(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "";
        return s.Replace("ي", "ی")
                .Replace("ك", "ک")
                .Replace("ة", "ه")
                .ToLowerInvariant()
                .Trim();
    }

    private void OnSearchChanged(object? sender, TextChangedEventArgs e)
    {
        ApplyFilter();
    }

    private async void OnAddUserClick(object? sender, RoutedEventArgs e)
    {
        var editWindow = new UserEditWindow(null);
        var result = await editWindow.ShowDialog<bool>(this);

        if (result)
        {
            LoadUsers();
            StatusText.Foreground = new SolidColorBrush(Color.Parse("#10B981"));
            StatusText.Text = "✓ کاربر جدید اضافه شد";
        }
    }

    private async void OnEditUserClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (sender is not Button btn || btn.Tag is not int userId) return;

            using var db = DatabaseService.CreateContext();
            var user = db.Users.FirstOrDefault(u => u.Id == userId);
            if (user == null) return;

            var editWindow = new UserEditWindow(user);
            var result = await editWindow.ShowDialog<bool>(this);

            if (result)
            {
                LoadUsers();
                StatusText.Foreground = new SolidColorBrush(Color.Parse("#10B981"));
                StatusText.Text = "✓ تغییرات ذخیره شد";
            }
        }
        catch (Exception ex)
        {
            StatusText.Foreground = new SolidColorBrush(Color.Parse("#EF4444"));
            StatusText.Text = $"خطا: {ex.Message}";
        }
    }

    private async void OnChangePasswordClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (sender is not Button btn || btn.Tag is not int userId) return;

            using var db = DatabaseService.CreateContext();
            var user = db.Users.FirstOrDefault(u => u.Id == userId);
            if (user == null) return;

            var result = await ShowChangePasswordDialog(user);

            if (result)
            {
                StatusText.Foreground = new SolidColorBrush(Color.Parse("#10B981"));
                StatusText.Text = $"✓ رمز عبور «{user.Username}» تغییر کرد";
            }
        }
        catch (Exception ex)
        {
            StatusText.Foreground = new SolidColorBrush(Color.Parse("#EF4444"));
            StatusText.Text = $"خطا: {ex.Message}";
        }
    }

    private async System.Threading.Tasks.Task<bool> ShowChangePasswordDialog(User user)
    {
        var dialog = new Window
        {
            Title = $"تغییر رمز — {user.Username}",
            Width = 420,
            Height = 350,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            FlowDirection = FlowDirection.RightToLeft,
            FontFamily = new FontFamily("Vazirmatn,IRANSans,Segoe UI"),
            Background = new SolidColorBrush(Color.Parse("#F1F5F9"))
        };

        var panel = new StackPanel
        {
            Margin = new Avalonia.Thickness(24),
            Spacing = 14
        };

        panel.Children.Add(new TextBlock
        {
            Text = $"تغییر رمز عبور «{user.Username}»",
            FontSize = 16,
            FontWeight = FontWeight.Bold,
            Foreground = new SolidColorBrush(Color.Parse("#0F172A"))
        });

        panel.Children.Add(new TextBlock
        {
            Text = "رمز جدید:",
            FontSize = 13,
            FontWeight = FontWeight.SemiBold,
            Foreground = new SolidColorBrush(Color.Parse("#334155"))
        });

        var newPassBox = new TextBox
        {
            PasswordChar = '●',
            FontSize = 14,
            Padding = new Avalonia.Thickness(12, 10),
            Watermark = "حداقل ۴ کاراکتر"
        };
        panel.Children.Add(newPassBox);

        panel.Children.Add(new TextBlock
        {
            Text = "تکرار رمز جدید:",
            FontSize = 13,
            FontWeight = FontWeight.SemiBold,
            Foreground = new SolidColorBrush(Color.Parse("#334155"))
        });

        var confirmPassBox = new TextBox
        {
            PasswordChar = '●',
            FontSize = 14,
            Padding = new Avalonia.Thickness(12, 10)
        };
        panel.Children.Add(confirmPassBox);

        var statusText = new TextBlock
        {
            Text = "",
            FontSize = 12,
            FontWeight = FontWeight.SemiBold,
            Foreground = new SolidColorBrush(Color.Parse("#DC2626"))
        };
        panel.Children.Add(statusText);

        var btnPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 10
        };

        var saveBtn = new Button
        {
            Content = "ذخیره",
            FontSize = 14,
            FontWeight = FontWeight.Bold,
            Background = new SolidColorBrush(Color.Parse("#10B981")),
            Foreground = new SolidColorBrush(Color.Parse("#FFFFFF")),
            Padding = new Avalonia.Thickness(24, 10)
        };

        saveBtn.Click += (s, e) =>
        {
            var newPass = newPassBox.Text ?? "";
            var confirmPass = confirmPassBox.Text ?? "";

            if (string.IsNullOrWhiteSpace(newPass) || newPass.Length < 4)
            {
                statusText.Text = "رمز باید حداقل ۴ کاراکتر باشد";
                return;
            }

            if (newPass != confirmPass)
            {
                statusText.Text = "رمز و تکرارش یکسان نیستند";
                return;
            }

            try
            {
                using var db = DatabaseService.CreateContext();
                var dbUser = db.Users.FirstOrDefault(u => u.Id == user.Id);
                if (dbUser == null) return;

                var salt = PasswordHasher.GenerateSalt();
                var hash = PasswordHasher.HashPassword(newPass, salt);

                dbUser.PasswordHash = hash;
                dbUser.PasswordSalt = salt;
                db.SaveChanges();

                dialog.Close(true);
            }
            catch (Exception ex)
            {
                statusText.Text = $"خطا: {ex.Message}";
            }
        };

        var cancelBtn = new Button
        {
            Content = "انصراف",
            FontSize = 14,
            Background = new SolidColorBrush(Color.Parse("#F1F5F9")),
            Foreground = new SolidColorBrush(Color.Parse("#475569")),
            BorderBrush = new SolidColorBrush(Color.Parse("#CBD5E1")),
            BorderThickness = new Avalonia.Thickness(1),
            Padding = new Avalonia.Thickness(24, 10)
        };
        cancelBtn.Click += (s, e) => dialog.Close(false);

        btnPanel.Children.Add(saveBtn);
        btnPanel.Children.Add(cancelBtn);
        panel.Children.Add(btnPanel);

        dialog.Content = panel;

        return await dialog.ShowDialog<bool>(this);
    }

    private async void OnDeleteUserClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (sender is not Button btn || btn.Tag is not int userId) return;

            using var db = DatabaseService.CreateContext();
            var user = db.Users.FirstOrDefault(u => u.Id == userId);
            if (user == null) return;

            if (AuthService.CurrentUser?.Id == user.Id)
            {
                StatusText.Foreground = new SolidColorBrush(Color.Parse("#EF4444"));
                StatusText.Text = "نمی‌تونی حساب خودت رو حذف کنی";
                return;
            }

            if (user.Role == UserRole.Admin)
            {
                var adminCount = db.Users.Count(u => u.Role == UserRole.Admin && u.IsActive);
                if (adminCount <= 1)
                {
                    StatusText.Foreground = new SolidColorBrush(Color.Parse("#EF4444"));
                    StatusText.Text = "نمی‌تونی آخرین ادمین فعال رو حذف کنی";
                    return;
                }
            }

            var confirm = await ShowConfirmDialog(
                "تأیید حذف",
                $"آیا مطمئنی می‌خوای کاربر «{user.Username}» رو حذف کنی؟\n\n" +
                "این کاربر غیرفعال می‌شه ولی سابقه‌اش می‌مونه."
            );

            if (!confirm) return;

            user.IsActive = false;
            db.SaveChanges();

            LoadUsers();
            StatusText.Foreground = new SolidColorBrush(Color.Parse("#10B981"));
            StatusText.Text = $"✓ کاربر «{user.Username}» غیرفعال شد";
        }
        catch (Exception ex)
        {
            StatusText.Foreground = new SolidColorBrush(Color.Parse("#EF4444"));
            StatusText.Text = $"خطا: {ex.Message}";
        }
    }

    private async System.Threading.Tasks.Task<bool> ShowConfirmDialog(string title, string message)
    {
        var dialog = new Window
        {
            Title = title,
            Width = 440,
            Height = 240,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            FlowDirection = FlowDirection.RightToLeft,
            FontFamily = new FontFamily("Vazirmatn,IRANSans,Segoe UI"),
            Background = new SolidColorBrush(Color.Parse("#F1F5F9"))
        };

        var panel = new StackPanel
        {
            Margin = new Avalonia.Thickness(24),
            Spacing = 16
        };

        panel.Children.Add(new TextBlock
        {
            Text = "⚠️ " + title,
            FontSize = 16,
            FontWeight = FontWeight.Bold,
            Foreground = new SolidColorBrush(Color.Parse("#DC2626"))
        });

        panel.Children.Add(new TextBlock
        {
            Text = message,
            FontSize = 13,
            Foreground = new SolidColorBrush(Color.Parse("#334155")),
            TextWrapping = TextWrapping.Wrap
        });

        var btnPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 10
        };

        var yesBtn = new Button
        {
            Content = "بله، حذف کن",
            FontSize = 14,
            FontWeight = FontWeight.Bold,
            Background = new SolidColorBrush(Color.Parse("#DC2626")),
            Foreground = new SolidColorBrush(Color.Parse("#FFFFFF")),
            Padding = new Avalonia.Thickness(22, 10)
        };
        yesBtn.Click += (s, e) => dialog.Close(true);

        var noBtn = new Button
        {
            Content = "انصراف",
            FontSize = 14,
            Background = new SolidColorBrush(Color.Parse("#F1F5F9")),
            Foreground = new SolidColorBrush(Color.Parse("#475569")),
            BorderBrush = new SolidColorBrush(Color.Parse("#CBD5E1")),
            BorderThickness = new Avalonia.Thickness(1),
            Padding = new Avalonia.Thickness(22, 10)
        };
        noBtn.Click += (s, e) => dialog.Close(false);

        btnPanel.Children.Add(yesBtn);
        btnPanel.Children.Add(noBtn);
        panel.Children.Add(btnPanel);

        dialog.Content = panel;

        return await dialog.ShowDialog<bool>(this);
    }

    private void OnRefreshClick(object? sender, RoutedEventArgs e)
    {
        LoadUsers();
    }

    private void OnLoginHistoryClick(object? sender, RoutedEventArgs e)
    {
        new LoginHistoryWindow().Show();
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}