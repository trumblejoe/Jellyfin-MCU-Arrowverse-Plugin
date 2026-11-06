# Jellyfin Watch Order Playlists

A Jellyfin plugin that keeps playlists in sync with these community watch-order guides:

| Playlist | Source |
|---|---|
| Arrowverse | https://arrowverse.info/ (HTML table, already in viewing order) |
| MCU - Release / Chronological / Sacred Timeline Order | https://aaronperris.com/mcu/ (reads its data file `mcu.json`) |

A scheduled task runs daily at 04:00 and at server start. It downloads the guides, matches
each entry to your library, and creates or reorders the playlists. Only items you actually
have are added. Missing ones are skipped and appear automatically once they're in the library.

## No spoilers

- Episodes match on **show name + season + episode number** only. The plugin never reads the
  guide's episode-title column and never stores or logs synopses.
- Logs show only counts and show names. They never include episode or movie titles.
- MCU entries with a release date in the future are excluded, so unreleased titles never
  show up.
- If a guide can't be fetched or its layout changes, the existing playlist is left as it is.
  The plugin won't empty it.

Jellyfin still shows its own artwork and overviews when you browse a playlist. To hide those,
turn off "Display episode descriptions/overviews" in your Jellyfin client settings where
available.

## Requirements

Jellyfin **12.4** server (.NET 10). It's built against `Jellyfin.Controller` 12.4.0.

## Build

```powershell
dotnet build src/Jellyfin.Plugin.WatchOrder -c Release
```

Output: `src/Jellyfin.Plugin.WatchOrder/bin/Release/net10.0/Jellyfin.Plugin.WatchOrder.dll`

## Install

1. Create the folder `<jellyfin data dir>/plugins/WatchOrderPlaylists_1.0.0.0/`. The data
   dir is `C:\ProgramData\Jellyfin\Server` on Windows, `/var/lib/jellyfin` on Linux, and
   `/config` in Docker.
2. Copy `Jellyfin.Plugin.WatchOrder.dll` into that folder.
3. Restart Jellyfin.
4. Go to Dashboard → Plugins → **Watch Order Playlists**. Choose the owner and which
   playlists you want, then save.
5. Go to Dashboard → Scheduled Tasks → **Sync watch-order playlists** → Run. You can also
   wait for the next scheduled run.

## Settings

| Setting | Default | Notes |
|---|---|---|
| Playlist owner | first admin | |
| Visible to all users | on | |
| Arrowverse | on | |
| MCU Release / Chronological / Sacred Timeline | Release on | Each one becomes its own playlist |
| Canon only (one per MCU order) | on | Set separately for each order. Turn it off to also include the guide's non-canon entries |
| Series aliases | empty | `Name on guide = Name in Jellyfin`, one per line. Only needed if a show you own gets skipped; the server log lists any unmatched show names |

## How it works

- `Sources/ArrowverseSource.cs` parses the `#episode-list` table, reading only the
  #, Series, Episode and Air Date columns.
- `Sources/McuSource.cs` parses `mcu.json`. It sorts by `release_date`, `timeline_order` or
  `sacred_timeline`.
- `Sync/LibraryMatcher.cs` normalizes show and movie names. It ignores "Marvel's" and
  "DC's" prefixes, a trailing "(2014)", punctuation, and `&` vs. "and". When two shows share
  a name, the air year decides which one is meant.
- `Sync/SyncPlaylistsTask.cs` finds the playlist by name under the owner, then creates it,
  or replaces its contents in order when anything changed.

If you rename a playlist, the plugin stops managing it and creates a fresh one with the
original name on the next sync.
