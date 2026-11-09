"""Weekly list maintenance, run by .github/workflows/update-lists.yml (or by hand).

For every built-in list (src/Jellyfin.Plugin.WatchOrder/Lists/*.txt):
  updates: finished  -> skipped.
  updates: auto      -> release-order list: newly aired episodes of its shows are APPENDED in air
                        order (same-day episodes by TVmaze air time).
  updates: ongoing   -> hand-ordered list: newly aired episodes of a show are INSERTED right after
                        that show's last episode in the list, keeping each show's run together.
Both also get:
  - new full-length Season 0 specials (aired in the last 120 days, 40+ minutes, not a concert/
    documentary/behind-the-scenes entry) of shows whose specials the list already uses;
  - due 'upcoming: movie: Title (YYYY-MM-DD) [| alt: Other Title] [| after: Some Movie]' lines,
    turned into 'movie:' entries (auto lists: at the end; ongoing lists: after the named movie,
    or at the end with 'after: end').
Reported (in the GitHub issue) only when no safe rule exists: due plain 'upcoming:' reminders
(e.g. a brand-new series), shows missing from shows.json, and placements that couldn't be found.

Never reads out or prints episode titles: only show names and SxxEyy codes.

Usage: python -I tools/lists/update_lists.py [--dry-run] [--today YYYY-MM-DD] [--report report.md]
Prints 'changed=<true|false>' and 'attention=<true|false>' (also to $GITHUB_OUTPUT).
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
UPCOMING = re.compile(r"^upcoming:\s*(movie:\s*)?(.+?)\s*\((\d{4})(?:-(\d{2})-(\d{2}))?\)\s*((?:\|.*)?)$", re.I)
SPECIALS_LOOKBACK_DAYS = 120


def to_date(d):
    return datetime.strptime(d, "%B %d, %Y").date()


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


class ListFile:
    def __init__(self, path):
        self.path = path
        self.lines = open(path, encoding="utf-8").read().rstrip("\n").split("\n")
        self.changed = False

    def field(self, key):
        return next((l.split(":", 1)[1].strip() for l in self.lines if l.startswith(key + ":")), None)

    def scan(self):
        """shows (in order), codes per show, and index of each show's last code line."""
        shows, have, last = [], {}, {}
        show = None
        for i, line in enumerate(self.lines):
            s = line.strip()
            if s.startswith("@"):
                show = s[1:].strip()
                if show not in have:
                    shows.append(show)
                    have[show] = set()
            elif (m := CODE.match(s)) and show:
                season, first = int(m[1]), int(m[2])
                for e in range(first, (int(m[4]) if m[4] else first) + 1):
                    have[show].add((season, e))
                last[show] = i
            elif s.startswith("movie:"):
                show = None  # a movie line ends the current show block
        return shows, have, last

    def append(self, new_lines):
        self.lines += new_lines
        self.changed = True

    def insert_after(self, index, new_lines):
        self.lines[index + 1:index + 1] = new_lines
        self.changed = True

    def save(self):
        open(self.path, "w", encoding="utf-8", newline="\n").write("\n".join(self.lines) + "\n")


_tvmaze = {}


def airstamps(slug):
    """{(season, episode): 'ISO airstamp'} from TVmaze, to order same-day episodes. Optional."""
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
        except Exception:  # air times are only a tie-breaker; dates still order correctly
            pass
    return _tvmaze[slug]


def blocks(items, show_order):
    """[(date, show, slug, s, e)] -> list lines ('@ Show' + compressed codes), in air order."""
    items = sorted(items, key=lambda x: (x[0], airstamps(x[2]).get((x[3], x[4]), "") if x[3] else "",
                                         show_order.index(x[1]), x[3], x[4]))
    out, cur, group = [], None, []
    for _, show, _, s, e in items:
        if show != cur:
            out += compress(group)
            out.append(f"@ {show}")
            cur, group = show, []
        group.append((s, e))
    return out + compress(group)


def process(lf, today, partial, changes, notes):
    mode = lf.field("updates")
    shows, have, _ = lf.scan()
    new = []  # (date, show, slug, season, episode)
    for show in shows:
        slug = CONFIG["slugs"].get(show)
        if not slug:
            notes.append(f"`{show}` has no TheTVDB slug in tools/lists/shows.json, so it isn't checked.")
            continue
        if show not in partial:
            for code, d in tvdb.episodes(slug):
                s, e = int(code[1:3]), int(code[4:6])
                if s > 0 and (s, e) not in have[show] and d and to_date(d) <= today:
                    new.append((to_date(d), show, slug, s, e))
        if any(s == 0 for s, _ in have[show]):
            for code, d, path in tvdb.specials(slug):
                e = int(code[4:])
                if (0, e) in have[show] or not d:
                    continue
                aired = to_date(d)
                if today - timedelta(days=SPECIALS_LOOKBACK_DAYS) <= aired <= today and tvdb.is_full_special(path):
                    new.append((aired, show, slug, 0, e))

    if new:
        added = {}
        for _, show, _, s, e in sorted(new, key=lambda x: (x[0], x[3], x[4])):
            added.setdefault(show, []).append((s, e))
        if mode == "auto":
            lf.append(blocks(new, shows))
        else:  # ongoing: keep each show's run together
            for show, codes in added.items():
                _, _, last = lf.scan()
                lf.insert_after(last[show], compress(sorted(codes)))
        changes.append(f"- **{lf.field('playlist')}**: added " + "; ".join(
            f"{show} {', '.join(compress(sorted(c)))}" for show, c in added.items()))

    # Due upcoming lines.
    for i in reversed(range(len(lf.lines))):
        m = UPCOMING.match(lf.lines[i].strip())
        if not m:
            continue
        due = date(int(m[3]), int(m[4] or 1), int(m[5] or 1))
        if due > today:
            continue
        title = m[2].strip()
        if not m[1]:
            notes.append(f"Reminder due ({due.isoformat()}): **{title}**. Ask Claude to add it once it's out, "
                         "or to move the reminder's date.")
            continue
        opts = [p.strip() for p in m[6].split("|") if p.strip()]
        alts = [o.split(":", 1)[1].strip() for o in opts if o.lower().startswith("alt:")]
        after = next((o.split(":", 1)[1].strip() for o in opts if o.lower().startswith("after:")), "end")
        movie_line = f"movie: {title} ({due.year})" + "".join(f" | {a}" for a in alts)
        del lf.lines[i]
        lf.changed = True
        if mode == "auto" or after.lower() == "end":
            lf.append([movie_line])
        else:
            anchor = next((j for j, l in enumerate(lf.lines)
                           if l.startswith("movie:") and re.sub(r"\s*\(\d{4}\).*", "", l[6:].strip()).lower() == after.lower()), None)
            if anchor is None:
                lf.append([movie_line])
                notes.append(f"Added **{title}** at the end because the movie it should follow (\"{after}\") "
                             "wasn't found. Ask Claude to move it.")
            else:
                lf.insert_after(anchor, [movie_line])
        changes.append(f"- **{lf.field('playlist')}**: added movie {title}")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--dry-run", action="store_true")
    ap.add_argument("--today", default=date.today().isoformat())
    ap.add_argument("--report", default=None)
    args = ap.parse_args()
    today = date.fromisoformat(args.today)

    changes, report = [], []
    for name in sorted(os.listdir(LISTS)):
        if not name.endswith(".txt"):
            continue
        lf = ListFile(os.path.join(LISTS, name))
        if lf.field("updates") not in ("auto", "ongoing"):
            continue
        notes = []
        process(lf, today, set(CONFIG.get("partial", {}).get(lf.field("playlist"), [])), changes, notes)
        if lf.changed and not args.dry_run:
            lf.save()
        if notes:
            report.append(f"### {lf.field('playlist')}\n" + "\n".join(f"- {n}" for n in notes))

    body = []
    if changes:
        body.append("## Added automatically\n" + "\n".join(changes))
    if report:
        body.append("## Needs Claude\nThese couldn't be placed automatically. Ask Claude to "
                    "\"handle the open list-update issue\".\n\n" + "\n\n".join(report))
    text = "\n\n".join(body) if body else "Nothing to update."
    print(text)
    if args.report:
        open(args.report, "w", encoding="utf-8").write(text + "\n")
    flags = f"changed={'true' if changes else 'false'}\nattention={'true' if report else 'false'}\n"
    print(flags, end="")
    if os.environ.get("GITHUB_OUTPUT"):
        with open(os.environ["GITHUB_OUTPUT"], "a", encoding="utf-8") as f:
            f.write(flags)


if __name__ == "__main__":
    main()
