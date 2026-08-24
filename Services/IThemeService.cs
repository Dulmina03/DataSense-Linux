using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace DataSense.Services;

public record ThemeOption(string Id, string DisplayName, string Icon)
{
    public string FormattedName => $"{Icon}  {DisplayName}";
}

public interface IThemeService
{
    IReadOnlyList<ThemeOption> AvailableThemes { get; }
    string CurrentThemeId { get; }
    ThemeOption CurrentTheme { get; }
    event Action<string>? ThemeChanged;
    void ApplyTheme(string themeId);
    Task InitializeAsync();
    IReadOnlyList<string> GetProcessPaletteHex(string? themeId = null);
}
