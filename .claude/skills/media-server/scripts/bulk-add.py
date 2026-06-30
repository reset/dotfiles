#!/usr/bin/env python3
"""
bulk-add.py — Add movies or TV shows to Radarr/Sonarr from scene-named
folders, with validation to avoid phantom adds.

Usage:
  python3 /opt/arr/bulk-add.py movies          # add new movies in /downloads/movies
  python3 /opt/arr/bulk-add.py tv              # add new shows in /downloads/tv-shows
  python3 /opt/arr/bulk-add.py movies --dry-run

Design notes (lessons learned, encoded in tests at bulk-add_test.py):
  - The parser must strip season/episode markers (S01E01, S01-S04), bracket
    scene tags ([apopleptc]), and quality suffixes — otherwise the search
    term passed to Radarr/Sonarr is garbage and lookup returns garbage.
  - When the folder name contains a year, the script ONLY accepts a lookup
    result whose year matches. Otherwise it bails on that folder.
  - Without a year, the script requires the lookup result's title to
    overlap (substring) with the parsed title. Sonarr's fuzzy match
    happily returns unrelated shows; we don't trust it.
  - Folders containing a `.ignore` file (Jellyfin convention) are skipped.
  - Within a single run, dedup by ID — N season folders for one series
    should add the series once, not N times.
"""

import json
import os
import re
import sys
import time
import urllib.error
import urllib.parse
import urllib.request

# Parser and normalize live in arrlib so disk-audit.py can use the same logic.
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from arrlib import normalize, parse_title_year  # noqa: E402


def best_match(parsed_title: str, parsed_year: int | None, results: list[dict]) -> dict | None:
    """Pick the lookup result that confidently matches the parsed folder.

    Rules:
      - If parsed_year is set, require a result with that year. Otherwise bail.
      - If no parsed year, require title overlap between the parsed title and a
        result's title (substring match in either direction).
      - Among multiple overlapping results, prefer the one with the LONGEST
        overlapping title. This breaks ambiguity like a "Dragon Ball Z Kai"
        folder matching both "Dragon Ball Z" and "Dragon Ball Z Kai" — the
        longer match wins because it's more specific.
      - Returns None when no result confidently matches; the caller skips
        the folder rather than blindly adding the wrong thing.
    """
    if not results:
        return None
    pt_norm = normalize(parsed_title)
    if parsed_year is not None:
        candidates = [r for r in results if r.get('year') == parsed_year]
        if not candidates:
            return None
    else:
        candidates = results

    # Filter to overlap-matched candidates, sorted by closeness to parsed title
    # (smallest length difference first). Closeness beats longer-is-better:
    # `Beavis and Butt-Head` (len-equal) beats `Mike Judge's Beavis and Butt-Head`
    # for a folder named "Beavis and Butthead", while `Dragon Ball Z Kai` (14 chars)
    # still beats `Dragon Ball Z` (11 chars) for a folder named "Dragonball Z Kai"
    # because both must contain the full normalized parsed title before either
    # is considered, and the Kai variant is closer to the parsed length.
    overlapping: list[tuple[int, dict]] = []
    for r in candidates:
        rn = normalize(r['title'])
        if not rn:
            continue
        if rn in pt_norm or pt_norm in rn:
            overlapping.append((abs(len(rn) - len(pt_norm)), r))
    if overlapping:
        overlapping.sort(key=lambda x: x[0])
        return overlapping[0][1]

    # Year-matched but no title overlap: trust the year as a strong signal.
    if parsed_year is not None:
        return candidates[0]
    return None


def should_skip_folder(folder_path: str) -> bool:
    """Honor the Jellyfin .ignore convention: skip any folder that contains one."""
    return os.path.exists(os.path.join(folder_path, '.ignore'))


def _api(base: str, key: str, method: str, path: str, data: dict | None = None):
    url = f'{base}{path}'
    body = json.dumps(data).encode() if data else None
    req = urllib.request.Request(
        url, data=body,
        headers={'X-Api-Key': key, 'Content-Type': 'application/json'},
        method=method,
    )
    try:
        with urllib.request.urlopen(req) as r:
            return json.loads(r.read()), None
    except urllib.error.HTTPError as e:
        return None, e.read().decode()[:300]


def _run() -> None:
    DRY_RUN = '--dry-run' in sys.argv
    MODE = next((a for a in sys.argv[1:] if not a.startswith('--')), None)

    if MODE not in ('movies', 'tv'):
        print('Usage: bulk-add.py [movies|tv] [--dry-run]')
        sys.exit(1)

    if MODE == 'movies':
        key = re.search(r'<ApiKey>([^<]+)', open('/opt/arr/radarr/config.xml').read()).group(1)
        base = 'http://localhost:7878/api/v3'
        root_dir = '/var/lib/transmission-daemon/downloads/movies'
        cnt_dir = '/downloads/movies'
        lookup = '/movie/lookup'
        add_path = '/movie'
        id_field = 'tmdbId'
    else:
        key = re.search(r'<ApiKey>([^<]+)', open('/opt/arr/sonarr/config.xml').read()).group(1)
        base = 'http://localhost:8989/api/v3'
        root_dir = '/var/lib/transmission-daemon/downloads/tv-shows'
        cnt_dir = '/downloads/tv-shows'
        lookup = '/series/lookup'
        add_path = '/series'
        id_field = 'tvdbId'

    existing, _ = _api(base, key, 'GET', add_path)
    existing_ids = {item[id_field] for item in (existing or [])}
    print(f'Library has {len(existing_ids)} existing entries')

    seen_ids: set[int] = set()
    added: list[str] = []
    skipped: list[str] = []
    skipped_ignored: list[str] = []
    rejected: list[tuple[str, str]] = []
    failed: list[tuple[str, str]] = []

    for entry in sorted(os.listdir(root_dir)):
        folder = os.path.join(root_dir, entry)
        if should_skip_folder(folder):
            skipped_ignored.append(entry)
            continue

        title, year = parse_title_year(entry)
        if not title:
            rejected.append((entry, 'unparseable name'))
            continue

        term = f'{title} {year}' if year else title
        results, err = _api(base, key, 'GET', f'{lookup}?term={urllib.parse.quote(term)}')
        if err or not results:
            failed.append((entry, 'lookup failed'))
            continue

        hit = best_match(title, year, results)
        if hit is None:
            rejected.append((entry, f'no confident match (parsed: {title!r} year={year})'))
            continue

        if hit[id_field] in existing_ids:
            skipped.append(hit['title'])
            continue
        if hit[id_field] in seen_ids:
            # Already queued earlier in this run from a sibling folder.
            continue
        seen_ids.add(hit[id_field])

        if DRY_RUN:
            print(f'  WOULD ADD: {hit["title"]} ({hit.get("year", "")}) — from "{entry}"')
            added.append(hit['title'])
            continue

        payload = {
            'title': hit['title'],
            id_field: hit[id_field],
            'qualityProfileId': 1,
            'rootFolderPath': cnt_dir,
            'monitored': True,
        }
        if MODE == 'movies':
            payload['addOptions'] = {'searchForMovie': False}
        else:
            payload['addOptions'] = {'searchForMissingEpisodes': False, 'monitor': 'all'}
        resp, err = _api(base, key, 'POST', add_path, payload)
        if err and ('409' in err or 'already' in err.lower()):
            skipped.append(hit['title'])
        elif err:
            failed.append((entry, err[:80]))
        else:
            added.append(f'{resp["title"]} ({resp.get("year", "")})')
        time.sleep(0.25)

    print()
    print(f'Added           : {len(added)}')
    print(f'Skipped         : {len(skipped)} (already in library)')
    print(f'Skipped (.ignore): {len(skipped_ignored)}')
    print(f'Rejected        : {len(rejected)} (no confident match)')
    print(f'Failed          : {len(failed)}')
    for t in added:
        print(f'  + {t}')
    for e, r in rejected:
        print(f'  - {e}: {r}')
    for e, r in failed:
        print(f'  ! {e}: {r}')

    if added and not DRY_RUN:
        scan_cmd = 'DownloadedMoviesScan' if MODE == 'movies' else 'DownloadedEpisodesScan'
        resp, _ = _api(base, key, 'POST', '/command', {'name': scan_cmd, 'path': cnt_dir})
        print(f"\nScan command {resp.get('id') if resp else '?'}: {resp.get('status') if resp else 'failed'}")


if __name__ == '__main__':
    _run()
