using System;
using System.IO;
using System.Text.Json;
using ShopManager.Desktop.Model;

namespace ShopManager.Desktop.Services;

/// <summary>
/// سرویس ذخیره و بارگذاری تنظیمات فروشگاه
/// مشابه PreferencesService — ذخیره در JSON
/// </summary>
public static class StoreSettingsService
{
    private static StoreSettings _current = new();

    /// <summary>تنظیمات فعلی</summary>
    public static StoreSettings Current => _current;

    /// <summary>مسیر فایل store-settings.json</summary>
    private static string SettingsPath
    {
        get
        {
            var folder = DatabaseService.DataFolder;
            return Path.Combine(folder, "store-settings.json");
        }
    }

    /// <summary>بارگذاری از فایل</summary>
    public static StoreSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                var settings = JsonSerializer.Deserialize<StoreSettings>(json);
                if (settings != null)
                {
                    _current = settings;
                }
            }
        }
        catch { }

        return _current;
    }

    /// <summary>ذخیره در فایل</summary>
    public static void Save(StoreSettings settings)
    {
        try
        {
            _current = settings;
            var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions
            {
                WriteIndented = true
            });
            File.WriteAllText(SettingsPath, json);
        }
        catch { }
    }
}