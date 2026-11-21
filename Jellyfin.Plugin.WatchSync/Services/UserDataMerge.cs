using System;
using System.Collections.Generic;
using System.Linq;

namespace Jellyfin.Plugin.WatchSync.Services;

/// <summary>
/// Watch state of one copy, free of Jellyfin types so the merge can be unit-tested.
/// </summary>
/// <param name="Played">Whether the copy is marked played.</param>
/// <param name="PlayCount">How many times it was played.</param>
/// <param name="PositionTicks">Resume position.</param>
/// <param name="LastPlayedDate">When it was last played.</param>
/// <param name="RuntimeTicks">Duration of this copy, used to carry the resume position across copies.</param>
public sealed record CopyState(bool Played, int PlayCount, long PositionTicks, DateTime? LastPlayedDate, long? RuntimeTicks);

/// <summary>
/// Which fields the merge may change; the others keep each copy's own value.
/// </summary>
/// <param name="Played">Merge the played flag.</param>
/// <param name="Position">Merge the resume position.</param>
/// <param name="PlayCount">Merge the play count.</param>
/// <param name="LastPlayedDate">Merge the last played date.</param>
public sealed record MergeOptions(bool Played = true, bool Position = true, bool PlayCount = true, bool LastPlayedDate = true);

/// <summary>
/// Merges the watch state of several copies of the same content (used by the full sync).
/// Played if any copy is played, highest play count, latest date, and the resume position
/// of the most recently played copy, scaled to each copy's runtime. Never loses history.
/// </summary>
public static class UserDataMerge
{
    /// <summary>
    /// Computes the merged state of each copy, in the same order as <paramref name="copies"/>.
    /// </summary>
    /// <param name="copies">The current state of every copy.</param>
    /// <param name="options">Fields allowed to change.</param>
    /// <returns>The state each copy should have.</returns>
    public static IReadOnlyList<CopyState> Merge(IReadOnlyList<CopyState> copies, MergeOptions? options = null)
    {
        options ??= new MergeOptions();

        var played = copies.Any(c => c.Played);
        var playCount = copies.Max(c => c.PlayCount);
        var lastPlayed = copies.Max(c => c.LastPlayedDate);

        // Resume point: the most recently played copy that has one.
        var resumeSource = copies
            .Where(c => !c.Played && c.PositionTicks > 0)
            .OrderByDescending(c => c.LastPlayedDate ?? DateTime.MinValue)
            .FirstOrDefault();

        return copies.Select(copy => copy with
        {
            Played = options.Played ? played : copy.Played,
            PlayCount = options.PlayCount ? playCount : copy.PlayCount,
            LastPlayedDate = options.LastPlayedDate ? lastPlayed : copy.LastPlayedDate,
            PositionTicks = options.Position ? Position(copy, resumeSource, played) : copy.PositionTicks
        }).ToList();
    }

    private static long Position(CopyState copy, CopyState? resumeSource, bool played)
    {
        if (played || resumeSource is null)
        {
            return 0;
        }

        if (ReferenceEquals(copy, resumeSource))
        {
            return copy.PositionTicks;
        }

        // Different encodes of the same content can differ in length: keep the same percentage.
        if (resumeSource.RuntimeTicks > 0 && copy.RuntimeTicks > 0)
        {
            return (long)((double)resumeSource.PositionTicks / resumeSource.RuntimeTicks.Value * copy.RuntimeTicks.Value);
        }

        return resumeSource.PositionTicks;
    }
}
