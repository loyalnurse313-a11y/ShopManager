using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using ShopManager.Desktop.Services;
using ShopManager.Desktop.ViewModels;
using ShopManager.Domain.Helpers;

namespace ShopManager.Desktop.Views;

public partial class CustomersWindow : Window
{
    private List<CustomerRow> _allRows = new();

    public CustomersWindow()
    {
        InitializeComponent();
        LoadCustomers();
    }

    private void LoadCustomers()
    {
        try
        {
            using var db = DatabaseService.CreateContext();

            var customers = db.Customers
                .OrderByDescending(c => c.LastPurchaseAt)
                .ToList();

            _allRows = customers.Select((c, index) => new CustomerRow
            {
                Id = c.Id,
                RowNumber = PersianNumber.ToPersian(index + 1),
                Name = c.Name,
                Phone = PersianNumber.ToPersianDigits(c.Phone),
                PurchaseCount = PersianNumber.ToPersian(c.PurchaseCount),
                LastPurchaseDate = PersianNumber.ToPersianDigits(JalaliDate.ToShamsi(c.LastPurchaseAt)),
                TotalAmount = PersianNumber.ToToman(c.TotalPurchasedAmount),
                RawName = c.Name,
                RawPhone = c.Phone
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

        List<CustomerRow> filtered;
        if (string.IsNullOrWhiteSpace(normalized))
        {
            filtered = _allRows;
        }
        else
        {
            filtered = _allRows
                .Where(r => Normalize(r.RawName).Contains(normalized) ||
                            Normalize(r.RawPhone).Contains(normalized))
                .ToList();
        }

        CustomersList.ItemsSource = null;
        CustomersList.ItemsSource = filtered;

        if (filtered.Count == 0)
        {
            EmptyPanel.IsVisible = true;
            CustomersScrollViewer.IsVisible = false;
            EmptyTitle.Text = string.IsNullOrWhiteSpace(normalized)
                ? "هنوز مشتری‌ای ثبت نشده"
                : "مشتری‌ای با این جستجو یافت نشد";
        }
        else
        {
            EmptyPanel.IsVisible = false;
            CustomersScrollViewer.IsVisible = true;
        }

        StatusText.Text = $"تعداد مشتری‌ها: {PersianNumber.ToPersian(filtered.Count)}";
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

    /// <summary>خروجی اکسل از مشتری‌ها</summary>
    private async void OnExportExcelClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            using var db = DatabaseService.CreateContext();

            var customers = db.Customers
                .OrderByDescending(c => c.LastPurchaseAt)
                .ToList();

            if (customers.Count == 0)
            {
                StatusText.Text = "داده‌ای برای خروجی وجود ندارد";
                return;
            }

            await ExcelExportHelper.ExportCustomersAsync(this, customers);

            StatusText.Text = "✓ فایل اکسل با موفقیت ذخیره شد";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"خطا: {ex.Message}";
        }
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}