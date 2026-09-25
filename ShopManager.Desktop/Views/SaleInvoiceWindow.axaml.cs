using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using ShopManager.Desktop.Services;
using ShopManager.Domain.Enums;
using ShopManager.Domain.Helpers;

namespace ShopManager.Desktop.Views;

public partial class SaleInvoiceWindow : Window
{
    private string _invoiceNumber = "";
    private List<InvoiceItemData> _items = new();
    private string _dateShamsi = "";
    private string _customerName = "—";
    private string _customerPhone = "—";
    private bool _isCash = true;
    private string? _cardTerminal = null;
    private PaymentStatus _paymentStatus = PaymentStatus.Cash;
    private decimal _totalAmount = 0;

    public SaleInvoiceWindow()
    {
        InitializeComponent();
    }

    public void LoadInvoice(string invoiceNumber)
    {
        _invoiceNumber = invoiceNumber;

        try
        {
            using var db = DatabaseService.CreateContext();

            var sales = db.Sales
                .Where(s => s.InvoiceNumber == invoiceNumber && s.EntryType == EntryType.Normal)
                .OrderBy(s => s.Id)
                .ToList();

            if (sales.Count == 0)
            {
                StatusText.Text = "فاکتوری با این شماره پیدا نشد";
                return;
            }

            var itemsDict = db.Items.ToDictionary(i => i.Id);

            _dateShamsi = sales.First().DateShamsi;
            _paymentStatus = sales.First().PaymentStatus;
            _isCash = _paymentStatus == PaymentStatus.Cash;
            _cardTerminal = sales.First().CardTerminal;

            var customerId = sales.First().CustomerId;
            if (customerId.HasValue)
            {
                var customer = db.Customers.FirstOrDefault(c => c.Id == customerId.Value);
                if (customer != null)
                {
                    _customerName = customer.Name;
                    _customerPhone = customer.Phone;
                }
            }

            _items = sales.Select(s =>
            {
                var item = itemsDict.GetValueOrDefault(s.ItemId);
                return new InvoiceItemData
                {
                    ItemName = item?.Name ?? "—",
                    Unit = item?.Unit ?? "—",
                    Qty = s.Qty,
                    UnitPrice = s.SaleUnitPrice,
                    Total = s.Revenue
                };
            }).ToList();

            _totalAmount = _items.Sum(i => i.Total);

            RenderInvoice();
            LoadStoreHeader();
        }
        catch (Exception ex)
        {
            StatusText.Text = $"خطا: {ex.Message}";
        }
    }

    /// <summary>
    /// 🆕 بارگذاری سربرگ فروشگاه (لوگو + نام + تلفن + آدرس)
    /// </summary>
    private void LoadStoreHeader()
    {
        try
        {
            var settings = StoreSettingsService.Current;

            // نام فروشگاه
            if (!string.IsNullOrWhiteSpace(settings.StoreName))
            {
                StoreNameText.Text = settings.StoreName;
            }

            // تلفن
            if (!string.IsNullOrWhiteSpace(settings.Phone))
            {
                StorePhoneText.Text = $"📞 {PersianNumber.ToPersianDigits(settings.Phone)}";
                StorePhoneText.IsVisible = true;
            }
            else
            {
                StorePhoneText.IsVisible = false;
            }

            // آدرس
            if (!string.IsNullOrWhiteSpace(settings.Address))
            {
                StoreAddressText.Text = settings.Address;
                StoreAddressText.IsVisible = true;
            }
            else
            {
                StoreAddressText.IsVisible = false;
            }

            // متن پایین
            if (!string.IsNullOrWhiteSpace(settings.FooterText))
            {
                FooterMessageText.Text = settings.FooterText;
            }
            else
            {
                FooterMessageText.Text = "از خرید شما سپاسگزاریم";
            }

            // 🆕 لوگو
            if (settings.ShowLogoOnInvoice &&
                !string.IsNullOrWhiteSpace(settings.LogoPath) &&
                File.Exists(settings.LogoPath))
            {
                try
                {
                    StoreLogoImage.Source = new Bitmap(settings.LogoPath);
                    LogoBorder.IsVisible = true;
                }
                catch
                {
                    LogoBorder.IsVisible = false;
                }
            }
            else
            {
                LogoBorder.IsVisible = false;
            }
        }
        catch
        {
            LogoBorder.IsVisible = false;
        }
    }

    private void RenderInvoice()
    {
        InvoiceNumberText.Text = PersianNumber.ToPersianDigits(_invoiceNumber);
        DateText.Text = PersianNumber.ToPersianDigits(_dateShamsi);
        CustomerNameText.Text = _customerName;
        CustomerPhoneText.Text = PersianNumber.ToPersianDigits(_customerPhone);
        TotalAmountText.Text = PersianNumber.ToToman(_totalAmount);

        var payTypeText = _paymentStatus switch
        {
            PaymentStatus.Cash => "نقدی",
            PaymentStatus.Card => string.IsNullOrWhiteSpace(_cardTerminal)
                ? "کارتی"
                : $"کارتی ({_cardTerminal})",
            PaymentStatus.Credit => "نسیه",
            _ => "—"
        };
        PaymentStatusText.Text = payTypeText;

        ItemsPanel.Children.Clear();

        int rowNumber = 1;
        foreach (var item in _items)
        {
            var grid = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("28,*,45,55,80,90")
            };

            var numText = new TextBlock
            {
                Text = PersianNumber.ToPersian(rowNumber),
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.Parse("#475569")),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(numText, 0);

            var nameText = new TextBlock
            {
                Text = item.ItemName,
                FontSize = 12,
                FontWeight = FontWeight.SemiBold,
                Foreground = new SolidColorBrush(Color.Parse("#0F172A")),
                VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.Wrap
            };
            Grid.SetColumn(nameText, 1);

            var unitText = new TextBlock
            {
                Text = item.Unit,
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.Parse("#475569")),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(unitText, 2);

            var qtyText = new TextBlock
            {
                Text = PersianNumber.ToPersian(item.Qty),
                FontSize = 12,
                FontWeight = FontWeight.Bold,
                Foreground = new SolidColorBrush(Color.Parse("#4F46E5")),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(qtyText, 3);

            var priceText = new TextBlock
            {
                Text = PersianNumber.ToPersian(item.UnitPrice),
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.Parse("#475569")),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(priceText, 4);

            var totalText = new TextBlock
            {
                Text = PersianNumber.ToPersian(item.Total),
                FontSize = 12,
                FontWeight = FontWeight.Bold,
                Foreground = new SolidColorBrush(Color.Parse("#059669")),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(totalText, 5);

            grid.Children.Add(numText);
            grid.Children.Add(nameText);
            grid.Children.Add(unitText);
            grid.Children.Add(qtyText);
            grid.Children.Add(priceText);
            grid.Children.Add(totalText);

            var row = new Border
            {
                Padding = new Thickness(10, 8),
                BorderBrush = new SolidColorBrush(Color.Parse("#F1F5F9")),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Child = grid
            };

            ItemsPanel.Children.Add(row);
            rowNumber++;
        }
    }

    private void OnPrintClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            var settings = StoreSettingsService.Current;

            var html = SaleInvoiceHtmlBuilder.BuildInvoiceHtml(
                settings,
                settings.PaperSize,
                settings.ShowLogoOnInvoice,
                _invoiceNumber,
                _dateShamsi,
                _customerName,
                _customerPhone,
                _isCash,
                _cardTerminal,
                _paymentStatus,
                _items,
                _totalAmount,
                autoPrint: false);

            var tempPath = Path.Combine(Path.GetTempPath(), $"invoice-{_invoiceNumber}-{DateTime.Now:HHmmss}.html");
            File.WriteAllText(tempPath, html, Encoding.UTF8);

            Process.Start(new ProcessStartInfo
            {
                FileName = tempPath,
                UseShellExecute = true
            });

            StatusText.Text = "فاکتور توی مرورگر باز شد — Ctrl+P برای چاپ";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"خطا در چاپ: {ex.Message}";
        }
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}