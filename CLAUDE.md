# jellyfin-watch-order

Personal project: Jellyfin 12.4 plugin (.NET 10) that syncs playlists from arrowverse.info
and aaronperris.com/mcu (`mcu.json`). See README.md for layout.

## Hard rule: no spoilers
- Never print, log, commit, or quote episode titles, movie titles from the MCU feed, or
  synopses, including in chat output while debugging. Inspect source data by structure
  (field names, counts, show names, SxxEyy codes) only.
- Episodes are matched by show + season + episode number. Don't add title-based matching.
- Unreleased MCU items (future `release_date`) must stay excluded.
- Built-in lists (`src/.../Lists/*.txt`) hold show names, SxxEyy codes and movie title+year
  only: never episode titles or the curator's notes. To build one from a titled list, map
  titles to codes privately (scratch files, print codes/counts only) against the numbering
  the user's library uses (TheTVDB aired order for Batman), and verify counts per season.
- Don't commit downloaded copies of the guide pages/JSON as fixtures. The only copy of guide
  data in git is the stripped backup on the `data` branch, written by `update-data.yml`.
  It may hold movie titles (needed for matching) but never episode titles or synopses.

## Build
`dotnet` is installed per-user at `%LOCALAPPDATA%\Microsoft\dotnet\dotnet.exe` (not on PATH).
`& "$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe" test -c Release` (tests), then `build src/Jellyfin.Plugin.WatchOrder -c Release`.
Tests use made-up sample data only, never real guide content.
List upkeep: `.github/workflows/update-lists.yml` (weekly) runs `tools/lists/update_lists.py`, which ADDS
(never just reports) wherever a safe rule exists: `auto` lists append new episodes; `ongoing` lists insert
them after the show's last episode; due `upcoming: movie: ... | after: X` lines become movie entries;
new 40+-minute specials for shows whose specials a list uses. Then it tests, commits and auto-releases.
Only things with no rule (plain `upcoming:` reminders such as brand-new series) go to the `list-updates`
issue. When asked to handle that issue: add the new series/movie in the right place, add its show to
tools/lists/shows.json, remove or re-date the reminder, run tests, commit. The user won't edit lists by hand.
Tests for `auto`/`ongoing` lists use "at least" counts (those lists grow on their own).
CI: `.github/workflows/build.yml` builds and tests on push. Pushing a `v*` tag publishes a GitHub Release zip.

## Repo
Public personal repo `trumblejoe/Jellyfin-MCU-Arrowverse-Plugin`. Repo-local git identity is trumblejoe.

Jellyfin APIs change between majors. Check the source at the matching tag
(e.g. github.com/jellyfin/jellyfin/tree/v12.4) rather than relying on memory.
