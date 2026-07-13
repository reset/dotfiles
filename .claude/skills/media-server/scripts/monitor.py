#!/usr/bin/env python3
"""
Health monitor for the home *arr stack + Transmission.
Usage: python3 /opt/arr/monitor.py [--quiet]
Exit: 0 if all clean, 1 if any issues found.
Logs to /opt/arr/monitor.log (append).
"""
import sys
import json
import re
import urllib.request
import urllib.error
import os
import random
from datetime import datetime

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from arrlib import make_trans_rpc, to_host_path  # noqa: E402

QUIET = "--quiet" in sys.argv
LOG_FILE = "/opt/arr/monitor.log"

# Only spot-check video files; .nfo / .png / sample artifacts are noise.
VIDEO_EXTS = ('.mkv', '.mp4', '.avi', '.m4v', '.ts', '.m2ts')

issues = []
ok_msgs = []

def log(msg):
    if not QUIET:
        print(msg)

def get_api_key(config_path):
    with open(config_path) as f:
        m = re.search(r'<ApiKey>([^<]+)</ApiKey>', f.read())
        return m.group(1) if m else None

def arr_health(name, base_url, key, api_version="v3"):
    try:
        req = urllib.request.Request(
            f"{base_url}/api/{api_version}/health",
            headers={"X-Api-Key": key}
        )
        with urllib.request.urlopen(req, timeout=10) as r:
            items = json.loads(r.read())
        errors = [i for i in items if i.get("type") in ("error", "warning")]
        if errors:
            for e in errors:
                issues.append(f"{name}: [{e['type'].upper()}] {e['message']}")
        else:
            ok_msgs.append(f"{name}: healthy")
    except Exception as e:
        issues.append(f"{name}: unreachable — {e}")

def check_jellyfin_notification(name, base_url, key):
    """Assert the Jellyfin (MediaBrowser) Connect notification exists and will
    trigger a library update on import. This is load-bearing for library
    freshness on batch season imports — without it, Jellyfin falls back to its
    real-time file monitor, which races on multi-file imports and leaves
    seasons half-scanned. The notification lives in the app's DB, not in any
    tracked config, so it's easy to lose on a rebuild — hence this check."""
    try:
        req = urllib.request.Request(
            f"{base_url}/api/v3/notification",
            headers={"X-Api-Key": key}
        )
        with urllib.request.urlopen(req, timeout=10) as r:
            notifications = json.loads(r.read())
    except Exception as e:
        issues.append(f"{name} Jellyfin notification: could not query — {e}")
        return
    mb = [n for n in notifications if n.get("implementation") == "MediaBrowser"]
    if not mb:
        issues.append(f"{name}: Jellyfin (MediaBrowser) Connect notification MISSING — library won't auto-update on import")
        return
    n = mb[0]
    update_library = any(
        f.get("name") == "updateLibrary" and f.get("value")
        for f in n.get("fields", [])
    )
    if not n.get("onDownload"):
        issues.append(f"{name}: Jellyfin notification present but 'On Import' (onDownload) is disabled")
    elif not update_library:
        issues.append(f"{name}: Jellyfin notification present but 'Update Library' is off")
    else:
        ok_msgs.append(f"{name}: Jellyfin Connect notification present (updates library on import)")

# Check Sonarr
sonarr_key = get_api_key("/opt/arr/sonarr/config.xml")
if sonarr_key:
    arr_health("Sonarr", "http://localhost:8989", sonarr_key)
else:
    issues.append("Sonarr: could not read API key")

# Check Radarr
radarr_key = get_api_key("/opt/arr/radarr/config.xml")
if radarr_key:
    arr_health("Radarr", "http://localhost:7878", radarr_key)
else:
    issues.append("Radarr: could not read API key")

# Jellyfin Connect notification presence (Sonarr + Radarr) — see docstring
if sonarr_key:
    check_jellyfin_notification("Sonarr", "http://localhost:8989", sonarr_key)
if radarr_key:
    check_jellyfin_notification("Radarr", "http://localhost:7878", radarr_key)

# Check Prowlarr
prowlarr_key = get_api_key("/opt/arr/prowlarr/config.xml")
if prowlarr_key:
    arr_health("Prowlarr", "http://localhost:9696", prowlarr_key, api_version="v1")
else:
    issues.append("Prowlarr: could not read API key")

# Check Transmission — errored torrents
URL = "http://localhost:9091/transmission/rpc"
USER, PASS = "transmission", os.environ.get("TRANSMISSION_PASS", "")
if not PASS:
    # Don't proceed; subsequent requests would fail with 401 and add a
    # misleading "unreachable" issue on top of the real cause.
    issues.append("Transmission: TRANSMISSION_PASS env var not set")
    PASS = None
try:
    if PASS is None:
        raise RuntimeError("skipped: no TRANSMISSION_PASS")
    rpc = make_trans_rpc(URL, USER, PASS)
    result = rpc("torrent-get", {"fields": ["id", "name", "errorString", "downloadDir", "files", "percentDone"]})
    torrents = result.get("torrents", [])

    errored = [t for t in torrents if t.get("errorString", "").strip()]
    if errored:
        for t in errored[:5]:
            issues.append(f"Transmission: torrent '{t['name']}' has error: {t['errorString']}")
        if len(errored) > 5:
            issues.append(f"Transmission: ... and {len(errored) - 5} more errored torrents")
    else:
        ok_msgs.append(f"Transmission: {len(torrents)} torrents, none errored")

    # Spot-check hardlink integrity — sample completed torrents and verify
    # their first VIDEO file exists. Translate container paths (Transmission's
    # view) to host paths (where this script can stat them). Without these
    # filters/translations the check was always false-positive: random PNG
    # samples appeared "missing" because the path wasn't translated, and even
    # if it were, screenshots aren't worth alerting on.
    completed = [t for t in torrents if t.get("percentDone", 0) >= 1.0]
    sample_torrents = random.sample(completed, min(5, len(completed)))
    missing = []
    for t in sample_torrents:
        video_files = [f for f in t.get("files", []) if f["name"].lower().endswith(VIDEO_EXTS)]
        if not video_files:
            continue
        first = video_files[0]
        full_path = to_host_path(os.path.join(t["downloadDir"], first["name"]))
        if not os.path.exists(full_path):
            missing.append(f"{t['name']}: missing {first['name']}")
    if missing:
        for m in missing:
            issues.append(f"Hardlink check: {m}")
    else:
        ok_msgs.append(f"Hardlink spot-check: {len(sample_torrents)} sampled torrents have video file present")

except Exception as e:
    if PASS is not None:
        issues.append(f"Transmission: unreachable — {e}")

# Report
timestamp = datetime.now().strftime("%Y-%m-%d %H:%M:%S")
if issues:
    summary = f"[{timestamp}] ISSUES ({len(issues)}):\n" + "\n".join(f"  - {i}" for i in issues)
    if ok_msgs:
        summary += "\nOK:\n" + "\n".join(f"  + {o}" for o in ok_msgs)
else:
    summary = f"[{timestamp}] OK: all checks passed ({', '.join(ok_msgs)})"

log(summary)

# Append to log file
try:
    with open(LOG_FILE, "a") as f:
        f.write(summary + "\n")
except Exception as e:
    log(f"Warning: could not write to {LOG_FILE}: {e}")

sys.exit(1 if issues else 0)
