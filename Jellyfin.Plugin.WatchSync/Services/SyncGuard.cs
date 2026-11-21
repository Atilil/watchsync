using System;
using System.Threading;

namespace Jellyfin.Plugin.WatchSync.Services;

/// <summary>
/// Marks the writes made by WatchSync itself. Jellyfin raises <c>UserDataSaved</c> synchronously
/// inside <c>SaveUserData</c>, on the caller's flow, so the live sync can recognise and ignore
/// the events caused by WatchSync's own writes (live or full sync) instead of looping.
/// </summary>
public static class SyncGuard
{
    private static readonly AsyncLocal<bool> Writing = new();

    /// <summary>
    /// Gets a value indicating whether the current flow is inside a WatchSync write.
    /// </summary>
    public static bool IsWriting => Writing.Value;

    /// <summary>
    /// Runs <paramref name="write"/> flagged as a WatchSync write.
    /// </summary>
    /// <param name="write">The write to perform.</param>
    public static void Run(Action write)
    {
        Writing.Value = true;
        try
        {
            write();
        }
        finally
        {
            Writing.Value = false;
        }
    }
}
