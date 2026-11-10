using Jellyfin.Plugin.WatchOrder.Configuration;
using Jellyfin.Plugin.WatchOrder.Sync;

namespace Jellyfin.Plugin.WatchOrder.Tests;

public class UserPlaylistTests
{
    private static readonly Guid Alice = Guid.NewGuid();
    private static readonly Guid Bob = Guid.NewGuid();

    private static PluginConfiguration Config(params string[] excluded) => new()
    {
        EnableArrowverse = true,
        EnableMcuRelease = true,
        EnableMcuSacredTimeline = true,
        EnabledLists = ["One Chicago Release"],
        ExcludedUserPlaylists = excluded
    };

    [Fact]
    public void Enabled_names_come_from_the_settings()
    {
        Assert.Equal(
            ["Arrowverse", "MCU Release", "MCU Sacred Timeline", "One Chicago Release"],
            ManagedPlaylists.EnabledNames(Config()).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Every_user_gets_every_ticked_playlist_by_default()
    {
        Assert.Equal(4, ManagedPlaylists.NamesFor(Config(), Alice).Count);
        Assert.Equal(4, ManagedPlaylists.NamesFor(Config(), Bob).Count);
    }

    [Fact]
    public void An_opt_out_applies_only_to_that_user()
    {
        // Saved by the settings page with Jellyfin's dash-less user ids.
        var config = Config($"{Bob:N}|One Chicago Release", $"{Bob:N}|mcu sacred timeline");

        Assert.Contains("One Chicago Release", ManagedPlaylists.NamesFor(config, Alice));
        Assert.DoesNotContain("One Chicago Release", ManagedPlaylists.NamesFor(config, Bob));
        Assert.DoesNotContain("MCU Sacred Timeline", ManagedPlaylists.NamesFor(config, Bob));
        Assert.Contains("MCU Release", ManagedPlaylists.NamesFor(config, Bob));
    }

    [Fact]
    public void Unticking_a_list_removes_it_for_everyone()
    {
        var config = Config();
        config.EnabledLists = [];

        Assert.DoesNotContain("One Chicago Release", ManagedPlaylists.NamesFor(config, Alice));
    }

    [Fact]
    public void Malformed_opt_outs_are_ignored()
    {
        var config = Config("not-a-guid|Arrowverse", "no separator", $"{Alice:N}");

        Assert.Equal(4, ManagedPlaylists.NamesFor(config, Alice).Count);
    }
}
