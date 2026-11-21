using System.Collections.Generic;

namespace Jellyfin.Plugin.WatchSync.Services;

/// <summary>
/// The kinds of media WatchSync can match.
/// </summary>
public enum MediaKind
{
    /// <summary>A movie.</summary>
    Movie,

    /// <summary>A TV episode.</summary>
    Episode
}

/// <summary>
/// Plain description of a library item, holding only what identity matching needs.
/// Kept free of Jellyfin types so the matching rule can be unit-tested without a server.
/// </summary>
/// <param name="Kind">Movie or episode.</param>
/// <param name="ProviderIds">The item's own provider ids (e.g. Tmdb, Imdb).</param>
/// <param name="SeriesProviderIds">For an episode, the provider ids of its series.</param>
/// <param name="Season">For an episode, its season number.</param>
/// <param name="Episode">For an episode, its episode number.</param>
public sealed record MediaFacts(
    MediaKind Kind,
    IReadOnlyDictionary<string, string> ProviderIds,
    IReadOnlyDictionary<string, string>? SeriesProviderIds = null,
    int? Season = null,
    int? Episode = null);
