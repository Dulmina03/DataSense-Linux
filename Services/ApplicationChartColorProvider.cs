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
        "#38BDF8", // 0: Electric Cyan
        "#A855F7", // 1: Sophisticated Violet
        "#6366F1", // 2: Indigo
        "#14B8A6", // 3: Teal
        "#F59E0B", // 4: Muted Amber
        "#3B82F6", // 5: Blue
        "#C084FC", // 6: Muted Purple
        "#10B981"  // 7: Emerald
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
        "Brush.ChartSegment8"
    };

    private static readonly IBrush[] StaticPaletteBrushes = new IBrush[]
    {
        new SolidColorBrush(Color.Parse("#38BDF8")),
        new SolidColorBrush(Color.Parse("#A855F7")),
        new SolidColorBrush(Color.Parse("#6366F1")),
        new SolidColorBrush(Color.Parse("#14B8A6")),
        new SolidColorBrush(Color.Parse("#F59E0B")),
        new SolidColorBrush(Color.Parse("#3B82F6")),
        new SolidColorBrush(Color.Parse("#C084FC")),
        new SolidColorBrush(Color.Parse("#10B981"))
    };

    private static readonly IBrush OtherBrush = new SolidColorBrush(Color.Parse("#64748B"));

    // Signature presets for known common applications to guarantee beautiful initial layouts
    private static readonly Dictionary<string, int> KnownPresets = new(StringComparer.OrdinalIgnoreCase)
    {
        { "brave", 0 },
        { "brave-browser", 0 },
        { "chrome", 0 },
        { "google-chrome", 0 },
        { "chromium", 0 },
        { "firefox", 0 },

        { "steam", 1 },
        { "steamwebhelper", 1 },

        { "code", 2 },
        { "vscode", 2 },
        { "cursor", 2 },
        { "antigravity", 2 },
        { "antigravity-ide", 2 },

        { "discord", 3 },
        { "telegram", 3 },
        { "telegram-desktop", 3 },
        { "slack", 3 },

        { "spotify", 4 },
        { "vlc", 4 },
        { "obs", 4 },

        { "node", 5 },
        { "dotnet", 5 },
        { "datasense", 5 },

        { "python", 6 },
        { "python3", 6 },
        { "docker", 6 },

        { "git", 7 },
        { "curl", 7 },
        { "wget", 7 },
        { "nethogs", 7 }
    };

    private readonly ConcurrentDictionary<string, int> _sessionAssignments = new(StringComparer.OrdinalIgnoreCase);

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
            // Deterministic stable hash across 8 palette entries
            int hash = GetStableHash(key);
            return Math.Abs(hash) % PaletteTokens.Length;
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
        if (index < 0) return "#64748B";
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
