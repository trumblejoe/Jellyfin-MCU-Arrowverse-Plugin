# Jellyfin Watch Order Playlists

A Jellyfin plugin that keeps playlists in sync with these community watch-order guides:

| Playlist | Source |
|---|---|
| Arrowverse | https://arrowverse.info/ (HTML table, already in viewing order) |
| MCU Release, MCU Chronological, MCU Sacred Timeline, MCU Hybrid, each also as an "Extended" version | https://aaronperris.com/mcu/ (reads its data file `mcu.json`) |

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
| Who gets which playlist | everyone gets everything | A grid of ticked playlists by ticked users; untick a cell to leave that playlist out for that user |
| Remove playlists I turn off | on | At the next sync, deletes this plugin's playlists for anything unticked (a playlist, a user, or a per-user opt-out). Playlists you made yourself are never touched |
| Start at my next unwatched item | on | Each user's playlists drop watched items from the front, so Play picks up where they left off. Something watched out of order further down stays in its place until they get there. To skip an item, mark it watched; marking one unwatched moves the start back |
| Arrowverse | on | |
| MCU playlists | MCU Release on | Six checkboxes: `MCU Release`, `MCU Chronological`, `MCU Sacred Timeline` (canon only), each with an `... Extended` version that also includes the guide's non-canon entries. The names sort in canon/Extended pairs on Jellyfin's playlist page |
| Backup data URL | empty | Optional: where to download list updates from, and a fallback copy of the guides (see below) |
| Series aliases | empty | One row per alias: the name on the guide → the name in Jellyfin (both boxes suggest names). Only needed if a show you own gets skipped; the server log lists any unmatched show names |

### MCU Hybrid (best first watch)

**MCU Hybrid** is built from the same guide data, for people who've never seen the MCU:

- **Release order**, so nothing plays before what it assumes you've seen. That makes it spoiler-safe,
  unlike the story orders (Chronological and Sacred Timeline), where dozens of later-made items play
  before Endgame.
- **Older films from other universes** (the Fox X-Men films, Raimi's Spider-Man, Blade, Daredevil,
  Elektra, Fantastic Four, ...) released before the Multiverse Saga play together, in release order,
  right before it starts. Newcomers start with Iron Man instead of 13 older films, and the
  Infinity Saga runs straight through. Later ones stay at their release dates.
- **Each Disney+, Netflix and Hulu season plays straight through.** Broadcast shows (Agents of
  S.H.I.E.L.D., Inhumans, Cloak & Dagger) keep air-date order, so tie-in episodes follow the films
  they react to.
- Same items as MCU Release, including the web shorts.

Chronological and Sacred Timeline suit a rewatch. Use **Who gets which playlist** to give each
user the right one.

## Built-in lists

Some franchises have no online guide with a data feed, so their watch orders ship inside the
plugin as small text files in `src/Jellyfin.Plugin.WatchOrder/Lists/`. Each one becomes a
checkbox under **Built-in lists** on the settings page. They work like the other playlists:
per-user copies, watched items removed, and missing items reported. They change only with
plugin releases, and nothing is downloaded.

| List | Covers |
|---|---|
| Alien & Predator Release / Chronological | The Alien and Predator films, the AVP crossovers, Prey, Killer of Killers, Badlands and Alien: Earth, in release order and in story order |
| Avatar Release | James Cameron's Avatar films (release order is also story order) |
| Batman TAS Fan Order | A fan-curated order across Batman: The Animated Series (including its two movies, as show specials S00E02/S00E03) and The New Batman Adventures; 111 items |
| Battlestar Galactica Release | The 2003 miniseries, the 2004 series, its webisodes/minisodes, Razor, The Plan, Caprica and Blood & Chrome, by release date; 126 entries (the miniseries and Razor are listed both as movies and as TVDB specials, so either library layout works) |
| Breaking Bad Release | Breaking Bad, Better Call Saul and El Camino, by air date; 126 items |
| DCAU Release | Every DC Animated Universe series (Batman TAS, Superman TAS, The New Batman Adventures, Batman Beyond, Static Shock, The Zeta Project, Justice League and Unlimited) and its 4 movies, by air date; 388 items |
| DCU Release | DC Studios' DCU in release order, per [JustWatch's guide](https://guides.justwatch.com/us/james-gunn-dcu-in-order) (Sept 2026); 35 items. Needs updating as new titles release |
| Doctor Who Release | The modern era (2005 on) with Torchwood, The Sarah Jane Adventures, Class and The War Between the Land and the Sea, by air date, with the full-length specials; 303 items. TheTVDB files 2023 onward as a separate series, "Doctor Who (2023)", so your library needs it as its own show |
| Fast & Furious Release / Chronological | The 11 films (including Hobbs & Shaw), in release order and in the widely used story order |
| Hunger Games Release / Chronological | The Hunger Games films; story order starts with The Ballad of Songbirds & Snakes (Sunrise on the Reaping is added when it releases) |
| Harry Potter Release | The 8 films in release order |
| Middle-earth Release / Chronological | The Lord of the Rings and Hobbit films, The War of the Rohirrim and The Rings of Power, in release order and in story order |
| One Chicago Release | Chicago Fire, P.D., Med and Justice plus the Law & Order: SVU crossover episodes, in broadcast order down to the time slot so crossover nights play correctly; 810 items |
| Saved by the Bell Chronological | Chronological (production) order from IJBall's Saved by the Bell FAQ, including the Hawaiian Style TV movie; 87 items |
| Star Wars Chronological | Canon films and series in story order, per [StarWars.com's viewing guide](https://www.starwars.com/news/star-wars-movies-and-series-guide) and its [official Clone Wars episode order](https://www.starwars.com/news/star-wars-the-clone-wars-chronological-episodeorder); 414 items. Needs updating as new titles release |
| Star Wars Release | The same canon films and series in release order, by air date; 414 items |
| Stargate Optimized | [GateWorld's Optimized Stargate Watch Order](https://gateworld.net/news/2024/09/optimized-stargate-watch-order) (SG-1, Atlantis, Universe including its kino web shorts as specials, and 4 movies); 392 items |

| The Wire Release | David Simon's Baltimore: The Corner, The Wire, then We Own This City; 72 episodes |
| Transformers Release / Chronological | The live-action films and Transformers One; story order starts with Transformers One, Bumblebee and Rise of the Beasts |
| Wizarding World Release / Chronological | The Harry Potter and Fantastic Beasts films, in release order and in story order |

A year on a show line, as in `@ Doctor Who (2023)`, is strict: only a series whose year (in its
name, premiere date or production year) is within 2 of it matches, so same-named shows from
different eras can't be mixed up. A Series Alias overrides it. `Doctor Who (2023) = Doctor Who (2005)`
picks that year's series, and an alias without a year accepts the named show whatever its year.

**Movies** are found in the Movies library by title and year (alternate titles after `|`),
or among a show's specials if that's where your library files them.

**List format.** One item per line. Blank lines and lines starting with `#` are ignored.

```
playlist: My List Name          # playlist name (required)
description: What it is and who it suits.   # shown on the playlist in Jellyfin; spoiler-free
updates: finished               # 'finished', or 'ongoing' if new titles are still coming (only those get checked for updates)
@ Some Show                     # show for the codes below; "@ Some Show (2014)" picks between same-named shows
S01E13                          # one episode
S01E02-E05                      # a range within one season
movie: Some Movie (1993)        # a movie by title and year (Movies library, or a show's specials)
movie: Main Title (2001) | Other Title   # alternate titles after "|"
```

- **Episode codes must match your library's numbering.** The Batman list uses TheTVDB aired
  order.
- **Lists contain no episode titles or notes,** to keep them spoiler-free.
- **Adding a list:** add a `.txt` file to that folder and release. No code changes needed.

### Keeping lists current

Each list says how it's maintained:

| `updates:` | Meaning |
|---|---|
| `finished` | The franchise is done. Never checked. |
| `auto` | A release-order list. Newly aired episodes of its shows are **added at the end** each week. |
| `ongoing` | A hand-ordered list. Newly aired episodes of a show are **added right after that show's last episode**, keeping each show's run where the guide placed it. |

Announced titles that TVDB can't track are written as `upcoming:` lines:

- `upcoming: movie: Title (2027-05-28) | alt: Other Title | after: Earlier Movie` becomes a
  playlist movie once its date passes. In `auto` lists it goes at the end; in `ongoing` lists it
  goes after the named movie. If the date slips, the movie simply shows as missing until it's
  in your library.
- `upcoming: New series (2027-01-01)` is only a reminder, for things with no safe rule yet,
  like a brand-new series that has no TVDB page.

A new series that already has a TVDB page can be **pre-registered**. Add just its
`@ Show (year)` line where it belongs, and its TVDB slug to `tools/lists/shows.json`, and its
episodes are added automatically as they air. For example, HBO's Harry Potter series
(`@ Harry Potter (2026)`) sits at the end of both Wizarding World lists.

For shows whose specials a list uses (e.g. Doctor Who), new specials of 40+ minutes are added
too. Concerts, documentaries and behind-the-scenes specials are skipped.

The **Update lists** workflow (`.github/workflows/update-lists.yml`) runs every Monday, and can
be run by hand from the Actions tab:

1. It adds everything above, runs the tests, commits, and publishes the lists to the `data`
   branch (`lists/*.txt` + `lists/index.json`). Servers with a **Backup data URL** set download
   them on their next daily sync. No plugin release and no Jellyfin restart needed. Servers
   without the URL keep the lists built into the plugin, which update with releases.
2. Anything with no safe rule (due reminders, a show missing from `tools/lists/shows.json`) goes
   into one GitHub issue labelled `list-updates`, which emails you. It holds show names and
   episode codes only. Ask Claude to "handle the open list-update issue". The issue closes itself
   once nothing is left.

The updater is `tools/lists/update_lists.py`. Try it locally with
`python -I tools/lists/update_lists.py --dry-run [--today 2027-12-31]`.

## Playlist descriptions

Every playlist the plugin creates gets a short, spoiler-free **description**, shown on the
playlist's page in Jellyfin and in most apps. It says what the order is and who it's best for
(first watch or rewatch), so your users can pick the right one. The same text appears under each
checkbox on the settings page. Built-in lists carry theirs in a `description:` line.

## Status line

The top of the settings page shows when the sync last ran (and whether it failed), when the MCU
and Arrowverse guides last changed, and when list updates were last received, so you can see
at a glance that the weekly updates are arriving. It's worked out from Jellyfin's task history
and the files already on the server; nothing extra is stored.

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
- `Sources/BuiltInLists.cs` reads the built-in `Lists/*.txt` watch orders.
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
