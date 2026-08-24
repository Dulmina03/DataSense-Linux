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
        "#A855F7", // 1: Electric Violet
        "#D000FF", // 2: Neon Magenta
        "#FF007F", // 3: Hot Pink / Rose
        "#00E5FF", // 4: Electric Turquoise
        "#8B5CF6", // 5: Soft Purple
        "#818CF8", // 6: Indigo
        "#14B8A6", // 7: Teal
        "#34D399", // 8: Emerald
        "#60A5FA", // 9: Soft Blue
        "#FBBF24", // 10: Amber
        "#FB7185"  // 11: Rose
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
        new SolidColorBrush(Color.Parse("#A855F7")),
        new SolidColorBrush(Color.Parse("#D000FF")),
        new SolidColorBrush(Color.Parse("#FF007F")),
        new SolidColorBrush(Color.Parse("#00E5FF")),
        new SolidColorBrush(Color.Parse("#8B5CF6")),
        new SolidColorBrush(Color.Parse("#818CF8")),
        new SolidColorBrush(Color.Parse("#14B8A6")),
        new SolidColorBrush(Color.Parse("#34D399")),
        new SolidColorBrush(Color.Parse("#60A5FA")),
        new SolidColorBrush(Color.Parse("#FBBF24")),
        new SolidColorBrush(Color.Parse("#FB7185"))
    };

    private static readonly IBrush OtherBrush = new SolidColorBrush(Color.Parse("#6E6E9B"));

    // Signature presets for known common applications to guarantee beautiful initial layouts
    private static readonly Dictionary<string, int> KnownPresets = new(StringComparer.OrdinalIgnoreCase)
    {
        { "brave", 0 },
        { "brave-browser", 0 },
        { "chrome", 0 },
        { "google-chrome", 0 },
        { "chromium", 0 },
        { "firefox", 0 },

        { "code", 1 },
        { "vscode", 1 },
        { "cursor", 1 },
        { "antigravity", 1 },
        { "antigravity-ide", 1 },

        { "telegram", 2 },
        { "telegram-desktop", 2 },

        { "vlc", 3 },
        { "obs", 3 },

        { "node", 4 },
        { "dotnet", 4 },
        { "datasense", 4 },

        { "python", 5 },
        { "python3", 5 },

        { "docker", 6 },

        { "git", 7 },
        { "curl", 7 },
        { "wget", 7 },
        { "nethogs", 7 },

        { "spotify", 8 },

        { "slack", 9 },

        { "steam", 10 },
        { "steamwebhelper", 10 },

        { "discord", 11 }
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
        if (index < 0)
        {
            // Try resolving theme resource if Application.Current is available
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

    public string GetColorHex(string? processIdentifier)
    {
        int index = GetColorIndex(processIdentifier);
        if (index < 0) return "#6E6E9B";
        return PaletteHex[index % PaletteHex.Length];
    }

    public string GetColorToken(string? processIdentifier)
    {
        int index = GetColorIndex(processIdentifier);
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
