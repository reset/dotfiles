---
name: media-server
description: Manage Jamie's home media server — the *arr stack (Sonarr, Radarr, Prowlarr) + Transmission + Jellyfin running at 192.168.1.28. Use whenever the user wants to add/import shows or movies, fix health checks, check seeding, manage Jellyfin libraries, debug download issues, or do anything involving the home server media setup. Triggers on "add a show", "import movies", "Sonarr", "Radarr", "Jellyfin", "Seerr", "Transmission", "media server", "download a movie", "health check", "seeding broken", "*arr".
---

# Media Server

Jamie's home media server at `reset@192.168.1.28` (hostname: `linux-build-1`). DHCP reservation locks it to this IP permanently.

**SSH access**: on LAN use `reset@192.168.1.28`. Off LAN, a Cloudflare tunnel publishes the box at `ssh.reset.dev`; with the `~/.ssh/config` block in place, `ssh reset.dev` Just Works from anywhere (see the SSH section in `~/.claude/CLAUDE.md`).

## Stack overview

Everything runs in Docker, managed by a single compose file at `/opt/arr/docker-compose.yml`. All containers use `restart: unless-stopped` — they come up automatically on boot.

| Service | Image | Port | Config path | .home URL |
|---------|-------|------|-------------|-----------|
| Sonarr | linuxserver/sonarr | 8989 | `/opt/arr/sonarr/` | `http://sonarr.home` |
| Radarr | linuxserver/radarr | 7878 | `/opt/arr/radarr/` | `http://radarr.home` |
| Prowlarr | linuxserver/prowlarr | 9696 | `/opt/arr/prowlarr/` | `http://prowlarr.home` |
| Transmission | linuxserver/transmission | 9091 | `/opt/arr/transmission/` | `http://transmission.home` |
| Jellyfin | linuxserver/jellyfin | 8096 | `/opt/arr/jellyfin/` | `http://jellyfin.home` |
| Jellystat | cyfershepard/jellystat | 3000 | `/opt/arr/jellystat/` | `http://jellystat.home` |
| Seerr | ghcr.io/seerr-team/seerr | 5055 | `/opt/arr/overseerr/` | `http://seerr.home` |
| Pi-hole | pihole/pihole | 53 (DNS), 8080 (web) | `/opt/arr/pihole/` | `http://pihole.home` |
| Caddy | caddy | 80 | `/opt/arr/caddy/Caddyfile` | — |
| Cloudflared | cloudflare/cloudflared | — | — (token-managed) | — |
| unpackerr | golift/unpackerr | — | `/opt/arr/unpackerr/` | — |

**Remote access via Cloudflare tunnel**: `watch.reset.dev` → Jellyfin (port 8096), `ssh.reset.dev` → sshd (port 22). Both ride the same `cloudflared` container; routes are managed in the Cloudflare Zero Trust dashboard (tunnel name `stormbreaker-server`), not in a local config file.

**Note:** Transmission was previously a native systemd service. It was migrated to Docker; the systemd service is disabled (`systemctl is-enabled transmission-daemon` returns `disabled`).

**Auth:** Sonarr, Radarr, and Prowlarr have authentication disabled for local addresses (`authenticationMethod: none`, `authenticationRequired: disabledForLocalAddresses`). No login required on the home network.

## Managing the stack

```bash
# Status
ssh reset@192.168.1.28 "docker ps --format 'table {{.Names}}\t{{.Status}}'"

# Restart a service
ssh reset@192.168.1.28 "cd /opt/arr && docker compose restart sonarr"

# Restart everything
ssh reset@192.168.1.28 "cd /opt/arr && docker compose restart"

# View logs
ssh reset@192.168.1.28 "docker logs sonarr --tail 50"

# Reload Caddy config without restart
ssh reset@192.168.1.28 "docker exec caddy caddy reload --config /etc/caddy/Caddyfile"
```

## Key paths

| Path | What it is |
|------|-----------|
| `/opt/arr/docker-compose.yml` | Single compose file for all services |
| `/var/lib/transmission-daemon/downloads/` | All downloads root |
| `/var/lib/transmission-daemon/downloads/tv-shows/` | TV downloads + Sonarr library root |
| `/var/lib/transmission-daemon/downloads/movies/` | Movie downloads + Radarr library root |
| `/var/lib/transmission-daemon/downloads/tv-sonarr/` | Sonarr's active download category dir |
| `/var/lib/transmission-daemon/downloads/radarr/` | Radarr's active download category dir |
| `/opt/arr/jellyfin/` | Jellyfin config + metadata cache |
| `/opt/arr/jellystat/db/` | Jellystat's Postgres data volume |

The download directory and library root are the **same directory** for both Sonarr and Radarr. Jellyfin mounts these read-only at `/media`.

## Container path mapping

Sonarr/Radarr/Prowlarr/Transmission all mount `/var/lib/transmission-daemon/downloads/` as `/downloads/` inside the container. Jellyfin mounts the same path as `/media` (read-only). A remote path mapping is configured in Sonarr and Radarr:

- Remote (Transmission reports): `/var/lib/transmission-daemon/downloads/`
- Local (container sees): `/downloads/`

Scripts that SSH into the host must use host paths (`/var/lib/transmission-daemon/downloads/...`) for filesystem ops, but pass container paths (`/downloads/...`) to the Sonarr/Radarr APIs.

## DNS and reverse proxy

Pi-hole handles DNS for the whole network (Orbi is configured to use `192.168.1.28` as primary DNS, `1.1.1.1` as secondary). Local `.home` records are configured in Pi-hole's `dns.hosts` config.

Caddy is the reverse proxy — all `.home` names route through it on port 80. Caddy config is at `/opt/arr/caddy/Caddyfile`.

To add a new `.home` DNS record:
```bash
# Via Pi-hole API (Pi-hole v6)
PW='...'  # from 1Password: Pi-hole Admin (media-server)
TOKEN=$(curl -s -X POST http://192.168.1.28:8080/api/auth \
  -H 'Content-Type: application/json' \
  -d "{\"password\":\"$PW\"}" | python3 -c 'import sys,json; print(json.load(sys.stdin)["session"]["sid"])')

# PATCH the full hosts array (replaces existing, so include all records)
curl -s -X PATCH http://192.168.1.28:8080/api/config \
  -H 'Content-Type: application/json' \
  -H "X-FTL-SID: $TOKEN" \
  -d '{"config":{"dns":{"hosts":["192.168.1.28 sonarr.home","192.168.1.28 radarr.home",...]}}}'
```

To add a new reverse proxy entry, append to `/opt/arr/caddy/Caddyfile`:
```
http://newservice.home {
    reverse_proxy localhost:PORT
}
```
Then reload: `docker exec caddy caddy reload --config /etc/caddy/Caddyfile`

## Getting API keys

Never hardcode API keys — read them live from the config files:

```bash
# Sonarr
ssh reset@192.168.1.28 "grep -oP '(?<=<ApiKey>)[^<]+' /opt/arr/sonarr/config.xml"

# Radarr
ssh reset@192.168.1.28 "grep -oP '(?<=<ApiKey>)[^<]+' /opt/arr/radarr/config.xml"

# Prowlarr
ssh reset@192.168.1.28 "grep -oP '(?<=<ApiKey>)[^<]+' /opt/arr/prowlarr/config.xml"

# Jellyfin API key (read from Seerr's saved settings — Jellyfin doesn't expose it in a single file)
ssh reset@192.168.1.28 "sudo python3 -c 'import json; print(json.load(open(\"/opt/arr/overseerr/settings.json\"))[\"jellyfin\"][\"apiKey\"])'"

# Seerr API key
ssh reset@192.168.1.28 "sudo python3 -c 'import json; print(json.load(open(\"/opt/arr/overseerr/settings.json\"))[\"main\"][\"apiKey\"])'"

# Transmission credentials — username: reset, password in 1Password: "Media Server (*arr / Transmission)"
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

Transmission requires a two-step auth: first request returns a session ID header, second request uses it. Credentials: username `reset`, password in 1Password ("Media Server (*arr / Transmission)").

```python
import urllib.request, json, base64

URL = 'http://192.168.1.28:9091/transmission/rpc'
AUTH = base64.b64encode(b'reset:PASSWORD').decode()

req = urllib.request.Request(URL, headers={'Authorization': 'Basic ' + AUTH})
try:
    urllib.request.urlopen(req)
except urllib.error.HTTPError as e:
    session_id = e.headers.get('X-Transmission-Session-Id', '')

def rpc(method, args=None):
    req = urllib.request.Request(URL,
        data=json.dumps({'method': method, 'arguments': args or {}}).encode(),
        headers={'Authorization': 'Basic ' + AUTH,
                 'X-Transmission-Session-Id': session_id,
                 'Content-Type': 'application/json'})
    with urllib.request.urlopen(req) as r:
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

### Jellyfin library scene-folder duplicates
The hardlink-and-keep-seeding architecture means every imported movie exists in two folders: the scene-named torrent folder (e.g. `Movie.Name.2024.1080p.WEBRip-GROUP/`) and the cleanly-named Radarr folder (`Movie Name (2024)/`). Jellyfin scans both and creates two library items for the same TMDB ID. Two ways to handle:

1. **MergeVersions API** (preferred, no filesystem mutation): `POST /Videos/MergeVersions?ids=ID1,ID2,...` collapses duplicates into one item with multiple selectable "versions". Run via Jellyfin admin API. Scene folders keep seeding; library count matches actual unique films.
2. **`.ignore` files**: drop a `.ignore` file inside any folder Jellyfin should skip. Used for scene folders we don't want catalogued at all (e.g. `[scene-tag] Show Name`). Path: `/var/lib/transmission-daemon/downloads/{movies,tv-shows}/<folder>/.ignore`.

### Transmission config path changed from systemd
The native systemd Transmission stored state in `/var/lib/transmission-daemon/.config/transmission-daemon/`. The Docker container uses `/opt/arr/transmission/` — all 2204 resume files and torrent state were migrated there. The downloads directory (`/var/lib/transmission-daemon/downloads/`) is the same in both cases.

### Pi-hole v6 API differences
Pi-hole v6 changed the web password env var from `WEBPASSWORD` to `FTLCONF_webserver_api_password` in docker-compose. Local DNS records are managed via the API (`dns.hosts` config array), not by editing `custom.list` directly — that file is auto-generated and will be overwritten. Pi-hole web UI moved to port 8080 to free port 80 for Caddy.

### systemd-resolved conflicts with Pi-hole
Ubuntu's `systemd-resolved` stub listener occupies port 53 on loopback by default. Required fix on the host: `DNSStubListener=no` in `/etc/systemd/resolved.conf`, then `systemctl restart systemd-resolved`. Already applied.

## Jellyfin

Jellyfin replaced Plex in 2026-06. Public access via `https://watch.reset.dev` (Cloudflare tunnel → `localhost:8096`). API auth uses `X-Emby-Token` header (the API key is also stored in Seerr's `settings.json`).

```bash
JF_KEY=$(ssh reset@192.168.1.28 "sudo python3 -c 'import json; print(json.load(open(\"/opt/arr/overseerr/settings.json\"))[\"jellyfin\"][\"apiKey\"])'")

# Server info
curl -sf http://192.168.1.28:8096/System/Info -H "X-Emby-Token: $JF_KEY"

# List libraries
curl -sf http://192.168.1.28:8096/Library/VirtualFolders -H "X-Emby-Token: $JF_KEY"

# Trigger library scan (all libraries)
curl -sf -X POST http://192.168.1.28:8096/Library/Refresh -H "X-Emby-Token: $JF_KEY"

# List users
curl -sf http://192.168.1.28:8096/Users -H "X-Emby-Token: $JF_KEY"

# Get all movies (TotalRecordCount + items)
ADMIN=3b436fbff61444bfb530cb58cb355f02  # 'reset' admin user id — find via /Users
curl -sf "http://192.168.1.28:8096/Users/$ADMIN/Items?Recursive=true&IncludeItemTypes=Movie&Fields=ProviderIds,Path&Limit=10000" -H "X-Emby-Token: $JF_KEY"

# Merge duplicate movie items (use after a re-scan creates scene-folder duplicates)
curl -sf -X POST "http://192.168.1.28:8096/Videos/MergeVersions?ids=ID1,ID2" -H "X-Emby-Token: $JF_KEY"
```

Current libraries (configured in Jellyfin admin):
- Movies → `/media/movies` (container path) = `/var/lib/transmission-daemon/downloads/movies` (host)
- TV Shows → `/media/tv-shows` = `/var/lib/transmission-daemon/downloads/tv-shows`

Real-time monitoring is enabled on both libraries, so new files imported by Sonarr/Radarr appear in Jellyfin within seconds without an explicit refresh.

Hardware transcoding is enabled (VAAPI, `/dev/dri/renderD128`). The Haswell iGPU (i7-4770R / HD 5200) hardware-decodes H.264, MPEG-2, and VC1 only — anything HEVC/VP9/AV1 falls back to CPU.

## Jellystat (Jellyfin analytics — Tautulli replacement)

`http://jellystat.home` / `http://localhost:3000`. Postgres backend in `jellystat-db` container. Initial setup is interactive (admin account + Jellyfin URL + API key). Credentials in 1Password: "Jellystat (media-server)".

## Seerr (request frontend)

`http://seerr.home` / `http://localhost:5055`. Wired to Jellyfin via `mediaServerType=2` in `settings.json`. Plex login was removed (`newPlexLogin: false`). Use Seerr admin → Jobs → "Jellyfin Full Library Sync" to refresh availability state after a big import batch.

## Indexer (Prowlarr → IPTorrents)

Prowlarr has IPTorrents configured and synced to both Sonarr (TV categories) and Radarr (movie categories). From outside Docker use `192.168.1.28:9696`.

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

### Trigger Jellyfin library refresh

Usually unnecessary — real-time monitoring picks up new files within seconds. But for a forced full re-scan:

```bash
JF_KEY=$(ssh reset@192.168.1.28 "sudo python3 -c 'import json; print(json.load(open(\"/opt/arr/overseerr/settings.json\"))[\"jellyfin\"][\"apiKey\"])'")
curl -sf -X POST http://192.168.1.28:8096/Library/Refresh -H "X-Emby-Token: $JF_KEY"
```

## Disk Management

### Understanding the directory layout

Every downloaded file exists in two folders — this is by design, not waste:

1. **Scene/torrent folder** (`movies/Scene.Name.Year.1080p.x264-GROUP/`) — original download path; Transmission seeds from here.
2. **Clean folder** (`movies/Title (Year)/`) — what Radarr/Sonarr created via hardlink for the media server.

Both folder entries point to the **same inode** (confirmed: `nlink=2`). Deleting the scene folder frees zero bytes — it just breaks seeding. Don't delete scene folders to save space.

### What actually uses unique disk space

- **`/downloads/radarr/`** — Radarr's download category staging dir. Files here are either in-progress or downloaded but not yet imported. In the combined `du -sh /downloads/*` output, files already hardlinked to `movies/` show as 0 here (inodes counted in movies first); files NOT yet imported show their full size here.
- **`/downloads/tv-sonarr/`** — same pattern for TV.
- **Quality profile misfires** — the biggest risk. A UHD Blu-ray Remux can be 50–80G vs 3–8G for an equivalent 1080p encode.
- **unpackerr RAR extraction** — unpackerr auto-extracts RAR-packed releases into the staging dir. The extracted file is owned by root, and the original RAR parts are owned by debian-transmission. Until Radarr/Sonarr imports and Transmission removes the torrent, both the RAR parts AND the extracted file occupy disk. A 55G BR-DISK release becomes ~110G of staged files. Profile changes don't retroactively reject already-grabbed content — the grab and the profile change are decoupled.

### Cleaning radarr/tv-sonarr staging

Staging dirs accumulate imported-but-not-yet-cleared torrents. Safe to clean up anything already imported (Radarr/Sonarr has the file). The hardlinked copy in `movies/` or `tv-shows/` survives Transmission deletion.

**Note: Radarr/Sonarr API returns 500 when disk is full.** If you get empty or 500 responses, check `df -h /` first.

**Note: `torrent-remove --delete-local-data` can't delete root-owned files** (unpackerr extracts as root). If staging files survive after Transmission removal, use `sudo rm -rf` on the specific folder.

```python
# Run via: ssh reset@192.168.1.28 'python3 << PYEOF ... PYEOF'
import urllib.request, json, re, os

key = open("/opt/arr/radarr/config.xml").read()
api_key = re.search(r"<ApiKey>([^<]+)", key).group(1)
base = "http://192.168.1.28:7878/api/v3"

with urllib.request.urlopen(base + "/movie?apikey=" + api_key) as r:
    movies = json.loads(r.read())

imported = {}
for m in movies:
    if m.get("movieFile"):
        imported[m["id"]] = {
            "title": m["title"], "year": m["year"],
            "file_path": m["movieFile"]["path"],
        }

URL = "http://192.168.1.28:9091/transmission/rpc"
req = urllib.request.Request(URL, data=b"{}", headers={"Content-Type": "application/json"})
try: urllib.request.urlopen(req); sid = ""
except urllib.error.HTTPError as e: sid = e.headers.get("X-Transmission-Session-Id", "")

def rpc(method, args=None):
    req = urllib.request.Request(URL,
        data=json.dumps({"method": method, "arguments": args or {}}).encode(),
        headers={"X-Transmission-Session-Id": sid, "Content-Type": "application/json"})
    with urllib.request.urlopen(req) as r:
        return json.loads(r.read())["arguments"]

result = rpc("torrent-get", {"fields": ["id", "name", "downloadDir", "totalSize", "percentDone"]})
radarr_torrents = [t for t in result["torrents"] if "radarr" in t["downloadDir"] and t["percentDone"] == 1.0]

can_remove = []
for t in sorted(radarr_torrents, key=lambda x: -x["totalSize"]):
    torrent_lower = t["name"].lower().replace(".", " ").replace("-", " ")
    matched = None
    for mid, info in imported.items():
        if info["title"].lower() in torrent_lower and str(info["year"]) in t["name"]:
            matched = info; break
    size_g = t["totalSize"] / 1024/1024/1024
    if matched:
        host_path = matched["file_path"].replace("/downloads/", "/var/lib/transmission-daemon/downloads/")
        file_exists = os.path.exists(host_path)
        can_remove.append(t["id"])
        print("IMPORTED %.1fG %s -> %s file_exists=%s" % (size_g, t["name"], matched["title"], file_exists))
    else:
        print("NO-MATCH %.1fG %s" % (size_g, t["name"]))

# Verify the list looks right before uncommenting:
# rpc("torrent-remove", {"ids": can_remove, "delete-local-data": True})
# For NO-MATCH items, look them up manually in Radarr — title/year mismatches are common
# (e.g. "2019" in torrent name but Radarr has year 2020, or subtitle differences)
```

After torrent removal, if files remain (root-owned by unpackerr):
```bash
ssh reset@192.168.1.28 "sudo rm -rf '/var/lib/transmission-daemon/downloads/radarr/FOLDER-NAME'"
```

### Checking disk usage

```bash
# Overall
ssh reset@192.168.1.28 "df -h /"

# By top-level download subdirs (hardlinks counted once, movies before radarr)
ssh reset@192.168.1.28 "du -sh /var/lib/transmission-daemon/downloads/*"

# Largest movie folders
ssh reset@192.168.1.28 "du -sh /var/lib/transmission-daemon/downloads/movies/* | sort -rh | head -20"

# Largest TV folders
ssh reset@192.168.1.28 "du -sh /var/lib/transmission-daemon/downloads/tv-shows/* | sort -rh | head -20"
```

### Quality profiles

**Radarr (movies):**

| ID | Name | Allowed | Notes |
|----|------|---------|-------|
| 4 | HD-1080p | HDTV-1080p, WEB 1080p, Bluray-1080p | Default. Bluray-1080p included — older catalog films often only exist as Bluray rips. The "Too Large" custom format (-10000) auto-rejects BR-DISK/Remux behemoths. |
| 5 | Ultra-HD | HDTV-2160p, WEB 2160p, Bluray-2160p | Remux-2160p and BR-DISK intentionally removed (50–80G). |

**Sonarr (TV):**

| ID | Name | Notes |
|----|------|-------|
| 1 | Any | Most shows — includes everything up to Bluray-1080p |
| 4 | HD-1080p | Same as Radarr HD-1080p |
| 5 | Ultra-HD | Same as Radarr Ultra-HD — check for accidental use |
| 6 | HD-720p/1080p | A few shows (The Bridge, Workaholics, Ren & Stimpy, Devotion) |

**Never leave a movie on Ultra-HD (profile 5) accidentally.** Check which movies are on Ultra-HD:

```bash
RADARR_KEY=$(ssh reset@192.168.1.28 "grep -oP '(?<=<ApiKey>)[^<]+' /opt/arr/radarr/config.xml")
ssh reset@192.168.1.28 "curl -sf 'http://192.168.1.28:7878/api/v3/movie?apikey=$RADARR_KEY' | python3 -c \"
import sys, json
for m in json.load(sys.stdin):
    if m['qualityProfileId'] == 5:
        f = m.get('movieFile')
        size = str(f['size']//1024//1024//1024)+'G' if f else 'no file'
        print(m['title'], m['year'], size)
\""
```

### Auditing oversized files

**Movies with Bluray-1080p files** (Bluray-1080p is allowed in the profile, but the largest rips are worth auditing — use the "Too Large" custom format score and file size to triage):

```bash
RADARR_KEY=$(ssh reset@192.168.1.28 "grep -oP '(?<=<ApiKey>)[^<]+' /opt/arr/radarr/config.xml")
ssh reset@192.168.1.28 "curl -sf 'http://192.168.1.28:7878/api/v3/movie?apikey=$RADARR_KEY' | python3 -c \"
import sys, json
movies = [m for m in json.load(sys.stdin) if m.get('movieFile') and m['movieFile']['quality']['quality']['name'] == 'Bluray-1080p']
for m in sorted(movies, key=lambda x: -x['movieFile']['size']):
    f = m['movieFile']
    print(str(f['size']//1024//1024//1024) + 'G', m['id'], f['id'], m['title'])
\""
```

**Re-grab a movie at the new quality** (delete file record → Radarr auto-searches):

```bash
RADARR_KEY=$(ssh reset@192.168.1.28 "grep -oP '(?<=<ApiKey>)[^<]+' /opt/arr/radarr/config.xml")
MOVIE_ID=11   # from audit above
FILE_ID=67    # from audit above

# Delete file from Radarr (removes clean folder copy; scene/torrent folder keeps seeding)
ssh reset@192.168.1.28 "curl -sf -X DELETE 'http://192.168.1.28:7878/api/v3/moviefile/$FILE_ID?apikey=$RADARR_KEY'"

# Trigger search
ssh reset@192.168.1.28 "curl -sf -X POST 'http://192.168.1.28:7878/api/v3/command?apikey=$RADARR_KEY' \
  -H 'Content-Type: application/json' \
  -d '{\"name\":\"MoviesSearch\",\"movieIds\":[$MOVIE_ID]}'"
```

**TV episodes over 2GB** (legitimate for long episodes, but worth checking):

```bash
SONARR_KEY=$(ssh reset@192.168.1.28 "grep -oP '(?<=<ApiKey>)[^<]+' /opt/arr/sonarr/config.xml")
ssh reset@192.168.1.28 "python3 -c \"
import json, urllib.request
KEY = '$SONARR_KEY'
BASE = 'http://192.168.1.28:8989/api/v3'
with urllib.request.urlopen(BASE + '/series?apikey=' + KEY) as r:
    series_list = json.loads(r.read())
big = []
for s in series_list:
    with urllib.request.urlopen(BASE + '/episodefile?seriesId=' + str(s['id']) + '&apikey=' + KEY) as r:
        for f in json.loads(r.read()):
            if f['size'] > 2*1024*1024*1024:
                big.append((f['size'], s['title'], f['relativePath'].split('/')[-1], f['quality']['quality']['name']))
for size, series, fname, quality in sorted(big, reverse=True)[:20]:
    print(str(size//1024//1024//1024) + 'G [' + quality + '] ' + series + ' / ' + fname)
\""
```

### Docker cleanup

Unused images and orphaned volumes accumulate over time. Safe to prune anytime:

```bash
ssh reset@192.168.1.28 "docker image prune -f && docker volume prune -f"
```

Typically recovers 5–15G. Check first with `docker system df`.

### Fixing "Unable to save resume file: No space left on device"

After freeing space, Transmission torrents in error state need a **stop then start** — a plain `torrent-start` on an already-errored torrent doesn't clear the error:

```python
import urllib.request, json, time

URL = 'http://192.168.1.28:9091/transmission/rpc'
req = urllib.request.Request(URL, data=b'{}', headers={'Content-Type': 'application/json'})
try: urllib.request.urlopen(req)
except urllib.error.HTTPError as e: sid = e.headers.get('X-Transmission-Session-Id', '')

def rpc(method, args=None):
    req = urllib.request.Request(URL,
        data=json.dumps({'method': method, 'arguments': args or {}}).encode(),
        headers={'X-Transmission-Session-Id': sid, 'Content-Type': 'application/json'})
    with urllib.request.urlopen(req) as r:
        return json.loads(r.read())

result = rpc('torrent-get', {'fields': ['id', 'error']})
errored_ids = [t['id'] for t in result['arguments']['torrents'] if t['error'] != 0]
rpc('torrent-stop', {'ids': errored_ids})
time.sleep(2)
rpc('torrent-start', {'ids': errored_ids})
```

Note: Transmission RPC has `rpc-authentication-required: false` — no credentials needed.
