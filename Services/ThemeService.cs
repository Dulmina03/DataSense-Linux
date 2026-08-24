using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Media;
using Avalonia.Threading;
using DataSense.Database;

namespace DataSense.Services;

public class ThemeService : IThemeService
{
    private readonly INetworkUsageRepository? _repository;

    public static readonly IReadOnlyList<ThemeOption> Themes = new List<ThemeOption>
    {
        new("Neon Space", "Neon Space", "🌌"),
        new("Deep Violet", "Deep Violet", "💜"),
        new("Cyber Ocean", "Cyber Ocean", "🌊"),
        new("Aurora", "Aurora", "🌃"),
        new("Cyber Pink", "Cyber Pink", "🌸"),
        new("Arctic Light", "Arctic Light", "🤍")
    };

    public IReadOnlyList<ThemeOption> AvailableThemes => Themes;

    public string CurrentThemeId { get; private set; } = "Neon Space";

    public ThemeOption CurrentTheme => Themes.FirstOrDefault(t => t.Id == CurrentThemeId) ?? Themes[0];

    public ThemeService(INetworkUsageRepository? repository = null)
    {
        _repository = repository;
    }

    public async Task InitializeAsync()
    {
        if (_repository != null)
        {
            try
            {
                var savedTheme = await _repository.GetSettingAsync("AppTheme");
                if (!string.IsNullOrWhiteSpace(savedTheme) && Themes.Any(t => t.Id == savedTheme))
                {
                    CurrentThemeId = savedTheme;
                    ApplyThemeInternal(savedTheme);
                }
            }
            catch { }
        }
    }

    public void ApplyTheme(string themeId)
    {
        var matched = Themes.FirstOrDefault(t => t.Id.Equals(themeId, StringComparison.OrdinalIgnoreCase));
        if (matched == null) return;

        CurrentThemeId = matched.Id;
        ApplyThemeInternal(matched.Id);

        if (_repository != null)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await _repository.SaveSettingAsync("AppTheme", matched.Id);
                }
                catch { }
            });
        }
    }

    private static void ApplyThemeInternal(string themeId)
    {
        var (bg, navBg, surf, surfElev, surfSubtle, surfHov, border, borderSubtle, borderStr, accent, accentBright, accentDeep, dl, dlBright, ul, ulBright, textPrim, textSec, textMuted) = GetThemePalette(themeId);

        void UpdateResources()
        {
            if (Application.Current == null) return;

            var res = Application.Current.Resources;

            // Backgrounds & Surfaces
            res["Brush.AppBackground"] = new SolidColorBrush(Color.Parse(bg));
            res["Brush.NavigationBackground"] = new SolidColorBrush(Color.Parse(navBg));
            res["Brush.Surface"] = new SolidColorBrush(Color.Parse(surf));
            res["Brush.SurfaceElevated"] = new SolidColorBrush(Color.Parse(surfElev));
            res["Brush.SurfaceSubtle"] = new SolidColorBrush(Color.Parse(surfSubtle));
            res["Brush.SurfaceHover"] = new SolidColorBrush(Color.Parse(surfHov));

            // Borders
            res["Brush.Border"] = new SolidColorBrush(Color.Parse(border));
            res["Brush.BorderSubtle"] = new SolidColorBrush(Color.Parse(borderSubtle));
            res["Brush.BorderStrong"] = new SolidColorBrush(Color.Parse(borderStr));

            // Typography
            res["Brush.TextPrimary"] = new SolidColorBrush(Color.Parse(textPrim));
            res["Brush.TextSecondary"] = new SolidColorBrush(Color.Parse(textSec));
            res["Brush.TextMuted"] = new SolidColorBrush(Color.Parse(textMuted));

            // Accents
            res["Brush.Accent"] = new SolidColorBrush(Color.Parse(accent));
            res["Brush.AccentBright"] = new SolidColorBrush(Color.Parse(accentBright));
            res["Brush.AccentDeep"] = new SolidColorBrush(Color.Parse(accentDeep));
            res["Brush.AccentGlow"] = new SolidColorBrush(Color.Parse(accent));
            res["Brush.AmbientGlow"] = new SolidColorBrush(Color.Parse(accentDeep));

            // Download & Upload
            res["Brush.Download"] = new SolidColorBrush(Color.Parse(dl));
            res["Brush.DownloadBright"] = new SolidColorBrush(Color.Parse(dlBright));
            res["Brush.DownloadGlow"] = new SolidColorBrush(Color.Parse(dl));
            res["Brush.Upload"] = new SolidColorBrush(Color.Parse(ul));
            res["Brush.UploadBright"] = new SolidColorBrush(Color.Parse(ulBright));
            res["Brush.UploadGlow"] = new SolidColorBrush(Color.Parse(ul));

            // Bar Gradients
            var dlBarGrad = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
                GradientStops = new GradientStops
                {
                    new(Color.Parse(dlBright), 0),
                    new(Color.Parse(dl), 1)
                }
            };
            res["Brush.DownloadBarGradient"] = dlBarGrad;

            var ulBarGrad = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
                GradientStops = new GradientStops
                {
                    new(Color.Parse(ulBright), 0),
                    new(Color.Parse(ul), 1)
                }
            };
            res["Brush.UploadBarGradient"] = ulBarGrad;

            // System overrides
            res["SystemRegionBrush"] = new SolidColorBrush(Color.Parse(bg));
            res["SystemControlBackgroundAltHighBrush"] = new SolidColorBrush(Color.Parse(bg));
            res["SystemControlBackgroundBaseLowBrush"] = new SolidColorBrush(Color.Parse(surf));
            res["SystemControlBackgroundChromeLowBrush"] = new SolidColorBrush(Color.Parse(surfElev));
        }

        if (Dispatcher.UIThread.CheckAccess())
        {
            UpdateResources();
        }
        else
        {
            Dispatcher.UIThread.Post(UpdateResources);
        }
    }

    private static (
        string bg, string navBg, string surf, string surfElev, string surfSubtle, string surfHov,
        string border, string borderSubtle, string borderStr,
        string accent, string accentBright, string accentDeep,
        string dl, string dlBright,
        string ul, string ulBright,
        string textPrim, string textSec, string textMuted
    ) GetThemePalette(string themeId)
    {
        return themeId switch
        {
            "Deep Violet" => (
                "#0E0720", "#0A0417", "#180D33", "#24144B", "#140A2B", "#2B1758",
                "#3D207A", "#2A1654", "#5D31B8",
                "#9D4EDD", "#C77DFF", "#7B2CBF",
                "#38BDF8", "#7DD3FC",
                "#C77DFF", "#E0AAFF",
                "#FFFFFF", "#B8A9D9", "#7E6E9E"
            ),
            "Cyber Ocean" => (
                "#040D1A", "#020812", "#0A192F", "#112A4D", "#071324", "#16355F",
                "#1E4378", "#122C52", "#2A5EAA",
                "#00F0FF", "#38BDF8", "#0284C7",
                "#00F0FF", "#38BDF8",
                "#818CF8", "#A5B4FC",
                "#FFFFFF", "#94A3B8", "#64748B"
            ),
            "Aurora" => (
                "#051512", "#030E0C", "#0B2520", "#123932", "#081C18", "#194C43",
                "#1F5E52", "#14423A", "#2B8272",
                "#10B981", "#34D399", "#059669",
                "#06B6D4", "#22D3EE",
                "#34D399", "#6EE7B7",
                "#FFFFFF", "#A7F3D0", "#5E8E7E"
            ),
            "Cyber Pink" => (
                "#160614", "#10040E", "#260C22", "#3B1335", "#1E091B", "#4D1945",
                "#6A225F", "#4A1743", "#943085",
                "#FF007F", "#FF3399", "#D9006C",
                "#00F0FF", "#38BDF8",
                "#FF3399", "#FF66B2",
                "#FFFFFF", "#F472B6", "#9D5C80"
            ),
            "Arctic Light" => (
                "#0F172A", "#0B1120", "#1E293B", "#334155", "#182030", "#3E4F69",
                "#475569", "#2F3D4E", "#64748B",
                "#38BDF8", "#7DD3FC", "#0284C7",
                "#38BDF8", "#7DD3FC",
                "#A78BFA", "#C4B5FD",
                "#FFFFFF", "#CBD5E1", "#64748B"
            ),
            _ => ( // Neon Space (Default)
                "#0B0B16", "#0D0D1A", "#16162C", "#1B1C38", "#131326", "#222244",
                "#2D2B55", "#1E1C3D", "#4A4780",
                "#A100FF", "#D000FF", "#7A00CC",
                "#00D8F6", "#00F0FF",
                "#A855F7", "#D000FF",
                "#FFFFFF", "#A0A0C0", "#6E6E9B"
            )
        };
    }
}
