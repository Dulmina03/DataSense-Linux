using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace DataSense.Services;

/// <summary>
/// High-performance Linux application icon and identity resolution service.
/// Parses installed desktop entries (.desktop files), resolves theme icons from standard XDG paths,
/// and caches decoded bitmaps for smooth rendering in the UI.
/// </summary>
public class LinuxApplicationIconService : IAppIconService
{
    private sealed record DesktopEntry(
        string Name, 
        string GenericName, 
        string Icon, 
        string Exec, 
        string StartupWMClass);

    private readonly ConcurrentDictionary<string, DesktopEntry> _desktopEntries = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, IImage> _iconCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lazy<IImage> _genericApplicationIconLazy;

    private static readonly string[] DesktopFileLocations =
    {
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local/share/applications"),
        "/usr/local/share/applications",
        "/usr/share/applications",
        "/var/lib/flatpak/exports/share/applications",
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local/share/flatpak/exports/share/applications"),
        "/var/lib/snapd/desktop/applications"
    };

    private static readonly string[] IconSearchBaseDirectories =
    {
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local/share/icons"),
        "/usr/local/share/icons",
        "/usr/share/icons",
        "/usr/share/pixmaps",
        "/usr/local/share/pixmaps"
    };

    private static readonly string[] PreferredThemeNames =
    {
        "hicolor",
        "Yaru",
        "Adwaita",
        "Papirus",
        "breeze",
        "gnome",
        "locolor",
        "default"
    };

    private static readonly string[] PreferredSizes =
    {
        "48x48",
        "64x64",
        "32x32",
        "128x128",
        "256x256",
        "512x512",
        "24x24",
        "16x16"
    };

    private static readonly string[] SupportedExtensions =
    {
        ".png",
        ".jpg",
        ".jpeg",
        ".webp",
        ".ico"
    };

    // Common Linux CLI/Runtime/Process Friendly Display Names
    private static readonly Dictionary<string, (string DisplayName, string? IconHint)> KnownApplicationAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        { "brave", ("Brave Web Browser", "brave-browser") },
        { "brave-browser", ("Brave Web Browser", "brave-browser") },
        { "chrome", ("Google Chrome", "google-chrome") },
        { "google-chrome", ("Google Chrome", "google-chrome") },
        { "google-chrome-stable", ("Google Chrome", "google-chrome") },
        { "chromium", ("Chromium", "chromium") },
        { "firefox", ("Firefox", "firefox") },
        { "code", ("Visual Studio Code", "code") },
        { "vscode", ("Visual Studio Code", "code") },
        { "cursor", ("Cursor", "co.anysphere.cursor") },
        { "antigravity", ("Antigravity IDE", "code") },
        { "antigravity-ide", ("Antigravity IDE", "code") },
        { "discord", ("Discord", "discord") },
        { "steam", ("Steam", "steam") },
        { "spotify", ("Spotify", "spotify") },
        { "slack", ("Slack", "slack") },
        { "telegram-desktop", ("Telegram", "telegram") },
        { "vlc", ("VLC Media Player", "vlc") },
        { "obs", ("OBS Studio", "com.obsproject.Studio") },
        { "dotnet", (".NET Runtime", null) },
        { "node", ("Node.js", null) },
        { "python", ("Python", null) },
        { "python3", ("Python 3", null) },
        { "curl", ("cURL", null) },
        { "wget", ("Wget", null) },
        { "git", ("Git", null) },
        { "nethogs", ("Nethogs", null) },
        { "systemd-resolved", ("systemd-resolved", null) },
        { "datasense", ("DataSense", "datasense") }
    };

    public IImage GenericApplicationIcon => _genericApplicationIconLazy.Value;

    public LinuxApplicationIconService()
    {
        _genericApplicationIconLazy = new Lazy<IImage>(CreateGenericApplicationIcon);
        IndexDesktopEntries();
    }

    public string GetApplicationDisplayName(string processIdentifier, string? executablePath = null)
    {
        if (string.IsNullOrWhiteSpace(processIdentifier))
            return "Application";

        string cleanName = NormalizeProcessName(processIdentifier);

        // 1. Check known aliases first (authoritative clean display names)
        if (KnownApplicationAliases.TryGetValue(cleanName, out var alias) && !string.IsNullOrWhiteSpace(alias.DisplayName))
        {
            return alias.DisplayName;
        }

        // 2. Check if desktop entry matches
        if (_desktopEntries.TryGetValue(cleanName, out var entry) && !string.IsNullOrWhiteSpace(entry.Name))
        {
            return entry.Name;
        }

        // 3. Check executable name if provided
        if (!string.IsNullOrWhiteSpace(executablePath))
        {
            string execName = NormalizeProcessName(Path.GetFileName(executablePath));
            if (KnownApplicationAliases.TryGetValue(execName, out var execAlias) && !string.IsNullOrWhiteSpace(execAlias.DisplayName))
            {
                return execAlias.DisplayName;
            }

            if (_desktopEntries.TryGetValue(execName, out var execEntry) && !string.IsNullOrWhiteSpace(execEntry.Name))
            {
                return execEntry.Name;
            }
        }

        // 4. Clean formatting: Title-case or keep recognizable
        return FormatGenericDisplayName(cleanName);
    }

    public IImage GetApplicationIcon(string processIdentifier, string? executablePath = null)
    {
        if (string.IsNullOrWhiteSpace(processIdentifier))
            return GenericApplicationIcon;

        string cleanName = NormalizeProcessName(processIdentifier);

        if (_iconCache.TryGetValue(cleanName, out var cachedIcon))
            return cachedIcon;

        // Try resolving through the pipeline
        IImage? resolved = ResolveIconForProcess(cleanName, executablePath);
        IImage finalIcon = resolved ?? GenericApplicationIcon;

        _iconCache.TryAdd(cleanName, finalIcon);
        return finalIcon;
    }

    private IImage? ResolveIconForProcess(string cleanName, string? executablePath)
    {
        // 1. Check desktop entries for icon metadata
        string? iconNameOrPath = null;
        if (_desktopEntries.TryGetValue(cleanName, out var entry) && !string.IsNullOrWhiteSpace(entry.Icon))
        {
            iconNameOrPath = entry.Icon;
        }
        else if (!string.IsNullOrWhiteSpace(executablePath))
        {
            string execName = NormalizeProcessName(Path.GetFileName(executablePath));
            if (_desktopEntries.TryGetValue(execName, out var execEntry) && !string.IsNullOrWhiteSpace(execEntry.Icon))
            {
                iconNameOrPath = execEntry.Icon;
            }
        }

        // 2. Check known alias hint
        if (string.IsNullOrWhiteSpace(iconNameOrPath) && KnownApplicationAliases.TryGetValue(cleanName, out var alias))
        {
            iconNameOrPath = alias.IconHint;
        }

        // 3. Fallback to process name itself as icon hint
        if (string.IsNullOrWhiteSpace(iconNameOrPath))
        {
            iconNameOrPath = cleanName;
        }

        // 4. Resolve absolute or theme icon file
        string? iconFilePath = FindIconFile(iconNameOrPath);
        if (iconFilePath != null && File.Exists(iconFilePath))
        {
            try
            {
                var bitmap = new Bitmap(iconFilePath);
                return bitmap;
            }
            catch
            {
                // Fall through on corrupt / unsupported image file or headless test runner
            }
        }

        return null;
    }

    private string? FindIconFile(string iconNameOrPath)
    {
        if (string.IsNullOrWhiteSpace(iconNameOrPath))
            return null;

        // If it's already an absolute path
        if (Path.IsPathRooted(iconNameOrPath) && File.Exists(iconNameOrPath))
        {
            return iconNameOrPath;
        }

        string rawName = Path.GetFileNameWithoutExtension(iconNameOrPath);

        // 1. Direct search in pixmaps
        foreach (var ext in SupportedExtensions)
        {
            string pixmapPath = Path.Combine("/usr/share/pixmaps", rawName + ext);
            if (File.Exists(pixmapPath)) return pixmapPath;

            string localPixmapPath = Path.Combine("/usr/local/share/pixmaps", rawName + ext);
            if (File.Exists(localPixmapPath)) return localPixmapPath;
        }

        // 2. Search in theme directories across sizes
        foreach (var baseDir in IconSearchBaseDirectories)
        {
            if (!Directory.Exists(baseDir)) continue;

            foreach (var theme in PreferredThemeNames)
            {
                string themePath = Path.Combine(baseDir, theme);
                if (!Directory.Exists(themePath)) continue;

                foreach (var size in PreferredSizes)
                {
                    string appsDir = Path.Combine(themePath, size, "apps");
                    if (!Directory.Exists(appsDir)) continue;

                    foreach (var ext in SupportedExtensions)
                    {
                        string candidate = Path.Combine(appsDir, rawName + ext);
                        if (File.Exists(candidate))
                        {
                            return candidate;
                        }
                    }
                }
            }
        }

        // 3. Fallback search for any match under hicolor/
        foreach (var baseDir in IconSearchBaseDirectories)
        {
            string hicolor = Path.Combine(baseDir, "hicolor");
            if (!Directory.Exists(hicolor)) continue;

            try
            {
                foreach (var ext in SupportedExtensions)
                {
                    var matches = Directory.GetFiles(hicolor, rawName + ext, SearchOption.AllDirectories);
                    if (matches.Length > 0)
                    {
                        return matches[0];
                    }
                }
            }
            catch
            {
                // Ignore search permission issues
            }
        }

        return null;
    }

    private void IndexDesktopEntries()
    {
        foreach (var dir in DesktopFileLocations)
        {
            if (!Directory.Exists(dir)) continue;

            try
            {
                var files = Directory.GetFiles(dir, "*.desktop", SearchOption.AllDirectories);
                foreach (var file in files)
                {
                    ParseAndIndexDesktopFile(file);
                }
            }
            catch
            {
                // Ignore directory scan errors
            }
        }
    }

    private void ParseAndIndexDesktopFile(string filePath)
    {
        try
        {
            string baseFileName = Path.GetFileNameWithoutExtension(filePath);
            bool isUrlHandler = baseFileName.EndsWith("-url-handler", StringComparison.OrdinalIgnoreCase);

            string? name = null;
            string? genericName = null;
            string? icon = null;
            string? exec = null;
            string? wmClass = null;

            bool inDesktopEntry = false;

            foreach (var line in File.ReadLines(filePath))
            {
                string trimmed = line.Trim();
                if (trimmed.StartsWith('[') && trimmed.EndsWith(']'))
                {
                    inDesktopEntry = trimmed.Equals("[Desktop Entry]", StringComparison.OrdinalIgnoreCase);
                    continue;
                }

                if (!inDesktopEntry || trimmed.StartsWith('#') || !trimmed.Contains('='))
                    continue;

                int eqIdx = trimmed.IndexOf('=');
                string key = trimmed.Substring(0, eqIdx).Trim();
                string val = trimmed.Substring(eqIdx + 1).Trim();

                if (key.Equals("Name", StringComparison.Ordinal) && name == null)
                    name = val;
                else if (key.Equals("GenericName", StringComparison.Ordinal) && genericName == null)
                    genericName = val;
                else if (key.Equals("Icon", StringComparison.Ordinal) && icon == null)
                    icon = val;
                else if (key.Equals("Exec", StringComparison.Ordinal) && exec == null)
                    exec = val;
                else if (key.Equals("StartupWMClass", StringComparison.Ordinal) && wmClass == null)
                    wmClass = val;
            }

            if (string.IsNullOrWhiteSpace(name) && string.IsNullOrWhiteSpace(icon))
                return;

            var entry = new DesktopEntry(
                name ?? baseFileName,
                genericName ?? string.Empty,
                icon ?? string.Empty,
                exec ?? string.Empty,
                wmClass ?? string.Empty);

            // Index by file name
            _desktopEntries.TryAdd(baseFileName, entry);

            if (isUrlHandler)
                return;

            // Index by stripped names (e.g., com.brave.Browser -> brave, com.anthropic.Claude -> claude)
            string lastPart = baseFileName.Split('.').Last();
            _desktopEntries.TryAdd(lastPart, entry);

            // Index by executable binary name from Exec
            if (!string.IsNullOrWhiteSpace(exec))
            {
                string execBinary = exec.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];
                string execBase = Path.GetFileName(execBinary);
                _desktopEntries.TryAdd(execBase, entry);

                // Strip "-stable", "-bin", "-browser"
                string execClean = NormalizeProcessName(execBase);
                _desktopEntries.TryAdd(execClean, entry);
            }

            // Index by StartupWMClass
            if (!string.IsNullOrWhiteSpace(wmClass))
            {
                _desktopEntries.TryAdd(wmClass.ToLowerInvariant(), entry);
            }
        }
        catch
        {
            // Ignore corrupted individual desktop files
        }
    }

    private static string NormalizeProcessName(string processIdentifier)
    {
        string clean = Path.GetFileName(processIdentifier).ToLowerInvariant().Trim();
        if (clean.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            clean = clean.Substring(0, clean.Length - 4);

        return clean;
    }

    private static string FormatGenericDisplayName(string rawName)
    {
        if (string.IsNullOrWhiteSpace(rawName))
            return "Application";

        // Capitalize first character
        if (rawName.Length == 1)
            return rawName.ToUpperInvariant();

        return char.ToUpperInvariant(rawName[0]) + rawName.Substring(1);
    }

    private static IImage CreateGenericApplicationIcon()
    {
        try
        {
            var group = new DrawingGroup();
            using var ctx = group.Open();

            // Dark rounded badge container
            var bgGeometry = new RoundedRect(new Rect(0, 0, 32, 32), 6, 6);
            ctx.DrawRectangle(
                new SolidColorBrush(Color.FromArgb(255, 24, 30, 42)),
                new Pen(new SolidColorBrush(Color.FromArgb(255, 51, 65, 85)), 1),
                bgGeometry);

            // Header bar line
            ctx.DrawLine(
                new Pen(new SolidColorBrush(Color.FromArgb(255, 71, 85, 105)), 1.5),
                new Point(7, 10), new Point(25, 10));

            // Terminal prompt chevron (>) in cyan accent
            var chevron = new StreamGeometry();
            using (var sCtx = chevron.Open())
            {
                sCtx.BeginFigure(new Point(9, 15), false);
                sCtx.LineTo(new Point(14, 19));
                sCtx.LineTo(new Point(9, 23));
            }
            ctx.DrawGeometry(null, new Pen(new SolidColorBrush(Color.FromArgb(255, 56, 189, 248)), 1.5), chevron);

            // Cursor line (_)
            ctx.DrawLine(
                new Pen(new SolidColorBrush(Color.FromArgb(255, 148, 163, 184)), 1.5),
                new Point(17, 23), new Point(23, 23));

            return new DrawingImage { Drawing = group };
        }
        catch
        {
            // Headless unit test environment without platform render interface initialized
            return new HeadlessFallbackImage();
        }
    }

    private sealed class HeadlessFallbackImage : IImage
    {
        public Size Size => new(32, 32);
        public void Draw(DrawingContext context, Rect sourceRect, Rect destRect) { }
    }
}
