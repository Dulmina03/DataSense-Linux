using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using DataSense.Database;

namespace DataSense.Services;

public record ThemeDefinition(
    string Id,
    string DisplayName,
    string Icon,
    bool IsLight,
    // Backgrounds & Surfaces
    string AppBackground,
    string NavigationBackground,
    string Surface,
    string SurfaceElevated,
    string SurfaceSubtle,
    string SurfaceHover,
    // Borders & Dividers
    string Border,
    string BorderSubtle,
    string BorderStrong,
    string Divider,
    // Typography
    string TextPrimary,
    string TextSecondary,
    string TextMuted,
    string TextDisabled,
    string TextOnAccent,
    // Accents
    string AccentPrimary,
    string AccentSecondary,
    string AccentTertiary,
    string AccentHover,
    string AccentGlow,
    string AccentSurface,
    string AmbientGlow,
    // Download
    string Download,
    string DownloadBright,
    string DownloadMuted,
    string DownloadGlow,
    string DownloadSurface,
    // Upload
    string Upload,
    string UploadBright,
    string UploadDeep,
    string UploadGlow,
    string UploadSurface,
    // Status
    string Success,
    string SuccessSurface,
    string Warning,
    string Danger,
    string DangerSurface,
    // Charts
    string ChartGrid,
    string ChartAxis,
    string ChartTooltipBackground,
    string ChartTooltipText,
    string ChartSegmentOther,
    // 12 Process Colors
    string[] ProcessPalette,
    // Multi-stop Diagonal Background Atmospheric Gradient (Top-Left ↘ Bottom-Right)
    (string Hex, double Offset)[] AppBackgroundGradientStops,
    // Gradients (Start and End hex colors)
    (string Start, string End) DownloadBarGradient,
    (string Start, string End) UploadBarGradient,
    (string Start, string End) HeroDownloadGradient,
    (string Start, string End) HeroUploadGradient,
    (string Start, string End) HeroUsageGradient,
    (string Start, string End) DownloadAreaGradient,
    (string Start, string End) UploadAreaGradient,
    (string Start, string End) ActiveNavGradient,
    (string Start, string End) GradientDownload,
    (string Start, string End) GradientUpload,
    (string Start, string End) GradientVioletPink,
    (string Start, string End) GradientCyanPink
);

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

    private static readonly Dictionary<string, ThemeDefinition> ThemeDefinitions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Neon Space"] = new(
            Id: "Neon Space",
            DisplayName: "Neon Space",
            Icon: "🌌",
            IsLight: false,
            AppBackground: "#000000",
            NavigationBackground: "#20020814",
            Surface: "#24061224",
            SurfaceElevated: "#2E0A1A32",
            SurfaceSubtle: "#14040C18",
            SurfaceHover: "#3810264A",
            Border: "#283C6699",
            BorderSubtle: "#18284568",
            BorderStrong: "#454E80B8",
            Divider: "#20284568",
            TextPrimary: "#F5F7FF",
            TextSecondary: "#CAD0E8",
            TextMuted: "#8E98B8",
            TextDisabled: "#545D78",
            TextOnAccent: "#FFFFFF",
            AccentPrimary: "#A100FF",
            AccentSecondary: "#00E5FF",
            AccentTertiary: "#FF007F",
            AccentHover: "#B833FF",
            AccentGlow: "#D000FF",
            AccentSurface: "#30A100FF",
            AmbientGlow: "#4A00E0",
            Download: "#00D8F6",
            DownloadBright: "#00F0FF",
            DownloadMuted: "#00D8F6",
            DownloadGlow: "#00D8F6",
            DownloadSurface: "#3000D8F6",
            Upload: "#A855F7",
            UploadBright: "#D000FF",
            UploadDeep: "#8B5CF6",
            UploadGlow: "#A855F7",
            UploadSurface: "#30A855F7",
            Success: "#39FF88",
            SuccessSurface: "#3039FF88",
            Warning: "#FFD166",
            Danger: "#FF4D6D",
            DangerSurface: "#30FF4D6D",
            ChartGrid: "#1A3C6699",
            ChartAxis: "#8E98B8",
            ChartTooltipBackground: "#E8061222",
            ChartTooltipText: "#F5F7FF",
            ChartSegmentOther: "#6E6E9B",
            ProcessPalette: new[]
            {
                "#00F0FF", "#39FF88", "#FFD166", "#FF007F", "#A855F7", "#00D8F6",
                "#818CF8", "#10B981", "#D000FF", "#8B5CF6", "#FF4D6D", "#00E5FF"
            },
            AppBackgroundGradientStops: new (string, double)[]
            {
                ("#000000", 0.00),
                ("#030C1E", 0.25),
                ("#071938", 0.50),
                ("#16123D", 0.75),
                ("#070414", 1.00)
            },
            DownloadBarGradient: ("#22E3FF", "#0077A8"),
            UploadBarGradient: ("#C56CFF", "#6D28D9"),
            HeroDownloadGradient: ("#3800D8F6", "#0016162C"),
            HeroUploadGradient: ("#38A855F7", "#0016162C"),
            HeroUsageGradient: ("#284A00E0", "#0016162C"),
            DownloadAreaGradient: ("#2200D8F6", "#0200D8F6"),
            UploadAreaGradient: ("#22A855F7", "#02A855F7"),
            ActiveNavGradient: ("#2EA100FF", "#08A100FF"),
            GradientDownload: ("#00D8F6", "#00F0FF"),
            GradientUpload: ("#A855F7", "#D000FF"),
            GradientVioletPink: ("#A100FF", "#FF007F"),
            GradientCyanPink: ("#00F0FF", "#FF007F")
        ),
        ["Deep Violet"] = new(
            Id: "Deep Violet",
            DisplayName: "Deep Violet",
            Icon: "💜",
            IsLight: false,
            AppBackground: "#01040A",
            NavigationBackground: "#20040816",
            Surface: "#24081228",
            SurfaceElevated: "#2E0E1A36",
            SurfaceSubtle: "#14050C1C",
            SurfaceHover: "#3814264E",
            Border: "#284A6598",
            BorderSubtle: "#182E446B",
            BorderStrong: "#45587CB8",
            Divider: "#202E446B",
            TextPrimary: "#F5F7FF",
            TextSecondary: "#D8C8E8",
            TextMuted: "#9F8EB2",
            TextDisabled: "#5E4B6E",
            TextOnAccent: "#FFFFFF",
            AccentPrimary: "#C026FF",
            AccentSecondary: "#8B5CF6",
            AccentTertiary: "#FF4ECD",
            AccentHover: "#D24DFF",
            AccentGlow: "#FF4ECD",
            AccentSurface: "#30C026FF",
            AmbientGlow: "#7B2CBF",
            Download: "#C026FF",
            DownloadBright: "#D966FF",
            DownloadMuted: "#C026FF",
            DownloadGlow: "#C026FF",
            DownloadSurface: "#30C026FF",
            Upload: "#FF4ECD",
            UploadBright: "#FF7EE0",
            UploadDeep: "#B5179E",
            UploadGlow: "#FF4ECD",
            UploadSurface: "#30FF4ECD",
            Success: "#5EEAD4",
            SuccessSurface: "#305EEAD4",
            Warning: "#FBBF24",
            Danger: "#FB7185",
            DangerSurface: "#30FB7185",
            ChartGrid: "#1A4A6598",
            ChartAxis: "#9F8EB2",
            ChartTooltipBackground: "#E8081228",
            ChartTooltipText: "#F5F7FF",
            ChartSegmentOther: "#7E6E9E",
            ProcessPalette: new[]
            {
                "#C026FF", "#FF4ECD", "#8B5CF6", "#E0AAFF", "#D946EF", "#7C3AED",
                "#F43F5E", "#A855F7", "#C77DFF", "#9333EA", "#F472B6", "#5EEAD4"
            },
            AppBackgroundGradientStops: new (string, double)[]
            {
                ("#000000", 0.00),
                ("#0C031A", 0.25),
                ("#1D0838", 0.50),
                ("#300C4A", 0.75),
                ("#0A0214", 1.00)
            },
            DownloadBarGradient: ("#D966FF", "#7B2CBF"),
            UploadBarGradient: ("#FF7EE0", "#B5179E"),
            HeroDownloadGradient: ("#38C026FF", "#0021122C"),
            HeroUploadGradient: ("#38FF4ECD", "#0021122C"),
            HeroUsageGradient: ("#288B5CF6", "#0021122C"),
            DownloadAreaGradient: ("#22C026FF", "#02C026FF"),
            UploadAreaGradient: ("#22FF4ECD", "#02FF4ECD"),
            ActiveNavGradient: ("#30C026FF", "#08C026FF"),
            GradientDownload: ("#C026FF", "#D966FF"),
            GradientUpload: ("#FF4ECD", "#FF7EE0"),
            GradientVioletPink: ("#C026FF", "#FF4ECD"),
            GradientCyanPink: ("#8B5CF6", "#FF4ECD")
        ),
        ["Cyber Ocean"] = new(
            Id: "Cyber Ocean",
            DisplayName: "Cyber Ocean",
            Icon: "🌊",
            IsLight: false,
            AppBackground: "#000208",
            NavigationBackground: "#20010814",
            Surface: "#24041424",
            SurfaceElevated: "#2E081C30",
            SurfaceSubtle: "#14030D18",
            SurfaceHover: "#380C2848",
            Border: "#28306A98",
            BorderSubtle: "#18204E6C",
            BorderStrong: "#45448CB8",
            Divider: "#20204E6C",
            TextPrimary: "#F2FCFF",
            TextSecondary: "#C0DCED",
            TextMuted: "#7C9EB5",
            TextDisabled: "#426075",
            TextOnAccent: "#FFFFFF",
            AccentPrimary: "#00E5FF",
            AccentSecondary: "#008CFF",
            AccentTertiary: "#00FFC6",
            AccentHover: "#38EFFF",
            AccentGlow: "#00FFC6",
            AccentSurface: "#3000E5FF",
            AmbientGlow: "#005FB8",
            Download: "#00E5FF",
            DownloadBright: "#5CF2FF",
            DownloadMuted: "#00E5FF",
            DownloadGlow: "#00E5FF",
            DownloadSurface: "#3000E5FF",
            Upload: "#008CFF",
            UploadBright: "#4DAEFF",
            UploadDeep: "#005FB8",
            UploadGlow: "#008CFF",
            UploadSurface: "#30008CFF",
            Success: "#00FFC6",
            SuccessSurface: "#3000FFC6",
            Warning: "#FFD166",
            Danger: "#FF647C",
            DangerSurface: "#30FF647C",
            ChartGrid: "#1A306A98",
            ChartAxis: "#7C9EB5",
            ChartTooltipBackground: "#E8041424",
            ChartTooltipText: "#F2FCFF",
            ChartSegmentOther: "#64748B",
            ProcessPalette: new[]
            {
                "#00E5FF", "#008CFF", "#00FFC6", "#38BDF8", "#06B6D4", "#60A5FA",
                "#2DD4BF", "#0284C7", "#818CF8", "#22D3EE", "#67E8F9", "#3B82F6"
            },
            AppBackgroundGradientStops: new (string, double)[]
            {
                ("#000000", 0.00),
                ("#010C1A", 0.25),
                ("#031E38", 0.50),
                ("#063A5C", 0.75),
                ("#010B14", 1.00)
            },
            DownloadBarGradient: ("#5CF2FF", "#0072B5"),
            UploadBarGradient: ("#4DAEFF", "#00529E"),
            HeroDownloadGradient: ("#3800E5FF", "#000D1C29"),
            HeroUploadGradient: ("#38008CFF", "#000D1C29"),
            HeroUsageGradient: ("#28008CFF", "#000D1C29"),
            DownloadAreaGradient: ("#2200E5FF", "#0200E5FF"),
            UploadAreaGradient: ("#22008CFF", "#02008CFF"),
            ActiveNavGradient: ("#3000E5FF", "#0800E5FF"),
            GradientDownload: ("#00E5FF", "#5CF2FF"),
            GradientUpload: ("#008CFF", "#4DAEFF"),
            GradientVioletPink: ("#008CFF", "#00FFC6"),
            GradientCyanPink: ("#00E5FF", "#00FFC6")
        ),
        ["Aurora"] = new(
            Id: "Aurora",
            DisplayName: "Aurora",
            Icon: "🌃",
            IsLight: false,
            AppBackground: "#000306",
            NavigationBackground: "#20020A12",
            Surface: "#24061522",
            SurfaceElevated: "#2E0A1F30",
            SurfaceSubtle: "#14040F18",
            SurfaceHover: "#38102C46",
            Border: "#2838708E",
            BorderSubtle: "#1826506A",
            BorderStrong: "#454E92B0",
            Divider: "#2026506A",
            TextPrimary: "#F3FFFA",
            TextSecondary: "#C5E2D9",
            TextMuted: "#80A398",
            TextDisabled: "#486960",
            TextOnAccent: "#FFFFFF",
            AccentPrimary: "#36F1B4",
            AccentSecondary: "#7C5CFF",
            AccentTertiary: "#22D3EE",
            AccentHover: "#5BF5C5",
            AccentGlow: "#36F1B4",
            AccentSurface: "#3036F1B4",
            AmbientGlow: "#0E4A3F",
            Download: "#22D3EE",
            DownloadBright: "#67E8F9",
            DownloadMuted: "#22D3EE",
            DownloadGlow: "#22D3EE",
            DownloadSurface: "#3022D3EE",
            Upload: "#7C5CFF",
            UploadBright: "#A78BFA",
            UploadDeep: "#5B21B6",
            UploadGlow: "#7C5CFF",
            UploadSurface: "#307C5CFF",
            Success: "#36F1B4",
            SuccessSurface: "#3036F1B4",
            Warning: "#FACC15",
            Danger: "#FB7185",
            DangerSurface: "#30FB7185",
            ChartGrid: "#1A38708E",
            ChartAxis: "#80A398",
            ChartTooltipBackground: "#E8061522",
            ChartTooltipText: "#F3FFFA",
            ChartSegmentOther: "#5E8E7E",
            ProcessPalette: new[]
            {
                "#36F1B4", "#22D3EE", "#7C5CFF", "#10B981", "#A78BFA", "#34D399",
                "#6EE7B7", "#06B6D4", "#C084FC", "#2DD4BF", "#F472B6", "#38BDF8"
            },
            AppBackgroundGradientStops: new (string, double)[]
            {
                ("#000000", 0.00),
                ("#021410", 0.25),
                ("#052B26", 0.50),
                ("#121A38", 0.75),
                ("#020812", 1.00)
            },
            DownloadBarGradient: ("#67E8F9", "#0E7490"),
            UploadBarGradient: ("#A78BFA", "#581C87"),
            HeroDownloadGradient: ("#3822D3EE", "#0010201D"),
            HeroUploadGradient: ("#387C5CFF", "#0010201D"),
            HeroUsageGradient: ("#2836F1B4", "#0010201D"),
            DownloadAreaGradient: ("#2222D3EE", "#0222D3EE"),
            UploadAreaGradient: ("#227C5CFF", "#027C5CFF"),
            ActiveNavGradient: ("#3036F1B4", "#0836F1B4"),
            GradientDownload: ("#22D3EE", "#67E8F9"),
            GradientUpload: ("#7C5CFF", "#A78BFA"),
            GradientVioletPink: ("#7C5CFF", "#36F1B4"),
            GradientCyanPink: ("#22D3EE", "#36F1B4")
        ),
        ["Cyber Pink"] = new(
            Id: "Cyber Pink",
            DisplayName: "Cyber Pink",
            Icon: "🌸",
            IsLight: false,
            AppBackground: "#010309",
            NavigationBackground: "#20050816",
            Surface: "#24081226",
            SurfaceElevated: "#2E0E1A34",
            SurfaceSubtle: "#14050C1A",
            SurfaceHover: "#3814264C",
            Border: "#284A6598",
            BorderSubtle: "#182E446B",
            BorderStrong: "#45587CB8",
            Divider: "#202E446B",
            TextPrimary: "#FFF5FC",
            TextSecondary: "#E8CDE0",
            TextMuted: "#AC8EA0",
            TextDisabled: "#694C60",
            TextOnAccent: "#FFFFFF",
            AccentPrimary: "#FF1493",
            AccentSecondary: "#FF4FD8",
            AccentTertiary: "#9D4EDD",
            AccentHover: "#FF3BA7",
            AccentGlow: "#FF4FD8",
            AccentSurface: "#30FF1493",
            AmbientGlow: "#8A0E4F",
            Download: "#FF4FD8",
            DownloadBright: "#FF7FE2",
            DownloadMuted: "#FF4FD8",
            DownloadGlow: "#FF4FD8",
            DownloadSurface: "#30FF4FD8",
            Upload: "#9D4EDD",
            UploadBright: "#C77DFF",
            UploadDeep: "#7209B7",
            UploadGlow: "#9D4EDD",
            UploadSurface: "#309D4EDD",
            Success: "#5EEAD4",
            SuccessSurface: "#305EEAD4",
            Warning: "#FBBF24",
            Danger: "#FF5C8A",
            DangerSurface: "#30FF5C8A",
            ChartGrid: "#1A4A6598",
            ChartAxis: "#AC8EA0",
            ChartTooltipBackground: "#E8081226",
            ChartTooltipText: "#FFF5FC",
            ChartSegmentOther: "#9D5C80",
            ProcessPalette: new[]
            {
                "#FF1493", "#FF4FD8", "#9D4EDD", "#FF66B2", "#D946EF", "#FF007F",
                "#C77DFF", "#E879F9", "#F43F5E", "#A855F7", "#FB7185", "#00F0FF"
            },
            AppBackgroundGradientStops: new (string, double)[]
            {
                ("#000000", 0.00),
                ("#0E0314", 0.25),
                ("#280628", 0.50),
                ("#420B3A", 0.75),
                ("#0E0212", 1.00)
            },
            DownloadBarGradient: ("#FF7FE2", "#C026D3"),
            UploadBarGradient: ("#C77DFF", "#6B21A8"),
            HeroDownloadGradient: ("#38FF4FD8", "#00261020"),
            HeroUploadGradient: ("#389D4EDD", "#00261020"),
            HeroUsageGradient: ("#28FF1493", "#00261020"),
            DownloadAreaGradient: ("#22FF4FD8", "#02FF4FD8"),
            UploadAreaGradient: ("#229D4EDD", "#029D4EDD"),
            ActiveNavGradient: ("#30FF1493", "#08FF1493"),
            GradientDownload: ("#FF4FD8", "#FF7FE2"),
            GradientUpload: ("#9D4EDD", "#C77DFF"),
            GradientVioletPink: ("#FF1493", "#FF4FD8"),
            GradientCyanPink: ("#FF4FD8", "#9D4EDD")
        ),
        ["Arctic Light"] = new(
            Id: "Arctic Light",
            DisplayName: "Arctic Light",
            Icon: "🤍",
            IsLight: true,
            AppBackground: "#F4F7FB",
            NavigationBackground: "#90EDF4FB",
            Surface: "#B8FFFFFF",
            SurfaceElevated: "#D0FFFFFF",
            SurfaceSubtle: "#60EDF4FB",
            SurfaceHover: "#E6FFFFFF",
            Border: "#306080A0",
            BorderSubtle: "#1C8098B0",
            BorderStrong: "#505A7A98",
            Divider: "#25BAC6D6",
            TextPrimary: "#111827",
            TextSecondary: "#374151",
            TextMuted: "#6B7280",
            TextDisabled: "#9CA3AF",
            TextOnAccent: "#FFFFFF",
            AccentPrimary: "#6D28D9",
            AccentSecondary: "#0891B2",
            AccentTertiary: "#DB2777",
            AccentHover: "#5B21B6",
            AccentGlow: "#8B5CF6",
            AccentSurface: "#206D28D9",
            AmbientGlow: "#DDD6FE",
            Download: "#0891B2",
            DownloadBright: "#06B6D4",
            DownloadMuted: "#0891B2",
            DownloadGlow: "#0891B2",
            DownloadSurface: "#250891B2",
            Upload: "#7C3AED",
            UploadBright: "#8B5CF6",
            UploadDeep: "#5B21B6",
            UploadGlow: "#7C3AED",
            UploadSurface: "#257C3AED",
            Success: "#059669",
            SuccessSurface: "#25059669",
            Warning: "#D97706",
            Danger: "#DC2626",
            DangerSurface: "#25DC2626",
            ChartGrid: "#E2E8F0",
            ChartAxis: "#6B7280",
            ChartTooltipBackground: "#FAFFFFFF",
            ChartTooltipText: "#111827",
            ChartSegmentOther: "#94A3B8",
            ProcessPalette: new[]
            {
                "#0891B2", "#7C3AED", "#059669", "#DB2777", "#D97706", "#2563EB",
                "#9333EA", "#0D9488", "#EA580C", "#4F46E5", "#E11D48", "#0284C7"
            },
            AppBackgroundGradientStops: new (string, double)[]
            {
                ("#FFFFFF", 0.00),
                ("#F0F6FC", 0.25),
                ("#E1EDF8", 0.50),
                ("#D5E4F5", 0.75),
                ("#CBE0F5", 1.00)
            },
            DownloadBarGradient: ("#06B6D4", "#0891B2"),
            UploadBarGradient: ("#8B5CF6", "#7C3AED"),
            HeroDownloadGradient: ("#250891B2", "#00FFFFFF"),
            HeroUploadGradient: ("#257C3AED", "#00FFFFFF"),
            HeroUsageGradient: ("#186D28D9", "#00FFFFFF"),
            DownloadAreaGradient: ("#200891B2", "#020891B2"),
            UploadAreaGradient: ("#207C3AED", "#027C3AED"),
            ActiveNavGradient: ("#256D28D9", "#086D28D9"),
            GradientDownload: ("#0891B2", "#06B6D4"),
            GradientUpload: ("#7C3AED", "#8B5CF6"),
            GradientVioletPink: ("#6D28D9", "#DB2777"),
            GradientCyanPink: ("#0891B2", "#DB2777")
        )
    };

    public IReadOnlyList<ThemeOption> AvailableThemes => Themes;

    public string CurrentThemeId { get; private set; } = "Neon Space";

    public ThemeOption CurrentTheme => Themes.FirstOrDefault(t => t.Id.Equals(CurrentThemeId, StringComparison.OrdinalIgnoreCase)) ?? Themes[0];

    public event Action<string>? ThemeChanged;

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
                if (!string.IsNullOrWhiteSpace(savedTheme) && Themes.Any(t => t.Id.Equals(savedTheme, StringComparison.OrdinalIgnoreCase)))
                {
                    CurrentThemeId = savedTheme;
                    ApplyThemeInternal(savedTheme);
                    return;
                }
            }
            catch { }
        }

        // Apply default if no saved theme
        ApplyThemeInternal(CurrentThemeId);
    }

    public void ApplyTheme(string themeId)
    {
        var matched = Themes.FirstOrDefault(t => t.Id.Equals(themeId, StringComparison.OrdinalIgnoreCase));
        if (matched == null) return;

        CurrentThemeId = matched.Id;
        ApplyThemeInternal(matched.Id);
        ThemeChanged?.Invoke(matched.Id);

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

    public IReadOnlyList<string> GetProcessPaletteHex(string? themeId = null)
    {
        string targetId = themeId ?? CurrentThemeId;
        if (ThemeDefinitions.TryGetValue(targetId, out var def))
        {
            return def.ProcessPalette;
        }
        return ThemeDefinitions["Neon Space"].ProcessPalette;
    }

    public static ThemeDefinition GetThemeDefinition(string themeId)
    {
        if (ThemeDefinitions.TryGetValue(themeId, out var def))
            return def;
        return ThemeDefinitions["Neon Space"];
    }

    private static void ApplyThemeInternal(string themeId)
    {
        var def = GetThemeDefinition(themeId);

        void UpdateResources()
        {
            if (Application.Current == null) return;

            var res = Application.Current.Resources;

            // Set RequestedThemeVariant for FluentTheme controls
            Application.Current.RequestedThemeVariant = def.IsLight ? ThemeVariant.Light : ThemeVariant.Dark;

            // 1. Backgrounds & Surfaces (Glass Translucency & Theme Atmospheric Gradient)
            res["Brush.AppBackground"] = new SolidColorBrush(Color.Parse(def.AppBackground));
            res["Brush.AppBackgroundGradient"] = CreateDiagonalGradient(def.AppBackgroundGradientStops);
            res["Brush.NavigationBackground"] = new SolidColorBrush(Color.Parse(def.NavigationBackground));
            res["Brush.Surface"] = new SolidColorBrush(Color.Parse(def.Surface));
            res["Brush.SurfaceElevated"] = new SolidColorBrush(Color.Parse(def.SurfaceElevated));
            res["Brush.SurfaceSubtle"] = new SolidColorBrush(Color.Parse(def.SurfaceSubtle));
            res["Brush.SurfaceHover"] = new SolidColorBrush(Color.Parse(def.SurfaceHover));
            res["Brush.GlassSurface"] = res["Brush.Surface"];
            res["Brush.GlassSurfaceElevated"] = res["Brush.SurfaceElevated"];
            res["Brush.GlassHighlight"] = new SolidColorBrush(def.IsLight ? Color.Parse("#40FFFFFF") : Color.Parse("#14FFFFFF"));
            res["Brush.GlassShadow"] = new SolidColorBrush(def.IsLight ? Color.Parse("#10000000") : Color.Parse("#20000000"));

            // 1.5. Liquid Glass Interaction System Semantic Brushes
            res["Brush.LiquidGlass.Surface"] = new SolidColorBrush(def.IsLight ? Color.Parse("#30FFFFFF") : Color.Parse("#14FFFFFF"));
            res["Brush.LiquidGlass.SurfaceHover"] = new SolidColorBrush(def.IsLight ? Color.Parse("#55FFFFFF") : Color.Parse("#24FFFFFF"));
            res["Brush.LiquidGlass.SurfacePressed"] = new SolidColorBrush(def.IsLight ? Color.Parse("#3DFFFFFF") : Color.Parse("#18FFFFFF"));
            res["Brush.LiquidGlass.SurfaceSelected"] = new SolidColorBrush(Color.Parse(WithAlpha(def.AccentPrimary, def.IsLight ? "35" : "30")));
            res["Brush.LiquidGlass.SurfaceSelectedHover"] = new SolidColorBrush(Color.Parse(WithAlpha(def.AccentPrimary, def.IsLight ? "48" : "44")));
            res["Brush.LiquidGlass.Border"] = new SolidColorBrush(def.IsLight ? Color.Parse("#3094A3B8") : Color.Parse("#25FFFFFF"));
            res["Brush.LiquidGlass.BorderHover"] = new SolidColorBrush(def.IsLight ? Color.Parse("#6064748B") : Color.Parse("#55FFFFFF"));
            res["Brush.LiquidGlass.BorderSelected"] = new SolidColorBrush(Color.Parse(WithAlpha(def.AccentPrimary, def.IsLight ? "90" : "85")));
            res["Brush.LiquidGlass.InnerHighlight"] = new SolidColorBrush(def.IsLight ? Color.Parse("#90FFFFFF") : Color.Parse("#40FFFFFF"));
            res["Brush.LiquidGlass.Glow"] = new SolidColorBrush(Color.Parse(WithAlpha(def.AccentPrimary, "25")));
            res["Brush.LiquidGlass.ActiveIndicator"] = new SolidColorBrush(Color.Parse(def.AccentPrimary));
            res["Brush.LiquidGlass.PillBackground"] = new SolidColorBrush(def.IsLight ? Color.Parse("#30EDF4FB") : Color.Parse("#1A061224"));
            res["Brush.LiquidGlass.PillBorder"] = new SolidColorBrush(def.IsLight ? Color.Parse("#40CBD5E1") : Color.Parse("#30284568"));

            // 2. Borders & Dividers
            res["Brush.Border"] = new SolidColorBrush(Color.Parse(def.Border));
            res["Brush.BorderSubtle"] = new SolidColorBrush(Color.Parse(def.BorderSubtle));
            res["Brush.BorderStrong"] = new SolidColorBrush(Color.Parse(def.BorderStrong));
            res["Brush.GlassBorder"] = res["Brush.Border"];
            res["Brush.Divider"] = new SolidColorBrush(Color.Parse(def.Divider));

            // 3. Typography (Theme-Aware High-Contrast Typography)
            res["Brush.TextPrimary"] = new SolidColorBrush(Color.Parse(def.TextPrimary));
            res["Brush.TextSecondary"] = new SolidColorBrush(Color.Parse(def.TextSecondary));
            res["Brush.TextMuted"] = new SolidColorBrush(Color.Parse(def.TextMuted));
            res["Brush.TextDisabled"] = new SolidColorBrush(Color.Parse(def.TextDisabled));
            res["Brush.TextOnAccent"] = new SolidColorBrush(Color.Parse(def.TextOnAccent));

            // 4. Accents
            res["Brush.Accent"] = new SolidColorBrush(Color.Parse(def.AccentPrimary));
            res["Brush.AccentBright"] = new SolidColorBrush(Color.Parse(def.AccentSecondary));
            res["Brush.AccentDeep"] = new SolidColorBrush(Color.Parse(def.AccentTertiary));
            res["Brush.AccentHover"] = new SolidColorBrush(Color.Parse(def.AccentHover));
            res["Brush.AccentGlow"] = new SolidColorBrush(Color.Parse(def.AccentGlow));
            res["Brush.AccentSurface"] = new SolidColorBrush(Color.Parse(def.AccentSurface));
            res["Brush.AmbientGlow"] = new SolidColorBrush(Color.Parse(def.AmbientGlow));

            // 5. Download Palette
            res["Brush.Download"] = new SolidColorBrush(Color.Parse(def.Download));
            res["Brush.DownloadBright"] = new SolidColorBrush(Color.Parse(def.DownloadBright));
            res["Brush.DownloadMuted"] = new SolidColorBrush(Color.Parse(def.DownloadMuted));
            res["Brush.DownloadGlow"] = new SolidColorBrush(Color.Parse(def.DownloadGlow));
            res["Brush.DownloadSurface"] = new SolidColorBrush(Color.Parse(def.DownloadSurface));

            // 6. Upload Palette
            res["Brush.Upload"] = new SolidColorBrush(Color.Parse(def.Upload));
            res["Brush.UploadBright"] = new SolidColorBrush(Color.Parse(def.UploadBright));
            res["Brush.UploadDeep"] = new SolidColorBrush(Color.Parse(def.UploadDeep));
            res["Brush.UploadGlow"] = new SolidColorBrush(Color.Parse(def.UploadGlow));
            res["Brush.UploadSurface"] = new SolidColorBrush(Color.Parse(def.UploadSurface));

            // 7. Status & Feedback
            res["Brush.Success"] = new SolidColorBrush(Color.Parse(def.Success));
            res["Brush.SuccessSurface"] = new SolidColorBrush(Color.Parse(def.SuccessSurface));
            res["Brush.Warning"] = new SolidColorBrush(Color.Parse(def.Warning));
            res["Brush.Danger"] = new SolidColorBrush(Color.Parse(def.Danger));
            res["Brush.DangerSurface"] = new SolidColorBrush(Color.Parse(def.DangerSurface));

            // 8. Charts, Gridlines & Tooltips
            res["Brush.ChartGrid"] = new SolidColorBrush(Color.Parse(def.ChartGrid));
            res["Brush.ChartAxis"] = new SolidColorBrush(Color.Parse(def.ChartAxis));
            res["Brush.ChartTooltip"] = new SolidColorBrush(Color.Parse(def.ChartTooltipBackground));
            res["Brush.ChartTooltipText"] = new SolidColorBrush(Color.Parse(def.ChartTooltipText));
            res["Brush.ChartSegmentOther"] = new SolidColorBrush(Color.Parse(def.ChartSegmentOther));

            // 9. 12 Process Chart Segment Brushes
            for (int i = 0; i < def.ProcessPalette.Length; i++)
            {
                var brush = new SolidColorBrush(Color.Parse(def.ProcessPalette[i]));
                res[$"Brush.ChartSegment{i + 1}"] = brush;
                res[$"Brush.ProcessChart{i + 1}"] = brush;
            }

            // 10. Linear Gradients
            res["Brush.DownloadBarGradient"] = CreateLinearGradient(def.DownloadBarGradient.Start, def.DownloadBarGradient.End, isVertical: true);
            res["Brush.UploadBarGradient"] = CreateLinearGradient(def.UploadBarGradient.Start, def.UploadBarGradient.End, isVertical: true);
            res["Brush.HeroDownloadGradient"] = CreateLinearGradient(def.HeroDownloadGradient.Start, def.HeroDownloadGradient.End, isVertical: true);
            res["Brush.HeroUploadGradient"] = CreateLinearGradient(def.HeroUploadGradient.Start, def.HeroUploadGradient.End, isVertical: true);
            res["Brush.HeroUsageGradient"] = CreateLinearGradient(def.HeroUsageGradient.Start, def.HeroUsageGradient.End, isVertical: true);
            res["Brush.DownloadAreaGradient"] = CreateLinearGradient(def.DownloadAreaGradient.Start, def.DownloadAreaGradient.End, isVertical: true);
            res["Brush.UploadAreaGradient"] = CreateLinearGradient(def.UploadAreaGradient.Start, def.UploadAreaGradient.End, isVertical: true);
            res["Brush.ActiveNavGradient"] = CreateLinearGradient(def.ActiveNavGradient.Start, def.ActiveNavGradient.End, isVertical: false);
            res["Brush.GradientDownload"] = CreateLinearGradient(def.GradientDownload.Start, def.GradientDownload.End, isVertical: false);
            res["Brush.GradientUpload"] = CreateLinearGradient(def.GradientUpload.Start, def.GradientUpload.End, isVertical: false);
            res["Brush.GradientVioletPink"] = CreateLinearGradient(def.GradientVioletPink.Start, def.GradientVioletPink.End, isVertical: false);
            res["Brush.GradientCyanPink"] = CreateLinearGradient(def.GradientCyanPink.Start, def.GradientCyanPink.End, isVertical: false);
            res["Brush.GradientAccent"] = CreateLinearGradient(def.AccentPrimary, def.AccentSecondary, isVertical: false);

            // 11. FluentTheme System Overrides
            res["SystemRegionBrush"] = new SolidColorBrush(Color.Parse(def.AppBackground));
            res["SystemControlBackgroundAltHighBrush"] = new SolidColorBrush(Color.Parse(def.AppBackground));
            res["SystemControlBackgroundBaseLowBrush"] = new SolidColorBrush(Color.Parse(def.Surface));
            res["SystemControlBackgroundChromeMediumLowBrush"] = new SolidColorBrush(Color.Parse(def.Surface));
            res["SystemControlBackgroundChromeLowBrush"] = new SolidColorBrush(Color.Parse(def.SurfaceElevated));
            res["SystemControlBackgroundListLowBrush"] = new SolidColorBrush(Color.Parse(def.Surface));
            res["SystemControlForegroundBaseHighBrush"] = new SolidColorBrush(Color.Parse(def.TextPrimary));
            res["SystemControlForegroundBaseMediumBrush"] = new SolidColorBrush(Color.Parse(def.TextSecondary));
            res["SystemControlForegroundBaseMediumLowBrush"] = new SolidColorBrush(Color.Parse(def.TextMuted));
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

    private static LinearGradientBrush CreateDiagonalGradient((string Hex, double Offset)[] stops)
    {
        var gradientStops = new GradientStops();
        foreach (var stop in stops)
        {
            gradientStops.Add(new GradientStop(Color.Parse(stop.Hex), stop.Offset));
        }

        return new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
            GradientStops = gradientStops
        };
    }

    private static LinearGradientBrush CreateLinearGradient(string startHex, string endHex, bool isVertical)
    {
        return new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = isVertical ? new RelativePoint(0, 1, RelativeUnit.Relative) : new RelativePoint(1, 0, RelativeUnit.Relative),
            GradientStops = new GradientStops
            {
                new(Color.Parse(startHex), 0),
                new(Color.Parse(endHex), 1)
            }
        };
    }

    private static string WithAlpha(string hexColor, string alphaHex)
    {
        var hex = hexColor.TrimStart('#');
        if (hex.Length == 8) hex = hex.Substring(2);
        if (hex.Length == 6) return $"#{alphaHex}{hex}";
        return $"#{alphaHex}FFFFFF";
    }
}
