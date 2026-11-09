"""Weekly list maintenance, run by .github/workflows/update-lists.yml (or by hand).

For every built-in list (src/Jellyfin.Plugin.WatchOrder/Lists/*.txt):
  updates: finished  -> skipped.
  updates: auto      -> newly aired regular-season episodes of the list's shows are APPENDED in
                        air order (release-order lists: new episodes always air after everything
                        already listed). Same-day episodes are ordered by TVmaze air time.
  updates: ongoing   -> newly aired episodes are only REPORTED (hand-ordered lists need a person
                        to place them).
For auto and ongoing lists, also reported:
  - new Season 0 specials (aired in the last 60 days) of shows whose specials the list uses,
  - 'upcoming:' lines whose date has passed (new movies/series to add).

Never reads or prints episode titles: only show names and SxxEyy codes.

Usage: python -I tools/lists/update_lists.py [--dry-run] [--today YYYY-MM-DD] [--report report.md]
Exit code 0. Prints 'changed=<true|false>' and 'attention=<true|false>' for the workflow.
"""
import argparse
import json
import os
import re
import sys
import urllib.request
from datetime import date, datetime, timedelta

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import tvdb  # noqa: E402

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
LISTS = os.path.join(ROOT, "src", "Jellyfin.Plugin.WatchOrder", "Lists")
CONFIG = json.load(open(os.path.join(os.path.dirname(__file__), "shows.json"), encoding="utf-8"))
CODE = re.compile(r"^S(\d+)E(\d+)(?:-(?:S(\d+))?E(\d+))?$", re.I)
UPCOMING = re.compile(r"^upcoming:\s*(.+?)\s*\((\d{4})(?:-(\d{2})-(\d{2}))?\)\s*$", re.I)


def parse(path):
    lines = open(path, encoding="utf-8").read().split("\n")
    info = {"path": path, "lines": lines, "playlist": None, "updates": None, "shows": [],
            "have": {}, "upcoming": []}
    show = None
    for line in lines:
        s = line.strip()
        if s.startswith("playlist:"):
            info["playlist"] = s[9:].strip()
        elif s.startswith("updates:"):
            info["updates"] = s[8:].strip().lower()
        elif s.startswith("@"):
            show = s[1:].strip()
            if show not in info["shows"]:
                info["shows"].append(show)
                info["have"][show] = set()
        elif (m := CODE.match(s)) and show:
            season, first = int(m[1]), int(m[2])
            last = int(m[4]) if m[4] else first
            for e in range(first, last + 1):
                info["have"][show].add((season, e))
        elif (m := UPCOMING.match(s)):
            due = date(int(m[2]), int(m[3] or 1), int(m[4] or 1))
            info["upcoming"].append((m[1], due))
    return info


def aired(d, today):
    return d is not None and datetime.strptime(d, "%B %d, %Y").date() <= today


def to_date(d):
    return datetime.strptime(d, "%B %d, %Y").date()


_tvmaze = {}


def airstamps(slug):
    """{(season, episode): 'ISO airstamp'} from TVmaze, for ordering same-day episodes."""
    if slug not in _tvmaze:
        _tvmaze[slug] = {}
        try:
            sid = tvdb.series_id(slug)
            if sid:
                req = urllib.request.Request(f"https://api.tvmaze.com/lookup/shows?thetvdb={sid}", headers={"User-Agent": tvdb.UA})
                with urllib.request.urlopen(req, timeout=60) as r:
                    show = json.load(r)
                with urllib.request.urlopen(f"https://api.tvmaze.com/shows/{show['id']}/episodes", timeout=60) as r:
                    for e in json.load(r):
                        if e.get("season") and e.get("number") and e.get("airstamp"):
                            _tvmaze[slug][(e["season"], e["number"])] = e["airstamp"]
        except Exception:  # times are only a tie-breaker; dates still order correctly
            pass
    return _tvmaze[slug]


def compress(codes):
    """[(s, e), ...] in order -> ['S01E01-E03', 'S02E01', ...] (ranges within a season)."""
    out, run = [], None
    for s, e in codes:
        if run and run[0] == s and run[2] + 1 == e:
            run[2] = e
            continue
        if run:
            out.append(f"S{run[0]:02}E{run[1]:02}" + (f"-E{run[2]:02}" if run[2] != run[1] else ""))
        run = [s, e, e]
    if run:
        out.append(f"S{run[0]:02}E{run[1]:02}" + (f"-E{run[2]:02}" if run[2] != run[1] else ""))
    return out


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--dry-run", action="store_true")
    ap.add_argument("--today", default=date.today().isoformat())
    ap.add_argument("--report", default=None)
    args = ap.parse_args()
    today = date.fromisoformat(args.today)

    changed, report = [], []
    for name in sorted(os.listdir(LISTS)):
        if not name.endswith(".txt"):
            continue
        info = parse(os.path.join(LISTS, name))
        mode = info["updates"]
        if mode not in ("auto", "ongoing"):
            continue
        partial = set(CONFIG.get("partial", {}).get(info["playlist"], []))
        new_eps, notes = [], []

        for show in info["shows"]:
            slug = CONFIG["slugs"].get(show)
            if not slug:
                notes.append(f"`{show}` has no TheTVDB slug in tools/lists/shows.json, so it can't be checked.")
                continue
            have = info["have"][show]
            if show not in partial:
                for code, d in tvdb.episodes(slug):
                    s, e = int(code[1:3]), int(code[4:6])
                    if s > 0 and (s, e) not in have and aired(d, today):
                        new_eps.append((to_date(d), show, slug, s, e))
            if any(s == 0 for s, _ in have):
                recent = [code for code, d in tvdb.specials(slug)
                          if aired(d, today) and to_date(d) >= today - timedelta(days=60)
                          and (0, int(code[4:6])) not in have]
                if recent:
                    notes.append(f"New `{show}` specials aired recently: {', '.join(recent)}. Add any that are full episodes.")

        for title, due in info["upcoming"]:
            if due <= today:
                notes.append(f"Upcoming item is due (listed for {due.isoformat()}): **{title}**. "
                             "If it's released, add it to the list (and remove its `upcoming:` line); if not, update the date.")

        if new_eps:
            new_eps.sort(key=lambda x: (x[0], airstamps(x[2]).get((x[3], x[4]), ""), info["shows"].index(x[1]), x[3], x[4]))
            if mode == "auto":
                block, cur = [], None
                group = []
                for _, show, _, s, e in new_eps:
                    if show != cur:
                        if group:
                            block += compress(group)
                        block.append(f"@ {show}")
                        cur, group = show, []
                    group.append((s, e))
                block += compress(group)
                if not args.dry_run:
                    text = "\n".join(info["lines"]).rstrip("\n") + "\n" + "\n".join(block) + "\n"
                    open(info["path"], "w", encoding="utf-8", newline="\n").write(text)
                changed.append(f"- **{info['playlist']}**: added {len(new_eps)} episode(s): "
                               + ", ".join(f"{show} {', '.join(compress([(s, e) for _, sh, _, s, e in new_eps if sh == show]))}"
                                           for show in dict.fromkeys(x[1] for x in new_eps)))
            else:
                by_show = {}
                for _, show, _, s, e in new_eps:
                    by_show.setdefault(show, []).append((s, e))
                notes.append("Newly aired episodes to place: " + "; ".join(
                    f"`{show}` {', '.join(compress(sorted(c)))}" for show, c in by_show.items()))

        if notes:
            report.append(f"### {info['playlist']}\n" + "\n".join(f"- {n}" for n in notes))

    body = []
    if changed:
        body.append("## Added automatically\n" + "\n".join(changed))
    if report:
        body.append("## Needs a person\nThese lists need someone to place new items (ask Claude: "
                    "\"handle the open list-update issue\").\n\n" + "\n\n".join(report))
    text = "\n\n".join(body) if body else "Nothing to update."
    print(text)
    if args.report:
        open(args.report, "w", encoding="utf-8").write(text + "\n")
    print(f"changed={'true' if changed else 'false'}")
    print(f"attention={'true' if report else 'false'}")
    if os.environ.get("GITHUB_OUTPUT"):
        with open(os.environ["GITHUB_OUTPUT"], "a", encoding="utf-8") as f:
            f.write(f"changed={'true' if changed else 'false'}\nattention={'true' if report else 'false'}\n")


if __name__ == "__main__":
    main()
