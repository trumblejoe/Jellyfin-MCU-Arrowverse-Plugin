# Jellyfin Watch Order Playlists

A Jellyfin plugin that keeps playlists in sync with these community watch-order guides:

| Playlist | Source |
|---|---|
| Arrowverse | https://arrowverse.info/ (HTML table, already in viewing order) |
| MCU Release, MCU Chronological, MCU Sacred Timeline, each also as an "Extended" version | https://aaronperris.com/mcu/ (reads its data file `mcu.json`) |

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

## Get the plugin

**From GitHub (no .NET needed):** download the zip from the
[Releases](../../releases) page. GitHub Actions builds and tests every push to `main`. Only people with write access can trigger workflows. To publish a
new release, push a version tag:

```powershell
git tag v1.0.1
git push origin v1.0.1
```

**Build it yourself:**

```powershell
dotnet test -c Release                               # run the tests
dotnet build src/Jellyfin.Plugin.WatchOrder -c Release
```

The DLL ends up in `src/Jellyfin.Plugin.WatchOrder/bin/Release/net10.0/Jellyfin.Plugin.WatchOrder.dll`.

## Install

### From the Jellyfin catalog (recommended; gets updates)

1. Dashboard → Plugins → **Repositories** (or **Manage Repositories**) → add:
   - Name: `Watch Order Playlists`
   - URL: `https://github.com/trumblejoe/Jellyfin-MCU-Arrowverse-Plugin/releases/latest/download/manifest.json`
2. Dashboard → Plugins → **Catalog** → **Watch Order Playlists** → Install.
3. Restart Jellyfin, then follow steps 4 and 5 below.

Jellyfin will then offer an update each time a new version tag is released.
If you installed by hand before, delete that `WatchOrderPlaylists` folder first so the
plugin isn't loaded twice.

### By hand

1. Create the folder `<jellyfin data dir>/plugins/WatchOrderPlaylists/`. The data
   dir is `C:\ProgramData\Jellyfin\Server` on Windows, `/var/lib/jellyfin` on Linux, and
   `/config` in Docker.
2. Copy `Jellyfin.Plugin.WatchOrder.dll` into that folder. If you downloaded the release
   zip, unzip it there.
3. Restart Jellyfin.
4. Go to Dashboard → Plugins → **Watch Order Playlists**. Tick the users and which
   playlists you want, then save.
5. Go to Dashboard → Scheduled Tasks → **Sync watch-order playlists** → Run. You can also
   wait for the next scheduled run.

## Settings

| Setting | Default | Notes |
|---|---|---|
| Users | first admin | Each ticked user gets their own private copy of each playlist |
| Remove watched items | on | Keeps each user's next unwatched item first, so Play picks up where they left off. Items drop off as soon as that user finishes them; marking one unwatched puts it back |
| Arrowverse | on | |
| MCU playlists | MCU Release on | Six checkboxes: `MCU Release`, `MCU Chronological`, `MCU Sacred Timeline` (canon only), each with an `... Extended` version that also includes the guide's non-canon entries. The names sort in canon/Extended pairs on Jellyfin's playlist page |
| Backup data URL | empty | Optional GitHub backup to fall back on (see below) |
| Series aliases | empty | `Name on guide = Name in Jellyfin`, one per line. Only needed if a show you own gets skipped; the server log lists any unmatched show names |

## Missing from your library

The bottom of the plugin's settings page lists what your ticked playlists include that isn't
in your library:

- **Playlist table:** for each playlist, how many items you have, and how many episodes and
  movies/specials are missing.
- **Shows not in your library:** shown by the guide's spelling. If you have a show under a
  different name, add a Series Alias with the name shown.
- **Shows with missing episodes:** the show, how many episodes are missing, and which seasons.
- **Movies and specials:** a count only, with titles hidden to avoid spoilers.

It's worked out when you open the page (or click Refresh, or save) from the guides saved at
the last sync and your current library. It doesn't download anything or store anything. A
fixed alias shows up right after you save, with no sync needed.

## How it works

- `Sources/ArrowverseSource.cs` parses the `#episode-list` table, reading only the
  #, Series, Episode and Air Date columns.
- `Sources/McuSource.cs` parses `mcu.json`. It sorts by `release_date`, `timeline_order` or
  `sacred_timeline`.
- `Sync/LibraryMatcher.cs` normalizes show and movie names. It ignores "Marvel's" and
  "DC's" prefixes, a trailing "(2014)", punctuation, and `&` vs. "and". When two shows share
  a name, the air year decides which one is meant.
- `Sync/WatchedListener.cs` removes an item from a user's playlists the moment that user
  finishes it (when "Remove watched items" is on).
- `Sources/GuideDownloader.cs` + `Sources/GuideSnapshotStore.cs` download each guide and pick the
  site or the newest backup (see below). `tools/GuideDataUpdater` is the same code run by the
  GitHub workflow.
- `Sync/MissingReport.cs` + `Api/WatchOrderController.cs` (admin-only `GET /WatchOrder/Missing`)
  build the settings page's "Missing from your library" section.
- `Sync/SyncPlaylistsTask.cs` finds each user's own playlist by name, then creates it,
  or replaces its contents in order when anything changed.

If you rename a playlist, the plugin stops managing it and creates a fresh one with the
original name on the next sync.

## If a guide site goes offline

Each guide has two backup copies, and the plugin uses them in this order:

1. **The site itself.**
2. **The newest backup:** either the copy saved on your Jellyfin server or, if you've
   entered a **Backup data URL** in settings, the copy on that GitHub branch. Whichever
   backup gets used is also saved on the server.

- **Server copy:** saved after each good download, in
  `<jellyfin data dir>/plugins/Jellyfin.Plugin.WatchOrder/` (kept across plugin updates and
  included in Jellyfin backups).
- **GitHub copy (opt-in):** the Backup data URL setting is blank by default, so installs
  never contact GitHub unless the server admin enters a URL. Even then, GitHub is contacted
  only when a site fails, looks broken, or there's no server copy yet. Reading the files is
  free and doesn't run any workflows. This repo's backup is at
  `https://raw.githubusercontent.com/trumblejoe/Jellyfin-MCU-Arrowverse-Plugin/data/`.
  The **Update guide data** workflow refreshes it daily. It commits only when
  the data changes. If a site is down or looks broken, the run fails (GitHub emails you) and
  the existing copy is kept. You can also run it by hand under Actions → Update guide data → Run workflow.
- **Broken-site check:** a download with under 80% of the backup's entries is treated as a
  broken site, and the backup is used instead. With a Backup data URL set, this also
  protects a brand-new install.
- **What the backups contain:** only ordering data: show names, season and episode numbers,
  dates, order positions, and movie titles (needed for matching). They never contain episode
  titles or synopses.
- **Inactivity limit:** GitHub turns off scheduled workflows in public repos after 60 days
  without repo activity, and emails you first. Re-enable it under Actions if that happens.
  The plugin keeps working from the server copy meanwhile.

## License

GPL-3.0. See [LICENSE](LICENSE). This matches Jellyfin's plugin libraries and official plugins.
