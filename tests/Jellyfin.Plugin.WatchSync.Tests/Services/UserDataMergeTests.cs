using Jellyfin.Plugin.WatchSync.Services;

namespace Jellyfin.Plugin.WatchSync.Tests.Services;

public class UserDataMergeTests
{
    private static readonly DateTime March1 = new(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime March3 = new(2026, 3, 3, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void AfterMigration_HistoryIsCopiedToTheBlankCopy()
    {
        var watched = new CopyState(true, 2, 0, March3, 100);
        var blank = new CopyState(false, 0, 0, null, 100);

        var merged = UserDataMerge.Merge([watched, blank]);

        Assert.Equal(watched, merged[0]);
        Assert.Equal(watched, merged[1]);
    }

    [Fact]
    public void PlayedOnAnyCopy_WinsOverUnplayed_AndClearsResumePoints()
    {
        var played = new CopyState(true, 1, 0, March1, 100);
        var inProgress = new CopyState(false, 0, 40, March3, 100);

        var merged = UserDataMerge.Merge([played, inProgress]);

        Assert.All(merged, m => Assert.True(m.Played));
        Assert.All(merged, m => Assert.Equal(0, m.PositionTicks));
        Assert.All(merged, m => Assert.Equal(March3, m.LastPlayedDate));
    }

    [Fact]
    public void ResumePoint_ComesFromMostRecentCopy_ScaledToEachRuntime()
    {
        var older = new CopyState(false, 0, 10, March1, 100);
        var newer = new CopyState(false, 0, 40, March3, 100);
        var longerCut = new CopyState(false, 0, 0, null, 200);

        var merged = UserDataMerge.Merge([older, newer, longerCut]);

        Assert.Equal(40, merged[0].PositionTicks);
        Assert.Equal(40, merged[1].PositionTicks);
        Assert.Equal(80, merged[2].PositionTicks);
    }

    [Fact]
    public void PlayCount_TakesTheHighest()
    {
        var merged = UserDataMerge.Merge([new CopyState(true, 3, 0, March1, 100), new CopyState(true, 1, 0, March1, 100)]);

        Assert.All(merged, m => Assert.Equal(3, m.PlayCount));
    }

    [Fact]
    public void DisabledField_KeepsEachCopyValue()
    {
        var a = new CopyState(true, 3, 0, March1, 100);
        var b = new CopyState(false, 1, 0, null, 100);

        var merged = UserDataMerge.Merge([a, b], new MergeOptions(PlayCount: false));

        Assert.True(merged[1].Played);
        Assert.Equal(1, merged[1].PlayCount);
    }

    [Fact]
    public void IdenticalCopies_AreUnchanged()
    {
        var a = new CopyState(false, 0, 0, null, 100);

        var merged = UserDataMerge.Merge([a, a with { RuntimeTicks = 200 }]);

        Assert.Equal(a, merged[0]);
        Assert.Equal(a with { RuntimeTicks = 200 }, merged[1]);
    }
}
