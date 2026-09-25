using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using ShopManager.Domain.Entities;
using ShopManager.Domain.Enums;
using ShopManager.Infrastructure.Services;

namespace ShopManager.Desktop.Services;

/// <summary>
/// کمک‌کننده برای باز کردن Save Dialog و فراخوانی ExcelExportService
/// </summary>
public static class ExcelExportHelper
{
    /// <summary>انتخاب مسیر و ذخیره فایل</summary>
    private static async Task<string?> PickSavePathAsync(Window owner, string suggestedName)
    {
        var file = await owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "ذخیره فایل اکسل",
            SuggestedFileName = suggestedName,
            DefaultExtension = "xlsx",
            FileTypeChoices = new[]
            {
                new FilePickerFileType("Excel")
                {
                    Patterns = new[] { "*.xlsx" }
                }
            }
        });

        return file?.Path.LocalPath;
    }

    /// <summary>خروجی اکسل خریدها</summary>
    public static async Task ExportPurchasesAsync(Window owner,
        List<Purchase> purchases, Dictionary<int, Item> itemsDict)
    {
        try
        {
            var path = await PickSavePathAsync(owner,
                $"سابقه-خرید-{Domain.Helpers.JalaliDate.TodayShamsi().Replace('/', '-')}.xlsx");

            if (string.IsNullOrWhiteSpace(path)) return;

            ExcelExportService.ExportPurchases(path, purchases, itemsDict);
        }
        catch (Exception ex)
        {
            throw new Exception($"خطا در خروجی اکسل: {ex.Message}");
        }
    }

    /// <summary>خروجی اکسل فروش‌ها</summary>
    public static async Task ExportSalesAsync(Window owner,
        List<Sale> sales, Dictionary<int, Item> itemsDict)
    {
        try
        {
            var path = await PickSavePathAsync(owner,
                $"سابقه-فروش-{Domain.Helpers.JalaliDate.TodayShamsi().Replace('/', '-')}.xlsx");

            if (string.IsNullOrWhiteSpace(path)) return;

            ExcelExportService.ExportSales(path, sales, itemsDict);
        }
        catch (Exception ex)
        {
            throw new Exception($"خطا در خروجی اکسل: {ex.Message}");
        }
    }

    /// <summary>خروجی اکسل کالاها</summary>
    public static async Task ExportItemsAsync(Window owner,
        List<(Item, decimal, decimal, decimal, decimal)> rows)
    {
        try
        {
            var path = await PickSavePathAsync(owner,
                $"لیست-کالاها-{Domain.Helpers.JalaliDate.TodayShamsi().Replace('/', '-')}.xlsx");

            if (string.IsNullOrWhiteSpace(path)) return;

            ExcelExportService.ExportItems(path, rows);
        }
        catch (Exception ex)
        {
            throw new Exception($"خطا در خروجی اکسل: {ex.Message}");
        }
    }

    /// <summary>خروجی اکسل دفتر صندوق</summary>
    public static async Task ExportCashLedgerAsync(Window owner, List<CashLedger> ledger)
    {
        try
        {
            var path = await PickSavePathAsync(owner,
                $"دفتر-صندوق-{Domain.Helpers.JalaliDate.TodayShamsi().Replace('/', '-')}.xlsx");

            if (string.IsNullOrWhiteSpace(path)) return;

            ExcelExportService.ExportCashLedger(path, ledger);
        }
        catch (Exception ex)
        {
            throw new Exception($"خطا در خروجی اکسل: {ex.Message}");
        }
    }

    /// <summary>خروجی اکسل مشتری‌ها</summary>
    public static async Task ExportCustomersAsync(Window owner, List<Customer> customers)
    {
        try
        {
            var path = await PickSavePathAsync(owner,
                $"مشتری‌ها-{Domain.Helpers.JalaliDate.TodayShamsi().Replace('/', '-')}.xlsx");

            if (string.IsNullOrWhiteSpace(path)) return;

            ExcelExportService.ExportCustomers(path, customers);
        }
        catch (Exception ex)
        {
            throw new Exception($"خطا در خروجی اکسل: {ex.Message}");
        }
    }
}