using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace DataSense.Services;

/// <summary>
/// Deterministic and session-stable application color provider.
/// Assigns harmonized, distinct palette brushes from the DataSense visual design system
/// ensuring that each application preserves its exact assigned color across refreshes and rank shifts.
/// </summary>
public class ApplicationChartColorProvider : IApplicationChartColorProvider
{
    public static ApplicationChartColorProvider Instance { get; } = new();

    private static readonly string[] PaletteHex = new[]
    {
        "#00F0FF", // 0: Electric Cyan
        "#34D399", // 1: Emerald
        "#FBBF24", // 2: Amber
        "#FF007F", // 3: Hot Pink
        "#A855F7", // 4: Violet
        "#60A5FA", // 5: Sky Blue
        "#818CF8", // 6: Indigo
        "#14B8A6", // 7: Teal
        "#D000FF", // 8: Magenta
        "#8B5CF6", // 9: Purple
        "#FB7185", // 10: Rose
        "#00E5FF"  // 11: Turquoise
    };

    private static readonly string[] PaletteTokens = new[]
    {
        "Brush.ChartSegment1",
        "Brush.ChartSegment2",
        "Brush.ChartSegment3",
        "Brush.ChartSegment4",
        "Brush.ChartSegment5",
        "Brush.ChartSegment6",
        "Brush.ChartSegment7",
        "Brush.ChartSegment8",
        "Brush.ChartSegment9",
        "Brush.ChartSegment10",
        "Brush.ChartSegment11",
        "Brush.ChartSegment12"
    };

    private static readonly IBrush[] StaticPaletteBrushes = new IBrush[]
    {
        new SolidColorBrush(Color.Parse("#00F0FF")),
        new SolidColorBrush(Color.Parse("#34D399")),
        new SolidColorBrush(Color.Parse("#FBBF24")),
        new SolidColorBrush(Color.Parse("#FF007F")),
        new SolidColorBrush(Color.Parse("#A855F7")),
        new SolidColorBrush(Color.Parse("#60A5FA")),
        new SolidColorBrush(Color.Parse("#818CF8")),
        new SolidColorBrush(Color.Parse("#14B8A6")),
        new SolidColorBrush(Color.Parse("#D000FF")),
        new SolidColorBrush(Color.Parse("#8B5CF6")),
        new SolidColorBrush(Color.Parse("#FB7185")),
        new SolidColorBrush(Color.Parse("#00E5FF"))
    };

    private static readonly (string Start, string End)[] GradientPairs = new[]
    {
        ("#22F6FF", "#0088CC"), // 0: Electric Cyan
        ("#4ADE80", "#059669"), // 1: Emerald
        ("#FCD34D", "#D97706"), // 2: Amber
        ("#FF3399", "#BE123C"), // 3: Hot Pink
        ("#C084FC", "#6D28D9"), // 4: Violet
        ("#60A5FA", "#1D4ED8"), // 5: Sky Blue
        ("#A5B4FC", "#4338CA"), // 6: Indigo
        ("#2DD4BF", "#0F766E"), // 7: Teal
        ("#E879F9", "#9333EA"), // 8: Magenta
        ("#A78BFA", "#5B21B6"), // 9: Purple
        ("#FDA4AF", "#BE123C"), // 10: Rose
        ("#38BDF8", "#0284C7")  // 11: Turquoise
    };

    private static readonly IBrush[] StaticPaletteGradients = CreatePaletteGradients();

    private static IBrush[] CreatePaletteGradients()
    {
        var list = new IBrush[GradientPairs.Length];
        for (int i = 0; i < GradientPairs.Length; i++)
        {
            list[i] = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                GradientStops = new GradientStops
                {
                    new GradientStop(Color.Parse(GradientPairs[i].Start), 0.0),
                    new GradientStop(Color.Parse(GradientPairs[i].End), 1.0)
                }
            };
        }
        return list;
    }

    private static readonly IBrush OtherBrush = new SolidColorBrush(Color.Parse("#6E6E9B"));

    private static readonly IBrush OtherGradient = new LinearGradientBrush
    {
        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
        EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
        GradientStops = new GradientStops
        {
            new GradientStop(Color.Parse("#8E8EB8"), 0.0),
            new GradientStop(Color.Parse("#4E4E78"), 1.0)
        }
    };

    // Signature presets for known common applications to guarantee beautiful initial layouts
    private static readonly Dictionary<string, int> KnownPresets = new(StringComparer.OrdinalIgnoreCase)
    {
        { "brave", 0 },
        { "brave-browser", 0 },
        { "chrome", 0 },
        { "google-chrome", 0 },
        { "chromium", 0 },
        { "firefox", 0 },

        { "spotify", 1 },

        { "steam", 2 },
        { "steamwebhelper", 2 },

        { "discord", 3 },

        { "code", 4 },
        { "vscode", 4 },
        { "cursor", 4 },
        { "antigravity", 4 },
        { "antigravity-ide", 4 },

        { "slack", 5 },

        { "docker", 6 },

        { "git", 7 },
        { "curl", 7 },
        { "wget", 7 },
        { "nethogs", 7 },

        { "telegram", 8 },
        { "telegram-desktop", 8 },

        { "python", 9 },
        { "python3", 9 },

        { "vlc", 10 },
        { "obs", 10 },

        { "node", 11 },
        { "dotnet", 11 },
        { "datasense", 11 }
    };

    private readonly ConcurrentDictionary<string, int> _sessionAssignments = new(StringComparer.OrdinalIgnoreCase);
    private int _nextDynamicIndex = 0;

    public int GetColorIndex(string? processIdentifier)
    {
        if (string.IsNullOrWhiteSpace(processIdentifier))
            return 0;

        string clean = processIdentifier.Trim().ToLowerInvariant();

        if (IsOtherProcess(clean))
            return -1; // Special marker for Other

        if (KnownPresets.TryGetValue(clean, out int preset))
            return preset;

        return _sessionAssignments.GetOrAdd(clean, key =>
        {
            // Consecutive non-duplicate allocation across 12 palette entries
            int next = System.Threading.Interlocked.Increment(ref _nextDynamicIndex) - 1;
            return (next % PaletteTokens.Length + PaletteTokens.Length) % PaletteTokens.Length;
        });
    }

    public IBrush GetColorBrush(string? processIdentifier)
    {
        int index = GetColorIndex(processIdentifier);
        return GetColorBrushByIndex(index);
    }

    public IBrush GetColorBrushByIndex(int index)
    {
        if (index < 0)
        {
            if (Application.Current != null &&
                Application.Current.TryFindResource("Brush.ChartSegmentOther", out var otherRes) &&
                otherRes is IBrush ob)
            {
                return ob;
            }
            return OtherBrush;
        }

        string token = PaletteTokens[index % PaletteTokens.Length];
        if (Application.Current != null &&
            Application.Current.TryFindResource(token, out var res) &&
            res is IBrush b)
        {
            return b;
        }

        return StaticPaletteBrushes[index % StaticPaletteBrushes.Length];
    }

    public IBrush GetGradientBrush(string? processIdentifier)
    {
        int index = GetColorIndex(processIdentifier);
        return GetGradientBrushByIndex(index);
    }

    public IBrush GetGradientBrushByIndex(int index)
    {
        if (index < 0)
        {
            return OtherGradient;
        }

        return StaticPaletteGradients[index % StaticPaletteGradients.Length];
    }

    public string GetColorHex(string? processIdentifier)
    {
        int index = GetColorIndex(processIdentifier);
        return GetColorHexByIndex(index);
    }

    public string GetColorHexByIndex(int index)
    {
        if (index < 0)
        {
            if (Application.Current != null &&
                Application.Current.TryFindResource("Brush.ChartSegmentOther", out var otherRes) &&
                otherRes is ISolidColorBrush scb)
            {
                return $"#{scb.Color.R:X2}{scb.Color.G:X2}{scb.Color.B:X2}";
            }
            return "#6E6E9B";
        }

        string token = PaletteTokens[index % PaletteTokens.Length];
        if (Application.Current != null &&
            Application.Current.TryFindResource(token, out var res) &&
            res is ISolidColorBrush brush)
        {
            return $"#{brush.Color.R:X2}{brush.Color.G:X2}{brush.Color.B:X2}";
        }

        return PaletteHex[index % PaletteHex.Length];
    }

    public string GetColorToken(string? processIdentifier)
    {
        int index = GetColorIndex(processIdentifier);
        return GetColorTokenByIndex(index);
    }

    public string GetColorTokenByIndex(int index)
    {
        if (index < 0) return "Brush.ChartSegmentOther";
        return PaletteTokens[index % PaletteTokens.Length];
    }

    private static bool IsOtherProcess(string name)
    {
        return name.Equals("other", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("others", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("[other]", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("other applications", StringComparison.OrdinalIgnoreCase);
    }

    private static int GetStableHash(string str)
    {
        unchecked
        {
            int hash1 = 5381;
            int hash2 = hash1;

            for (int i = 0; i < str.Length && str[i] != '\0'; i += 2)
            {
                hash1 = ((hash1 << 5) + hash1) ^ str[i];
                if (i == str.Length - 1 || str[i + 1] == '\0')
                    break;
                hash2 = ((hash2 << 5) + hash2) ^ str[i + 1];
            }

            return hash1 + (hash2 * 1566083941);
        }
    }
}
