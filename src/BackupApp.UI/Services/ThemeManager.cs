using System.Windows;
using System.Windows.Media;

namespace BackupApp.UI.Services;

public enum AppTheme
{
    Light,
    Dark
}

public static class ThemeManager
{
    public static AppTheme CurrentTheme { get; private set; } = AppTheme.Light;

    public static void ApplyTheme(AppTheme theme)
    {
        CurrentTheme = theme;
        var res = Application.Current.Resources;

        if (theme == AppTheme.Dark)
        {
            res["AppBackgroundBrush"] = new SolidColorBrush(Color.FromRgb(0x20, 0x20, 0x20));
            res["AppSurfaceBrush"] = new SolidColorBrush(Color.FromRgb(0x2C, 0x2C, 0x2C));
            res["AppSurfaceHoverBrush"] = new SolidColorBrush(Color.FromRgb(0x38, 0x38, 0x38));
            res["AppTextPrimaryBrush"] = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF));
            res["AppTextSecondaryBrush"] = new SolidColorBrush(Color.FromRgb(0xB0, 0xB0, 0xB0));
            res["AppBorderBrush"] = new SolidColorBrush(Color.FromRgb(0x3F, 0x3F, 0x3F));
            res["AppAccentBrush"] = new SolidColorBrush(Color.FromRgb(0x4C, 0xC2, 0xFF));
            res["AppAccentHoverBrush"] = new SolidColorBrush(Color.FromRgb(0x60, 0xCD, 0xFF));
            res["AppSuccessBrush"] = new SolidColorBrush(Color.FromRgb(0x6C, 0xCB, 0x5F));
            res["AppWarningBrush"] = new SolidColorBrush(Color.FromRgb(0xF7, 0x63, 0x0C));
            res["AppErrorBrush"] = new SolidColorBrush(Color.FromRgb(0xFF, 0x99, 0xA4));
        }
        else
        {
            res["AppBackgroundBrush"] = new SolidColorBrush(Color.FromRgb(0xF8, 0xF9, 0xFA));
            res["AppSurfaceBrush"] = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF));
            res["AppSurfaceHoverBrush"] = new SolidColorBrush(Color.FromRgb(0xF0, 0xF2, 0xF5));
            res["AppTextPrimaryBrush"] = new SolidColorBrush(Color.FromRgb(0x1C, 0x1C, 0x1C));
            res["AppTextSecondaryBrush"] = new SolidColorBrush(Color.FromRgb(0x5C, 0x5C, 0x5C));
            res["AppBorderBrush"] = new SolidColorBrush(Color.FromRgb(0xDF, 0xE1, 0xE5));
            res["AppAccentBrush"] = new SolidColorBrush(Color.FromRgb(0x00, 0x67, 0xC0));
            res["AppAccentHoverBrush"] = new SolidColorBrush(Color.FromRgb(0x18, 0x79, 0xD6));
            res["AppSuccessBrush"] = new SolidColorBrush(Color.FromRgb(0x10, 0x7C, 0x10));
            res["AppWarningBrush"] = new SolidColorBrush(Color.FromRgb(0xD8, 0x3B, 0x01));
            res["AppErrorBrush"] = new SolidColorBrush(Color.FromRgb(0xA8, 0x00, 0x00));
        }
    }
}
