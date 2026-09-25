using System;
using System.IO;
using System.Text.Json;
using ShopManager.Desktop.Models;

namespace ShopManager.Desktop.Services;

/// <summary>
/// سرویس ذخیره و بارگذاری تنظیمات ظاهری
/// </summary>
public static class PreferencesService
{
    private static AppPreferences _current = new();

    /// <summary>تنظیمات فعلی</summary>
    public static AppPreferences Current => _current;

    /// <summary>مسیر فایل preferences.json</summary>
    private static string PreferencesPath
    {
        get
        {
            var folder = DatabaseService.DataFolder;
            return Path.Combine(folder, "preferences.json");
        }
    }

    /// <summary>بارگذاری تنظیمات از فایل</summary>
    public static AppPreferences Load()
    {
        try
        {
            if (File.Exists(PreferencesPath))
            {
                var json = File.ReadAllText(PreferencesPath);
                var prefs = JsonSerializer.Deserialize<AppPreferences>(json);
                if (prefs != null)
                {
                    _current = prefs;
                }
            }
        }
        catch { }

        return _current;
    }

    /// <summary>ذخیره تنظیمات در فایل</summary>
    public static void Save(AppPreferences prefs)
    {
        try
        {
            _current = prefs;
            var json = JsonSerializer.Serialize(prefs, new JsonSerializerOptions
            {
                WriteIndented = true
            });
            File.WriteAllText(PreferencesPath, json);
        }
        catch { }
    }
}