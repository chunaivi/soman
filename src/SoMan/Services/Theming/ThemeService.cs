using MaterialDesignThemes.Wpf;
using SoMan.Services.Config;

namespace SoMan.Services.Theming;

public interface IThemeService
{
    /// <summary>Currently-active base theme ("Dark" or "Light").</summary>
    string CurrentTheme { get; }

    /// <summary>Apply saved theme from config at startup.</summary>
    Task ApplyStartupThemeAsync();

    /// <summary>Apply a theme at runtime and persist it.</summary>
    Task SetThemeAsync(string theme);

    /// <summary>Flip Dark<->Light and persist.</summary>
    Task ToggleAsync();
}

public class ThemeService : IThemeService
{
    private readonly IConfigService _config;
    private readonly PaletteHelper _palette = new();

    public string CurrentTheme { get; private set; } = "Dark";

    public ThemeService(IConfigService config)
    {
        _config = config;
    }

    public async Task ApplyStartupThemeAsync()
    {
        var saved = await _config.GetAsync("Theme", "Dark");
        ApplyBaseTheme(saved);
        CurrentTheme = Normalize(saved);
    }

    public async Task SetThemeAsync(string theme)
    {
        var norm = Normalize(theme);
        ApplyBaseTheme(norm);
        CurrentTheme = norm;
        await _config.SetAsync("Theme", norm);
    }

    public Task ToggleAsync()
        => SetThemeAsync(CurrentTheme == "Dark" ? "Light" : "Dark");

    private void ApplyBaseTheme(string theme)
    {
        var t = _palette.GetTheme();
        t.SetBaseTheme(Normalize(theme) == "Dark" ? BaseTheme.Dark : BaseTheme.Light);
        _palette.SetTheme(t);
    }

    private static string Normalize(string? s)
        => string.Equals(s?.Trim(), "Light", StringComparison.OrdinalIgnoreCase) ? "Light" : "Dark";
}
