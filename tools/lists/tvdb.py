"""Read TheTVDB aired-order episode numbering and air dates (public web pages, no API key)."""
import html
import re
import time
import urllib.request

UA = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0 Safari/537.36"
_cache = {}


def _get(url):
    if url not in _cache:
        req = urllib.request.Request(url, headers={"User-Agent": UA})
        with urllib.request.urlopen(req, timeout=60) as r:
            _cache[url] = r.read().decode("utf-8", errors="replace")
        time.sleep(1)  # be polite
    return _cache[url]


def episodes(slug):
    """[(code 'SxxEyy', airdate 'Month D, YYYY' or None)] in TheTVDB aired order.
    Titles are deliberately not returned (spoiler-free)."""
    h = _get(f"https://thetvdb.com/series/{slug}/allseasons/official")
    if "<title>404" in h:
        raise RuntimeError(f"TheTVDB page not found for '{slug}'")
    out = []
    for m in re.finditer(r'(S\d\dE\d\d)</span>(.{0,1500}?)<a href="/series/[^"]*/episodes/\d+"[^>]*>', h, re.S):
        tail = h[m.end():m.end() + 800]
        d = re.search(r"([A-Z][a-z]+ \d{1,2}, \d{4})", tail)
        out.append((m.group(1), d.group(1) if d else None))
    return out


def specials(slug):
    """[(code 'S00Eyy', airdate or None, episode page path)] from the Season 0 table."""
    h = _get(f"https://thetvdb.com/series/{slug}/seasons/official/0")
    out = []
    for m in re.finditer(r'<td>\s*(S00E\d+)\s*</td>\s*<td>\s*<a href="(/series/[^"]+/episodes/\d+)"', h):
        tail = h[m.end():m.end() + 800]
        d = re.search(r"([A-Z][a-z]+ \d{1,2}, \d{4})", tail)
        out.append((m.group(1), d.group(1) if d else None, m.group(2)))
    return out


NOT_A_STORY = re.compile(r"prom|concert|confidential|behind the|making of|documentary|recap|"
                         r"unleashed|extra|special look|first look|trailer|tribute|outtake|interview", re.I)


def is_full_special(path, min_minutes=40):
    """True if a Season 0 entry looks like a full story: long enough, and its title doesn't mark
    it as a concert, documentary, behind-the-scenes, etc. (The title is only checked here,
    never returned or printed.)"""
    h = _get("https://thetvdb.com" + path)
    m = re.search(r"Runtime</strong>\s*<span>\s*(\d+)\s*minutes?", h, re.I) or re.search(r"(\d+)\s*minutes", h)
    title = re.search(r"<title>([^<]*)", h)
    if not m or int(m.group(1)) < min_minutes:
        return False
    return not (title and NOT_A_STORY.search(html.unescape(title.group(1))))


def series_id(slug):
    """TheTVDB numeric series id (used to look the show up on TVmaze for air times)."""
    h = _get(f"https://thetvdb.com/series/{slug}")
    m = re.search(r"TheTVDB\.com Series ID</strong>\s*<span>\s*(\d+)", h) or re.search(r'/series/(\d+)/', h)
    return int(m.group(1)) if m else None


def norm(t):
    return re.sub(r"[^a-z0-9]", "", html.unescape(t).lower().replace("&", "and"))
