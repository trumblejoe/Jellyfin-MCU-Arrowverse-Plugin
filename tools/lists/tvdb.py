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
    h = _get(f"https://thetvdb.com/series/{slug}/seasons/official/0")
    out = []
    for m in re.finditer(r'(S00E\d\d)</span>(.{0,1500}?)<a href="/series/[^"]*/episodes/\d+"[^>]*>', h, re.S):
        tail = h[m.end():m.end() + 800]
        d = re.search(r"([A-Z][a-z]+ \d{1,2}, \d{4})", tail)
        out.append((m.group(1), d.group(1) if d else None))
    return out


def series_id(slug):
    """TheTVDB numeric series id (used to look the show up on TVmaze for air times)."""
    h = _get(f"https://thetvdb.com/series/{slug}")
    m = re.search(r"TheTVDB\.com Series ID</strong>\s*<span>\s*(\d+)", h) or re.search(r'/series/(\d+)/', h)
    return int(m.group(1)) if m else None


def norm(t):
    return re.sub(r"[^a-z0-9]", "", html.unescape(t).lower().replace("&", "and"))
