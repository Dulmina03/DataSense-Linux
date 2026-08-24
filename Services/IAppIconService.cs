using Avalonia.Media;

namespace DataSense.Services;

/// <summary>
/// Service responsible for resolving Linux application icons and display names
/// from installed desktop entries (.desktop files) and system icon themes.
/// </summary>
public interface IAppIconService
{
    /// <summary>
    /// Resolves an application icon as an Avalonia IImage for the given process identifier or executable path.
    /// Always returns a valid IImage (falls back to a generic application icon if no icon can be resolved).
    /// </summary>
    IImage GetApplicationIcon(string processIdentifier, string? executablePath = null);

    /// <summary>
    /// Resolves a human-friendly application display name (e.g. "Brave Web Browser", "Visual Studio Code")
    /// from matching desktop entries, or formats the process identifier cleanly.
    /// </summary>
    string GetApplicationDisplayName(string processIdentifier, string? executablePath = null);

    /// <summary>
    /// Gets the generic application icon fallback.
    /// </summary>
    IImage GenericApplicationIcon { get; }
}
