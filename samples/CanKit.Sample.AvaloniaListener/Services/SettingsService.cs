using System;
using System.IO;
using System.Text.Json;

namespace CanKit.Sample.AvaloniaListener.Services;

public enum AppLanguage
{
    Auto,
    ZhCN,
    EnUS
}

public sealed class AppSettings
{
    public AppLanguage Language { get; set; } = AppLanguage.Auto;
}

public static class SettingsService
{
    private static readonly string AppFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "CanKit.Sample.AvaloniaListener");
    private static readonly string SettingsPath = Path.Combine(AppFolder, "settings.json");

    public static AppSettings Current { get; private set; } = new();

    public static void Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                var settings = JsonSerializer.Deserialize<AppSettings>(json);
                if (settings != null)
                    Current = settings;
            }
        }
        catch { /* ignore and use defaults */ }
    }

    public static void Save()
    {
        try
        {
            Directory.CreateDirectory(AppFolder);
            var json = JsonSerializer.Serialize(Current, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(SettingsPath, json);
        }
        catch { /* ignore */ }
    }
}

