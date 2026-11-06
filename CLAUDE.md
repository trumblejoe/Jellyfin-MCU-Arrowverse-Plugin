# jellyfin-watch-order

Personal project: Jellyfin 12.4 plugin (.NET 10) that syncs playlists from arrowverse.info
and aaronperris.com/mcu (`mcu.json`). See README.md for layout.

## Hard rule: no spoilers
- Never print, log, commit, or quote episode titles, movie titles from the MCU feed, or
  synopses, including in chat output while debugging. Inspect source data by structure
  (field names, counts, show names, SxxEyy codes) only.
- Episodes are matched by show + season + episode number. Don't add title-based matching.
- Unreleased MCU items (future `release_date`) must stay excluded.
- Don't commit downloaded copies of the guide pages/JSON as fixtures.

## Build
`dotnet` is installed per-user at `%LOCALAPPDATA%\Microsoft\dotnet\dotnet.exe` (not on PATH).
`& "$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe" build src/Jellyfin.Plugin.WatchOrder -c Release`

Jellyfin APIs change between majors. Check the source at the matching tag
(e.g. github.com/jellyfin/jellyfin/tree/v12.4) rather than relying on memory.
