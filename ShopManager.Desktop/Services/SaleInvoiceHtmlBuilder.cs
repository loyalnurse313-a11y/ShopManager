using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using ShopManager.Desktop.Model;
using ShopManager.Domain.Entities;
using ShopManager.Domain.Enums;
using ShopManager.Domain.Helpers;

namespace ShopManager.Desktop.Services;

/// <summary>
/// سازنده HTML فاکتور — برای چاپ در اندازه‌های مختلف
/// </summary>
public static class SaleInvoiceHtmlBuilder
{
    /// <summary>
    /// تبدیل لوگو به base64 برای امبد در HTML
    /// </summary>
    private static string? GetLogoBase64(string logoPath)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(logoPath)) return null;
            if (!File.Exists(logoPath)) return null;

            var ext = Path.GetExtension(logoPath).ToLower();
            var mime = ext switch
            {
                ".png" => "image/png",
                ".jpg" or ".jpeg" => "image/jpeg",
                ".bmp" => "image/bmp",
                ".gif" => "image/gif",
                ".ico" => "image/x-icon",
                _ => "image/png"
            };

            var bytes = File.ReadAllBytes(logoPath);
            if (bytes.Length == 0) return null;

            var base64 = Convert.ToBase64String(bytes);
            return $"data:{mime};base64,{base64}";
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// ساخت HTML فاکتور
    /// </summary>
    public static string BuildInvoiceHtml(
        StoreSettings settings,
        string paperSize,
        bool showLogo,
        string invoiceNumber,
        string dateShamsi,
        string customerName,
        string customerPhone,
        bool isCash,
        string? cardTerminal,
        PaymentStatus paymentStatus,
        List<InvoiceItemData> items,
        decimal totalAmount,
        bool autoPrint)
    {
        var isThermal = paperSize == "80mm" || paperSize == "58mm";

        var sb = new StringBuilder();

        sb.AppendLine("<!DOCTYPE html>");
        sb.AppendLine("<html dir='rtl' lang='fa'>");
        sb.AppendLine("<head>");
        sb.AppendLine("<meta charset='UTF-8'>");
        sb.AppendLine("<title>فاکتور فروش</title>");
        sb.AppendLine("<style>");

        // ─── تنظیمات صفحه بر اساس اندازه کاغذ ───
        string pageCss, containerWidth, fontSize, logoMax;

        switch (paperSize)
        {
            case "A5":
                pageCss = "size: A5; margin: 6mm;";
                containerWidth = "135mm";
                fontSize = "10";
                logoMax = "90px";
                break;
            case "80mm":
                pageCss = "size: 80mm auto; margin: 2mm;";
                containerWidth = "76mm";
                fontSize = "9";
                logoMax = "50px";
                break;
            case "58mm":
                pageCss = "size: 58mm auto; margin: 1.5mm;";
                containerWidth = "55mm";
                fontSize = "8";
                logoMax = "40px";
                break;
            default: // A4
                pageCss = "size: A4; margin: 10mm;";
                containerWidth = "190mm";
                fontSize = "12";
                logoMax = "120px";
                break;
        }

        sb.AppendLine("* { font-family: Vazirmatn, Tahoma, sans-serif; box-sizing: border-box; }");
        sb.AppendLine($"body {{ margin: 0; padding: 10mm 6mm; background: white; color: #0F172A; font-size: {fontSize}pt; }}");
        sb.AppendLine($"@page {{ {pageCss} }}");
        sb.AppendLine($".invoice {{ max-width: {containerWidth}; margin: 0 auto; }}");

        // ─── هدر با لوگو ───
        sb.AppendLine(".header { text-align: center; padding-bottom: 20px; border-bottom: 2px solid #0F172A; margin-bottom: 20px; }");
        sb.AppendLine($".header-logo {{ max-width: {logoMax}; max-height: {logoMax}; margin: 0 auto 12px auto; display: block; }}");
        sb.AppendLine(".header h1 { margin: 0; font-size: 1.5em; font-weight: bold; color: #0F172A; }");
        sb.AppendLine(".header .sub { font-size: 0.85em; color: #64748B; margin-top: 4px; }");

        // ─── اطلاعات ───
        sb.AppendLine(".info { margin-bottom: 20px; }");
        sb.AppendLine(".info-row { display: flex; justify-content: space-between; padding: 6px 0; font-size: 0.95em; }");
        sb.AppendLine(".info-label { color: #64748B; }");
        sb.AppendLine(".info-value { color: #0F172A; font-weight: 600; }");

        // ─── جدول ───
        sb.AppendLine("table { width: 100%; border-collapse: collapse; margin-bottom: 20px; }");
        sb.AppendLine("th { background: #F1F5F9; color: #0F172A; padding: 10px; font-size: 0.85em; font-weight: 600; text-align: center; border-bottom: 1px solid #E2E8F0; }");
        sb.AppendLine("td { padding: 10px; font-size: 0.9em; text-align: center; border-bottom: 1px solid #F1F5F9; }");
        sb.AppendLine("td.name { text-align: right; font-weight: 600; color: #0F172A; }");
        sb.AppendLine("td.total { font-weight: bold; color: #047857; }");

        // ─── جمع کل ───
        sb.AppendLine(".summary { background: #F1F5F9; padding: 16px 20px; margin-bottom: 20px; border-radius: 6px; display: flex; justify-content: space-between; align-items: center; border-right: 4px solid #1E40AF; }");
        sb.AppendLine(".summary-row { display: flex; justify-content: space-between; width: 100%; align-items: center; }");
        sb.AppendLine(".summary-label { font-weight: 600; color: #0F172A; font-size: 1em; }");
        sb.AppendLine(".summary-amount { font-weight: bold; color: #1E40AF; font-size: 1.2em; }");

        // ─── فوتر ───
        sb.AppendLine(".footer { text-align: center; padding-top: 16px; border-top: 1px solid #E2E8F0; font-size: 0.9em; color: #64748B; margin-top: 20px; }");

        // ─── سبک رسید حرارتی ───
        if (isThermal)
        {
            sb.AppendLine(".invoice { padding: 0 3px; }");
            sb.AppendLine("th { background: white; color: #0F172A; border: none; border-bottom: 1px dashed #94A3B8; padding: 4px 2px; }");
            sb.AppendLine("td { border: none; padding: 4px 2px; }");
            sb.AppendLine("table { border-top: 1px dashed #94A3B8; border-bottom: 1px dashed #94A3B8; }");
            sb.AppendLine(".summary { background: white; border: none; border-top: 2px solid #0F172A; border-bottom: 2px solid #0F172A; border-radius: 0; padding: 8px 0; }");
            sb.AppendLine(".summary-amount { color: #0F172A; }");
            sb.AppendLine(".header { border-bottom: 1px dashed #94A3B8; padding-bottom: 8px; }");
            sb.AppendLine(".footer { border-top: 1px dashed #94A3B8; }");
        }

        sb.AppendLine("</style>");
        sb.AppendLine("</head>");
        sb.AppendLine("<body>");
        sb.AppendLine("<div class='invoice'>");

        // ═══════════ هدر با لوگو ═══════════
        sb.AppendLine("<div class='header'>");

        var logoShown = false;

        if (showLogo)
        {
            var logoData = GetLogoBase64(settings.LogoPath);
            if (logoData != null)
            {
                sb.AppendLine($"<img src='{logoData}' class='header-logo' alt='لوگو' />");
                logoShown = true;
            }
        }

        // escape نام فروشگاه برای جلوگیری از XSS
        sb.AppendLine($"<h1>{HtmlEncoder.Encode(settings.StoreName)}</h1>");

        if (!string.IsNullOrWhiteSpace(settings.Phone))
        {
            // escape تلفن فروشگاه برای جلوگیری از XSS
            sb.AppendLine($"<div class='sub'>{HtmlEncoder.Encode(PersianNumber.ToPersianDigits(settings.Phone))}</div>");
        }

        if (!string.IsNullOrWhiteSpace(settings.Address))
        {
            // escape آدرس فروشگاه برای جلوگیری از XSS
            sb.AppendLine($"<div class='sub'>{HtmlEncoder.Encode(settings.Address)}</div>");
        }

        sb.AppendLine("</div>");

        // ═══════════ اطلاعات فاکتور ═══════════
        sb.AppendLine("<div class='info'>");

        // escape شماره فاکتور برای جلوگیری از XSS
        sb.AppendLine($"<div class='info-row'><span class='info-label'>شماره فاکتور:</span><span class='info-value'>{HtmlEncoder.Encode(PersianNumber.ToPersianDigits(invoiceNumber))}</span></div>");
        // escape تاریخ برای جلوگیری از XSS
        sb.AppendLine($"<div class='info-row'><span class='info-label'>تاریخ:</span><span class='info-value'>{HtmlEncoder.Encode(PersianNumber.ToPersianDigits(dateShamsi))}</span></div>");

        if (!string.IsNullOrWhiteSpace(customerName) && customerName != "—")
        {
            // escape نام مشتری برای جلوگیری از XSS
            sb.AppendLine($"<div class='info-row'><span class='info-label'>مشتری:</span><span class='info-value'>{HtmlEncoder.Encode(customerName)}</span></div>");
        }

        if (!string.IsNullOrWhiteSpace(customerPhone) && customerPhone != "—")
        {
            // escape تلفن مشتری برای جلوگیری از XSS
            sb.AppendLine($"<div class='info-row'><span class='info-label'>تلفن:</span><span class='info-value'>{HtmlEncoder.Encode(PersianNumber.ToPersianDigits(customerPhone))}</span></div>");
        }

        var payText = paymentStatus switch
        {
            PaymentStatus.Cash => "نقدی",
            PaymentStatus.Card => string.IsNullOrWhiteSpace(cardTerminal)
                ? "کارتی"
                // escape شماره کارت‌خوان برای جلوگیری از XSS
                : $"کارتی ({HtmlEncoder.Encode(cardTerminal)})",
            PaymentStatus.Credit => "نسیه",
            _ => "—"
        };
        sb.AppendLine($"<div class='info-row'><span class='info-label'>روش پرداخت:</span><span class='info-value'>{payText}</span></div>");

        sb.AppendLine("</div>");

        // ═══════════ جدول اقلام ═══════════
        sb.AppendLine("<table>");
        sb.AppendLine("<thead><tr>");
        sb.AppendLine("<th style='width: 30px;'>#</th>");
        sb.AppendLine("<th>نام کالا</th>");
        sb.AppendLine("<th style='width: 50px;'>تعداد</th>");
        sb.AppendLine("<th style='width: 80px;'>قیمت</th>");
        sb.AppendLine("<th style='width: 90px;'>جمع</th>");
        sb.AppendLine("</tr></thead>");
        sb.AppendLine("<tbody>");

        int rowNum = 1;
        foreach (var item in items)
        {
            sb.AppendLine("<tr>");
            sb.AppendLine($"<td>{PersianNumber.ToPersian(rowNum)}</td>");
            // escape نام کالا برای جلوگیری از XSS (نمایش متن خام، نه HTML)
            sb.AppendLine($"<td class='name'>{HtmlEncoder.Encode(item.ItemName)}</td>");
            sb.AppendLine($"<td>{PersianNumber.ToPersian(item.Qty)}</td>");
            sb.AppendLine($"<td>{PersianNumber.ToPersian(item.UnitPrice)}</td>");
            sb.AppendLine($"<td class='total'>{PersianNumber.ToPersian(item.Total)}</td>");
            sb.AppendLine("</tr>");
            rowNum++;
        }

        sb.AppendLine("</tbody>");
        sb.AppendLine("</table>");

        // ═══════════ جمع کل ═══════════
        sb.AppendLine("<div class='summary'>");
        sb.AppendLine("<div class='summary-row'>");
        sb.AppendLine("<span class='summary-label'>جمع کل:</span>");
        sb.AppendLine($"<span class='summary-amount'>{PersianNumber.ToToman(totalAmount)}</span>");
        sb.AppendLine("</div>");
        sb.AppendLine("</div>");

        // ═══════════ فوتر ═══════════
        if (!string.IsNullOrWhiteSpace(settings.FooterText))
        {
            sb.AppendLine("<div class='footer'>");
            // escape متن پاورقی برای جلوگیری از XSS
            sb.AppendLine(HtmlEncoder.Encode(settings.FooterText));
            sb.AppendLine("</div>");
        }

        sb.AppendLine("</div>");

        // ═══════════ چاپ خودکار ═══════════
        if (autoPrint)
        {
            sb.AppendLine("<script>");
            sb.AppendLine("window.onload = function() {");
            sb.AppendLine("  setTimeout(function() { window.print(); }, 500);");
            sb.AppendLine("};");
            sb.AppendLine("</script>");
        }

        sb.AppendLine("</body>");
        sb.AppendLine("</html>");

        return sb.ToString();
    }

    /// <summary>
    /// پیش‌نمایش فاکتور نمونه
    /// </summary>
    public static string BuildPreviewHtml(StoreSettings settings, string paperSize, bool showLogo)
    {
        var items = new List<InvoiceItemData>
        {
            new InvoiceItemData { ItemName = "لیوان یکبار مصرف ۲۰۰cc", Unit = "بسته", Qty = 5, UnitPrice = 25000, Total = 125000 },
            new InvoiceItemData { ItemName = "بشقاب بزرگ یکبار مصرف", Unit = "بسته", Qty = 3, UnitPrice = 38000, Total = 114000 },
            new InvoiceItemData { ItemName = "قاشق چای‌خوری پلاستیکی", Unit = "کارتن", Qty = 2, UnitPrice = 95000, Total = 190000 },
            new InvoiceItemData { ItemName = "دستمال کاغذی جیبی", Unit = "عدد", Qty = 10, UnitPrice = 8500, Total = 85000 },
        };

        var total = items.Sum(i => i.Total);

        return BuildInvoiceHtml(
            settings,
            paperSize,
            showLogo,
            "POS-1405-0001",
            JalaliDate.TodayShamsi(),
            "مشتری نمونه",
            "09123456789",
            false,
            "بانک ملی - صندوق ۱",
            PaymentStatus.Card,
            items,
            total,
            autoPrint: false);
    }
}

/// <summary>
/// داده‌های یه قلم فاکتور
/// </summary>
public class InvoiceItemData
{
    public string ItemName { get; set; } = "";
    public string Unit { get; set; } = "";
    public decimal Qty { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal Total { get; set; }
}