#!/usr/bin/env python3
"""disk-audit.py — honest disk accounting for the hardlink-heavy media server.

`du -sh /downloads/*` lies on this box. Files in staging (`radarr/`, `tv-sonarr/`)
are hardlinked into the library (`movies/`, `tv-shows/`). Same bytes, two
filenames, one inode. `du` charges one of them and ignores the rest, so it
*looks* like staging is enormous when in fact deleting it frees almost nothing.

This tool reports:
  - Per-category totals: du-style (what du shows) + unique (what you'd really
    reclaim if you deleted that category)
  - Orphans in staging: dirs Transmission isn't managing — pure-reclaim
    candidates (unpackerr leftovers etc.)
  - Largest unique files in the library — re-grab triage targets
  - Seeding state (cumulative across all torrents)

Pass --seed-status to also print a per-torrent verdict — whether each torrent
has met IPTorrents-style seed requirements (ratio >= 1.0 OR seeded >= 72h).
Useful as a precondition check before removing torrents from Transmission.

Read-only. No filesystem mutation, no Transmission/Sonarr/Radarr writes.
"""
import argparse
import json
import os
import re
import sys
import urllib.error
import urllib.request
from collections import defaultdict

DOWNLOADS = '/var/lib/transmission-daemon/downloads'
CATEGORIES = ['movies', 'tv-shows', 'radarr', 'tv-sonarr']

# Transmission runs in a container and reports container-relative paths
# (`/downloads/...`) via RPC. Map them to host paths.
CONTAINER_DOWNLOADS = '/downloads'


def to_host_path(p: str) -> str:
    """Convert a Transmission-reported container path to the host filesystem path."""
    if p.startswith(CONTAINER_DOWNLOADS + '/') or p == CONTAINER_DOWNLOADS:
        return DOWNLOADS + p[len(CONTAINER_DOWNLOADS) :]
    return p

# Seed-policy thresholds — IPTorrents-style: ratio >= 1.0 OR seeded >= 72h
SEED_RATIO_TARGET = 1.0
SEED_TIME_TARGET = 72 * 3600  # 72 hours


def gb(n: int) -> float:
    return n / 1024 / 1024 / 1024


def trans_rpc(method: str, args: dict | None = None) -> dict:
    URL = 'http://localhost:9091/transmission/rpc'
    sid = ''
    try:
        urllib.request.urlopen(urllib.request.Request(URL, data=b'{}', headers={'Content-Type': 'application/json'}))
    except urllib.error.HTTPError as e:
        sid = e.headers.get('X-Transmission-Session-Id', '')
    req = urllib.request.Request(
        URL,
        data=json.dumps({'method': method, 'arguments': args or {}}).encode(),
        headers={'X-Transmission-Session-Id': sid, 'Content-Type': 'application/json'},
    )
    with urllib.request.urlopen(req) as r:
        return json.loads(r.read())['arguments']


def category_of(path: str) -> str | None:
    """Return the top-level category dir (movies / tv-shows / radarr / tv-sonarr) for a path."""
    if not path.startswith(DOWNLOADS + '/'):
        return None
    top = path[len(DOWNLOADS) + 1 :].split('/', 1)[0]
    return top if top in CATEGORIES else None


def walk_inodes(root: str):
    """Yield (inode, size, path) for every regular file under root."""
    for dirpath, _, files in os.walk(root):
        for f in files:
            p = os.path.join(dirpath, f)
            try:
                st = os.lstat(p)
            except OSError:
                continue
            if not os.path.isfile(p) or os.path.islink(p):
                continue
            yield (st.st_ino, st.st_size, p)


def seed_verdict(ratio: float, seconds_seeding: int) -> str:
    """Per-torrent verdict: safe / borderline / risky.

    Safe = met either target (ratio>=1.0 OR seeded>=72h). Deletion is OK by
    IPTorrents-style rules. Borderline = approaching one threshold but not
    there. Risky = far from either; deleting now hurts your ratio.
    """
    if ratio >= SEED_RATIO_TARGET or seconds_seeding >= SEED_TIME_TARGET:
        return 'safe'
    if ratio >= SEED_RATIO_TARGET * 0.5 or seconds_seeding >= SEED_TIME_TARGET * 0.5:
        return 'borderline'
    return 'risky'


def hours(seconds: int) -> str:
    h = seconds / 3600
    if h < 1:
        return f'{int(seconds / 60)}m'
    if h < 100:
        return f'{h:.1f}h'
    return f'{int(h / 24)}d'


def print_seed_status(paths_per_inode: dict[int, list[str]]) -> None:
    """Per-torrent seed verdict, sorted worst-first."""
    print()
    print('=== Per-torrent seed status ===')
    print('  Targets: ratio >= 1.0 OR seeded >= 72h. Either bar = safe to delete.')
    print()
    try:
        torrents = trans_rpc(
            'torrent-get',
            {
                'fields': [
                    'id',
                    'name',
                    'downloadDir',
                    'uploadRatio',
                    'secondsSeeding',
                    'totalSize',
                    'percentDone',
                ]
            },
        )['torrents']
    except Exception as e:
        print(f'  could not reach Transmission: {e}')
        return

    # Annotate each torrent with verdict + unique bytes (re-using inode map)
    annotated: list[tuple[str, float, int, int, str, str]] = []
    for t in torrents:
        if t['percentDone'] < 1.0:
            continue  # still downloading; skip
        verdict = seed_verdict(t['uploadRatio'], t['secondsSeeding'])
        # Unique-bytes-for-this-torrent: walk torrent dir and count inodes
        # whose other hardlinks are all inside this torrent dir
        torrent_path = to_host_path(os.path.join(t['downloadDir'], t['name']))
        unique = 0
        if os.path.exists(torrent_path):
            for ino, size, p in walk_inodes(torrent_path):
                others = [pp for pp in paths_per_inode[ino] if pp != p]
                if all(pp.startswith(torrent_path.rstrip('/') + '/') for pp in others):
                    unique += size
        elif os.path.isfile(torrent_path):
            try:
                st = os.lstat(torrent_path)
                if st.st_nlink == 1:
                    unique = st.st_size
            except OSError:
                pass
        annotated.append((verdict, t['uploadRatio'], t['secondsSeeding'], unique, t['name'], t['downloadDir']))

    # Sort: risky → borderline → safe; within each, by ratio ascending
    verdict_order = {'risky': 0, 'borderline': 1, 'safe': 2}
    annotated.sort(key=lambda x: (verdict_order[x[0]], x[1]))

    icons = {'safe': '✓', 'borderline': '~', 'risky': '✗'}
    counts = {'safe': 0, 'borderline': 0, 'risky': 0}
    print(f'  {"":<3} {"ratio":>6}  {"seeded":>8}  {"unique":>7}  name')
    print(f'  {"-" * 3} {"-" * 6}  {"-" * 8}  {"-" * 7}  {"-" * 40}')
    for verdict, ratio, secs, unique, name, _ in annotated:
        counts[verdict] += 1
        ratio_str = f'{ratio:.2f}' if ratio < 100 else '>99'
        print(f'  {icons[verdict]:<3} {ratio_str:>6}  {hours(secs):>8}  {gb(unique):>6.2f}G  {name[:70]}')
    print()
    print(f'  Verdict tally: safe={counts["safe"]}  borderline={counts["borderline"]}  risky={counts["risky"]}')
    print('  Removing a "safe" torrent: no ratio damage.')
    print('  Removing "borderline" or "risky": will hurt your standing — only do it if you have to.')


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__.split('\n', 1)[0])
    parser.add_argument(
        '--seed-status',
        action='store_true',
        help='Also print a per-torrent seed-policy verdict (ratio + seed-time vs IPTorrents-style targets)',
    )
    args = parser.parse_args()

    print(f'Scanning {DOWNLOADS} ...')

    # inode -> [paths]
    paths_per_inode: dict[int, list[str]] = defaultdict(list)
    inode_size: dict[int, int] = {}
    for ino, size, path in walk_inodes(DOWNLOADS):
        paths_per_inode[ino].append(path)
        inode_size[ino] = size

    # Per-category accounting
    stats = {c: {'total': 0, 'unique_to_cat': 0} for c in CATEGORIES}
    for ino, paths in paths_per_inode.items():
        size = inode_size[ino]
        cats_for_inode = {category_of(p) for p in paths}
        cats_for_inode.discard(None)
        if not cats_for_inode:
            continue
        for c in cats_for_inode:
            stats[c]['total'] += size
        if len(cats_for_inode) == 1:
            (c,) = cats_for_inode
            stats[c]['unique_to_cat'] += size

    print()
    print('=== Per-category bytes ===')
    print(f'  {"category":<11}  {"du-style":>10}  {"unique":>10}  {"shared":>10}')
    print(f'  {"-" * 11}  {"-" * 10}  {"-" * 10}  {"-" * 10}')
    for c in CATEGORIES:
        s = stats[c]
        shared = s['total'] - s['unique_to_cat']
        print(f'  {c:<11}  {gb(s["total"]):>9.1f}G  {gb(s["unique_to_cat"]):>9.1f}G  {gb(shared):>9.1f}G')
    print()
    print('  "unique" = bytes that exist ONLY in this category. Deleting the')
    print('  category would reclaim that much. "shared" = bytes also hardlinked')
    print('  into another category (deleting one side reclaims nothing).')

    # Orphans in staging
    print()
    print('=== Orphans in staging (not held by any active Transmission torrent) ===')
    try:
        torrents = trans_rpc('torrent-get', {'fields': ['name', 'downloadDir']})['torrents']
        held = {os.path.join(t['downloadDir'], t['name']) for t in torrents}
        held_names_per_dir: dict[str, set[str]] = defaultdict(set)
        for t in torrents:
            held_names_per_dir[t['downloadDir']].add(t['name'])
    except Exception as e:
        print(f'  could not reach Transmission: {e}')
        held = set()
        held_names_per_dir = {}

    orphan_total_unique = 0
    for cat in ('radarr', 'tv-sonarr'):
        cat_path = os.path.join(DOWNLOADS, cat)
        if not os.path.isdir(cat_path):
            continue
        for entry in sorted(os.listdir(cat_path)):
            full = os.path.join(cat_path, entry)
            if entry in held_names_per_dir.get(cat_path, set()):
                continue
            # Compute unique-to-this-folder bytes
            unique_bytes = 0
            for ino, size, p in walk_inodes(full):
                # Unique if every other hardlink for this inode is also inside `full`
                other_paths = [pp for pp in paths_per_inode[ino] if pp != p]
                if all(pp.startswith(full + '/') for pp in other_paths):
                    unique_bytes += size
            orphan_total_unique += unique_bytes
            print(f'  {cat:<10}  {gb(unique_bytes):>6.2f}G unique  {entry[:80]}')
    print(f'\n  Total orphan reclaim: {gb(orphan_total_unique):.1f}G')

    # Build inode -> torrent map so we can attribute each library file
    # to its seeding torrent (if any) and surface ratio/verdict alongside size.
    torrent_by_inode: dict[int, dict] = {}
    try:
        completed = [
            t for t in trans_rpc(
                'torrent-get',
                {'fields': ['name', 'downloadDir', 'uploadRatio', 'secondsSeeding', 'percentDone']},
            )['torrents']
            if t['percentDone'] >= 1.0
        ]
        for t in completed:
            tpath = to_host_path(os.path.join(t['downloadDir'], t['name']))
            if os.path.isfile(tpath):
                try:
                    torrent_by_inode[os.lstat(tpath).st_ino] = t
                except OSError:
                    pass
            elif os.path.isdir(tpath):
                for ino, _, _ in walk_inodes(tpath):
                    torrent_by_inode[ino] = t
    except Exception:
        pass

    # Largest unique-to-library files (re-grab / delete-for-disk triage)
    print()
    print('=== Largest unique files in library (top 20 by size) ===')
    print('  ratio/seeded = the torrent backing this file; "-" = no torrent (orphan or manual add)')
    print()
    print(f'  {"size":>7}  {"":<3} {"ratio":>6}  {"seeded":>8}  path')
    print(f'  {"-" * 7}  {"-" * 3} {"-" * 6}  {"-" * 8}  {"-" * 50}')
    icons = {'safe': '✓', 'borderline': '~', 'risky': '✗'}
    library_files: list[tuple[int, int, str]] = []
    for ino, paths in paths_per_inode.items():
        if any(category_of(p) in ('movies', 'tv-shows') for p in paths):
            display = next((p for p in paths if category_of(p) in ('movies', 'tv-shows')), paths[0])
            library_files.append((inode_size[ino], ino, display))
    library_files.sort(reverse=True)
    for size, ino, path in library_files[:20]:
        short = path[len(DOWNLOADS) + 1 :]
        t = torrent_by_inode.get(ino)
        if t:
            verdict = seed_verdict(t['uploadRatio'], t['secondsSeeding'])
            icon = icons[verdict]
            ratio_str = f'{t["uploadRatio"]:.2f}' if t['uploadRatio'] < 100 else '>99'
            seeded = hours(t['secondsSeeding'])
        else:
            icon = '?'
            ratio_str = '-'
            seeded = '-'
        print(f'  {gb(size):>6.2f}G  {icon:<3} {ratio_str:>6}  {seeded:>8}  {short[:60]}')

    # Seeding state
    print()
    print('=== Seeding state ===')
    try:
        s = trans_rpc('session-stats')
        cum = s.get('cumulative-stats', s)
        up = cum.get('uploadedBytes', 0)
        down = cum.get('downloadedBytes', 1) or 1
        print(f'  active torrents : {s.get("torrentCount")}')
        print(f'  cumulative up   : {gb(up):.0f}G')
        print(f'  cumulative down : {gb(down):.0f}G')
        print(f'  cumulative ratio: {up / down:.2f}')
    except Exception as e:
        print(f'  could not reach Transmission: {e}')

    if args.seed_status:
        print_seed_status(paths_per_inode)


if __name__ == '__main__':
    main()
