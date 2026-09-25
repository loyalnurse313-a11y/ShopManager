using System;
using System.Collections.Generic;
using System.IO;
using ClosedXML.Excel;
using ShopManager.Domain.Entities;
using ShopManager.Domain.Enums;

namespace ShopManager.Infrastructure.Services;

/// <summary>
/// سرویس خروجی اکسل — همه گزارش‌ها
/// </summary>
public static class ExcelExportService
{
    private static string GetPayType(PaymentStatus status) => status switch
    {
        PaymentStatus.Cash => "نقدی",
        PaymentStatus.Card => "کارتی",
        PaymentStatus.Credit => "نسیه",
        _ => "—"
    };

    public static void ExportPurchases(
        string filePath,
        List<Purchase> purchases,
        Dictionary<int, Item> itemsDict)
    {
        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add("سابقه خرید");

        ws.RightToLeft = true;

        var headers = new[]
        {
            "ردیف", "تاریخ", "کد کالا", "نام کالا", "واحد",
            "تعداد", "قیمت واحد", "جمع", "تأمین‌کننده", "وضعیت پرداخت"
        };

        for (int i = 0; i < headers.Length; i++)
        {
            var cell = ws.Cell(1, i + 1);
            cell.Value = headers[i];
            cell.Style.Font.Bold = true;
            cell.Style.Font.FontColor = XLColor.White;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#4F46E5");
            cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        }

        int row = 2;
        int index = 1;

        foreach (var p in purchases)
        {
            var item = itemsDict.GetValueOrDefault(p.ItemId);

            ws.Cell(row, 1).Value = index;
            ws.Cell(row, 2).Value = p.DateShamsi;
            ws.Cell(row, 3).Value = item?.ItemCode.ToString() ?? "—";
            ws.Cell(row, 4).Value = item?.Name ?? "—";
            ws.Cell(row, 5).Value = item?.Unit ?? "—";
            ws.Cell(row, 6).Value = p.Qty;
            ws.Cell(row, 7).Value = p.UnitCost;
            ws.Cell(row, 8).Value = p.TotalCost;
            ws.Cell(row, 9).Value = p.SupplierNote ?? "";
            ws.Cell(row, 10).Value = GetPayType(p.PaymentStatus);

            for (int col = 1; col <= 10; col++)
            {
                ws.Cell(row, col).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                ws.Cell(row, col).Style.Border.OutsideBorderColor = XLColor.FromHtml("#E2E8F0");
            }

            if (row % 2 == 0)
            {
                for (int col = 1; col <= 10; col++)
                {
                    ws.Cell(row, col).Style.Fill.BackgroundColor = XLColor.FromHtml("#F8FAFC");
                }
            }

            ws.Cell(row, 7).Style.NumberFormat.Format = "#,##0";
            ws.Cell(row, 8).Style.NumberFormat.Format = "#,##0";

            row++;
            index++;
        }

        var totalRow = row + 1;
        ws.Cell(totalRow, 4).Value = "جمع کل:";
        ws.Cell(totalRow, 4).Style.Font.Bold = true;
        ws.Cell(totalRow, 8).FormulaA1 = $"SUM(H2:H{row - 1})";
        ws.Cell(totalRow, 8).Style.Font.Bold = true;
        ws.Cell(totalRow, 8).Style.NumberFormat.Format = "#,##0";
        ws.Cell(totalRow, 8).Style.Fill.BackgroundColor = XLColor.FromHtml("#ECFDF5");

        ws.Columns().AdjustToContents();

        workbook.SaveAs(filePath);
    }

    public static void ExportSales(
        string filePath,
        List<Sale> sales,
        Dictionary<int, Item> itemsDict)
    {
        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add("سابقه فروش");

        ws.RightToLeft = true;

        var headers = new[]
        {
            "ردیف", "تاریخ", "شماره فاکتور", "کد کالا", "نام کالا", "واحد",
            "تعداد", "قیمت فروش", "درآمد", "بهای تمام‌شده", "سود", "وضعیت پرداخت"
        };

        for (int i = 0; i < headers.Length; i++)
        {
            var cell = ws.Cell(1, i + 1);
            cell.Value = headers[i];
            cell.Style.Font.Bold = true;
            cell.Style.Font.FontColor = XLColor.White;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#10B981");
            cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        }

        int row = 2;
        int index = 1;

        foreach (var s in sales)
        {
            var item = itemsDict.GetValueOrDefault(s.ItemId);

            ws.Cell(row, 1).Value = index;
            ws.Cell(row, 2).Value = s.DateShamsi;
            ws.Cell(row, 3).Value = s.InvoiceNumber ?? "—";
            ws.Cell(row, 4).Value = item?.ItemCode.ToString() ?? "—";
            ws.Cell(row, 5).Value = item?.Name ?? "—";
            ws.Cell(row, 6).Value = item?.Unit ?? "—";
            ws.Cell(row, 7).Value = s.Qty;
            ws.Cell(row, 8).Value = s.SaleUnitPrice;
            ws.Cell(row, 9).Value = s.Revenue;
            ws.Cell(row, 10).Value = s.Cost;
            ws.Cell(row, 11).Value = s.Profit;
            ws.Cell(row, 12).Value = GetPayType(s.PaymentStatus);

            for (int col = 1; col <= 12; col++)
            {
                ws.Cell(row, col).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                ws.Cell(row, col).Style.Border.OutsideBorderColor = XLColor.FromHtml("#E2E8F0");
            }

            if (row % 2 == 0)
            {
                for (int col = 1; col <= 12; col++)
                {
                    ws.Cell(row, col).Style.Fill.BackgroundColor = XLColor.FromHtml("#F8FAFC");
                }
            }

            ws.Cell(row, 8).Style.NumberFormat.Format = "#,##0";
            ws.Cell(row, 9).Style.NumberFormat.Format = "#,##0";
            ws.Cell(row, 10).Style.NumberFormat.Format = "#,##0";
            ws.Cell(row, 11).Style.NumberFormat.Format = "#,##0";

            row++;
            index++;
        }

        var totalRow = row + 1;
        ws.Cell(totalRow, 5).Value = "جمع کل:";
        ws.Cell(totalRow, 5).Style.Font.Bold = true;

        ws.Cell(totalRow, 9).FormulaA1 = $"SUM(I2:I{row - 1})";
        ws.Cell(totalRow, 10).FormulaA1 = $"SUM(J2:J{row - 1})";
        ws.Cell(totalRow, 11).FormulaA1 = $"SUM(K2:K{row - 1})";

        ws.Cell(totalRow, 9).Style.Font.Bold = true;
        ws.Cell(totalRow, 10).Style.Font.Bold = true;
        ws.Cell(totalRow, 11).Style.Font.Bold = true;

        ws.Cell(totalRow, 9).Style.NumberFormat.Format = "#,##0";
        ws.Cell(totalRow, 10).Style.NumberFormat.Format = "#,##0";
        ws.Cell(totalRow, 11).Style.NumberFormat.Format = "#,##0";

        ws.Cell(totalRow, 11).Style.Fill.BackgroundColor = XLColor.FromHtml("#ECFDF5");

        ws.Columns().AdjustToContents();

        workbook.SaveAs(filePath);
    }

    public static void ExportItems(
        string filePath,
        List<(Item Item, decimal WarehouseStock, decimal ShopStock, decimal AvgCost, decimal SalePrice)> rows)
    {
        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add("کالاها");

        ws.RightToLeft = true;

        var headers = new[]
        {
            "ردیف", "کد کالا", "نام کالا", "دسته", "واحد",
            "موجودی انبار", "موجودی مغازه", "میانگین خرید", "قیمت فروش"
        };

        for (int i = 0; i < headers.Length; i++)
        {
            var cell = ws.Cell(1, i + 1);
            cell.Value = headers[i];
            cell.Style.Font.Bold = true;
            cell.Style.Font.FontColor = XLColor.White;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#F59E0B");
            cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        }

        int row = 2;
        int index = 1;

        foreach (var r in rows)
        {
            ws.Cell(row, 1).Value = index;
            ws.Cell(row, 2).Value = r.Item.ItemCode;
            ws.Cell(row, 3).Value = r.Item.Name;
            ws.Cell(row, 4).Value = r.Item.Category ?? "—";
            ws.Cell(row, 5).Value = r.Item.Unit;
            ws.Cell(row, 6).Value = r.WarehouseStock;
            ws.Cell(row, 7).Value = r.ShopStock;
            ws.Cell(row, 8).Value = r.AvgCost;
            ws.Cell(row, 9).Value = r.SalePrice;

            for (int col = 1; col <= 9; col++)
            {
                ws.Cell(row, col).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                ws.Cell(row, col).Style.Border.OutsideBorderColor = XLColor.FromHtml("#E2E8F0");
            }

            ws.Cell(row, 8).Style.NumberFormat.Format = "#,##0";
            ws.Cell(row, 9).Style.NumberFormat.Format = "#,##0";

            row++;
            index++;
        }

        ws.Columns().AdjustToContents();

        workbook.SaveAs(filePath);
    }

    public static void ExportCashLedger(
        string filePath,
        List<CashLedger> ledger)
    {
        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add("دفتر صندوق");

        ws.RightToLeft = true;

        var headers = new[]
        {
            "ردیف", "تاریخ", "نوع", "طرف‌حساب", "ورودی", "خروجی", "توضیحات"
        };

        for (int i = 0; i < headers.Length; i++)
        {
            var cell = ws.Cell(1, i + 1);
            cell.Value = headers[i];
            cell.Style.Font.Bold = true;
            cell.Style.Font.FontColor = XLColor.White;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#0284C7");
            cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        }

        int row = 2;
        int index = 1;

        foreach (var l in ledger)
        {
            ws.Cell(row, 1).Value = index;
            ws.Cell(row, 2).Value = l.DateShamsi;
            ws.Cell(row, 3).Value = GetLedgerTypeDisplay(l.Type);
            ws.Cell(row, 4).Value = l.Counterparty ?? "—";
            ws.Cell(row, 5).Value = l.AmountIn ?? 0;
            ws.Cell(row, 6).Value = l.AmountOut ?? 0;
            ws.Cell(row, 7).Value = l.Note ?? "";

            for (int col = 1; col <= 7; col++)
            {
                ws.Cell(row, col).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                ws.Cell(row, col).Style.Border.OutsideBorderColor = XLColor.FromHtml("#E2E8F0");
            }

            ws.Cell(row, 5).Style.NumberFormat.Format = "#,##0";
            ws.Cell(row, 6).Style.NumberFormat.Format = "#,##0";

            row++;
            index++;
        }

        ws.Columns().AdjustToContents();

        workbook.SaveAs(filePath);
    }

    public static void ExportCustomers(
        string filePath,
        List<Customer> customers)
    {
        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add("مشتری‌ها");

        ws.RightToLeft = true;

        var headers = new[]
        {
            "ردیف", "نام", "شماره تماس", "تعداد خرید", "جمع خرید", "آخرین خرید"
        };

        for (int i = 0; i < headers.Length; i++)
        {
            var cell = ws.Cell(1, i + 1);
            cell.Value = headers[i];
            cell.Style.Font.Bold = true;
            cell.Style.Font.FontColor = XLColor.White;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#8B5CF6");
            cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        }

        int row = 2;
        int index = 1;

        foreach (var c in customers)
        {
            ws.Cell(row, 1).Value = index;
            ws.Cell(row, 2).Value = c.Name;
            ws.Cell(row, 3).Value = c.Phone;
            ws.Cell(row, 4).Value = c.PurchaseCount;
            ws.Cell(row, 5).Value = c.TotalPurchasedAmount;
            ws.Cell(row, 6).Value = c.LastPurchaseAt.ToString("yyyy-MM-dd");

            for (int col = 1; col <= 6; col++)
            {
                ws.Cell(row, col).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                ws.Cell(row, col).Style.Border.OutsideBorderColor = XLColor.FromHtml("#E2E8F0");
            }

            ws.Cell(row, 5).Style.NumberFormat.Format = "#,##0";

            row++;
            index++;
        }

        ws.Columns().AdjustToContents();

        workbook.SaveAs(filePath);
    }

    private static string GetLedgerTypeDisplay(LedgerType type)
    {
        return type switch
        {
            LedgerType.InitialCapital => "سرمایه اولیه",
            LedgerType.LoanGiven => "وام داده‌شده",
            LedgerType.LoanRepaid => "وام گرفته‌شده",
            LedgerType.ProfitWithdrawal => "برداشت سود",
            LedgerType.ProfitDistribution => "تقسیم سود",
            _ => "سایر"
        };
    }
}