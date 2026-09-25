using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using ShopManager.Desktop.Services;
using ShopManager.Domain.Entities;
using ShopManager.Domain.Helpers;

// ═══ رفع تداخل با DocumentFormat.OpenXml ═══
using Color = Avalonia.Media.Color;

namespace ShopManager.Desktop.Views;

public partial class AddItemWindow : Window
{
    private Item? _editingItem;

    /// <summary>سازنده — اگه itemData null باشه یعنی افزودن جدید</summary>
    public AddItemWindow(Item? itemData = null)
    {
        InitializeComponent();

        _editingItem = itemData;

        if (_editingItem == null)
        {
            HeaderText.Text = "افزودن کالای جدید";
            InitialStockCard.IsVisible = true;
        }
        else
        {
            HeaderText.Text = $"ویرایش کالا: {_editingItem.Name}";
            InitialStockCard.IsVisible = false;

            // ─── پر کردن فرم ───
            ItemCodeTextBox.Text = _editingItem.ItemCode.ToString();
            NameTextBox.Text = _editingItem.Name;
            CategoryTextBox.Text = _editingItem.Category ?? "";

            UnitComboBox.SelectedIndex = _editingItem.Unit switch
            {
                "عدد" => 0,
                "بسته" => 1,
                "رول" => 2,
                "کارتن" => 3,
                "کیلوگرم" => 4,
                "لیتر" => 5,
                _ => 0
            };

            if (_editingItem.OpeningWarehouseUnitCost.HasValue)
            {
                PurchasePriceTextBox.Text = _editingItem.OpeningWarehouseUnitCost.Value.ToString("0");
            }

            MarkupPctTextBox.Text = _editingItem.MarkupPct.ToString("0");

            if (_editingItem.SalePrice.HasValue)
            {
                SalePriceTextBox.Text = _editingItem.SalePrice.Value.ToString("0");
            }

            CriticalPctTextBox.Text = (_editingItem.LowStockCriticalPct * 100).ToString("0");
            WarningPctTextBox.Text = (_editingItem.LowStockWarningPct * 100).ToString("0");

            if (_editingItem.LowStockCriticalFixed.HasValue)
            {
                CriticalFixedTextBox.Text = _editingItem.LowStockCriticalFixed.Value.ToString("0");
            }

            if (_editingItem.LowStockWarningFixed.HasValue)
            {
                WarningFixedTextBox.Text = _editingItem.LowStockWarningFixed.Value.ToString("0");
            }

            // ─── پیش‌نمایش ───
            UpdatePreview();
        }
    }

    /// <summary>وقتی قیمت خرید یا درصد عوض شد، پیش‌نمایش رو حساب کن</summary>
    private void OnPriceChanged(object? sender, TextChangedEventArgs e)
    {
        UpdatePreview();
    }

    private void UpdatePreview()
    {
        var purchaseText = PersianNumber.ToEnglishDigits(PurchasePriceTextBox.Text ?? "").Trim();
        var pctText = PersianNumber.ToEnglishDigits(MarkupPctTextBox.Text ?? "").Trim();

        if (decimal.TryParse(purchaseText, out var purchase) && purchase > 0 &&
            decimal.TryParse(pctText, out var pct))
        {
            var sale = purchase * (1 + pct / 100m);
            PreviewSalePriceText.Text = PersianNumber.ToToman(decimal.Round(sale, 0));
        }
        else
        {
            PreviewSalePriceText.Text = "—";
        }
    }

    private void OnSaveClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            StatusText.Text = "";

            // ─── خواندن مقادیر ───
            var codeText = PersianNumber.ToEnglishDigits(ItemCodeTextBox.Text ?? "").Trim();
            var nameText = NameTextBox.Text?.Trim() ?? "";
            var categoryText = CategoryTextBox.Text?.Trim();
            var unit = (UnitComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "عدد";

            var purchaseText = PersianNumber.ToEnglishDigits(PurchasePriceTextBox.Text ?? "").Trim();
            var pctText = PersianNumber.ToEnglishDigits(MarkupPctTextBox.Text ?? "").Trim();
            var saleText = PersianNumber.ToEnglishDigits(SalePriceTextBox.Text ?? "").Trim();

            var criticalPctText = PersianNumber.ToEnglishDigits(CriticalPctTextBox.Text ?? "10").Trim();
            var warningPctText = PersianNumber.ToEnglishDigits(WarningPctTextBox.Text ?? "25").Trim();
            var criticalFixedText = PersianNumber.ToEnglishDigits(CriticalFixedTextBox.Text ?? "").Trim();
            var warningFixedText = PersianNumber.ToEnglishDigits(WarningFixedTextBox.Text ?? "").Trim();

            var openingWhText = PersianNumber.ToEnglishDigits(OpeningWarehouseQtyTextBox.Text ?? "0").Trim();
            var openingShopText = PersianNumber.ToEnglishDigits(OpeningShopQtyTextBox.Text ?? "0").Trim();

            // ─── اعتبارسنجی ───
            if (string.IsNullOrWhiteSpace(codeText))
            {
                StatusText.Text = "کد کالا را وارد کنید";
                return;
            }

            if (!int.TryParse(codeText, out int itemCode))
            {
                StatusText.Text = "کد کالا باید عدد باشد";
                return;
            }

            if (string.IsNullOrWhiteSpace(nameText))
            {
                StatusText.Text = "نام کالا را وارد کنید";
                return;
            }

            decimal markupPct = 30;
            if (!string.IsNullOrWhiteSpace(pctText) && decimal.TryParse(pctText, out var pctVal))
            {
                markupPct = pctVal;
            }

            using var db = DatabaseService.CreateContext();

            // ─── چک یکتا بودن کد ───
            if (_editingItem == null)
            {
                if (db.Items.Any(i => i.ItemCode == itemCode))
                {
                    StatusText.Text = $"کد کالا {itemCode} قبلاً استفاده شده";
                    return;
                }
            }
            else
            {
                if (db.Items.Any(i => i.Id != _editingItem.Id && i.ItemCode == itemCode))
                {
                    StatusText.Text = $"کد کالا {itemCode} قبلاً استفاده شده";
                    return;
                }
            }

            // ─── قیمت خرید ───
            decimal? purchasePrice = null;
            if (!string.IsNullOrWhiteSpace(purchaseText) && decimal.TryParse(purchaseText, out var pp))
                purchasePrice = pp;

            // ─── قیمت فروش ───
            decimal? salePrice = null;
            if (!string.IsNullOrWhiteSpace(saleText) && decimal.TryParse(saleText, out var sp))
                salePrice = sp;

            // ─── حد هشدار درصدی ───
            decimal criticalPct = 0.10m;
            if (decimal.TryParse(criticalPctText, out var cp) && cp >= 0)
                criticalPct = cp / 100m;

            decimal warningPct = 0.25m;
            if (decimal.TryParse(warningPctText, out var wp) && wp >= 0)
                warningPct = wp / 100m;

            // ─── حد هشدار ثابت ───
            decimal? criticalFixed = null;
            if (!string.IsNullOrWhiteSpace(criticalFixedText) && decimal.TryParse(criticalFixedText, out var cf))
                criticalFixed = cf;

            decimal? warningFixed = null;
            if (!string.IsNullOrWhiteSpace(warningFixedText) && decimal.TryParse(warningFixedText, out var wf))
                warningFixed = wf;

            if (_editingItem == null)
            {
                // ═══ افزودن جدید ═══
                decimal openingWh = 0;
                if (decimal.TryParse(openingWhText, out var owh))
                    openingWh = owh;

                decimal openingShop = 0;
                if (decimal.TryParse(openingShopText, out var osh))
                    openingShop = osh;

                var item = new Item
                {
                    ItemCode = itemCode,
                    Name = nameText,
                    Category = string.IsNullOrWhiteSpace(categoryText) ? null : categoryText,
                    Unit = unit,
                    OpeningWarehouseQty = openingWh,
                    OpeningWarehouseUnitCost = purchasePrice,
                    OpeningShopQty = openingShop,
                    MarkupPct = markupPct,
                    SalePrice = salePrice,
                    LowStockCriticalPct = criticalPct,
                    LowStockWarningPct = warningPct,
                    LowStockCriticalFixed = criticalFixed,
                    LowStockWarningFixed = warningFixed,
                    CreatedAt = DateTime.UtcNow,
                    IsActive = true
                };

                db.Items.Add(item);
                db.SaveChanges();

                Close(true);
            }
            else
            {
                // ═══ ویرایش ═══
                var item = db.Items.FirstOrDefault(i => i.Id == _editingItem.Id);
                if (item == null)
                {
                    StatusText.Text = "کالا پیدا نشد";
                    return;
                }

                item.ItemCode = itemCode;
                item.Name = nameText;
                item.Category = string.IsNullOrWhiteSpace(categoryText) ? null : categoryText;
                item.Unit = unit;
                item.MarkupPct = markupPct;
                item.SalePrice = salePrice;
                item.LowStockCriticalPct = criticalPct;
                item.LowStockWarningPct = warningPct;
                item.LowStockCriticalFixed = criticalFixed;
                item.LowStockWarningFixed = warningFixed;

                // بهای موجودی اولیه (فقط اگه کاربر پر کرده)
                if (purchasePrice.HasValue)
                {
                    item.OpeningWarehouseUnitCost = purchasePrice;
                }

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