---
name: media-server
description: Manage Jamie's home media server — the *arr stack (Sonarr, Radarr, Prowlarr) + Transmission + Plex running at 192.168.1.28. Use whenever the user wants to add/import shows or movies, fix health checks, check seeding, manage Plex libraries, debug download issues, or do anything involving the home server media setup. Triggers on "add a show", "import movies", "Sonarr", "Radarr", "Plex", "Transmission", "media server", "download a movie", "health check", "seeding broken", "*arr".
---

# Media Server

Jamie's home media server at `reset@192.168.1.28`. SSH access is available directly from this machine.

## Stack overview

| Service | Type | Port | Config path |
|---------|------|------|-------------|
| Transmission | Native systemd service | 9091 | `/etc/transmission-daemon/settings.json` |
| Sonarr | Docker (linuxserver/sonarr) | 8989 | `/opt/arr/sonarr/` |
| Radarr | Docker (linuxserver/radarr) | 7878 | `/opt/arr/radarr/` |
| Prowlarr | Docker (linuxserver/prowlarr) | 9696 | `/opt/arr/prowlarr/` |
| Plex | Native systemd service | 32400 | `/var/lib/plexmediaserver/` |

## Key paths

| Path | What it is |
|------|-----------|
| `/var/lib/transmission-daemon/downloads/` | All downloads root |
| `/var/lib/transmission-daemon/downloads/tv-shows/` | TV downloads + Sonarr library root |
| `/var/lib/transmission-daemon/downloads/movies/` | Movie downloads + Radarr library root |
| `/var/lib/transmission-daemon/downloads/tv-sonarr/` | Sonarr's active download category dir |
| `/var/lib/transmission-daemon/downloads/radarr/` | Radarr's active download category dir |

Note: the download directory and library root are the **same directory** for both Sonarr and Radarr. Plex is pointed at these same paths.

## Container path mapping

Sonarr/Radarr/Prowlarr run in Docker. Their containers see `/downloads/` for what the host has at `/var/lib/transmission-daemon/downloads/`. A remote path mapping is configured in both Sonarr and Radarr:

- Remote (Transmission reports): `/var/lib/transmission-daemon/downloads/`
- Local (container sees): `/downloads/`

Scripts that SSH into the host must use host paths (`/var/lib/transmission-daemon/downloads/...`) for filesystem ops, but pass container paths (`/downloads/...`) to the Sonarr/Radarr APIs.

## Getting API keys

Never hardcode API keys — read them live from the config files:

```bash
# Sonarr
ssh reset@192.168.1.28 "grep -oP '(?<=<ApiKey>)[^<]+' /opt/arr/sonarr/config.xml"

# Radarr
ssh reset@192.168.1.28 "grep -oP '(?<=<ApiKey>)[^<]+' /opt/arr/radarr/config.xml"

# Prowlarr
ssh reset@192.168.1.28 "grep -oP '(?<=<ApiKey>)[^<]+' /opt/arr/prowlarr/config.xml"

# Plex token
ssh reset@192.168.1.28 "sudo grep -oP 'PlexOnlineToken=\"\K[^\"]+' '/var/lib/plexmediaserver/Library/Application Support/Plex Media Server/Preferences.xml'"

# Transmission credentials (in settings.json, or just use: transmission / media-server)
```

## Common API patterns

All *arr APIs follow the same shape. Substitute host/port/key as needed.

```bash
# Health check
curl -sf http://192.168.1.28:8989/api/v3/health -H 'X-Api-Key: KEY'

# List series/movies
curl -sf http://192.168.1.28:8989/api/v3/series -H 'X-Api-Key: KEY'
curl -sf http://192.168.1.28:7878/api/v3/movie  -H 'X-Api-Key: KEY'

# Search/lookup before adding
curl -sf "http://192.168.1.28:8989/api/v3/series/lookup?term=TITLE" -H 'X-Api-Key: KEY'
curl -sf "http://192.168.1.28:7878/api/v3/movie/lookup?term=TITLE"  -H 'X-Api-Key: KEY'

# Add a series (POST)
curl -sf -X POST http://192.168.1.28:8989/api/v3/series \
  -H 'X-Api-Key: KEY' -H 'Content-Type: application/json' \
  -d '{"title":"...","tvdbId":12345,"qualityProfileId":1,"rootFolderPath":"/downloads/tv-shows","monitored":true,"addOptions":{"searchForMissingEpisodes":false,"monitor":"all"}}'

# Trigger a downloaded scan
curl -sf -X POST http://192.168.1.28:8989/api/v3/command \
  -H 'X-Api-Key: KEY' -H 'Content-Type: application/json' \
  -d '{"name":"DownloadedEpisodesScan","path":"/downloads/tv-shows"}'

# Manual import (preferred over DownloadedEpisodesScan for batch work)
curl -sf -X POST http://192.168.1.28:8989/api/v3/command \
  -H 'X-Api-Key: KEY' -H 'Content-Type: application/json' \
  -d '{"name":"ManualImport","files":[...],"importMode":"copy"}'
```

## Transmission API

Transmission requires a two-step auth: first request returns a session ID header, second request uses it.

```python
import urllib.request, json

URL = 'http://192.168.1.28:9091/transmission/rpc'
USER, PASS = 'transmission', 'media-server'

handler = urllib.request.HTTPPasswordMgrWithDefaultRealm()
handler.add_password(None, URL, USER, PASS)
opener = urllib.request.build_opener(urllib.request.HTTPBasicAuthHandler(handler))

try:
    opener.open(URL)
except urllib.error.HTTPError as e:
    session_id = e.headers.get('X-Transmission-Session-Id', '')

def rpc(method, args=None):
    req = urllib.request.Request(URL,
        data=json.dumps({'method': method, 'arguments': args or {}}).encode(),
        headers={'X-Transmission-Session-Id': session_id, 'Content-Type': 'application/json'})
    with opener.open(req) as r:
        return json.loads(r.read())['arguments']
```

## Hard-won lessons

### Hardlink vs move — the seeding trap
`copyUsingHardlinks: true` is set in both Sonarr and Radarr, but **`importMode: 'auto'` in ManualImport API calls does a move on same-filesystem**, not a hardlink. Always use `importMode: 'copy'` in ManualImport API payloads — this respects `copyUsingHardlinks` and leaves the original torrent files intact for seeding.

Automatic imports (triggered by Transmission completing a download) correctly hardlink.

### Manual import: use container paths in the API, host paths for filesystem
When building a ManualImport payload programmatically:
- File `path` fields in the API payload → **container path** (`/downloads/tv-shows/...`)
- `os.listdir()`, `os.path.exists()`, `os.link()` on the host → **host path** (`/var/lib/transmission-daemon/downloads/tv-shows/...`)

### Broken seeding repair
If files get moved instead of hardlinked, use this pattern to restore seeding without touching Sonarr/Radarr:

```python
# For each Transmission torrent file that's missing from its original path,
# find it by filename in the Sonarr/Radarr-managed dirs and hardlink it back
import os
sonarr_index = {}
for dirpath, dirs, files in os.walk('/var/lib/transmission-daemon/downloads/tv-shows'):
    for fname in files:
        sonarr_index.setdefault(fname.lower(), []).append(os.path.join(dirpath, fname))

# then for each missing file:
os.makedirs(os.path.dirname(original_path), exist_ok=True)
os.link(sonarr_index[filename.lower()][0], original_path)
```

### Anime series need series type = "anime"
Anime releases use absolute episode numbering (e.g. `- 017 -`). If a series isn't matching during import, check `seriesType`. Update via PUT to `/api/v3/series/{id}` with `seriesType: "anime"`.

### ManualImport with seriesId ignores the folder param
Passing `seriesId` to `GET /api/v3/manualimport` causes Sonarr to look up the series' own configured path and ignore the `folder` parameter. If you want to scan a specific folder for a specific series, omit `seriesId` and filter/match results yourself.

### Remote path mapping is required
Without a remote path mapping, Sonarr/Radarr report "directory does not exist inside container" health errors even when the directory is correctly mounted. Mapping must be set in both apps:
- Host: `192.168.1.28`
- Remote path: `/var/lib/transmission-daemon/downloads/`
- Local path: `/downloads/`

## Plex

```bash
# List libraries (shows paths)
curl -sf "http://192.168.1.28:32400/library/sections?X-Plex-Token=TOKEN" | python3 -c "
import sys, xml.etree.ElementTree as ET
root = ET.parse(sys.stdin).getroot()
for d in root.findall('Directory'):
    print(d.get('type'), d.get('title'), [l.get('path') for l in d.findall('Location')])
"

# Trigger library scan
curl -sf -X POST "http://192.168.1.28:32400/library/sections/SECTION_ID/refresh?X-Plex-Token=TOKEN"
```

Current libraries:
- `[movie]` Movies → `/var/lib/transmission-daemon/downloads/movies`
- `[show]` TV Shows → `/var/lib/transmission-daemon/downloads/tv-shows`

## Indexer (Prowlarr → IPTorrents)

Prowlarr has IPTorrents configured and synced to both Sonarr (TV categories) and Radarr (movie categories). The apps use Docker hostname `prowlarr:9696` internally. From outside Docker use `192.168.1.28:9696`.

User-facing download guide is at `/opt/arr/media-guide.md` on the server.

## Common Operations

### Add a single movie or show

Look up, then add. Always read API keys live from config.xml (never hardcode).

```bash
# Movie — lookup first, grab tmdbId from result
RADARR_KEY=$(ssh reset@192.168.1.28 "grep -oP '(?<=<ApiKey>)[^<]+' /opt/arr/radarr/config.xml")
curl -sf "http://192.168.1.28:7878/api/v3/movie/lookup?term=Parasite" -H "X-Api-Key: $RADARR_KEY" | python3 -c "
import sys,json; movies=json.load(sys.stdin)
for m in movies[:3]: print(m['tmdbId'], m['title'], m.get('year',''))
"
# Then add with HD-1080p quality profile (id=4) — NOT "Any" (grabs BR-DISK 60GB+ discs)
curl -sf -X POST http://192.168.1.28:7878/api/v3/movie \
  -H "X-Api-Key: $RADARR_KEY" -H 'Content-Type: application/json' \
  -d '{"tmdbId":496243,"title":"Parasite","year":2019,"qualityProfileId":4,"rootFolderPath":"/downloads/movies","monitored":true,"addOptions":{"searchForMovie":true}}'
```

```bash
# TV show — lookup first, grab tvdbId from result
SONARR_KEY=$(ssh reset@192.168.1.28 "grep -oP '(?<=<ApiKey>)[^<]+' /opt/arr/sonarr/config.xml")
curl -sf "http://192.168.1.28:8989/api/v3/series/lookup?term=Succession" -H "X-Api-Key: $SONARR_KEY" | python3 -c "
import sys,json; series=json.load(sys.stdin)
for s in series[:3]: print(s['tvdbId'], s['title'], s.get('year',''))
"
# Then add
curl -sf -X POST http://192.168.1.28:8989/api/v3/series \
  -H "X-Api-Key: $SONARR_KEY" -H 'Content-Type: application/json' \
  -d '{"title":"Succession","tvdbId":366524,"qualityProfileId":1,"rootFolderPath":"/downloads/tv-shows","monitored":true,"addOptions":{"searchForMissingEpisodes":true,"monitor":"all"}}'
```

### Quality profiles

| ID | Name | Use for |
|----|------|---------|
| 1 | Any | TV shows (file sizes are reasonable) |
| 4 | HD-1080p | Movies — **always use this**, never "Any" for movies |

"Any" for movies grabs BR-DISK full disc images (20–70 GB). HD-1080p caps at encoded 1080p (~2–8 GB).

### Bulk-add from scene-named folders

Script at `/opt/arr/bulk-add.py` on the server:

```bash
# Dry run first to see what it would add
ssh reset@192.168.1.28 "python3 /opt/arr/bulk-add.py movies --dry-run"
ssh reset@192.168.1.28 "python3 /opt/arr/bulk-add.py tv --dry-run"

# Actually add
ssh reset@192.168.1.28 "python3 /opt/arr/bulk-add.py movies"
ssh reset@192.168.1.28 "python3 /opt/arr/bulk-add.py tv"
```

The script parses `Title.Year.Quality` scene names, skips anything already in the library, and uses `importMode: 'copy'` to preserve hardlinks.

### Collection folders (multi-movie)

When a folder like `Indiana.Jones.Collection` contains multiple movies, `bulk-add.py` will only match one. Add the rest individually using the TMDB lookup pattern above — search by each film's individual title.

### Repair broken seeding

Run this if Sonarr/Radarr imported files via move instead of hardlink (symptoms: Transmission shows torrents as error/missing):

```bash
# Dry run first
ssh reset@192.168.1.28 "python3 /opt/arr/fix-seeding.py --dry-run"

# Apply
ssh reset@192.168.1.28 "python3 /opt/arr/fix-seeding.py"
```

Script reads all Transmission torrents via RPC, builds a filename→path index from `/downloads/tv-shows` and `/downloads/movies`, and hardlinks any missing torrent files back to their original paths.

### Health check

```bash
SONARR_KEY=$(ssh reset@192.168.1.28 "grep -oP '(?<=<ApiKey>)[^<]+' /opt/arr/sonarr/config.xml")
RADARR_KEY=$(ssh reset@192.168.1.28 "grep -oP '(?<=<ApiKey>)[^<]+' /opt/arr/radarr/config.xml")
curl -sf http://192.168.1.28:8989/api/v3/health -H "X-Api-Key: $SONARR_KEY"
curl -sf http://192.168.1.28:7878/api/v3/health -H "X-Api-Key: $RADARR_KEY"
```

Automated monitoring runs every 4 hours via `/etc/cron.d/arr-monitor`; logs to `/var/log/arr-monitor.log`. Script at `/opt/arr/monitor.py`.

### Trigger Plex library refresh

```bash
PLEX_TOKEN=$(ssh reset@192.168.1.28 "sudo grep -oP 'PlexOnlineToken=\"\K[^\"]+' '/var/lib/plexmediaserver/Library/Application Support/Plex Media Server/Preferences.xml'")
# Movies = section 1, TV = section 2 (verify with /library/sections first)
curl -sf -X POST "http://192.168.1.28:32400/library/sections/1/refresh?X-Plex-Token=$PLEX_TOKEN"
curl -sf -X POST "http://192.168.1.28:32400/library/sections/2/refresh?X-Plex-Token=$PLEX_TOKEN"
```
