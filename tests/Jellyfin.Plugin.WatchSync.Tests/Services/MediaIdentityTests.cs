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

    // --- Title + year fallback (movies only, only when ids cannot decide) ---

    [Fact]
    public void Fallback_Disabled_SameTitleAndYear_AreNotSame()
    {
        var a = TitledMovie("Dune", 2021, ("Tmdb", "438631"));
        var b = TitledMovie("Dune", 2021, ("Imdb", "tt1160419"));

        Assert.False(MediaIdentity.AreSame(a, b));
    }

    [Fact]
    public void Fallback_Enabled_NoCommonId_SameNormalizedTitleAndYear_AreSame()
    {
        var a = TitledMovie("Dune 4K", 2021, ("Tmdb", "438631"));
        var b = TitledMovie("Dune", 2021, ("Imdb", "tt1160419"));

        Assert.True(MediaIdentity.AreSame(a, b, useTitleYearFallback: true));
    }

    [Fact]
    public void Fallback_Enabled_DifferentYears_AreNotSame()
    {
        Assert.False(MediaIdentity.AreSame(TitledMovie("Dune", 1984), TitledMovie("Dune", 2021), useTitleYearFallback: true));
    }

    [Fact]
    public void Fallback_Enabled_MissingYear_AreNotSame()
    {
        Assert.False(MediaIdentity.AreSame(TitledMovie("Dune", 2021), TitledMovie("Dune", null), useTitleYearFallback: true));
    }

    [Fact]
    public void Fallback_Enabled_IdsConflict_AreNotSame()
    {
        var a = TitledMovie("Dune", 2021, ("Tmdb", "438631"));
        var b = TitledMovie("Dune", 2021, ("Tmdb", "841"));

        Assert.False(MediaIdentity.AreSame(a, b, useTitleYearFallback: true));
    }

    [Fact]
    public void Fallback_Enabled_NotUsedForEpisodes()
    {
        var a = new MediaFacts(MediaKind.Episode, Ids(), Ids(), 1, 1, "Pilot", 2008);
        var b = new MediaFacts(MediaKind.Episode, Ids(), Ids(), 1, 1, "Pilot", 2008);

        Assert.False(MediaIdentity.AreSame(a, b, useTitleYearFallback: true));
    }

    // --- GetKeys ---

    [Fact]
    public void GetKeys_Movie_OneKeyPerKnownProvider()
    {
        var keys = MediaIdentity.GetKeys(Movie(("Tmdb", "438631"), ("Imdb", "tt1160419"), ("Tvdb", "1")));

        Assert.Equal(["movie:tmdb:438631", "movie:imdb:tt1160419"], keys);
    }

    [Fact]
    public void GetKeys_Episode_UsesSeriesIdsSeasonAndEpisode()
    {
        var keys = MediaIdentity.GetKeys(Episode(1, 2, ("Tvdb", "81189"), ("Imdb", "tt0903747")));

        Assert.Equal(["episode:tvdb:81189:s1e2", "episode:imdb:tt0903747:s1e2"], keys);
    }

    [Fact]
    public void GetKeys_MovieAndEpisodeWithSameNumber_NeverShareAKey()
    {
        var movie = MediaIdentity.GetKeys(Movie(("Imdb", "tt0903747")));
        var episode = MediaIdentity.GetKeys(Episode(1, 2, ("Imdb", "tt0903747")));

        Assert.Empty(movie.Intersect(episode));
    }

    [Fact]
    public void GetKeys_EpisodeWithoutNumber_HasNoKey()
    {
        Assert.Empty(MediaIdentity.GetKeys(Episode(1, null, ("Tvdb", "81189"))));
    }

    [Fact]
    public void GetKeys_TitleFallback_CopiesShareTheTitleKey()
    {
        var a = MediaIdentity.GetKeys(TitledMovie("Dune 4K", 2021), useTitleYearFallback: true);
        var b = MediaIdentity.GetKeys(TitledMovie("Dune", 2021), useTitleYearFallback: true);

        Assert.Equal(["movie:title:dune:2021"], a);
        Assert.Equal(a, b);
    }

    // --- Helpers ---

    private static MediaFacts TitledMovie(string title, int? year, params (string Provider, string Id)[] ids)
        => new(MediaKind.Movie, Ids(ids), Title: title, Year: year);


    private static MediaFacts Movie(params (string Provider, string Id)[] ids)
        => new(MediaKind.Movie, Ids(ids));

    private static MediaFacts Episode(int? season, int? episode, params (string Provider, string Id)[] seriesIds)
        => new(MediaKind.Episode, Ids(), Ids(seriesIds), season, episode);

    private static Dictionary<string, string> Ids(params (string Provider, string Id)[] ids)
        => ids.ToDictionary(i => i.Provider, i => i.Id, StringComparer.OrdinalIgnoreCase);
}
