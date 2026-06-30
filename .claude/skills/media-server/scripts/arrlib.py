"""arrlib — shared utilities for the bulk-add / disk-audit / fix-seeding /
monitor scripts.

Keeps the scene-named-folder parser, the container-path translation, and
the Transmission RPC helper in one place so a fix in one tool applies
to all of them. Pure functions where possible; the RPC helper has I/O
but is constructed so its caller is testable.

Test coverage lives in arrlib_test.py, bulk-add_test.py, and
disk-audit_test.py.
"""
from __future__ import annotations

import base64
import json
import re
import urllib.error
import urllib.request
from typing import Callable

# Keywords that mark the end of a title in scene-named folders.
SCENE_KEYWORDS = (
    r'1080p|2160p|720p|480p|4k|uhd|hdr|hevc|x265|x264|h\.?26[45]|'
    r'bluray|bdrip|brrip|webrip|web-?dl|web|amzn|nf|netflix|hulu|atvp|'
    r'dsnp|hmax|stan|peacock|dvdrip|hdrip|repack|proper|remux|extended|'
    r'directors\.?cut|unrated|aac\d?|dd\+?\d|dts|atmos|truehd|'
    r'complete|collection|dual|dual-audio|10\s*bit|6ch|2ch|multi|telesync'
)


# Transmission runs in a container and reports container-relative paths
# (`/downloads/...`) via RPC. Scripts that stat files on the host need
# this translation.
HOST_DOWNLOADS = '/var/lib/transmission-daemon/downloads'
CONTAINER_DOWNLOADS = '/downloads'


def to_host_path(p: str) -> str:
    """Convert a Transmission-reported container path to the host filesystem path."""
    if p.startswith(CONTAINER_DOWNLOADS + '/') or p == CONTAINER_DOWNLOADS:
        return HOST_DOWNLOADS + p[len(CONTAINER_DOWNLOADS):]
    return p


def normalize(s: str) -> str:
    """Lowercase + alphanumeric-only, for fuzzy title overlap comparison."""
    return re.sub(r'[^a-z0-9]', '', s.lower())


def make_trans_rpc(url: str, user: str, password: str) -> Callable[..., dict]:
    """Return a closure that calls Transmission's RPC with auth + session token.

    Transmission requires a per-session token: the first request returns
    409 with an `X-Transmission-Session-Id` header, which subsequent
    requests must echo back. The token can expire, in which case any
    request 409s again with a new token. This helper handles both: the
    initial probe AND a one-time retry on 409 mid-session. Without the
    retry, a long-running script that straddles a session reset crashes
    midway through.

    Returns a function rpc(method, args=None) -> arguments dict.
    """
    auth = base64.b64encode(f'{user}:{password}'.encode()).decode()
    state = {'sid': ''}

    def _probe_session() -> None:
        try:
            urllib.request.urlopen(urllib.request.Request(
                url, data=b'{}',
                headers={'Authorization': 'Basic ' + auth, 'Content-Type': 'application/json'},
            ))
            # First request unexpectedly succeeded — no session token returned.
            # That's not normal for Transmission, but we tolerate it: leave sid
            # empty; if a subsequent request needs a token we'll learn it then.
            state['sid'] = ''
        except urllib.error.HTTPError as e:
            state['sid'] = e.headers.get('X-Transmission-Session-Id', '')

    _probe_session()

    def rpc(method: str, args: dict | None = None) -> dict:
        body = json.dumps({'method': method, 'arguments': args or {}}).encode()

        def _do() -> dict:
            req = urllib.request.Request(
                url, data=body,
                headers={
                    'Authorization': 'Basic ' + auth,
                    'X-Transmission-Session-Id': state['sid'],
                    'Content-Type': 'application/json',
                },
            )
            with urllib.request.urlopen(req) as r:
                return json.loads(r.read())['arguments']

        try:
            return _do()
        except urllib.error.HTTPError as e:
            if e.code != 409:
                raise
            # Session expired or rotated; refresh and retry once.
            state['sid'] = e.headers.get('X-Transmission-Session-Id', '')
            return _do()

    return rpc


def parse_title_year(name: str) -> tuple[str | None, int | None]:
    """Parse a folder or file name into (title, year).

    Year is found first so parenthesized years like `(2025)` aren't stripped
    along with other parenthesized tag groups. If no year is found, scene
    markers (S01E01, S01-S04), bracketed tags, and quality suffixes are
    stripped from the right side; whatever's left is the title.

    Returns (None, year) only if title resolution fails completely.
    """
    name = re.sub(r'\.(mkv|mp4|avi|m4v|ts|m2ts)$', '', name, flags=re.I)
    name = name.replace('.', ' ').replace('_', ' ').replace('-', ' ')

    year_match = re.search(r'\b(19[0-9]{2}|20[0-3][0-9])\b', name)
    if year_match:
        title_part = name[: year_match.start()]
        year: int | None = int(year_match.group(1))
    else:
        year = None
        title_part = name
        title_part = re.sub(
            r'\s+S\d{1,2}(E\d{1,2})?(\s+S\d{1,2})?.*$',
            '',
            title_part,
            flags=re.I,
        )
        title_part = re.sub(r'^\s*\[[^\]]+\]\s*', '', title_part)
        title_part = re.sub(rf'\s+(?:{SCENE_KEYWORDS}).*$', '', title_part, flags=re.I)
        title_part = re.sub(r'\s*\([^)]*\)\s*$', '', title_part)

    title_part = re.sub(r'\s+', ' ', title_part)
    title = title_part.strip(' -.,([')
    return (title or None, year)
