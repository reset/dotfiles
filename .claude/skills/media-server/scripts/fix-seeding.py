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

import urllib.request, urllib.error, json, os, sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from arrlib import to_host_path  # noqa: E402

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

# ── Transmission auth ────────────────────────────────────────────────
handler = urllib.request.HTTPPasswordMgrWithDefaultRealm()
handler.add_password(None, TRANSMISSION_URL, TRANSMISSION_USER, TRANSMISSION_PASS)
opener = urllib.request.build_opener(urllib.request.HTTPBasicAuthHandler(handler))

try:
    opener.open(TRANSMISSION_URL)
except urllib.error.HTTPError as e:
    session_id = e.headers.get('X-Transmission-Session-Id', '')

def rpc(method, args=None):
    req = urllib.request.Request(TRANSMISSION_URL,
        data=json.dumps({'method': method, 'arguments': args or {}}).encode(),
        headers={'X-Transmission-Session-Id': session_id, 'Content-Type': 'application/json'})
    with opener.open(req) as r:
        return json.loads(r.read())['arguments']

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
data = rpc('torrent-get', {'fields': ['name', 'downloadDir', 'files', 'percentDone']})
torrents = [t for t in data['torrents'] if t.get('percentDone', 0) >= 1.0]

missing = []
for t in torrents:
    dl = to_host_path(t['downloadDir'].rstrip('/'))
    for f in t['files']:
        if not f['name'].lower().endswith(VIDEO_EXTS):
            continue  # don't try to restore .nfo / sample / screenshot files
        path = os.path.join(dl, f['name'])
        if not os.path.exists(path):
            missing.append((t['name'], path, os.path.basename(f['name'])))

print(f'  {len(missing)} missing video files across {len(torrents)} completed torrents')

if not missing:
    print('\nAll torrent files present — seeding is healthy.')
    sys.exit(0)

# ── Repair ───────────────────────────────────────────────────────────
linked = not_found = errors = 0
print(f'\n{"DRY RUN — " if DRY_RUN else ""}Repairing...')

for torrent_name, orig_path, fname in missing:
    candidates = index.get(fname.lower(), [])
    if not candidates:
        not_found += 1
        print(f'  NOT FOUND: {fname}  (torrent: {torrent_name[:40]})')
        continue
    src = candidates[0]
    orig_dir = os.path.dirname(orig_path)
    if DRY_RUN:
        print(f'  WOULD LINK: {src}\n          -> {orig_path}')
        linked += 1
        continue
    try:
        os.makedirs(orig_dir, exist_ok=True)
        os.link(src, orig_path)
        linked += 1
    except Exception as e:
        errors += 1
        print(f'  ERROR: {fname}: {e}')

print(f'\nResults:')
print(f'  {"Would link" if DRY_RUN else "Linked"}: {linked}')
print(f'  Not found:  {not_found}')
print(f'  Errors:     {errors}')
