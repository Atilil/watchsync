using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Database.Implementations.Entities;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.WatchSync.Services;

/// <summary>
/// Listens for user data changes and copies them to the other copies of the same media.
/// </summary>
public class WatchSyncService : IHostedService
{
    /// <summary>
    /// Changes worth propagating: end of playback (including sessions that went silent),
    /// played/unplayed toggles (UI or API), and user data written by external tools.
    /// Playback progress (every ~10 s) and ratings are ignored.
    /// </summary>
    private static readonly HashSet<UserDataSaveReason> SyncReasons =
    [
        UserDataSaveReason.PlaybackFinished,
        UserDataSaveReason.TogglePlayed,
        UserDataSaveReason.UpdateUserData
    ];

    private readonly IUserDataManager _userDataManager;
    private readonly IUserManager _userManager;
    private readonly IMediaMatcher _mediaMatcher;
    private readonly IConflictResolver _conflictResolver;
    private readonly ILogger<WatchSyncService> _logger;

    /// <summary>
    /// Initializes the synchronization service.
    /// </summary>
    public WatchSyncService(
        IUserDataManager userDataManager,
        IUserManager userManager,
        IMediaMatcher mediaMatcher,
        IConflictResolver conflictResolver,
        ILogger<WatchSyncService> logger)
    {
        _userDataManager = userDataManager;
        _userManager = userManager;
        _mediaMatcher = mediaMatcher;
        _conflictResolver = conflictResolver;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("WatchSync started");
        _userDataManager.UserDataSaved += OnUserDataSaved;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("WatchSync stopped");
        _userDataManager.UserDataSaved -= OnUserDataSaved;
        return Task.CompletedTask;
    }

    private void OnUserDataSaved(object? sender, UserDataSaveEventArgs e)
    {
        try
        {
            ProcessUserDataSaved(e);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing UserDataSaved event");
        }
    }

    private void ProcessUserDataSaved(UserDataSaveEventArgs e)
    {
        if (SyncGuard.IsWriting || !SyncReasons.Contains(e.SaveReason))
        {
            return;
        }

        var config = Plugin.Instance?.Configuration;
        if (config is null || !config.IsEnabled || e.Item is null || e.UserId == Guid.Empty)
        {
            return;
        }

        if (config.ExcludedUserIds.Contains(e.UserId.ToString()))
        {
            return;
        }

        var user = _userManager.GetUserById(e.UserId);
        if (user is null)
        {
            return;
        }

        var matches = _mediaMatcher.FindMatchingItems(e.Item);
        if (matches.Count == 0)
        {
            return;
        }

        _logger.LogInformation(
            "{Reason}: syncing {ItemName} to {Count} copy(ies)",
            e.SaveReason,
            e.Item.Name,
            matches.Count);

        // Un-marking an item (TogglePlayed to unplayed) is Jellyfin's MarkUnplayed, which wipes the
        // play count, position and last played date. Mirror that wipe instead of merging fields.
        // Only this event proves a reset: a never-watched item looks the same.
        var isUnmark = e.SaveReason == UserDataSaveReason.TogglePlayed && e.UserData is { Played: false };

        foreach (var target in matches)
        {
            if (isUnmark)
            {
                ResetItem(e.Item, target, user);
            }
            else
            {
                SyncToItem(e.Item, target, user);
            }
        }
    }

    private void ResetItem(BaseItem source, BaseItem target, User user)
    {
        try
        {
            var targetData = _userDataManager.GetUserData(user, target);
            if (targetData is null
                || (!targetData.Played && targetData.PlayCount == 0 && targetData.PlaybackPositionTicks == 0 && targetData.LastPlayedDate is null))
            {
                return;
            }

            targetData.Played = false;
            targetData.PlayCount = 0;
            targetData.PlaybackPositionTicks = 0;
            targetData.LastPlayedDate = null;
            Save(user, target, targetData);

            _logger.LogInformation("Reset {TargetName} (un-marked on {SourceName})", target.Name, source.Name);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error resetting {TargetName}", target.Name);
        }
    }

    private void Save(User user, BaseItem target, UserItemData data)
    {
        SyncGuard.Run(() =>
            _userDataManager.SaveUserData(user, target, data, UserDataSaveReason.UpdateUserData, CancellationToken.None));
    }

    private void SyncToItem(BaseItem source, BaseItem target, User user)
    {
        try
        {
            var syncData = _conflictResolver.ResolveSyncData(source, target, user);
            if (!syncData.ShouldSync)
            {
                return;
            }

            var targetData = _userDataManager.GetUserData(user, target);
            if (targetData is null)
            {
                return;
            }

            if (syncData.Played.HasValue)
            {
                targetData.Played = syncData.Played.Value;
            }

            if (syncData.PlaybackPositionTicks.HasValue)
            {
                targetData.PlaybackPositionTicks = syncData.PlaybackPositionTicks.Value;
            }

            if (syncData.PlayCount.HasValue)
            {
                targetData.PlayCount = syncData.PlayCount.Value;
            }

            if (syncData.LastPlayedDate.HasValue)
            {
                targetData.LastPlayedDate = syncData.LastPlayedDate;
            }

            Save(user, target, targetData);

            _logger.LogInformation(
                "Synced {SourceName} -> {TargetName} (Played: {Played}, Position: {Position})",
                source.Name,
                target.Name,
                targetData.Played,
                targetData.PlaybackPositionTicks);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error syncing to {TargetName}", target.Name);
        }
    }
}
