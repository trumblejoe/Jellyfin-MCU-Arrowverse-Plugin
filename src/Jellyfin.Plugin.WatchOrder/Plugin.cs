using System.Globalization;
using Jellyfin.Plugin.WatchOrder.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.WatchOrder;

/// <summary>
/// Watch Order Playlists: keeps Jellyfin playlists in sync with community watch-order guides.
/// </summary>
public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;
    }

    public static Plugin? Instance { get; private set; }

    public override string Name => "Watch Order Playlists";

    public override Guid Id => Guid.Parse("6f0c9a51-2b7e-4d8a-9c3f-1e5a7b2d4c60");

    public override string Description =>
        "Builds and keeps playlists in sync with arrowverse.info and aaronperris.com/mcu. Spoiler-free: matches by show + season/episode only.";

    public IEnumerable<PluginPageInfo> GetPages()
    {
        return
        [
            new PluginPageInfo
            {
                Name = Name,
                EmbeddedResourcePath = string.Format(CultureInfo.InvariantCulture, "{0}.Configuration.configPage.html", GetType().Namespace)
            }
        ];
    }
}
