#!/usr/bin/env python3
"""
fix-seeding.py — Repair Transmission seeding after Sonarr/Radarr moves files.

Finds all torrent files that are missing from their expected paths, searches
the Sonarr/Radarr-managed directories for a matching filename, and creates
a hardlink at the original path so Transmission can resume seeding.

Usage:
  python3 /opt/arr/fix-seeding.py          # live run
  python3 /opt/arr/fix-seeding.py --dry-run # preview only
"""

import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from arrlib import make_trans_rpc, to_host_path  # noqa: E402

DRY_RUN = '--dry-run' in sys.argv

TRANSMISSION_URL  = 'http://localhost:9091/transmission/rpc'
TRANSMISSION_USER = 'transmission'
TRANSMISSION_PASS = os.environ.get('TRANSMISSION_PASS', '')
if not TRANSMISSION_PASS:
    sys.exit('TRANSMISSION_PASS env var not set; see ~/.claude/skills/media-server/skill.md for setup')

# Video extensions — only these are worth restoring; we don't care about
# resurrecting .nfo / .png / sample files.
VIDEO_EXTS = ('.mkv', '.mp4', '.avi', '.m4v', '.ts', '.m2ts')
SEARCH_ROOTS = [
    '/var/lib/transmission-daemon/downloads/tv-shows',
    '/var/lib/transmission-daemon/downloads/movies',
]

# ── Transmission RPC (handles session token + 409 refresh) ──────────
rpc = make_trans_rpc(TRANSMISSION_URL, TRANSMISSION_USER, TRANSMISSION_PASS)

# ── Build filename index from managed dirs ───────────────────────────
print('Indexing managed directories...')
index = {}
for root in SEARCH_ROOTS:
    for dirpath, dirs, files in os.walk(root):
        for fname in files:
            index.setdefault(fname.lower(), []).append(os.path.join(dirpath, fname))
print(f'  {len(index)} unique filenames indexed')

# ── Find missing torrent files ───────────────────────────────────────
# Only consider completed torrents — an in-progress download has files
# that legitimately don't exist yet on disk, and "repairing" them via
# hardlink from a same-named file in the library would corrupt the seed.
print('Scanning Transmission torrents...')
data = rpc('torrent-get', {'fields': ['id', 'name', 'downloadDir', 'files', 'percentDone']})
torrents = [t for t in data['torrents'] if t.get('percentDone', 0) >= 1.0]

missing = []  # (torrent_id, torrent_name, expected_path, expected_size, filename)
for t in torrents:
    dl = to_host_path(t['downloadDir'].rstrip('/'))
    for f in t['files']:
        if not f['name'].lower().endswith(VIDEO_EXTS):
            continue  # don't try to restore .nfo / sample / screenshot files
        path = os.path.join(dl, f['name'])
        if not os.path.exists(path):
            missing.append((t['id'], t['name'], path, f.get('length', 0), os.path.basename(f['name'])))

print(f'  {len(missing)} missing video files across {len(torrents)} completed torrents')

if not missing:
    print('\nAll torrent files present — seeding is healthy.')
    sys.exit(0)

# ── Repair ───────────────────────────────────────────────────────────
linked = not_found = errors = ambiguous = 0
restored_torrent_ids: set[int] = set()
print(f'\n{"DRY RUN — " if DRY_RUN else ""}Repairing...')

for torrent_id, torrent_name, orig_path, expected_size, fname in missing:
    candidates = index.get(fname.lower(), [])
    if not candidates:
        not_found += 1
        print(f'  NOT FOUND: {fname}  (torrent: {torrent_name[:40]})')
        continue
    # Pick the candidate whose size matches the expected file size (within 1%
    # tolerance to allow for filesystem-reported variance). This avoids
    # accidentally hardlinking a different-quality rip with the same scene
    # filename — same name, different bytes, would fail Transmission's hash
    # check anyway, but worse: it would deceive an operator reading "Linked".
    sized = []
    for c in candidates:
        try:
            sz = os.path.getsize(c)
        except OSError:
            continue
        if expected_size and abs(sz - expected_size) / max(expected_size, 1) <= 0.01:
            sized.append((sz, c))
    if sized:
        src = sized[0][1]
    elif len(candidates) == 1:
        src = candidates[0]  # only choice; trust it even without size confirmation
    else:
        # Multiple candidates, none size-matching. Don't guess.
        ambiguous += 1
        print(f'  AMBIGUOUS: {fname} — {len(candidates)} same-name files, none size-match {expected_size}B')
        continue
    orig_dir = os.path.dirname(orig_path)
    if DRY_RUN:
        print(f'  WOULD LINK: {src}\n          -> {orig_path}')
        linked += 1
        continue
    try:
        os.makedirs(orig_dir, exist_ok=True)
        os.link(src, orig_path)
        linked += 1
        restored_torrent_ids.add(torrent_id)
    except Exception as e:
        errors += 1
        print(f'  ERROR: {fname}: {e}')

# ── Re-verify torrents whose files were restored ─────────────────────
# Hardlinking puts the bytes back, but Transmission still thinks the
# torrent is errored/incomplete until verification. Stop+start is the
# documented (skill notes) way to clear "missing file" error state.
if restored_torrent_ids and not DRY_RUN:
    import time
    ids = list(restored_torrent_ids)
    print(f'\nRe-verifying {len(ids)} torrents (stop+start)...')
    rpc('torrent-stop', {'ids': ids})
    time.sleep(2)
    rpc('torrent-verify', {'ids': ids})
    rpc('torrent-start', {'ids': ids})

print(f'\nResults:')
print(f'  {"Would link" if DRY_RUN else "Linked"}: {linked}')
print(f'  Ambiguous:  {ambiguous}')
print(f'  Not found:  {not_found}')
print(f'  Errors:     {errors}')
