using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using Avalonia.Controls;

namespace CanKit.Sample.AvaloniaListener.Services;

public static class LocalizationService
{
    private static ResourceDictionary? _currentDict;

    public static AppLanguage CurrentLanguage => SettingsService.Current.Language;

    public static CultureInfo GetSystemUiCulture()
    {
        try { return CultureInfo.CurrentUICulture; } catch { return CultureInfo.InvariantCulture; }
    }

    public static void Initialize()
    {
        ApplyLanguage(ResolveActualLanguage(SettingsService.Current.Language));
    }

    public static void SetLanguage(AppLanguage lang)
    {
        SettingsService.Current.Language = lang;
        SettingsService.Save();
        ApplyLanguage(ResolveActualLanguage(lang));
    }

    private static AppLanguage ResolveActualLanguage(AppLanguage lang)
    {
        if (lang != AppLanguage.Auto)
            return lang;
        var sys = GetSystemUiCulture().Name;
        if (sys.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
            return AppLanguage.ZhCN;
        return AppLanguage.EnUS;
    }

    private static void ApplyLanguage(AppLanguage lang)
    {
        var app = Application.Current;
        if (app == null) return;

        var dictUri = lang switch
        {
            AppLanguage.ZhCN => new Uri("avares://CanKit.Sample.AvaloniaListener/Assets/Locales/Strings.zh-CN.axaml"),
            _ => new Uri("avares://CanKit.Sample.AvaloniaListener/Assets/Locales/Strings.en-US.axaml"),
        };

        var newDict = (ResourceDictionary)AvaloniaXamlLoader.Load(dictUri)!;

        if (_currentDict != null)
            app.Resources.MergedDictionaries.Remove(_currentDict);
        app.Resources.MergedDictionaries.Add(newDict);
        _currentDict = newDict;

        // Optionally update window titles immediately
        if (app.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop && desktop.MainWindow != null)
        {
            // Force title update via DynamicResource, nothing needed here if bindings use DynamicResource
        }
    }

    public static string GetString(string key, string? fallback = null)
    {
        var app = Application.Current;
        if (app != null)
        {
            try
            {
                if (app.Resources.TryGetResource(key, app.ActualThemeVariant ?? ThemeVariant.Default, out var value) && value is string s)
                    return s;
            }
            catch { }
        }
        return fallback ?? key;
    }
}
