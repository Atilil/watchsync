using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.WatchSync.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.WatchSync.Services;

/// <summary>
/// Finds the other copies of an item (same movie or episode in other libraries).
/// Candidates are fetched by provider id, then confirmed with <see cref="MediaIdentity"/>.
/// </summary>
public class MediaMatcher : IMediaMatcher
{
    private readonly ILibraryManager _libraryManager;
    private readonly ILogger<MediaMatcher> _logger;

    /// <summary>
    /// Initializes the matching service.
    /// </summary>
    /// <param name="libraryManager">Jellyfin library manager.</param>
    /// <param name="logger">Logger.</param>
    public MediaMatcher(ILibraryManager libraryManager, ILogger<MediaMatcher> logger)
    {
        _libraryManager = libraryManager;
        _logger = logger;
    }

    /// <inheritdoc />
    public IReadOnlyList<BaseItem> FindMatchingItems(BaseItem sourceItem)
    {
        var config = Plugin.Instance?.Configuration ?? new PluginConfiguration();
        var source = ToFacts(sourceItem);
        if (source is null)
        {
            return Array.Empty<BaseItem>();
        }

        var candidates = sourceItem is Episode episode
            ? FindEpisodeCandidates(episode, config)
            : FindMovieCandidates(source, config);

        var excludedLibraries = config.ExcludedLibraryIds ?? new List<string>();
        var matches = candidates
            .Where(item => item.Id != sourceItem.Id)
            .Where(item => !IsInExcludedLibrary(item, excludedLibraries))
            .Where(item => AreSame(source, item, config))
            .ToList();

        _logger.LogDebug("Found {Count} match(es) for {ItemName}", matches.Count, sourceItem.Name);
        return matches;
    }

    /// <inheritdoc />
    public bool AreItemsMatching(BaseItem item1, BaseItem item2)
    {
        var facts = ToFacts(item1);
        return facts is not null
            && AreSame(facts, item2, Plugin.Instance?.Configuration ?? new PluginConfiguration());
    }

    /// <summary>
    /// Translates a Jellyfin item into the plain facts used by <see cref="MediaIdentity"/>.
    /// Returns null for item types WatchSync does not handle.
    /// </summary>
    /// <param name="item">The Jellyfin item.</param>
    /// <returns>The item's facts, or null.</returns>
    public static MediaFacts? ToFacts(BaseItem item) => item switch
    {
        Movie movie => new MediaFacts(MediaKind.Movie, movie.ProviderIds, Title: movie.Name, Year: movie.ProductionYear),
        Episode episode => new MediaFacts(
            MediaKind.Episode,
            episode.ProviderIds,
            episode.Series?.ProviderIds,
            episode.ParentIndexNumber,
            episode.IndexNumber),
        _ => null
    };

    private static bool AreSame(MediaFacts source, BaseItem candidate, PluginConfiguration config)
    {
        var facts = ToFacts(candidate);
        return facts is not null && MediaIdentity.AreSame(
            source,
            facts,
            MovieProviders(config),
            SeriesProviders(config),
            config.UseTitleYearFallback);
    }

    private List<BaseItem> FindMovieCandidates(MediaFacts source, PluginConfiguration config)
    {
        var candidates = new List<BaseItem>();

        var ids = KnownIds(source.ProviderIds, MovieProviders(config));
        if (ids.Count > 0)
        {
            candidates.AddRange(_libraryManager.GetItemList(new InternalItemsQuery
            {
                IncludeItemTypes = [BaseItemKind.Movie],
                HasAnyProviderId = ids,
                IsVirtualItem = false,
                Recursive = true
            }));
        }

        // The title + year fallback cannot be queried by id: fetch the movies of the same year.
        if (config.UseTitleYearFallback && source.Year is not null)
        {
            candidates.AddRange(_libraryManager.GetItemList(new InternalItemsQuery
            {
                IncludeItemTypes = [BaseItemKind.Movie],
                Years = [source.Year.Value],
                IsVirtualItem = false,
                Recursive = true
            }));
        }

        return candidates.DistinctBy(item => item.Id).ToList();
    }

    private List<BaseItem> FindEpisodeCandidates(Episode episode, PluginConfiguration config)
    {
        var seriesIds = KnownIds(episode.Series?.ProviderIds, SeriesProviders(config));
        if (seriesIds.Count == 0 || episode.ParentIndexNumber is null || episode.IndexNumber is null)
        {
            return [];
        }

        var series = _libraryManager.GetItemList(new InternalItemsQuery
        {
            IncludeItemTypes = [BaseItemKind.Series],
            HasAnyProviderId = seriesIds,
            Recursive = true
        });
        if (series.Count == 0)
        {
            return [];
        }

        return _libraryManager.GetItemList(new InternalItemsQuery
        {
            IncludeItemTypes = [BaseItemKind.Episode],
            AncestorIds = series.Select(s => s.Id).ToArray(),
            ParentIndexNumber = episode.ParentIndexNumber,
            IndexNumber = episode.IndexNumber,
            IsVirtualItem = false,
            Recursive = true
        }).ToList();
    }

    /// <summary>
    /// The subset of <paramref name="ids"/> limited to the given providers, ready for a query.
    /// </summary>
    private static Dictionary<string, string> KnownIds(IReadOnlyDictionary<string, string>? ids, IReadOnlyList<string> providers)
    {
        var known = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (ids is null)
        {
            return known;
        }

        foreach (var provider in providers)
        {
            if (ids.TryGetValue(provider, out var id) && !string.IsNullOrEmpty(id))
            {
                known[provider] = id;
            }
        }

        return known;
    }

    private static List<string> MovieProviders(PluginConfiguration config)
    {
        var providers = new List<string>();
        if (config.UseTmdbId)
        {
            providers.Add("Tmdb");
        }

        if (config.UseImdbId)
        {
            providers.Add("Imdb");
        }

        return providers;
    }

    private static List<string> SeriesProviders(PluginConfiguration config)
    {
        var providers = new List<string>();
        if (config.UseTvdbId)
        {
            providers.Add("Tvdb");
        }

        if (config.UseImdbId)
        {
            providers.Add("Imdb");
        }

        return providers;
    }

    private static bool IsInExcludedLibrary(BaseItem item, List<string> excludedLibraries)
    {
        if (excludedLibraries.Count == 0)
        {
            return false;
        }

        var libraryId = GetLibraryId(item);
        return libraryId is not null && excludedLibraries.Contains(libraryId.Value.ToString());
    }

    private static Guid? GetLibraryId(BaseItem item)
    {
        var parent = item;
        while (parent != null)
        {
            if (parent is Folder folder && folder.IsTopParent)
            {
                return folder.Id;
            }

            parent = parent.GetParent();
        }

        return item.ParentId;
    }
}
