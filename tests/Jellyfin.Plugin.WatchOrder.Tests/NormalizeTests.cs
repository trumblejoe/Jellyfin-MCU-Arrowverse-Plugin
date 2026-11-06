using Jellyfin.Plugin.WatchOrder.Sync;

namespace Jellyfin.Plugin.WatchOrder.Tests;

public class NormalizeTests
{
    [Theory]
    [InlineData("Marvel's Agents of S.H.I.E.L.D.", "Agents of S.H.I.E.L.D.")]
    [InlineData("The Flash (2014)", "The Flash")]
    [InlineData("Superman &amp; Lois", "Superman and Lois")]
    [InlineData("Superman & Lois", "superman and lois")]
    [InlineData("DC's Legends of Tomorrow", "Legends of Tomorrow")]
    [InlineData("Marvel’s Daredevil", "Daredevil")]
    [InlineData("What If...?", "What If")]
    [InlineData("Pokémon", "Pokemon")]
    public void Library_and_guide_names_normalize_the_same(string a, string b)
    {
        Assert.Equal(LibraryMatcher.Normalize(b), LibraryMatcher.Normalize(a));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Blank_names_normalize_to_empty(string? value)
    {
        Assert.Equal(string.Empty, LibraryMatcher.Normalize(value));
    }

    [Fact]
    public void Different_shows_stay_different()
    {
        Assert.NotEqual(LibraryMatcher.Normalize("Daredevil"), LibraryMatcher.Normalize("Daredevil: Born Again"));
    }
}
