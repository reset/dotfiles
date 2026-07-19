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
| Bazarr | linuxserver/bazarr | 6767 | `/opt/arr/bazarr/` | `http://bazarr.home` |
| Transmission | linuxserver/transmission | 9091 | `/opt/arr/transmission/` | `http://transmission.home` |
| Jellyfin | linuxserver/jellyfin | 8096 | `/opt/arr/jellyfin/` | `http://jellyfin.home` |
| Jellystat | cyfershepard/jellystat | 3000 | `/opt/arr/jellystat/` | `http://jellystat.home` |
| Seerr | ghcr.io/seerr-team/seerr | 5055 | `/opt/arr/overseerr/` | `http://seerr.home` |
| Threadfin | fyb3roptik/threadfin | 34400 | `/opt/arr/threadfin/` | `http://threadfin.home` |
| Pi-hole | pihole/pihole | 53 (DNS), 8080 (web) | `/opt/arr/pihole/` | `http://pihole.home` |
| Caddy | caddy | 80 | `/opt/arr/caddy/Caddyfile` | — |
| Cloudflared | cloudflare/cloudflared | — | — (token-managed) | — |
| unpackerr | golift/unpackerr | — | `/opt/arr/unpackerr/` | — |

**Image versions are pinned, not `:latest`.** Every service in the compose file is pinned to an exact version tag (e.g. `lscr.io/linuxserver/sonarr:4.0.17.2952-ls312`, `postgres:16`), and the two images that don't publish a clean version tag (jellystat, threadfin) are pinned by digest (`@sha256:...`). This is deliberate: a `docker compose pull` can no longer silently pull a breaking major and take the stack down on the next restart. **Consequence:** Sonarr/Radarr will show a benign "New update is available" health warning — that's expected, the container image is pinned so the app updates on your schedule, not on restart. To update a service, bump its tag in the compose file, `docker compose pull <svc>`, then `docker compose up -d <svc>`. See "Updating a pinned image" below.

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

### Updating a pinned image

Images are version-pinned (see stack overview). To update one deliberately:
```bash
# 1. Find the current running version to know what you're moving from
ssh reset@192.168.1.28 'docker inspect sonarr --format "{{index .Config.Labels \"org.opencontainers.image.version\"}}"'
# 2. Edit the tag in /opt/arr/docker-compose.yml, then validate + pull (does NOT touch the running container)
ssh reset@192.168.1.28 'cd /opt/arr && docker compose config --quiet && docker compose pull sonarr'
# 3. Apply
ssh reset@192.168.1.28 'cd /opt/arr && docker compose up -d sonarr'
```
`docker compose pull` is the safety net — it fails loudly if the tag doesn't resolve, before any running container is disturbed. Backups of the compose file live alongside it as `docker-compose.yml.bak-<timestamp>`.

### Full-stack recreate → restart cloudflared afterward

If you ever recreate everything at once (`docker compose up -d` after editing many services, or a full `docker compose down && up`), **cloudflared can start before Jellyfin is listening**, pin a dead `[::1]:8096` origin connection, and serve **HTTP 502 on `watch.reset.dev`** even after Jellyfin is up (Jellyfin binds IPv4 only; cloudflared resolves `localhost` → `::1` and caches the failure). Local `curl http://localhost:8096` returns 200 while the tunnel 502s — that split is the tell. Fix is a lone restart once Jellyfin is up:
```bash
ssh reset@192.168.1.28 "cd /opt/arr && docker compose restart cloudflared"
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

## Remote access (Cloudflare tunnel)

The `cloudflared` container runs a token-managed tunnel named **`stormbreaker-server`** under the personal Cloudflare account (Jamie's vialstudios.com login, account ID `0e966f88c17f8eba2d5c984b24f18663`, zone `reset.dev`). Because it's token-managed, **routes live in the Cloudflare Zero Trust dashboard, not in any local config file** — there's no `cloudflared` config.yml on the box to edit.

Current routes:
- `reset.dev`, `www.reset.dev` → website
- `watch.reset.dev` → Jellyfin (`http://localhost:8096`)
- `ssh.reset.dev` → sshd (`ssh://localhost:22`)

To add a new route:
1. Cloudflare dashboard → **Zero Trust → Networks → Tunnels → stormbreaker-server**
2. **Routes** tab → **+ Add route → Published application**
3. Fill in: subdomain, domain `reset.dev`, **Service URL** with the **protocol prefix** that picks the route type:
   - `http://localhost:PORT` — HTTP services
   - `https://localhost:PORT` — HTTPS origin
   - `ssh://localhost:22` — SSH (use this prefix instead of a separate "Type: SSH" dropdown; the new UI infers protocol from the URL)
   - `tcp://localhost:PORT` — generic TCP
4. DNS record auto-created in Cloudflare for `<sub>.reset.dev`.

For SSH specifically, the client uses `cloudflared access ssh --hostname ssh.reset.dev` as a `ProxyCommand`. The full client config is documented under "SSH" in `~/.claude/CLAUDE.md`.

## Server-side scripts

Operational scripts live on the server at `/opt/arr/*.py` and are *also* backed up in this skill's `scripts/` folder (so they survive a server rebuild):

| Path on server | Purpose | Triggered by |
|----------------|---------|--------------|
| `/opt/arr/disk-audit.py` | Honest disk accounting (hardlink-aware) + `--seed-status` per-torrent verdict | Manual |
| `/opt/arr/bulk-add.py` | Parse scene-named folders and bulk-add to Radarr/Sonarr | Manual |
| `/opt/arr/fix-seeding.py` | Restore hardlinks for files Sonarr/Radarr moved instead of hardlinked | Manual when seeding shows error |
| `/opt/arr/monitor.py` | Health check for Sonarr/Radarr/Prowlarr + Bazarr + Transmission, runs every 4h via `/etc/cron.d/arr-monitor` | Cron |

**Sync local edits to the server**: run `~/.claude/skills/media-server/scripts/deploy`. It runs all local tests, tars the `.py` files (excluding `*_test.py`) over ssh to `reset.dev`, places them in `/opt/arr/` with `+x`, and runs `monitor.py` on the box as a post-deploy verification. Fails closed if any test fails.

**Redeploy to a new server**: same `deploy` script works as long as `ssh reset.dev` resolves to the new host. Two pieces of server-side state are NOT managed by `deploy` (or the compose file) and must be recreated separately on a fresh box:
- The monitor cron at `/etc/cron.d/arr-monitor`.
- The **Sonarr/Radarr → Jellyfin "Emby / Jellyfin" Connect notifications** — these live in each app's database, not in any tracked config, so a rebuilt Sonarr/Radarr loses them and silently reverts to Jellyfin's racy real-time-only scanning (see the "Batch season imports" lesson). Recreate both via `POST /api/v3/notification` (`implementation: MediaBrowser`, `updateLibrary: true`, host `192.168.1.28:8096`, Jellyfin API key). `monitor.py` asserts their presence so a missing one surfaces as a health issue.

`monitor.py` also checks **Bazarr** — liveness via `/api/system/status` plus a wiring assertion (`use_sonarr`/`use_radarr` on, and Sonarr/Radarr IPs not left at `127.0.0.1`), since a rebuilt `/opt/arr/bazarr` silently un-wires the bridge-networked container. And because images are pinned, it **demotes the benign "New update is available" *arr warning to info** (via `arrlib.is_update_notice`) so the cron doesn't report ISSUES every tick — real warnings/errors still alert. The pure helpers (`parse_bazarr_config`, `is_update_notice`) live in `arrlib.py` with tests in `arrlib_test.py`; `monitor.py` itself runs its checks at import time so it isn't unit-tested directly.

**Credentials**: the tracked copies of `fix-seeding.py` and `monitor.py` read the Transmission password from `$TRANSMISSION_PASS` (sanitized for the public-treat-as dotfiles repo). On the server, this needs to be set:
- For the cron entry: `TRANSMISSION_PASS=...` in `/etc/cron.d/arr-monitor` (one line above the schedule)
- For manual invocations: prepend `TRANSMISSION_PASS=... python3 /opt/arr/fix-seeding.py`
- The password is in 1Password: "Media Server (*arr / Transmission)"

Note: the *currently deployed* server scripts have the password hardcoded (predates the sanitization). When the env var pattern is adopted server-side, redeploy by overwriting with the sanitized copies + setting the env var.

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

### Batch season imports need the Sonarr→Jellyfin Connect trigger
Jellyfin's real-time file monitor races on **batch imports** — when a whole season pack lands and Sonarr hardlinks 10 files over a couple of minutes, the monitor fires mid-write and builds a half-populated, inconsistent series node (some episodes loosely attached, most missing, `ParentId=<series>` episode query returns 0). Single-episode grabs finish before the monitor looks, so they're fine; this only bites season packs. Symptom: a season shows in Sonarr as fully imported (`episodeFileCount == episodeCount`) but is missing/partial in Jellyfin.

Fix that's now in place: a Sonarr/Radarr → Jellyfin "Emby / Jellyfin" Connect notification (see the Jellyfin section) fires an authoritative library-update *after* the import completes, so the scan runs when files are settled. Sonarr uses **On Import Complete** (one scan per finished release — ideal for season packs); Radarr lacks that trigger so uses On Import/On Upgrade/On Rename.

One-off repair for a season already stuck in the partial state — force a recursive refresh of just that series item (no full library scan needed):
```bash
JF_KEY=$(ssh reset@192.168.1.28 "sudo python3 -c 'import json; print(json.load(open(\"/opt/arr/overseerr/settings.json\"))[\"jellyfin\"][\"apiKey\"])'")
SID=<series item id from /Users/{admin}/Items?SearchTerm=...&IncludeItemTypes=Series>
ssh reset@192.168.1.28 "curl -sf -X POST 'http://192.168.1.28:8096/Items/$SID/Refresh?Recursive=true&metadataRefreshMode=FullRefresh&imageRefreshMode=Default&replaceAllMetadata=false' -H \"X-Emby-Token: $JF_KEY\""
```
Renaming/refresh is hardlink-safe (imported files are `nlink=2`), so it never disturbs seeding.

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

New files appear in Jellyfin via two mechanisms: Jellyfin's own real-time file monitor (enabled on both libraries) **and** a Sonarr/Radarr → Jellyfin "Emby / Jellyfin" Connect notification that fires an authoritative library-update after each import completes. The Connect notification is the reliable path for batch imports (whole-season packs) — see the "Batch season imports need the Sonarr→Jellyfin Connect trigger" lesson below for why real-time monitoring alone isn't enough.

Both connections use the Jellyfin API key + `updateLibrary: true`, host `192.168.1.28:8096`:
- **Sonarr** (notification id 2): On Import, On Upgrade, **On Import Complete**, On Rename
- **Radarr** (notification id 2): On Import, On Upgrade, On Rename (Radarr's MediaBrowser has no On Import Complete — movies are single-file)

Hardware transcoding is enabled (VAAPI, `/dev/dri/renderD128`). The Haswell iGPU (i7-4770R / HD 5200) hardware-decodes H.264, MPEG-2, and VC1 only — anything HEVC/VP9/AV1 falls back to CPU.

## Live TV (Threadfin → Jellyfin)

Threadfin (the maintained fork of xTeVe) adds **live TV** to the stack. It emulates an HDHomeRun network tuner: it ingests an M3U playlist, lets you curate/filter which channels are active, and exposes an HDHomeRun `discover.json`/`lineup.json` + an XMLTV guide that **Jellyfin's native Live TV** consumes. Pipeline: `M3U source → Threadfin (curate) → HDHomeRun tuner in Jellyfin → watch.reset.dev`. Admin UI: `http://192.168.1.28:34400/web/` (or `http://threadfin.home`).

### The legal reality (read before promising a channel)
Free, legal M3U sources (**iptv-org** — `https://iptv-org.github.io/iptv/countries/us.m3u`, ~1563 US channels; category lists like `.../categories/sports.m3u`) carry publicly-available streams only. They do **not** carry licensed broadcast networks — no **Fox / FS1 / ESPN / Telemundo**. So live Fox (e.g. World Cup) is *not* obtainable through this pipeline: Fox-live-and-legal means a browser (Fubo/YouTube TV/Hulu Live/Sling — all DRM, none integrate into Jellyfin) or an OTA antenna + HDHomeRun. Threadfin proves and provides the *self-hosted live-TV capability*; it does not defeat DRM.

> **mjh.nz is EPG-only now.** `i.mjh.nz/PlutoTV/...` used to serve matched M3U + EPG pairs; they dropped M3U hosting — only `.xml` (EPG) files remain. Don't hand out `i.mjh.nz/*.m3u8` URLs, they 404. Use iptv-org for the tuner M3U.

### Compose service
```yaml
  threadfin:
    image: fyb3roptik/threadfin:latest
    container_name: threadfin
    network_mode: host
    environment:
      - PUID=111
      - PGID=113
      - TZ=America/Los_Angeles
      - THREADFIN_BIND_IP_ADDRESS=0.0.0.0
    volumes:
      - /opt/arr/threadfin:/home/threadfin/conf
    restart: unless-stopped
```

### Bind-IP gotcha (this will bite you)
On first boot Threadfin auto-picks an interface IP and **persists it in `settings.json` (`bindIpAddress`)** — it ignores the `THREADFIN_BIND_IP_ADDRESS` env var after that first write. On this box it grabbed the **Tailscale** IP (`100.x`), so nothing on loopback/LAN could reach it. Fix: set the field to the stable LAN IP and restart:
```bash
ssh reset@192.168.1.28 'sudo python3 -c "
import json; p=\"/opt/arr/threadfin/settings.json\"
d=json.load(open(p)); d[\"bindIpAddress\"]=\"192.168.1.28\"; json.dump(d,open(p,\"w\"),indent=2)"
cd /opt/arr && docker compose restart threadfin'
```
Pin it to `192.168.1.28` (not `0.0.0.0`) so the HDHomeRun `discover.json` **advertises a reachable BaseURL** — Jellyfin follows that URL for the lineup. Consequence: loopback won't work, so **Caddy must proxy the LAN IP, not `localhost`**.

### Caddy route
```
http://threadfin.home {
    reverse_proxy 192.168.1.28:34400 {
        header_up Host {upstream_hostport}
    }
}
```
The `header_up Host` line is required — Threadfin 502s on a forwarded `Host: threadfin.home` (same quirk the Transmission block handles). After editing, a plain `caddy reload` sometimes keeps a stale upstream (observed dialing `[::1]:34400` after the file already said `192.168.1.28`) — if a route 502s post-reload, `docker compose restart caddy` to force a clean load.

**DNS:** `threadfin.home` → `192.168.1.28` is added. **The Pi-hole web-API password is now `changeme`** — the compose `FTLCONF_webserver_api_password: "changeme"` env var forces the password on container start, and the July 2026 stack recreate applied it. So the API PATCH flow above **works** with `{"password":"changeme"}` (verified adding `bazarr.home`), and it's the preferred path — it needs **no Pi-hole restart, so no DNS blip for the house**. (`changeme` is a weak password on the DNS admin; LAN-only — port 8080 isn't tunneled — so low risk, but worth setting a real one someday.) **Fallback that needs no web password at all** — edit `pihole.toml` directly (SSH + sudo): add the record to the `dns.hosts` array in `/opt/arr/pihole/etc-pihole/pihole.toml`, then `docker compose restart pihole` (FTL re-reads the TOML on boot, but this *does* blip DNS). Verify either path with `dig +short <name>.home @192.168.1.28`.

### Configuring the source (Threadfin web UI — no clean API)
Threadfin's backend is an undocumented WebSocket; don't try to script it. Setup is UI-driven:
1. **Playlist** tab → **New** → paste the M3U URL (e.g. iptv-org US). Threadfin downloads + parses (~1563 streams). Registered under `settings.json → files.m3u`.
2. **Filter** tab → **New** → **Type: `M3U: Group Title`** → **Next**. **Gotcha:** the **Group Title dropdown** is the field that actually matches — *not* the "Filter Name"/"Filter Category" text fields (those are cosmetic labels). Pick the group from the dropdown, e.g. `Sports (79)`, `News (99)`. Save.
3. **Mapping** tab shows **only active (filtered) channels** — it's empty until a filter activates some. Threadfin caps active channels at **480**; with >480 and no filter, zero are active (that's why a fresh Mapping tab looks blank even though the M3U imported fine). Optional rename/renumber here; skip for a quick setup.

Endpoints Jellyfin cares about: `discover.json`, `lineup.json` (populates only after a filter is saved), and the guide at **`http://192.168.1.28:34400/threadfin.xml`** (note: *not* `/xmltv/threadfin.xml` — that 404s). With no real XMLTV source added in Threadfin, `threadfin.xml` is a placeholder/dummy guide — enough for channels to appear and tune; real EPG is a follow-up.

### Wiring Jellyfin (API — idempotent-ish, safe to re-run refresh)
```bash
JF_KEY=$(ssh reset@192.168.1.28 "sudo python3 -c 'import json; print(json.load(open(\"/opt/arr/overseerr/settings.json\"))[\"jellyfin\"][\"apiKey\"])'")

# 1. Add HDHomeRun tuner pointing at Threadfin
ssh reset@192.168.1.28 "curl -s -X POST 'http://192.168.1.28:8096/LiveTv/TunerHosts' \
  -H 'X-Emby-Token: $JF_KEY' -H 'Content-Type: application/json' \
  -d '{\"Type\":\"hdhomerun\",\"Url\":\"http://192.168.1.28:34400\",\"AllowHWTranscoding\":true}'"

# 2. Add XMLTV guide provider (EnableAllTuners applies it to the tuner above)
ssh reset@192.168.1.28 "curl -s -X POST 'http://192.168.1.28:8096/LiveTv/ListingProviders?validateListings=false' \
  -H 'X-Emby-Token: $JF_KEY' -H 'Content-Type: application/json' \
  -d '{\"Type\":\"xmltv\",\"Path\":\"http://192.168.1.28:34400/threadfin.xml\",\"EnableAllTuners\":true}'"

# 3. Refresh the guide (run this any time the Threadfin filter changes)
ssh reset@192.168.1.28 "JF_KEY=$JF_KEY; TASKID=\$(curl -s 'http://192.168.1.28:8096/ScheduledTasks' -H \"X-Emby-Token: \$JF_KEY\" | python3 -c 'import sys,json; print([t[\"Id\"] for t in json.load(sys.stdin) if t.get(\"Key\")==\"RefreshGuide\"][0])'); curl -s -X POST \"http://192.168.1.28:8096/ScheduledTasks/Running/\$TASKID\" -H \"X-Emby-Token: \$JF_KEY\""

# Verify channels landed
ssh reset@192.168.1.28 "curl -s 'http://192.168.1.28:8096/LiveTv/Channels?api_key=$JF_KEY' | python3 -c 'import sys,json; d=json.load(sys.stdin); print(d[\"TotalRecordCount\"], \"channels\")'"
```
**When the Threadfin filter changes, re-run step 3** (guide refresh) — Jellyfin caches the lineup and won't pick up added/removed channels until the guide rebuilds. Jellyfin's **Refresh Guide** scheduled task also runs on its own `IntervalTrigger` — set to **6h** (was Jellyfin's 24h default), so provider/Threadfin channel changes self-propagate within 6 hours without any manual step. To change the interval, POST the full trigger array to `/ScheduledTasks/{RefreshGuide-id}/Triggers` with `IntervalTicks` in 100ns units (6h = `216000000000`).

For an on-demand rebuild there's a tracked shim **`~/bin/tv-sync`** (in dotfiles) — it looks up the API key + task id on the server and fires `RefreshGuide`. Run `tv-sync` after adding channels in Threadfin instead of pasting the step-3 one-liner; allow ~60–90s, then reload the Live TV view.

### Buffer MUST be `ffmpeg` or Jellyfin playback fails
Threadfin's default buffer is `-` (direct passthrough) — it hands the raw HLS URL to the client and steps out ("Threadfin is no longer involved" in the log). Jellyfin's **browser player then throws "Playback failed due to a fatal player error"** on those HLS URLs. Fix: set the buffer to `ffmpeg` so Threadfin pulls the stream server-side, remuxes to MPEG-TS, and serves that — which Jellyfin transcodes cleanly. The container already ships `ffmpeg` at `/usr/bin/ffmpeg`. Set it both globally and per-playlist:
```bash
ssh reset@192.168.1.28 'sudo python3 -c "
import json; p=\"/opt/arr/threadfin/settings.json\"; d=json.load(open(p))
d[\"buffer\"]=\"ffmpeg\"
for pid in d.get(\"files\",{}).get(\"m3u\",{}): d[\"files\"][\"m3u\"][pid][\"buffer\"]=\"ffmpeg\"
json.dump(d,open(p,\"w\"),indent=2)"
cd /opt/arr && docker compose restart threadfin'
```
(Or in the UI: **Settings → Buffer → `ffmpeg`**, plus each Playlist's own buffer dropdown.) Cost: ffmpeg runs on the box (Haswell i7-4770R) — one or two concurrent streams are fine, it's not a transcode farm.

### Audio-but-no-video: Threadfin's ffmpeg probe is too small for 1080p60 sources
Symptom: a channel plays **audio but shows a single frozen frame** (often the channel-ident bumper) in Jellyfin's browser player. This is NOT a codec the browser can't play, and NOT a dead source — it's Threadfin's `ffmpeg.options` in `settings.json`.

The default template ships with `-analyzeduration 1000000 -probesize 1000000` (1s / 1MB). High-bitrate **1080p60 High-profile** streams (e.g. the mybunny/`tv123.me` line — the free iptv-org channels were simpler and never triggered this) only carry their video parameter sets (SPS/PPS) at keyframes. A 1MB probe is too small to capture them before `-c:v copy` begins, so the remuxed MPEG-TS **never contains the decode headers**. Downstream, ffprobe/Jellyfin/the browser report the video as `unspecified size` / "Could not find codec parameters" and decode zero frames — while the separate AAC audio track plays fine.

Diagnose (raw upstream decodes clean, Threadfin's output doesn't):
```bash
# upstream URL for a channel: grep its tvg-name in the M3U, take the following https line
UP="https://soursignal.com/.../..."          # from the M3U
# raw upstream — should report 1920x1080 and count hundreds of frames:
ssh reset@192.168.1.28 "docker exec threadfin timeout 40 ffmpeg -hide_banner -y -t 20 -i '$UP' -an -f null - 2>&1 | grep -Ei 'Video:|frame='"
# Threadfin's live output (stream id from lineup.json) — broken shows 'unspecified size', ~0 frames:
ssh reset@192.168.1.28 "docker exec threadfin timeout 40 ffmpeg -hide_banner -y -t 12 -i 'http://192.168.1.28:34400/stream/<id>' -an -f null - 2>&1 | grep -Ei 'Video:|unspecified|frame='"
```

Fix (global, repairs every channel) — bump the probe to 10MB, add `-bsf:v dump_extra=freq=keyframe` to re-insert SPS/PPS at every keyframe, and drop the two junk flags the default carried (`-movflags +faststart` is MP4-only and meaningless for mpegts; `-copyts` preserves the source's huge live timestamps and hurts player startup):
```bash
ssh reset@192.168.1.28 'sudo python3 - <<"PY"
import json,shutil
p="/opt/arr/threadfin/settings.json"; shutil.copy2(p,p+".bak-ffmpegfix")
d=json.load(open(p))
d["ffmpeg.options"]=("-hide_banner -loglevel error -analyzeduration 10000000 -probesize 10000000 "
 "-i [URL] -map 0:v -map 0:a:0 -c:v copy -bsf:v dump_extra=freq=keyframe "
 "-c:a aac -b:a 192k -ac 2 -f mpegts -fflags +genpts pipe:1")
json.dump(d,open(p,"w"),indent=2)
PY
cd /opt/arr && docker compose restart threadfin'
```
Then re-run the Threadfin-output decode check above — it should now report hundreds of frames. Reopen the channel in Jellyfin (stop/restart playback; the browser caches the broken session). No guide refresh needed — this is stream content, not the lineup. The embedded Closed Captions ride along with `-c:v copy` automatically; dropping `-c:s copy` is safe because `-map 0:v` never muxed a separate subtitle stream anyway.

### Choppy playback: low/variable upstream bitrate, not our pipeline
Symptom: a channel plays with **stuttery/choppy motion** (animation like Cartoon Network shows it worst — flat colors + fast pans expose dropped frames). First rule out our side, which is almost never the cause: Jellyfin Live TV runs `-codec:v copy -codec:a copy` (pure remux, **no re-encode** — confirm via `ps aux | grep jellyfin.*ffmpeg`), the box sits near-idle, and the Mac browser hardware-decodes h264. We pass the provider's bytes through verbatim; we can't add frames that never arrived.

The real cause on the mybunny/`tv123.me` line: its 1080p**60** channels are delivered at a **low, variable bitrate — ~2–5 Mbit/s (dipping to ~1.5)** where smooth 1080p60 wants 8–12. A real-time decode lands ~43 effective fps vs the nominal 59.94. Measure it (do this sparingly — see the connection-limit caveat):
```bash
# real-time decode: fps well under 59.94 and speed dropping below 1.0x = under-fed source
ssh reset@192.168.1.28 "docker exec threadfin timeout 50 ffmpeg -hide_banner -re -t 30 -i '<upstream-or-threadfin-url>' -an -f null - 2>&1 | grep -Ei 'frame=|concealing|corrupt' | tail -4"
# actual bitrate: capture 20s of stream-time, size/20/1024*8 = kbit/s
ssh reset@192.168.1.28 "docker exec threadfin sh -c \"timeout 25 ffmpeg -hide_banner -y -t 20 -i '<url>' -c copy -f mpegts /tmp/x.ts 2>/dev/null; stat -c%s /tmp/x.ts\""
```

Partial fix (rides out delivery *dips*, cannot recreate genuinely-dropped frames): bump Threadfin's RAM prebuffer `buffer.size.kb`. **Keep it ≤ ~2 MB** — 2048 measurably smoothed Cartoon Network; 8192 caused a ~20 s channel-start delay (Threadfin prebuffers the whole buffer at the stream's low bitrate before serving byte one) and timed players out. Default is 1024.
```bash
ssh reset@192.168.1.28 'sudo python3 -c "
import json,shutil; p=\"/opt/arr/threadfin/settings.json\"; shutil.copy2(p,p+\".bak-buffer\")
d=json.load(open(p)); d[\"buffer.size.kb\"]=2048; json.dump(d,open(p,\"w\"),indent=2)"
cd /opt/arr && docker compose restart threadfin'
```
**Connection-limit caveat (important):** these IPTV accounts cap concurrent streams (~1–2). Opening probe after probe while someone is watching trips the cap — which starves your probes *and* can degrade the live view, making measurements pessimistically bad. Diagnose with one or two pulls, then stop and let a human judge in the actual Jellyfin app. If a channel is choppy after the buffer bump and other channels are fine, it's that specific feed — ask the provider for a better/alternate one; there's no local fix for an under-fed source.

### Verifying a stream headlessly
- With `buffer = ffmpeg`, the stream endpoint serves real MPEG-TS, so `curl` **is** a valid test: pull `http://192.168.1.28:34400/stream/<id>` (from `lineup.json`) and confirm bytes flow and the first byte is `0x47` (TS sync). ~12 MB in ~12s = healthy.
- With `buffer = -` (passthrough), `curl` only grabs the `.m3u8` manifest, not video — inconclusive; test by clicking a channel in Jellyfin instead.
- To pre-screen which channels are alive, probe the **upstream** URLs in `urls.json` for a valid HLS manifest (starts with `#EXTM3U`). Expect a chunk of free iptv-org channels to be dead or `[Geo-blocked]` — that's the source, not the pipeline. On the sports list, ~24/30 were live; reliable ones included Tennis Channel, Stadium, PGA Tour, FIFA+ United States.

## Jellystat (Jellyfin analytics — Tautulli replacement)

`http://jellystat.home` / `http://localhost:3000`. Postgres backend in `jellystat-db` container. Initial setup is interactive (admin account + Jellyfin URL + API key). Credentials in 1Password: "Jellystat (media-server)".

## Seerr (request frontend)

`http://seerr.home` / `http://localhost:5055`. Wired to Jellyfin via `mediaServerType=2` in `settings.json`. Plex login was removed (`newPlexLogin: false`). Use Seerr admin → Jobs → "Jellyfin Full Library Sync" to refresh availability state after a big import batch.

### "I have to approve a request twice" — Seerr's servarr API timeout
Symptom: approving a request in Seerr appears to do nothing (stays unfulfilled / shows failed), and re-approving the same request works. It's **not** a two-step-by-design behavior — the first approval is silently *failing*. Seerr logs show:
```
[Sonarr API]: Error retrieving series by tvdb ID {"errorMessage":"timeout of 10000ms exceeded"}
[Media Request]: ...marking status as FAILED
```
Cause: before adding a title, Seerr calls Sonarr/Radarr to look up its full metadata. For a title the *arr hasn't cached yet, that lookup goes out to `skyhook.sonarr.tv` (Sonarr's TheTVDB proxy) and occasionally takes >10s. Seerr's `network.apiRequestTimeout` (in `settings.json`) governs **all** servarr API calls and defaults to `10000` ms — so a slow cold lookup gets killed and the request marked FAILED. The re-approval works because the *arr now has the metadata cached (warm lookups are ~60ms).

Fix — raise the timeout (30s is the community-standard value for this). This fork reads it live from `getSettings().network.apiRequestTimeout` in `dist/api/servarr/base.js`:
```bash
ssh reset@192.168.1.28 'sudo python3 - <<"PY"
import json, shutil
p="/opt/arr/overseerr/settings.json"; shutil.copy2(p, p+".bak-apitimeout")
d=json.load(open(p)); d["network"]["apiRequestTimeout"]=30000
json.dump(d, open(p,"w"), indent=2)
PY
cd /opt/arr && docker compose restart seerr'
```
Cost: the approval action in the UI can now block up to 30s on a genuinely dead upstream before erroring — rare, and a better trade than false FAILEDs. This mitigates intermittent skyhook/TheTVDB latency; there's no clean root-cause fix on the Sonarr side. (Editable via the UI too: Settings → Network → "API Request Timeout".)

## Bazarr (subtitles)

`http://bazarr.home` / `http://localhost:6767`. Fetches subtitles for the Sonarr/Radarr libraries. Bridge-networked with `6767:6767` published (same pattern as the *arr trio), `PUID=111`/`PGID=113` (= `debian-transmission`, so subtitle sidecars are owned like the media and don't disturb seeding), and mounts `/var/lib/transmission-daemon/downloads:/downloads` — **the same path Sonarr/Radarr use**, so no Bazarr path-mapping is needed.

**Config is `config.yaml`** (Bazarr 1.6+, not the old `config.ini`) at `/opt/arr/bazarr/config/config.yaml`. Because Bazarr is bridge-networked, its Sonarr/Radarr connections must point at the **LAN IP `192.168.1.28`**, not `127.0.0.1` (the default) — `localhost` inside the bridge container won't reach the *arr containers. To edit the config, **stop Bazarr first** (it rewrites the file on exit and will clobber live edits), then restart:
```bash
SONARR_KEY=$(ssh reset@192.168.1.28 "grep -oP '(?<=<ApiKey>)[^<]+' /opt/arr/sonarr/config.xml")
RADARR_KEY=$(ssh reset@192.168.1.28 "grep -oP '(?<=<ApiKey>)[^<]+' /opt/arr/radarr/config.xml")
ssh reset@192.168.1.28 "cd /opt/arr && docker compose stop bazarr"
ssh reset@192.168.1.28 "SONARR_KEY='$SONARR_KEY' RADARR_KEY='$RADARR_KEY' sudo -E python3" << 'PY'
import os, yaml, shutil
p = "/opt/arr/bazarr/config/config.yaml"; shutil.copy2(p, p + ".bak")
cfg = yaml.safe_load(open(p))
cfg["general"]["use_sonarr"] = True; cfg["general"]["use_radarr"] = True
cfg["sonarr"].update(ip="192.168.1.28", port=8989, apikey=os.environ["SONARR_KEY"])
cfg["radarr"].update(ip="192.168.1.28", port=7878, apikey=os.environ["RADARR_KEY"])
yaml.safe_dump(cfg, open(p, "w"), default_flow_style=False, sort_keys=True)
PY
ssh reset@192.168.1.28 "cd /opt/arr && docker compose start bazarr"
```
Verify the connection worked via Bazarr's API (key is `auth.apikey` in `config.yaml`) — a non-null `total` proves the library synced:
```bash
BZ_KEY=$(ssh reset@192.168.1.28 "sudo grep -A2 '^auth:' /opt/arr/bazarr/config/config.yaml | grep apikey | awk '{print \$2}'")
curl -sf "http://192.168.1.28:6767/api/series?start=0&length=1" -H "X-API-KEY: $BZ_KEY" | python3 -c 'import sys,json;print("series:",json.load(sys.stdin)["total"])'
curl -sf "http://192.168.1.28:6767/api/movies?start=0&length=1" -H "X-API-KEY: $BZ_KEY" | python3 -c 'import sys,json;print("movies:",json.load(sys.stdin)["total"])'
```
**Still requires UI setup** (needs Jamie's accounts/prefs, not scriptable cleanly): a **language profile** created + set as the series/movie default under Settings → Languages, and **subtitle provider accounts** (OpenSubtitles.com, etc.) under Settings → Providers. Without a language profile assigned, Bazarr syncs the library but never searches for subs.

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

Usually unnecessary — the Sonarr/Radarr Connect notification triggers a scan on import, and real-time monitoring backs it up. For a season stuck in Jellyfin's partial-scan state, prefer the per-series recursive refresh in the "Batch season imports" lesson. For a forced full re-scan of everything:

```bash
JF_KEY=$(ssh reset@192.168.1.28 "sudo python3 -c 'import json; print(json.load(open(\"/opt/arr/overseerr/settings.json\"))[\"jellyfin\"][\"apiKey\"])'")
curl -sf -X POST http://192.168.1.28:8096/Library/Refresh -H "X-Emby-Token: $JF_KEY"
```

## Disk Management

### ⚠️ The hardlink trap — read this before reclaiming space

`du -sh /downloads/*` lies on this box. Files in staging (`radarr/`, `tv-sonarr/`) are **hardlinked** into the library (`movies/`, `tv-shows/`) — same bytes, multiple filenames, one inode. `du` charges the bytes to one path arbitrarily and ignores the others, which makes staging look enormous when in fact deleting it would free almost nothing.

**Before doing anything that "frees" space, run `/opt/arr/disk-audit.py`.** It reports the honest numbers — per-category "unique" bytes (what you'd really reclaim) vs "shared" bytes (hardlinked, deleting one path frees zero). Also surfaces true orphans (staging dirs no torrent holds), largest unique-to-library files, and current seed ratio. Read-only — no mutation.

**Before removing torrents, run `/opt/arr/disk-audit.py --seed-status`.** Per-torrent verdict (`safe` / `borderline` / `risky`) against IPTorrents-style targets (ratio ≥ 1.0 OR seeded ≥ 72h, whichever first). Cumulative-ratio panic isn't the right lens; what matters is whether each individual torrent has paid its dues.

**Before deleting movies for disk reasons, run `/opt/arr/disk-audit.py --duplicates`.** Surfaces inodes in `movies/` that share a parsed (title, year) but live in different parent folders — i.e., a movie that exists as both a clean Radarr-managed folder AND a flat scene file or scene-named sibling. The `top 20 largest unique files` view shows the biggest single inode per movie; this view shows when a movie has *more than one* inode and how much disk a dedup would reclaim. Filters: movies only (TV episodes inherently produce many inodes per series), video extensions only (.mkv/.mp4/.avi/.m4v/.ts), and groups where total extra reclaim is ≥10MB (suppresses .nfo / sample-file noise).

Two things you can actually reclaim:
1. **Genuine orphans** — folders in staging that Transmission isn't tracking. Usually unpackerr leftovers (root-owned RAR extracts the torrent removal couldn't delete). The audit tool flags these explicitly.
2. **Big unique files** — oversized movies/episodes you'd re-grab at smaller quality. Library-side; doesn't touch seeding.

What you **cannot** reclaim by deleting staging:
- Any torrent that's been imported. The file in the library is hardlinked from the staging copy. Removing the staging copy frees 0 bytes and stops the torrent from seeding. Bad trade — surfaces as ratio damage with no disk benefit. The skill made this mistake once; the audit tool exists to prevent the second.

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

**Run `/opt/arr/disk-audit.py` first.** If "tv-sonarr unique" is 0 GB, removing torrents from there reclaims zero disk and only damages your seed ratio. The cleanup pattern below is mostly useful for unpackerr leftovers (root-owned files that `torrent-remove --delete-local-data` can't reach) and for the rare not-yet-imported torrent stuck in staging.

**For pure orphan cleanup** (the safe path), use the audit tool to identify orphans and `sudo rm -rf` them directly — no Transmission call needed because there's no torrent to remove.

**For removing imported torrents** (the trade-off path — frees no disk but stops seeding the torrent), continue with the pattern below. Worth it only if you're over your seed-keep window AND the audit tool confirms the torrent has unique bytes (unpackerr leftovers).

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

**Sonarr custom-format rejects (score -10000, applied to every profile):**

| Name | Threshold | Why |
|------|-----------|-----|
| `Too Large` | Size > 5 GB | Rejects pack-shaped behemoths that match no episode marker |
| `Too Large Episode` | Size > 2.5 GB AND title matches `S\d{1,2}E\d{1,2}` | Rejects single episodes encoded at wasteful bitrates (>~8 Mbps for a 45-min show). Season packs without per-episode markers pass through. |

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
