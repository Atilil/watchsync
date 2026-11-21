using System;
using System.Text.RegularExpressions;

namespace Jellyfin.Plugin.WatchSync.Utils;

/// <summary>
/// Utilities for media title manipulation.
/// </summary>
public static class TitleUtils
{
    private static readonly Regex QualityTags = new(
        @"\b(4k|uhd|hdr|dolby vision|dv|2160p|1080p|720p)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    /// <summary>
    /// Normalizes a title by removing quality indicators (4K, UHD, etc.)
    /// to allow comparison between different versions of the same media.
    /// </summary>
    /// <param name="title">The title to normalize.</param>
    /// <returns>The normalized title in lowercase, without quality indicators.</returns>
    public static string NormalizeTitle(string? title)
    {
        if (string.IsNullOrEmpty(title))
        {
            return string.Empty;
        }

        // Remove common quality indicators
        var normalized = QualityTags.Replace(title, "").ToLowerInvariant();

        // Remove empty parentheses or brackets
        normalized = Regex.Replace(normalized, @"\s*[\[\(]\s*[\]\)]\s*", " ");
        normalized = Regex.Replace(normalized, @"\s+", " ").Trim();

        return normalized;
    }
}
