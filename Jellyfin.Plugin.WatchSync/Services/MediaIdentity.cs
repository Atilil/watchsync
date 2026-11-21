using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.WatchSync.Services;

/// <summary>
/// Decides whether two library items are the same content, from their identity only
/// (never their file path).
/// </summary>
public static class MediaIdentity
{
    /// <summary>Providers that identify a movie, by default.</summary>
    public static readonly IReadOnlyList<string> DefaultMovieProviders = ["Tmdb", "Imdb"];

    /// <summary>Providers that identify a series, by default.</summary>
    public static readonly IReadOnlyList<string> DefaultSeriesProviders = ["Tvdb", "Imdb"];

    /// <summary>
    /// Returns true when both items are the same movie, or the same episode of the same series.
    /// </summary>
    /// <param name="a">First item.</param>
    /// <param name="b">Second item.</param>
    /// <param name="movieProviders">Providers to compare for movies; defaults to <see cref="DefaultMovieProviders"/>.</param>
    /// <param name="seriesProviders">Providers to compare for series; defaults to <see cref="DefaultSeriesProviders"/>.</param>
    /// <returns>True if the two items represent the same content.</returns>
    public static bool AreSame(
        MediaFacts a,
        MediaFacts b,
        IReadOnlyList<string>? movieProviders = null,
        IReadOnlyList<string>? seriesProviders = null)
    {
        if (a.Kind != b.Kind)
        {
            return false;
        }

        if (a.Kind == MediaKind.Episode)
        {
            if (a.Season is null || a.Episode is null || a.Season != b.Season || a.Episode != b.Episode)
            {
                return false;
            }

            return IdsAgree(a.SeriesProviderIds, b.SeriesProviderIds, seriesProviders ?? DefaultSeriesProviders);
        }

        return IdsAgree(a.ProviderIds, b.ProviderIds, movieProviders ?? DefaultMovieProviders);
    }

    /// <summary>
    /// True when at least one of <paramref name="providers"/> has the same id on both sides,
    /// and none of them has two different ids.
    /// </summary>
    private static bool IdsAgree(
        IReadOnlyDictionary<string, string>? a,
        IReadOnlyDictionary<string, string>? b,
        IReadOnlyList<string> providers)
    {
        if (a is null || b is null)
        {
            return false;
        }

        var anyMatch = false;
        foreach (var provider in providers)
        {
            if (!a.TryGetValue(provider, out var idA) || !b.TryGetValue(provider, out var idB))
            {
                continue;
            }

            if(!string.Equals(idA, idB, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            anyMatch = true;
        }

        return anyMatch;

    }
}
