using System;
using System.Collections.Generic;

namespace DataSense.Helpers;

public static class NetworkIdentityValidator
{
    private static readonly HashSet<string> InvalidNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "-", "--", "—", "–",
        "unknown", "unknown network", "unknown_network",
        "wifi", "wi-fi", "wireless",
        "mobile hotspot", "hotspot", "phone hotspot",
        "connected network", "network",
        "none", "disconnected", "null", "undefined",
        "n/a", "na", "offline"
    };

    /// <summary>
    /// Checks whether a network name or SSID is a legitimate, non-placeholder identity.
    /// Rejects null, empty, whitespace, dashes, generic "Wi-Fi", "Hotspot", and placeholders.
    /// </summary>
    public static bool IsValidNetworkName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return false;

        string trimmed = name.Trim();
        if (trimmed.Length == 0)
            return false;

        if (InvalidNames.Contains(trimmed))
            return false;

        if (trimmed.StartsWith("Interface: ", StringComparison.OrdinalIgnoreCase))
            return false;

        // Reject if all characters are punctuation or dashes
        bool hasAlphanumeric = false;
        foreach (char c in trimmed)
        {
            if (char.IsLetterOrDigit(c))
            {
                hasAlphanumeric = true;
                break;
            }
        }

        return hasAlphanumeric;
    }

    /// <summary>
    /// Normalizes a network name string for reliable grouping and display.
    /// </summary>
    public static string NormalizeNetworkName(string? rawName, string? interfaceName = null)
    {
        if (IsValidNetworkName(rawName))
        {
            return rawName!.Trim();
        }

        if (!string.IsNullOrWhiteSpace(interfaceName) && interfaceName != "None" && interfaceName != "Disconnected")
        {
            return $"Interface: {interfaceName.Trim()}";
        }

        return "Unknown Network";
    }
}
