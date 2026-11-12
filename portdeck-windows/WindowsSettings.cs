using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Win32;
using Media = System.Windows.Media;

namespace PortDeck.Windows;

internal enum PortDeckTheme
{
    Dark,
    Light,
    System
}

internal sealed class WindowsSettings
{
    public PortDeckTheme Theme { get; set; } = PortDeckTheme.Dark;
    public bool KeepFlyoutOpen { get; set; }
}

internal static class WindowsSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private static readonly string SettingsDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PortDeck");

    private static readonly string SettingsPath = Path.Combine(SettingsDirectory, "windows-settings.json");

    public static WindowsSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return new WindowsSettings();
            return JsonSerializer.Deserialize<WindowsSettings>(File.ReadAllText(SettingsPath), JsonOptions)
                   ?? new WindowsSettings();
        }
        catch
        {
            return new WindowsSettings();
        }
    }

    public static void Save(WindowsSettings settings)
    {
        Directory.CreateDirectory(SettingsDirectory);
        var temporaryPath = $"{SettingsPath}.tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(settings, JsonOptions));
        File.Move(temporaryPath, SettingsPath, true);
    }
}

internal static class ThemeManager
{
    private static readonly IReadOnlyDictionary<string, (string Dark, string Light)> Palette =
        new Dictionary<string, (string Dark, string Light)>
        {
            ["WindowBackground"] = ("#0B0E14", "#F2F4F8"),
            ["SurfaceBackground"] = ("#111621", "#FFFFFF"),
            ["CardBackground"] = ("#171D29", "#FFFFFF"),
            ["CardHoverBackground"] = ("#1C2432", "#F7F8FC"),
            ["HairlineBorder"] = ("#263043", "#DCE2EA"),
            ["PrimaryText"] = ("#F5F7FB", "#151923"),
            ["SecondaryText"] = ("#97A2B4", "#5F6A7B"),
            ["MutedText"] = ("#667287", "#8691A1"),
            ["Accent"] = ("#8B96FF", "#6572E8"),
            ["AccentBright"] = ("#AEB5FF", "#4F5BCB"),
            ["Healthy"] = ("#58D6A8", "#16815E"),
            ["Warning"] = ("#F5BE62", "#A4680E"),
            ["Danger"] = ("#FF7D8A", "#C9364D"),
            ["ActionBackground"] = ("#202838", "#F3F5F8"),
            ["MetadataBackground"] = ("#10151F", "#F5F7FA"),
            ["ScrollThumb"] = ("#45536A", "#A8B1C0"),
            ["ScrollThumbHover"] = ("#63728C", "#7D889A"),
            ["DangerActionBackground"] = ("#271B24", "#FFF0F3"),
            ["DangerActionBorder"] = ("#4A2934", "#F1BCC6")
        };

    public static void Apply(PortDeckTheme mode)
    {
        var useLightPalette = mode == PortDeckTheme.Light || mode == PortDeckTheme.System && SystemUsesLightTheme();
        var resources = System.Windows.Application.Current.Resources;
        foreach (var (key, colors) in Palette)
        {
            var color = (Media.Color)Media.ColorConverter.ConvertFromString(useLightPalette ? colors.Light : colors.Dark);
            if (resources[key] is Media.SolidColorBrush existing && !existing.IsFrozen)
            {
                existing.Color = color;
            }
            else
            {
                resources[key] = new Media.SolidColorBrush(color);
            }
        }
    }

    private static bool SystemUsesLightTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int value && value != 0;
        }
        catch
        {
            return false;
        }
    }
}
