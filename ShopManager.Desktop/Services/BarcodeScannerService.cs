using System;
using System.Text;
using System.Timers;

namespace ShopManager.Desktop.Services;

/// <summary>
/// سرویس بارکدخوان HID (Keyboard Wedge)
/// اکثر بارکدخوان‌ها مثل کیبورد کار می‌کنن — کاراکترها رو تایپ می‌کنن و آخرش Enter می‌زنن
/// این سرویس با تایمر، بین تایپ سریع اسکنر و تایپ آهسته انسان تفاوت می‌ذاره
/// </summary>
public class BarcodeScannerService : IDisposable
{
    public event EventHandler<string>? BarcodeScanned;

    public bool IsListening { get; private set; }

    private readonly StringBuilder _buffer = new();
    private readonly Timer _burstTimer;
    private readonly int _burstTimeoutMs;

    public BarcodeScannerService(int burstTimeoutMs = 80)
    {
        _burstTimeoutMs = burstTimeoutMs;
        _burstTimer = new Timer(burstTimeoutMs);
        _burstTimer.AutoReset = false;
        _burstTimer.Elapsed += OnBurstTimeout;
    }

    public void StartListening()
    {
        _buffer.Clear();
        IsListening = true;
    }

    public void StopListening()
    {
        IsListening = false;
        _buffer.Clear();
        _burstTimer.Stop();
    }

    public void ProcessKey(char character)
    {
        if (!IsListening) return;

        if (character == '\r' || character == '\n')
        {
            FinalizeBarcode();
            return;
        }

        _buffer.Append(character);

        _burstTimer.Stop();
        _burstTimer.Start();
    }

    private void FinalizeBarcode()
    {
        var barcode = _buffer.ToString().Trim();
        _buffer.Clear();
        _burstTimer.Stop();

        if (string.IsNullOrWhiteSpace(barcode)) return;
        if (barcode.Length < 3) return;

        BarcodeScanned?.Invoke(this, barcode);
    }

    private void OnBurstTimeout(object? sender, ElapsedEventArgs e)
    {
        _buffer.Clear();
    }

    public void Dispose()
    {
        _burstTimer?.Dispose();
    }
}