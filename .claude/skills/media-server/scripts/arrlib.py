"""arrlib — shared utilities for the bulk-add / disk-audit scripts.

Keeps the scene-named-folder parser in one place so a fix in one tool
applies to all of them. Pure functions, no side effects, no I/O.
Test coverage lives in bulk-add_test.py and disk-audit_test.py.
"""
from __future__ import annotations

import re

# Keywords that mark the end of a title in scene-named folders.
SCENE_KEYWORDS = (
    r'1080p|2160p|720p|480p|4k|uhd|hdr|hevc|x265|x264|h\.?26[45]|'
    r'bluray|bdrip|brrip|webrip|web-?dl|web|amzn|nf|netflix|hulu|atvp|'
    r'dsnp|hmax|stan|peacock|dvdrip|hdrip|repack|proper|remux|extended|'
    r'directors\.?cut|unrated|aac\d?|dd\+?\d|dts|atmos|truehd|'
    r'complete|collection|dual|dual-audio|10\s*bit|6ch|2ch|multi|telesync'
)


def normalize(s: str) -> str:
    """Lowercase + alphanumeric-only, for fuzzy title overlap comparison."""
    return re.sub(r'[^a-z0-9]', '', s.lower())


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
