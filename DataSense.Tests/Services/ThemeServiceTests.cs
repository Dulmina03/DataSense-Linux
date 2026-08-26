using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DataSense.Services;
using DataSense.Tests.Helpers;
using Xunit;

namespace DataSense.Tests.Services;

public class ThemeServiceTests
{
    [Fact]
    public void ThemeService_AvailableThemes_HasAllSixThemes()
    {
        var themeService = new ThemeService();
        var themes = themeService.AvailableThemes;

        Assert.Equal(6, themes.Count);
        Assert.Contains(themes, t => t.Id == "Neon Space");
        Assert.Contains(themes, t => t.Id == "Deep Violet");
        Assert.Contains(themes, t => t.Id == "Cyber Ocean");
        Assert.Contains(themes, t => t.Id == "Aurora");
        Assert.Contains(themes, t => t.Id == "Cyber Pink");
        Assert.Contains(themes, t => t.Id == "Arctic Light");
    }

    [Theory]
    [InlineData("Neon Space")]
    [InlineData("Deep Violet")]
    [InlineData("Cyber Ocean")]
    [InlineData("Aurora")]
    [InlineData("Cyber Pink")]
    [InlineData("Arctic Light")]
    public void ThemeDefinitions_EachTheme_HasCompleteAndValidPalette(string themeId)
    {
        var def = ThemeService.GetThemeDefinition(themeId);
        Assert.NotNull(def);
        Assert.Equal(themeId, def.Id);

        // Verify valid hex colors
        Assert.StartsWith("#", def.AppBackground);
        Assert.StartsWith("#", def.Surface);
        Assert.StartsWith("#", def.SurfaceElevated);
        Assert.StartsWith("#", def.Border);
        Assert.StartsWith("#", def.TextPrimary);
        Assert.StartsWith("#", def.TextSecondary);
        Assert.StartsWith("#", def.TextMuted);
        Assert.StartsWith("#", def.AccentPrimary);
        Assert.StartsWith("#", def.AccentSecondary);
        Assert.StartsWith("#", def.AccentTertiary);
        Assert.StartsWith("#", def.Download);
        Assert.StartsWith("#", def.Upload);
        Assert.StartsWith("#", def.Success);
        Assert.StartsWith("#", def.Warning);
        Assert.StartsWith("#", def.Danger);

        // Verify 12 process palette items
        Assert.NotNull(def.ProcessPalette);
        Assert.Equal(12, def.ProcessPalette.Length);
        foreach (var color in def.ProcessPalette)
        {
            Assert.StartsWith("#", color);
        }

        // Verify background gradient stops
        Assert.NotNull(def.AppBackgroundGradientStops);
        Assert.True(def.AppBackgroundGradientStops.Length >= 4);
        foreach (var stop in def.AppBackgroundGradientStops)
        {
            Assert.StartsWith("#", stop.Hex);
            Assert.InRange(stop.Offset, 0.0, 1.0);
        }

        // Verify gradients
        Assert.StartsWith("#", def.DownloadBarGradient.Start);
        Assert.StartsWith("#", def.DownloadBarGradient.End);
        Assert.StartsWith("#", def.UploadBarGradient.Start);
        Assert.StartsWith("#", def.UploadBarGradient.End);
        Assert.StartsWith("#", def.ActiveNavGradient.Start);
        Assert.StartsWith("#", def.ActiveNavGradient.End);
    }

    [Fact]
    public void ThemeDefinitions_AllThemes_HaveDistinctBackgroundsAndAccents()
    {
        var themeIds = new[] { "Neon Space", "Deep Violet", "Cyber Ocean", "Aurora", "Cyber Pink", "Arctic Light" };
        var backgrounds = new HashSet<string>();
        var surfaces = new HashSet<string>();
        var accents = new HashSet<string>();

        foreach (var id in themeIds)
        {
            var def = ThemeService.GetThemeDefinition(id);
            backgrounds.Add(def.AppBackground);
            surfaces.Add(def.Surface);
            accents.Add(def.AccentPrimary);
        }

        // All 6 themes must have distinct backgrounds, surfaces, and primary accents
        Assert.Equal(6, backgrounds.Count);
        Assert.Equal(6, surfaces.Count);
        Assert.Equal(6, accents.Count);
    }

    [Fact]
    public void ArcticLight_IsGenuineLightMode()
    {
        var def = ThemeService.GetThemeDefinition("Arctic Light");
        Assert.True(def.IsLight);
        Assert.Equal("#F4F7FB", def.AppBackground);
        Assert.Equal("#B8FFFFFF", def.Surface);
        Assert.Equal("#111827", def.TextPrimary);
    }

    [Fact]
    public void ThemeService_ApplyTheme_FiresThemeChangedEvent()
    {
        var themeService = new ThemeService();
        string? notifiedTheme = null;
        themeService.ThemeChanged += theme => notifiedTheme = theme;

        themeService.ApplyTheme("Cyber Ocean");

        Assert.Equal("Cyber Ocean", themeService.CurrentThemeId);
        Assert.Equal("Cyber Ocean", notifiedTheme);
    }

    [Fact]
    public async Task ThemeService_ApplyTheme_PersistsAndLoadsFromRepository()
    {
        using var context = await TestDatabaseFactory.CreateAsync();
        var themeService = new ThemeService(context.Repository);

        themeService.ApplyTheme("Aurora");
        Assert.Equal("Aurora", themeService.CurrentThemeId);

        // Allow async save to persist
        await Task.Delay(100);

        // Create new instance and initialize
        var newThemeService = new ThemeService(context.Repository);
        await newThemeService.InitializeAsync();

        Assert.Equal("Aurora", newThemeService.CurrentThemeId);
    }

    [Fact]
    public void ThemeService_GetProcessPaletteHex_Returns12ColorsForActiveTheme()
    {
        var themeService = new ThemeService();
        themeService.ApplyTheme("Cyber Pink");

        var palette = themeService.GetProcessPaletteHex();
        Assert.Equal(12, palette.Count);
        Assert.Equal("#FF1493", palette[0]);
    }
}
