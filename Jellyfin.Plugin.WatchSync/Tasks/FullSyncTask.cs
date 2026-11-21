using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.WatchSync.Configuration;
using Jellyfin.Plugin.WatchSync.Services;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.WatchSync.Tasks;

/// <summary>
/// Scheduled task that realigns the watch state of every group of copies, for every user.
/// Needed after files move: Jellyfin then reattaches the history to one copy only.
/// </summary>
public class FullSyncTask : IScheduledTask
{
    private readonly ILibraryManager _libraryManager;
    private readonly IUserManager _userManager;
    private readonly IUserDataManager _userDataManager;
    private readonly ILogger<FullSyncTask> _logger;

    /// <summary>
    /// Initializes the full synchronization task.
    /// </summary>
    public FullSyncTask(
        ILibraryManager libraryManager,
        IUserManager userManager,
        IUserDataManager userDataManager,
        ILogger<FullSyncTask> logger)
    {
        _libraryManager = libraryManager;
        _userManager = userManager;
        _userDataManager = userDataManager;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => "WatchSync Full Synchronization";

    /// <inheritdoc />
    public string Description => "Realigns watch history between all copies of the same media, for all users.";

    /// <inheritdoc />
    public string Category => "WatchSync";

    /// <inheritdoc />
    public string Key => "WatchSyncFullSync";

    /// <inheritdoc />
    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        return new[]
        {
            new TaskTriggerInfo
            {
                Type = TaskTriggerInfoType.WeeklyTrigger,
                DayOfWeek = DayOfWeek.Sunday,
                TimeOfDayTicks = TimeSpan.FromHours(3).Ticks
            }
        };
    }

    /// <inheritdoc />
    public Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        var config = Plugin.Instance?.Configuration;
        if (config is null || !config.IsEnabled)
        {
            _logger.LogInformation("Plugin disabled, full sync skipped");
            return Task.CompletedTask;
        }

        progress.Report(0);
        var groups = FindCopyGroups(config, cancellationToken);
        _logger.LogInformation("Full sync: {GroupCount} group(s) of copies found", groups.Count);

        var users = _userManager.GetUsers()
            .Where(u => !config.ExcludedUserIds.Contains(u.Id.ToString()))
            .ToList();

        var options = new MergeOptions(
            config.SyncWatchedStatus,
            config.SyncPlaybackPosition,
            config.SyncPlayCount,
            config.SyncLastPlayedDate);

        var updated = 0;
        for (var u = 0; u < users.Count; u++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            updated += SyncUser(users[u], groups, options);
            progress.Report(20 + (80.0 * (u + 1) / users.Count));
        }

        _logger.LogInformation(
            "Full sync completed: {GroupCount} group(s), {UserCount} user(s), {Updated} copy(ies) updated",
            groups.Count,
            users.Count,
            updated);
        progress.Report(100);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Buckets every movie and episode by identity key, then joins the items each bucket
    /// confirms with <see cref="MediaIdentity.AreSame"/> into groups of copies.
    /// </summary>
    private List<List<BaseItem>> FindCopyGroups(PluginConfiguration config, CancellationToken cancellationToken)
    {
        var excludedLibraries = config.ExcludedLibraryIds ?? new List<string>();
        var movieProviders = MediaMatcher.MovieProviders(config);
        var seriesProviders = MediaMatcher.SeriesProviders(config);

        var items = new List<BaseItem>();
        var facts = new List<MediaFacts>();
        foreach (var item in _libraryManager.GetItemList(new InternalItemsQuery
        {
            IncludeItemTypes = [BaseItemKind.Movie, BaseItemKind.Episode],
            IsVirtualItem = false,
            Recursive = true
        }))
        {
            var itemFacts = MediaMatcher.ToFacts(item);
            if (itemFacts is not null && !MediaMatcher.IsInExcludedLibrary(item, excludedLibraries))
            {
                items.Add(item);
                facts.Add(itemFacts);
            }
        }

        _logger.LogInformation("Full sync: {ItemCount} movie(s) and episode(s) loaded", items.Count);

        var buckets = new Dictionary<string, List<int>>();
        for (var i = 0; i < items.Count; i++)
        {
            foreach (var key in MediaIdentity.GetKeys(facts[i], movieProviders, seriesProviders, config.UseTitleYearFallback))
            {
                if (!buckets.TryGetValue(key, out var bucket))
                {
                    bucket = new List<int>();
                    buckets[key] = bucket;
                }

                bucket.Add(i);
            }
        }

        // Union-find: items confirmed as the same content end up with the same root.
        var parent = Enumerable.Range(0, items.Count).ToArray();
        int Root(int i) => parent[i] == i ? i : parent[i] = Root(parent[i]);

        foreach (var bucket in buckets.Values.Where(b => b.Count > 1))
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (var x = 0; x < bucket.Count; x++)
            {
                for (var y = x + 1; y < bucket.Count; y++)
                {
                    var (a, b) = (bucket[x], bucket[y]);
                    if (Root(a) != Root(b)
                        && MediaIdentity.AreSame(facts[a], facts[b], movieProviders, seriesProviders, config.UseTitleYearFallback))
                    {
                        parent[Root(a)] = Root(b);
                    }
                }
            }
        }

        return Enumerable.Range(0, items.Count)
            .GroupBy(Root)
            .Where(g => g.Count() > 1)
            .Select(g => g.Select(i => items[i]).ToList())
            .ToList();
    }

    private int SyncUser(Jellyfin.Database.Implementations.Entities.User user, List<List<BaseItem>> groups, MergeOptions options)
    {
        var allItems = groups.SelectMany(g => g).ToList();
        var batch = allItems.Count > 0
            ? _userDataManager.GetUserDataBatch(allItems, user)
            : new Dictionary<Guid, UserItemData>();

        var updated = 0;
        foreach (var group in groups)
        {
            var data = group
                .Select(item => batch.TryGetValue(item.Id, out var d) ? d : _userDataManager.GetUserData(user, item))
                .ToList();
            if (data.Any(d => d is null))
            {
                continue;
            }

            var current = group
                .Select((item, i) => new CopyState(data[i]!.Played, data[i]!.PlayCount, data[i]!.PlaybackPositionTicks, data[i]!.LastPlayedDate, item.RunTimeTicks))
                .ToList();
            var merged = UserDataMerge.Merge(current, options);

            for (var i = 0; i < group.Count; i++)
            {
                if (merged[i] == current[i])
                {
                    continue;
                }

                var target = data[i]!;
                target.Played = merged[i].Played;
                target.PlayCount = merged[i].PlayCount;
                target.PlaybackPositionTicks = merged[i].PositionTicks;
                target.LastPlayedDate = merged[i].LastPlayedDate;

                // Save on the cached instance (see MediaMatcher.FindMatchingItems).
                var item = _libraryManager.GetItemById(group[i].Id) ?? group[i];
                SyncGuard.Run(() =>
                    _userDataManager.SaveUserData(user, item, target, UserDataSaveReason.UpdateUserData, CancellationToken.None));
                updated++;

                _logger.LogInformation(
                    "Full sync: {ItemName} updated for {UserName} (Played: {Played}, Position: {Position})",
                    item.Name,
                    user.Username,
                    target.Played,
                    target.PlaybackPositionTicks);
            }
        }

        return updated;
    }
}
