using Jellyfin.Plugin.WatchOrder.Configuration;
using Jellyfin.Plugin.WatchOrder.Sources;
using Jellyfin.Plugin.WatchOrder.Sync;

namespace Jellyfin.Plugin.WatchOrder.Tests;

public class PlaylistNamingTests
{
    private static IEnumerable<string> AllMcuNames() =>
        Enum.GetValues<McuOrder>().SelectMany(o => new[] { McuSource.PlaylistName(o, false), McuSource.PlaylistName(o, true) });

    [Fact]
    public void Names_sort_as_canon_extended_pairs_on_jellyfins_playlist_page()
    {
        var sorted = AllMcuNames().Order(StringComparer.OrdinalIgnoreCase).ToList();

        Assert.Equal(
            [
                "MCU Chronological",
                "MCU Chronological Extended",
                "MCU Hybrid",
                "MCU Hybrid Extended",
                "MCU Release",
                "MCU Release Extended",
                "MCU Sacred Timeline",
                "MCU Sacred Timeline Extended"
            ],
            sorted);
    }

    [Fact]
    public void All_current_and_legacy_names_are_managed()
    {
        foreach (var name in AllMcuNames().Append("MCU - Release Order").Append("Arrowverse"))
        {
            Assert.Contains(name, ManagedPlaylists.Names);
        }
    }

    [Fact]
    public void Old_canon_only_setting_is_unchanged()
    {
        var config = new PluginConfiguration { EnableMcuSacredTimeline = true, McuSacredTimelineCanonOnly = true };

        Assert.False(config.MigrateLegacyCanonSettings());
        Assert.True(config.EnableMcuSacredTimeline);
        Assert.False(config.EnableMcuSacredTimelineExtended);
    }

    [Fact]
    public void Old_non_canon_setting_becomes_the_extended_playlist()
    {
        var config = new PluginConfiguration { EnableMcuRelease = true, McuReleaseCanonOnly = false };

        Assert.True(config.MigrateLegacyCanonSettings());
        Assert.False(config.EnableMcuRelease);
        Assert.True(config.EnableMcuReleaseExtended);

        // Runs every load, so it must only convert once.
        Assert.False(config.MigrateLegacyCanonSettings());
        Assert.True(config.EnableMcuReleaseExtended);
    }

    [Fact]
    public void Fresh_install_needs_no_migration()
    {
        Assert.False(new PluginConfiguration().MigrateLegacyCanonSettings());
    }
}
