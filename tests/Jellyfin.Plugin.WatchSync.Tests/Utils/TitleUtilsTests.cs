using Jellyfin.Plugin.WatchSync.Utils;

namespace Jellyfin.Plugin.WatchSync.Tests.Utils;

public class TitleUtilsTests
{
    [Fact]
    public void NormalizeTitle_RemovesQualityTag()
    {
        // Arrange
        var title = "The Matrix 4K";

        // Act
        var result = TitleUtils.NormalizeTitle(title);

        // Assert
        Assert.Equal("the matrix", result);
    }

    [Fact]
    public void NormalizeTitle_KeepsWordsContainingQualityLetters()
    {
        // Arrange
        var title = "The Adventures of Tintin";

        // Act
        var result = TitleUtils.NormalizeTitle(title);

        // Assert
        Assert.Equal("the adventures of tintin", result);
    }

    [Theory]
    [InlineData("Dune 4K")]
    [InlineData("Dune UHD")]
    [InlineData("Dune HDR")]
    [InlineData("Dune Dolby Vision")]
    [InlineData("Dune DV")]
    [InlineData("Dune 2160p")]
    [InlineData("Dune 1080p")]
    [InlineData("Dune 720p")]
    public void NormalizeTitle_RemovesEachQualityTag(string title)
    {
        Assert.Equal("dune", TitleUtils.NormalizeTitle(title));
    }
}
