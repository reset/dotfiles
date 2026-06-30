#!/usr/bin/env python3
"""
bulk-add.py — Add movies or TV shows to Radarr/Sonarr from a directory of
scene-named folders, then trigger a manual import.

Usage:
  python3 /opt/arr/bulk-add.py movies   # add everything in /downloads/movies
  python3 /opt/arr/bulk-add.py tv       # add everything in /downloads/tv-shows
  python3 /opt/arr/bulk-add.py movies --dry-run
"""

import urllib.request, urllib.parse, json, os, re, sys, time

DRY_RUN = '--dry-run' in sys.argv
MODE    = next((a for a in sys.argv[1:] if not a.startswith('--')), None)

if MODE not in ('movies', 'tv'):
    print('Usage: bulk-add.py [movies|tv] [--dry-run]')
    sys.exit(1)

if MODE == 'movies':
    API_KEY  = open('/opt/arr/radarr/config.xml').read()
    API_KEY  = re.search(r'<ApiKey>([^<]+)', API_KEY).group(1)
    BASE     = 'http://localhost:7878/api/v3'
    ROOT_DIR = '/var/lib/transmission-daemon/downloads/movies'
    CNT_DIR  = '/downloads/movies'
    LOOKUP   = '/movie/lookup'
    ADD      = '/movie'
    IMPORT   = 'ManualImport'
    ID_FIELD = 'tmdbId'
    ITEM_KEY = 'movie'
else:
    API_KEY  = open('/opt/arr/sonarr/config.xml').read()
    API_KEY  = re.search(r'<ApiKey>([^<]+)', API_KEY).group(1)
    BASE     = 'http://localhost:8989/api/v3'
    ROOT_DIR = '/var/lib/transmission-daemon/downloads/tv-shows'
    CNT_DIR  = '/downloads/tv-shows'
    LOOKUP   = '/series/lookup'
    ADD      = '/series'
    IMPORT   = 'ManualImport'
    ID_FIELD = 'tvdbId'
    ITEM_KEY = 'series'

def api(method, path, data=None):
    url = f'{BASE}{path}'
    body = json.dumps(data).encode() if data else None
    req = urllib.request.Request(url, data=body, headers={
        'X-Api-Key': API_KEY, 'Content-Type': 'application/json'
    }, method=method)
    try:
        with urllib.request.urlopen(req) as r:
            return json.loads(r.read()), None
    except urllib.error.HTTPError as e:
        return None, e.read().decode()[:300]

def parse_title_year(name):
    name = re.sub(r'\.(mkv|mp4|avi|m4v)$', '', name, flags=re.I)
    name = name.replace('.', ' ')
    m = re.search(r'\b(19[0-9]{2}|20[0-3][0-9])\b', name)
    if m:
        year  = int(m.group(1))
        title = re.sub(r'\s+(1080p|720p|2160p|bluray|web|hevc|x265|x264|bdrip|hdrip|amzn|remux).*$',
                       '', name[:m.start()], flags=re.I).strip()
        return title, year
    title = re.sub(r'\s+(1080p|720p|collection|bluray|web).*$', '', name, flags=re.I).strip()
    return title, None

# ── Get existing library to skip duplicates ──────────────────────────
existing, _ = api('GET', ADD)
existing_ids = {item[ID_FIELD] for item in (existing or [])}
print(f'Library has {len(existing_ids)} existing entries')

# ── Process each folder ──────────────────────────────────────────────
entries   = sorted(os.listdir(ROOT_DIR))
added     = []
skipped   = []
failed    = []

for entry in entries:
    title, year = parse_title_year(entry)
    if not title:
        continue
    term    = f'{title} {year}' if year else title
    results, err = api('GET', f'{LOOKUP}?term={urllib.parse.quote(term)}')
    if err or not results:
        failed.append((entry, f'lookup failed'))
        continue
    hit = results[0]
    if hit[ID_FIELD] in existing_ids:
        skipped.append(hit['title'])
        continue
    if DRY_RUN:
        print(f'  WOULD ADD: {hit["title"]} ({hit.get("year","")})')
        added.append(hit['title'])
        continue
    payload = {'title': hit['title'], ID_FIELD: hit[ID_FIELD],
               'qualityProfileId': 1, 'rootFolderPath': CNT_DIR,
               'monitored': True}
    if MODE == 'movies':
        payload['addOptions'] = {'searchForMovie': False}
    else:
        payload['addOptions'] = {'searchForMissingEpisodes': False, 'monitor': 'all'}
    resp, err = api('POST', ADD, payload)
    if err and ('409' in err or 'already' in err.lower()):
        skipped.append(hit['title'])
    elif err:
        failed.append((entry, err[:80]))
    else:
        added.append(f'{resp["title"]} ({resp.get("year","")})')
    time.sleep(0.25)

print(f'\nAdded:   {len(added)}')
print(f'Skipped: {len(skipped)} (already in library)')
print(f'Failed:  {len(failed)}')
for t in added:   print(f'  + {t}')
for e, r in failed: print(f'  ! {e}: {r}')

if added and not DRY_RUN:
    print(f'\nTriggering import scan...')
    resp, _ = api('POST', '/command', {
        'name': IMPORT, 'importMode': 'copy',
        'files': []  # empty = Radarr/Sonarr will scan root
    })
    # For a real scan use DownloadedMoviesScan / DownloadedEpisodesScan
    scan_cmd = 'DownloadedMoviesScan' if MODE == 'movies' else 'DownloadedEpisodesScan'
    resp, _ = api('POST', '/command', {'name': scan_cmd, 'path': CNT_DIR})
    print(f'  Scan command {resp.get("id")}: {resp.get("status")}')
