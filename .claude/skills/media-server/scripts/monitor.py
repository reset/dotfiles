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
import smtplib
import urllib.request
import urllib.error
import os
import random
from datetime import datetime
from email.message import EmailMessage

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from arrlib import (  # noqa: E402
    make_trans_rpc, to_host_path, parse_bazarr_config, is_update_notice,
    classify_disk_usage, should_send_alert,
)

QUIET = "--quiet" in sys.argv
LOG_FILE = "/opt/arr/monitor.log"

# Only spot-check video files; .nfo / .png / sample artifacts are noise.
VIDEO_EXTS = ('.mkv', '.mp4', '.avi', '.m4v', '.ts', '.m2ts')

# Disk-fullness alerting. A full root filesystem is the failure that cascades
# hardest on this box — it 503s Radarr/Sonarr adds (Seerr requests fail),
# wedges Transmission with "No space left on device", and stalls Jellyfin
# imports — yet none of it surfaces until a request fails. Thresholds are
# percent-of-usable (df's Use%); env-overridable so they can track the
# library's growth without a redeploy.
DISK_MOUNT = os.environ.get("DISK_MOUNT", "/")
DISK_WARN_PCT = float(os.environ.get("DISK_WARN_PCT", "90"))
DISK_CRIT_PCT = float(os.environ.get("DISK_CRIT_PCT", "95"))
# Last-alert state (level + date) so we email on worsening, nag at most once
# a day while bad, and send one recovery note — instead of every 4h tick.
ALERT_STATE_FILE = os.environ.get("ALERT_STATE_FILE", "/opt/arr/.monitor-disk-alert.json")

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
        # "New update is available" is expected under pinned images — demote to
        # info so it doesn't fire an alert every tick. Everything else is real.
        errors = [i for i in items if i.get("type") in ("error", "warning") and not is_update_notice(i)]
        notices = [i for i in items if is_update_notice(i)]
        if errors:
            for e in errors:
                issues.append(f"{name}: [{e['type'].upper()}] {e['message']}")
        else:
            ok_msgs.append(f"{name}: healthy")
        for n in notices:
            ok_msgs.append(f"{name}: {n['message']} (pinned image — benign)")
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

def bazarr_check(name, base_url, config_path):
    """Liveness + connection-wiring check for Bazarr. Bazarr isn't an *arr —
    its API and health model differ (no /api/v3/health), and its Sonarr/Radarr
    wiring lives in config.yaml (untracked), which defaults to use_*=False /
    ip=127.0.0.1 on a fresh /opt/arr/bazarr. Because Bazarr is bridge-networked,
    loopback can't reach the *arr containers, so a rebuild silently un-wires it —
    assert the wiring the same way we assert the Jellyfin Connect notification."""
    try:
        with open(config_path) as f:
            cfg = parse_bazarr_config(f.read())
    except Exception as e:
        issues.append(f"{name}: could not read {config_path} — {e}")
        return
    apikey = cfg.get("apikey")
    if not apikey:
        issues.append(f"{name}: no API key found in config.yaml")
        return
    try:
        req = urllib.request.Request(
            f"{base_url}/api/system/status",
            headers={"X-API-KEY": apikey}
        )
        with urllib.request.urlopen(req, timeout=10) as r:
            json.loads(r.read())
    except Exception as e:
        issues.append(f"{name}: unreachable — {e}")
        return
    drift = []
    if not cfg.get("use_sonarr"):
        drift.append("Sonarr connection disabled")
    if not cfg.get("use_radarr"):
        drift.append("Radarr connection disabled")
    for arr in ("sonarr", "radarr"):
        ip = cfg.get(f"{arr}_ip")
        if ip in ("127.0.0.1", "localhost"):
            drift.append(f"{arr} ip={ip} (bridge net can't reach *arr on loopback)")
    if drift:
        issues.append(f"{name}: reachable but wiring drift — {'; '.join(drift)}")
    else:
        ok_msgs.append(f"{name}: healthy, wired to Sonarr+Radarr")

def check_disk():
    """Disk-fullness check. Appends a warn/critical issue (or an ok note) and
    returns (level, used_pct) so the caller can drive the email alert. Uses
    statvfs directly and classify_disk_usage to bucket it — see that helper for
    why the percentage is over usable space, not the raw device total."""
    try:
        st = os.statvfs(DISK_MOUNT)
    except Exception as e:
        issues.append(f"Disk {DISK_MOUNT}: could not stat — {e}")
        return ("ok", 0.0)
    used_bytes = (st.f_blocks - st.f_bfree) * st.f_frsize
    avail_bytes = st.f_bavail * st.f_frsize
    level, pct = classify_disk_usage(used_bytes, avail_bytes, DISK_WARN_PCT, DISK_CRIT_PCT)
    free_gb = avail_bytes / 1024 ** 3
    line = f"Disk {DISK_MOUNT}: {pct:.1f}% used, {free_gb:.0f}G free"
    if level == "ok":
        ok_msgs.append(line)
    else:
        issues.append(f"[{level.upper()}] {line} (warn>={DISK_WARN_PCT:.0f}% crit>={DISK_CRIT_PCT:.0f}%)")
    return (level, pct)


def get_resend_key():
    """Resolve the Resend SMTP key without duplicating the secret. It already
    lives in Seerr's settings, and the cron runs this as root, so read it there
    (the same "read keys live from config, never hardcode" rule the skill uses
    for the *arr/Jellyfin keys). Env RESEND_API_KEY overrides for manual/test
    runs. Returns '' if neither is available, so the caller skips mail rather
    than crashing (e.g. a non-root manual run that can't read settings.json)."""
    env = os.environ.get("RESEND_API_KEY", "")
    if env:
        return env
    try:
        with open("/opt/arr/overseerr/settings.json") as f:
            d = json.load(f)
        return d["notifications"]["agents"]["email"]["options"].get("authPass", "")
    except Exception:
        return ""


def send_email(subject, body):
    """Send an alert through the Resend SMTP relay (the same relay Seerr and
    jfa-go use). The key is resolved live via get_resend_key() — never stored in
    this file, which is treated as public (see the skill). Best-effort: the
    caller catches failures so a mail hiccup can't mask the health result."""
    host = os.environ.get("SMTP_HOST", "smtp.resend.com")
    port = int(os.environ.get("SMTP_PORT", "465"))
    user = os.environ.get("SMTP_USER", "resend")
    password = get_resend_key()
    sender = os.environ.get("ALERT_EMAIL_FROM", "noreply@reset.dev")
    recipient = os.environ.get("ALERT_EMAIL_TO", "jamie@vialstudios.com")
    if not password:
        raise RuntimeError("Resend key unavailable (not in env or Seerr settings)")
    msg = EmailMessage()
    msg["Subject"] = subject
    msg["From"] = f"media-server <{sender}>"
    msg["To"] = recipient
    msg.set_content(body)
    with smtplib.SMTP_SSL(host, port, timeout=20) as s:
        s.login(user, password)
        s.send_message(msg)


def read_alert_state():
    try:
        with open(ALERT_STATE_FILE) as f:
            d = json.load(f)
        return d.get("level", "ok"), d.get("date", "")
    except Exception:
        return "ok", ""


def write_alert_state(level, date):
    try:
        with open(ALERT_STATE_FILE, "w") as f:
            json.dump({"level": level, "date": date}, f)
    except Exception as e:
        log(f"Warning: could not write {ALERT_STATE_FILE}: {e}")


# Check disk first — it's the highest-consequence failure and gates the email
# alert built after the report below.
disk_level, disk_pct = check_disk()

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

# Check Bazarr (subtitles) — liveness + Sonarr/Radarr wiring (see docstring)
bazarr_check("Bazarr", "http://localhost:6767", "/opt/arr/bazarr/config/config.yaml")

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

# Disk alert email — rate-limited by should_send_alert (fire on worsening,
# one nag/day while bad, one recovery note). Gated on disk level specifically;
# the body carries the full report so the mail is self-contained. Mail failures
# are non-fatal — they must not change the health exit code.
today = datetime.now().strftime("%Y-%m-%d")
prev_level, prev_date = read_alert_state()
send, is_recovery = should_send_alert(prev_level, prev_date, disk_level, today)
if send:
    if is_recovery:
        subject = f"[media-server] disk recovered — {disk_pct:.0f}% used"
    else:
        subject = f"[media-server] disk {disk_level.upper()} — {disk_pct:.0f}% used, {DISK_MOUNT}"
    try:
        send_email(subject, summary)
        log(f"Alert email sent: {subject}")
    except Exception as e:
        log(f"Warning: disk alert email failed: {e}")
write_alert_state(disk_level, today)

sys.exit(1 if issues else 0)
