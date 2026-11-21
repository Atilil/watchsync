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

    // Jellyfin raises UserDataSaved synchronously inside SaveUserData, on the caller's flow.
    // Set while WatchSync writes, so the event raised by its own write is recognised and ignored.
    private readonly AsyncLocal<bool> _writing = new();

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
        if (_writing.Value || !SyncReasons.Contains(e.SaveReason))
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

        foreach (var target in matches)
        {
            SyncToItem(e.Item, target, user);
        }
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

            _writing.Value = true;
            try
            {
                _userDataManager.SaveUserData(user, target, targetData, UserDataSaveReason.UpdateUserData, CancellationToken.None);
            }
            finally
            {
                _writing.Value = false;
            }

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
