using System;
using System.Collections.Generic;
using Jellyfin.Plugin.WatchSync.Utils;

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
    /// <param name="useTitleYearFallback">For movies whose ids cannot decide, compare normalized title + year.</param>
    /// <returns>True if the two items represent the same content.</returns>
    public static bool AreSame(
        MediaFacts a,
        MediaFacts b,
        IReadOnlyList<string>? movieProviders = null,
        IReadOnlyList<string>? seriesProviders = null,
        bool useTitleYearFallback = false)
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

            return IdsAgree(a.SeriesProviderIds, b.SeriesProviderIds, seriesProviders ?? DefaultSeriesProviders) == true;
        }

        var verdict = IdsAgree(a.ProviderIds, b.ProviderIds, movieProviders ?? DefaultMovieProviders);
        if (verdict.HasValue)
        {
            return verdict.Value;
        }

        return useTitleYearFallback && TitleAndYearMatch(a, b);
    }

    /// <summary>
    /// Identity keys of an item, used to bucket candidate copies before confirming them with
    /// <see cref="AreSame"/>. Two copies of the same content share at least one key; sharing a key
    /// is not enough on its own (ids may conflict elsewhere).
    /// </summary>
    /// <param name="facts">The item.</param>
    /// <param name="movieProviders">Providers used for movies; defaults to <see cref="DefaultMovieProviders"/>.</param>
    /// <param name="seriesProviders">Providers used for series; defaults to <see cref="DefaultSeriesProviders"/>.</param>
    /// <param name="useTitleYearFallback">Also emit a title + year key for movies.</param>
    /// <returns>Keys such as <c>movie:tmdb:438631</c> or <c>episode:tvdb:81189:s1e2</c>.</returns>
    public static IReadOnlyList<string> GetKeys(
        MediaFacts facts,
        IReadOnlyList<string>? movieProviders = null,
        IReadOnlyList<string>? seriesProviders = null,
        bool useTitleYearFallback = false)
    {
        var keys = new List<string>();

        if (facts.Kind == MediaKind.Episode)
        {
            if (facts.SeriesProviderIds is null || facts.Season is null || facts.Episode is null)
            {
                return keys;
            }

            foreach (var provider in seriesProviders ?? DefaultSeriesProviders)
            {
                if (facts.SeriesProviderIds.TryGetValue(provider, out var id) && !string.IsNullOrEmpty(id))
                {
                    keys.Add($"episode:{provider}:{id}:s{facts.Season}e{facts.Episode}".ToLowerInvariant());
                }
            }

            return keys;
        }

        foreach (var provider in movieProviders ?? DefaultMovieProviders)
        {
            if (facts.ProviderIds.TryGetValue(provider, out var id) && !string.IsNullOrEmpty(id))
            {
                keys.Add($"movie:{provider}:{id}".ToLowerInvariant());
            }
        }

        if (useTitleYearFallback && facts.Year is not null)
        {
            var title = TitleUtils.NormalizeTitle(facts.Title);
            if (title.Length > 0)
            {
                keys.Add($"movie:title:{title}:{facts.Year}");
            }
        }

        return keys;
    }

    /// <summary>
    /// True when both items have a production year, the same year, and the same normalized title.
    /// </summary>
    private static bool TitleAndYearMatch(MediaFacts a, MediaFacts b)
    {
        if (a.Year is null || a.Year != b.Year)
        {
            return false;
        }

        var titleA = TitleUtils.NormalizeTitle(a.Title);
        return titleA.Length > 0 && titleA == TitleUtils.NormalizeTitle(b.Title);
    }

    /// <summary>
    /// Compares the ids of the given providers. True: at least one matches and none conflicts.
    /// False: a provider has two different ids. Null: no provider is present on both sides.
    /// </summary>
    private static bool? IdsAgree(
        IReadOnlyDictionary<string, string>? a,
        IReadOnlyDictionary<string, string>? b,
        IReadOnlyList<string> providers)
    {
        if (a is null || b is null)
        {
            return null;
        }

        var anyMatch = false;
        foreach (var provider in providers)
        {
            if (!a.TryGetValue(provider, out var idA) || !b.TryGetValue(provider, out var idB))
            {
                continue;
            }

            if (!string.Equals(idA, idB, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            anyMatch = true;
        }

        return anyMatch ? true : null;
    }
}
