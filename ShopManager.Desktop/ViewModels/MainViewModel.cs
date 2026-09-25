using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ShopManager.Desktop.ViewModels;

/// <summary>
/// ViewModel پنجره اصلی — منطق UI اینجا نوشته می‌شه
/// </summary>
public partial class MainViewModel : ViewModelBase
{
    /// <summary>عنوان برنامه</summary>
    public string Title => "سیستم مدیریت مغازه";

    /// <summary>
    /// وقتی کاربر روی دکمه «کالاها» کلیک می‌کنه این اجرا می‌شه
    /// </summary>
    [RelayCommand]
    private void OpenItems()
    {
        // فعلاً فقط یه پیام نشون می‌ده
        System.Diagnostics.Debug.WriteLine("دکمه کالاها کلیک شد!");
    }
}