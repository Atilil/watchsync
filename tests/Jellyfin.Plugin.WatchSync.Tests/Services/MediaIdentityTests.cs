using Jellyfin.Plugin.WatchSync.Services;

namespace Jellyfin.Plugin.WatchSync.Tests.Services;

public class MediaIdentityTests
{
    // --- Movies: Tmdb or Imdb ---

    [Fact]
    public void Movies_SameTmdb_AreSame()
    {
        Assert.True(MediaIdentity.AreSame(Movie(("Tmdb", "438631")), Movie(("Tmdb", "438631"))));
    }

    [Fact]
    public void Movies_SameImdb_OneWithoutTmdb_AreSame()
    {
        var a = Movie(("Tmdb", "438631"), ("Imdb", "tt1160419"));
        var b = Movie(("Imdb", "tt1160419"));

        Assert.True(MediaIdentity.AreSame(a, b));
    }

    [Fact]
    public void Movies_SameTmdb_DifferentImdb_AreNotSame()
    {
        var a = Movie(("Tmdb", "438631"), ("Imdb", "tt1160419"));
        var b = Movie(("Tmdb", "438631"), ("Imdb", "tt0087182"));

        Assert.False(MediaIdentity.AreSame(a, b));
    }

    [Fact]
    public void Movies_NoIdInCommon_AreNotSame()
    {
        Assert.False(MediaIdentity.AreSame(Movie(("Tmdb", "1")), Movie(("Imdb", "tt2"))));
    }

    [Fact]
    public void Movies_OneWithoutAnyId_AreNotSame()
    {
        Assert.False(MediaIdentity.AreSame(Movie(("Tmdb", "438631")), Movie()));
    }

    [Fact]
    public void Movies_OnlyTvdbInCommon_AreNotSame()
    {
        Assert.False(MediaIdentity.AreSame(Movie(("Tvdb", "123")), Movie(("Tvdb", "123"))));
    }

    [Fact]
    public void Movies_ImdbCaseDiffers_AreSame()
    {
        Assert.True(MediaIdentity.AreSame(Movie(("Imdb", "tt1160419")), Movie(("Imdb", "TT1160419"))));
    }

    // --- Episodes: series Tvdb or Imdb + season + episode ---

    [Fact]
    public void Episodes_SameSeriesTvdb_SameSeasonAndEpisode_AreSame()
    {
        Assert.True(MediaIdentity.AreSame(Episode(1, 2, ("Tvdb", "81189")), Episode(1, 2, ("Tvdb", "81189"))));
    }

    [Fact]
    public void Episodes_SameSeries_DifferentEpisode_AreNotSame()
    {
        Assert.False(MediaIdentity.AreSame(Episode(1, 2, ("Tvdb", "81189")), Episode(1, 3, ("Tvdb", "81189"))));
    }

    [Fact]
    public void Episodes_SameSeries_DifferentSeason_AreNotSame()
    {
        Assert.False(MediaIdentity.AreSame(Episode(1, 2, ("Tvdb", "81189")), Episode(2, 2, ("Tvdb", "81189"))));
    }

    [Fact]
    public void Episodes_SeriesShareOnlyImdb_AreSame()
    {
        var a = Episode(1, 2, ("Tvdb", "81189"), ("Imdb", "tt0903747"));
        var b = Episode(1, 2, ("Imdb", "tt0903747"));

        Assert.True(MediaIdentity.AreSame(a, b));
    }

    [Fact]
    public void Episodes_SeriesSameImdb_DifferentTvdb_AreNotSame()
    {
        var a = Episode(1, 2, ("Tvdb", "81189"), ("Imdb", "tt0903747"));
        var b = Episode(1, 2, ("Tvdb", "99999"), ("Imdb", "tt0903747"));

        Assert.False(MediaIdentity.AreSame(a, b));
    }

    [Fact]
    public void Episodes_MissingEpisodeNumber_AreNotSame()
    {
        Assert.False(MediaIdentity.AreSame(Episode(1, null, ("Tvdb", "81189")), Episode(1, null, ("Tvdb", "81189"))));
    }

    [Fact]
    public void Episodes_SeriesTmdbIsIgnoredByDefault()
    {
        Assert.False(MediaIdentity.AreSame(Episode(1, 2, ("Tmdb", "1396")), Episode(1, 2, ("Tmdb", "1396"))));
    }

    // Known limitation: libraries using different episode orderings (e.g. TVDB seasons vs AniDB)
    // number the same episode differently, so they are not matched.
    [Fact]
    public void Episodes_DifferentNumbering_AreNotSame()
    {
        Assert.False(MediaIdentity.AreSame(Episode(3, 5, ("Tvdb", "81189")), Episode(1, 5, ("Tvdb", "81189"))));
    }

    // --- Mixed and extra providers ---

    [Fact]
    public void MovieAndEpisode_SameTmdbNumber_AreNotSame()
    {
        var movie = Movie(("Tmdb", "42"));
        var episode = new MediaFacts(MediaKind.Episode, Ids(("Tmdb", "42")), Ids(("Tmdb", "42")), 1, 1);

        Assert.False(MediaIdentity.AreSame(movie, episode));
    }

    [Fact]
    public void Episodes_ExtraSeriesProvider_IsUsedWhenRequested()
    {
        var a = Episode(1, 5, ("AniDB", "69"));
        var b = Episode(1, 5, ("AniDB", "69"));

        Assert.False(MediaIdentity.AreSame(a, b));
        Assert.True(MediaIdentity.AreSame(a, b, seriesProviders: ["Tvdb", "Imdb", "AniDB"]));
    }

    // --- Helpers ---

    private static MediaFacts Movie(params (string Provider, string Id)[] ids)
        => new(MediaKind.Movie, Ids(ids));

    private static MediaFacts Episode(int? season, int? episode, params (string Provider, string Id)[] seriesIds)
        => new(MediaKind.Episode, Ids(), Ids(seriesIds), season, episode);

    private static Dictionary<string, string> Ids(params (string Provider, string Id)[] ids)
        => ids.ToDictionary(i => i.Provider, i => i.Id, StringComparer.OrdinalIgnoreCase);
}
