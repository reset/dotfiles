#!/usr/bin/env python3
"""Tests for arrlib helpers — pure functions + the Transmission RPC client.

The RPC client is the tricky one: it has I/O, but its caller is testable
via urllib.request.urlopen mocking. Coverage focuses on the session-token
handshake (the historically fragile part) and the 409-refresh-and-retry
path that earlier ad-hoc implementations missed entirely.
"""
import io
import json
import unittest
from unittest import mock

import urllib.error
import urllib.request

import arrlib


class TestToHostPath(unittest.TestCase):
    def test_container_path_translated(self):
        self.assertEqual(
            arrlib.to_host_path('/downloads/movies/Foo (2024)/foo.mkv'),
            '/var/lib/transmission-daemon/downloads/movies/Foo (2024)/foo.mkv',
        )

    def test_bare_container_root(self):
        self.assertEqual(arrlib.to_host_path('/downloads'),
                         '/var/lib/transmission-daemon/downloads')

    def test_unrelated_path_unchanged(self):
        self.assertEqual(arrlib.to_host_path('/opt/arr/foo'), '/opt/arr/foo')


def _http_409_with_sid(sid: str) -> urllib.error.HTTPError:
    """Build a fake HTTPError carrying X-Transmission-Session-Id."""
    headers = mock.Mock()
    headers.get = lambda k, default='': sid if k == 'X-Transmission-Session-Id' else default
    return urllib.error.HTTPError('http://x', 409, 'Conflict', headers, io.BytesIO(b''))


def _http_ok(body: dict):
    """Build a fake urlopen context manager returning the given JSON body."""
    cm = mock.MagicMock()
    cm.__enter__.return_value.read.return_value = json.dumps(body).encode()
    return cm


class TestMakeTransRpc(unittest.TestCase):
    """make_trans_rpc handles the Transmission session-token dance.

    Concretely: every first request 409s with a session ID in the header;
    subsequent requests must echo that header. The token can expire
    mid-session, at which point a request 409s again with a new token.
    The previous ad-hoc implementations defined session_id only inside
    the except block, so any deviation from the 'first request always
    409s' invariant would crash. The retry on mid-session 409 was also
    absent — a long script straddling a session reset would hard-fail.
    """

    def test_initial_session_probe_captures_token(self):
        # First call (probe) → 409 with session id.
        # Then rpc('torrent-get') → 200 with payload.
        responses = [
            _http_409_with_sid('SID_A'),
            _http_ok({'arguments': {'torrents': []}}),
        ]

        def fake_urlopen(req, *args, **kw):
            r = responses.pop(0)
            if isinstance(r, urllib.error.HTTPError):
                raise r
            return r

        with mock.patch('arrlib.urllib.request.urlopen', side_effect=fake_urlopen):
            rpc = arrlib.make_trans_rpc('http://x', 'u', 'p')
            result = rpc('torrent-get')
        self.assertEqual(result, {'torrents': []})

    def test_session_token_expiry_triggers_one_retry(self):
        # probe → 409 (SID_A), rpc → 409 (SID_B, expired), retry → 200.
        responses = [
            _http_409_with_sid('SID_A'),
            _http_409_with_sid('SID_B'),
            _http_ok({'arguments': {'ok': True}}),
        ]

        def fake_urlopen(req, *args, **kw):
            r = responses.pop(0)
            if isinstance(r, urllib.error.HTTPError):
                raise r
            return r

        with mock.patch('arrlib.urllib.request.urlopen', side_effect=fake_urlopen):
            rpc = arrlib.make_trans_rpc('http://x', 'u', 'p')
            result = rpc('torrent-get')
        self.assertEqual(result, {'ok': True})

    def test_persistent_409_after_retry_raises(self):
        # probe → 409, rpc → 409, retry → 409 again. Should raise; we
        # don't retry indefinitely.
        responses = [
            _http_409_with_sid('SID_A'),
            _http_409_with_sid('SID_B'),
            _http_409_with_sid('SID_C'),
        ]

        def fake_urlopen(req, *args, **kw):
            r = responses.pop(0)
            if isinstance(r, urllib.error.HTTPError):
                raise r
            return r

        with mock.patch('arrlib.urllib.request.urlopen', side_effect=fake_urlopen):
            rpc = arrlib.make_trans_rpc('http://x', 'u', 'p')
            with self.assertRaises(urllib.error.HTTPError):
                rpc('torrent-get')

    def test_unexpected_initial_success_does_not_crash(self):
        # Per spec, Transmission's first request 409s. But if a future
        # version returns 200 directly, the helper must not crash —
        # subsequent rpc calls should still attempt to work (and may
        # 409 themselves; that's handled by the retry path).
        responses = [
            _http_ok({'arguments': {}}),  # probe unexpectedly succeeded
            _http_ok({'arguments': {'ok': True}}),
        ]

        def fake_urlopen(req, *args, **kw):
            r = responses.pop(0)
            if isinstance(r, urllib.error.HTTPError):
                raise r
            return r

        with mock.patch('arrlib.urllib.request.urlopen', side_effect=fake_urlopen):
            rpc = arrlib.make_trans_rpc('http://x', 'u', 'p')
            result = rpc('torrent-get')
        self.assertEqual(result, {'ok': True})


class TestIsUpdateNotice(unittest.TestCase):
    """is_update_notice demotes the benign "update available" warning that
    pinned images raise on every poll, without swallowing real warnings."""

    def test_update_warning_is_notice(self):
        self.assertTrue(arrlib.is_update_notice(
            {"type": "warning", "message": "New update is available: v4.0.19.2979"}))

    def test_real_warning_is_not_notice(self):
        self.assertFalse(arrlib.is_update_notice(
            {"type": "warning", "message": "Indexers are unavailable due to failures"}))

    def test_error_is_not_notice(self):
        # Even if an error mentioned an update, an error must stay an issue.
        self.assertFalse(arrlib.is_update_notice(
            {"type": "error", "message": "Download client is not available"}))

    def test_missing_fields_safe(self):
        self.assertFalse(arrlib.is_update_notice({}))


class TestClassifyDiskUsage(unittest.TestCase):
    """classify_disk_usage mirrors df's Use% (over usable space, excluding
    root-reserved blocks) and buckets it into ok/warn/critical."""

    def test_ok_below_warn(self):
        # 50% used → ok.
        level, pct = arrlib.classify_disk_usage(50, 50, 85, 93)
        self.assertEqual(level, 'ok')
        self.assertAlmostEqual(pct, 50.0)

    def test_warn_at_threshold(self):
        # Exactly at the warn threshold is a warn (>=).
        level, _ = arrlib.classify_disk_usage(85, 15, 85, 93)
        self.assertEqual(level, 'warn')

    def test_warn_band(self):
        level, _ = arrlib.classify_disk_usage(90, 10, 85, 93)
        self.assertEqual(level, 'warn')

    def test_critical_at_threshold(self):
        level, _ = arrlib.classify_disk_usage(93, 7, 85, 93)
        self.assertEqual(level, 'critical')

    def test_critical_takes_precedence(self):
        # 96% is over both thresholds — must classify as the more severe one.
        level, pct = arrlib.classify_disk_usage(96, 4, 85, 93)
        self.assertEqual(level, 'critical')
        self.assertAlmostEqual(pct, 96.0)

    def test_uses_usable_not_raw_total(self):
        # avail is what's usable by unprivileged callers; reserved blocks that
        # neither `used` nor `avail` count must not dilute the percentage.
        # 800 used, 40 avail → 800/840 = 95.2%, critical — even though a raw
        # 916-total device would read ~87%.
        level, pct = arrlib.classify_disk_usage(800, 40, 85, 93)
        self.assertEqual(level, 'critical')
        self.assertAlmostEqual(pct, 95.238, places=2)

    def test_zero_usable_is_ok_not_crash(self):
        level, pct = arrlib.classify_disk_usage(0, 0, 85, 93)
        self.assertEqual(level, 'ok')
        self.assertEqual(pct, 0.0)


class TestShouldSendAlert(unittest.TestCase):
    """should_send_alert rate-limits disk emails: fire on worsening, at most
    one nag/day while bad, one recovery notice on return to ok."""

    def test_first_time_warn_fires(self):
        # No prior state (prev 'ok') → crossing into warn alerts.
        send, recovery = arrlib.should_send_alert('ok', '', 'warn', '2026-08-02')
        self.assertTrue(send)
        self.assertFalse(recovery)

    def test_worsening_same_day_fires(self):
        # Already alerted warn today; escalating to critical must re-alert.
        send, recovery = arrlib.should_send_alert('warn', '2026-08-02', 'critical', '2026-08-02')
        self.assertTrue(send)
        self.assertFalse(recovery)

    def test_same_level_same_day_suppressed(self):
        # Still critical, already nagged today → stay quiet.
        send, _ = arrlib.should_send_alert('critical', '2026-08-02', 'critical', '2026-08-02')
        self.assertFalse(send)

    def test_same_level_new_day_nags_once(self):
        # Still critical but a new day → one reminder.
        send, recovery = arrlib.should_send_alert('critical', '2026-08-02', 'critical', '2026-08-03')
        self.assertTrue(send)
        self.assertFalse(recovery)

    def test_recovery_after_alert_notifies_once(self):
        send, recovery = arrlib.should_send_alert('critical', '2026-08-02', 'ok', '2026-08-03')
        self.assertTrue(send)
        self.assertTrue(recovery)

    def test_ok_to_ok_stays_silent(self):
        # Never alerted, still fine → no recovery spam.
        send, recovery = arrlib.should_send_alert('ok', '2026-08-02', 'ok', '2026-08-02')
        self.assertFalse(send)
        self.assertTrue(recovery)  # flag set, but send is what gates the email

    def test_improving_but_still_bad_does_not_renag_same_day(self):
        # critical → warn same day: severity dropped, already alerted today,
        # so no new email (the situation is already known and improving).
        send, _ = arrlib.should_send_alert('critical', '2026-08-02', 'warn', '2026-08-02')
        self.assertFalse(send)


class TestOrphanScanTrustworthy(unittest.TestCase):
    """orphan_scan_trustworthy gates the audit's orphan list on Transmission's
    view being complete — the exact guard whose absence let a full-disk cleanup
    delete live seeds."""

    def test_healthy_state_is_trustworthy(self):
        ok, reasons = arrlib.orphan_scan_trustworthy(True, 0, 80.0)
        self.assertTrue(ok)
        self.assertEqual(reasons, [])

    def test_rpc_failure_is_untrustworthy(self):
        # The dangerous case: no held set → everything looks orphaned.
        ok, reasons = arrlib.orphan_scan_trustworthy(False, 0, 50.0)
        self.assertFalse(ok)
        self.assertTrue(any("RPC failed" in r for r in reasons))

    def test_critical_disk_is_untrustworthy(self):
        ok, reasons = arrlib.orphan_scan_trustworthy(True, 0, 96.0)
        self.assertFalse(ok)
        self.assertTrue(any("critically full" in r for r in reasons))

    def test_errored_torrents_untrustworthy(self):
        ok, reasons = arrlib.orphan_scan_trustworthy(True, 5, 70.0)
        self.assertFalse(ok)
        self.assertTrue(any("error state" in r for r in reasons))

    def test_multiple_reasons_accumulate(self):
        ok, reasons = arrlib.orphan_scan_trustworthy(False, 3, 99.0)
        self.assertFalse(ok)
        self.assertEqual(len(reasons), 3)

    def test_disk_threshold_boundary(self):
        # Just under the crit threshold with everything else healthy → trusted.
        ok, _ = arrlib.orphan_scan_trustworthy(True, 0, 94.9, crit_pct=95.0)
        self.assertTrue(ok)


class TestParseBazarrConfig(unittest.TestCase):
    """parse_bazarr_config reads a few fields from Bazarr's config.yaml without
    pyyaml. The tricky bits: `apikey` recurs in provider blocks (must not shadow
    auth.apikey), and the bridge-networking wiring (use_*/ip) is what a rebuild
    silently resets — the monitor asserts it, so the parser must read it exactly.
    """

    # Mirrors real config.yaml key order (yaml.safe_dump sort_keys=True):
    # auth, general, opensubtitlescom (a provider with its OWN apikey), radarr,
    # sonarr. Indentation and quoting match what Bazarr writes.
    WIRED = (
        "---\n"
        "auth:\n"
        "  apikey: AUTHKEY123\n"
        "  password: ''\n"
        "  type: null\n"
        "general:\n"
        "  use_radarr: true\n"
        "  use_sonarr: true\n"
        "opensubtitlescom:\n"
        "  apikey: PROVIDERKEY_SHOULD_BE_IGNORED\n"
        "  password: 'secret'\n"
        "radarr:\n"
        "  apikey: ''\n"
        "  ip: 192.168.1.28\n"
        "  port: 7878\n"
        "sonarr:\n"
        "  apikey: ''\n"
        "  ip: 192.168.1.28\n"
        "  port: 8989\n"
    )

    def test_reads_auth_apikey_not_provider_apikey(self):
        cfg = arrlib.parse_bazarr_config(self.WIRED)
        self.assertEqual(cfg['apikey'], 'AUTHKEY123')

    def test_reads_wiring_when_configured(self):
        cfg = arrlib.parse_bazarr_config(self.WIRED)
        self.assertTrue(cfg['use_sonarr'])
        self.assertTrue(cfg['use_radarr'])
        self.assertEqual(cfg['sonarr_ip'], '192.168.1.28')
        self.assertEqual(cfg['radarr_ip'], '192.168.1.28')

    def test_detects_rebuild_drift_defaults(self):
        # A fresh /opt/arr/bazarr defaults to use_*=False and ip=127.0.0.1 —
        # the exact state the monitor must flag as un-wired.
        drifted = (
            "auth:\n"
            "  apikey: K\n"
            "general:\n"
            "  use_radarr: false\n"
            "  use_sonarr: false\n"
            "radarr:\n"
            "  ip: 127.0.0.1\n"
            "sonarr:\n"
            "  ip: 127.0.0.1\n"
        )
        cfg = arrlib.parse_bazarr_config(drifted)
        self.assertFalse(cfg['use_sonarr'])
        self.assertFalse(cfg['use_radarr'])
        self.assertEqual(cfg['sonarr_ip'], '127.0.0.1')
        self.assertEqual(cfg['radarr_ip'], '127.0.0.1')

    def test_missing_fields_absent_from_result(self):
        # Partial/garbage config must not raise and must not invent keys.
        cfg = arrlib.parse_bazarr_config("general:\n  some_other_key: true\n")
        self.assertNotIn('apikey', cfg)
        self.assertNotIn('use_sonarr', cfg)
        self.assertNotIn('sonarr_ip', cfg)

    def test_empty_input(self):
        self.assertEqual(arrlib.parse_bazarr_config(''), {})


class TestChooseRelinkSource(unittest.TestCase):
    """choose_relink_source picks the library file to hardlink back for a
    missing torrent video file. The historically-missed case is (3): a file
    the *arr renamed on import, invisible to a name lookup but recoverable by
    exact size. Wrong guesses are non-destructive (verify rejects them), so the
    only real failure mode is a *misleading* match — hence uniqueness gates."""

    def test_name_plus_exact_size(self):
        src, method = arrlib.choose_relink_source(
            1000, [('/lib/a.mkv', 1000)], ['/lib/a.mkv'])
        self.assertEqual((src, method), ('/lib/a.mkv', 'name+size'))

    def test_name_within_tolerance(self):
        # 0.5% off — inside the 1% default tolerance.
        src, method = arrlib.choose_relink_source(
            1000, [('/lib/a.mkv', 1005)], [])
        self.assertEqual((src, method), ('/lib/a.mkv', 'name+size'))

    def test_single_name_no_size_confirmation(self):
        # Only one same-name file, size doesn't match — trust the name anyway.
        src, method = arrlib.choose_relink_source(
            1000, [('/lib/a.mkv', 500)], [])
        self.assertEqual((src, method), ('/lib/a.mkv', 'name'))

    def test_renamed_on_import_unique_size(self):
        # The blind spot: no name match, exactly one exact-size library file.
        src, method = arrlib.choose_relink_source(
            1000, [], ['/lib/RenamedByRadarr.mkv'])
        self.assertEqual((src, method), ('/lib/RenamedByRadarr.mkv', 'size'))

    def test_size_fallback_requires_uniqueness(self):
        # Two files share the exact size — refuse to guess.
        src, method = arrlib.choose_relink_source(
            1000, [], ['/lib/x.mkv', '/lib/y.mkv'])
        self.assertEqual((src, method), (None, 'ambiguous'))

    def test_multiple_names_none_size_match(self):
        src, method = arrlib.choose_relink_source(
            1000, [('/lib/a.mkv', 10), ('/other/a.mkv', 20)], [])
        self.assertEqual((src, method), (None, 'ambiguous'))

    def test_nothing_found(self):
        src, method = arrlib.choose_relink_source(1000, [], [])
        self.assertEqual((src, method), (None, 'not-found'))

    def test_zero_expected_size_disables_size_fallback(self):
        # A 0-byte member (e.g. an empty .nfo) must not size-match anything.
        src, method = arrlib.choose_relink_source(0, [], ['/lib/x.mkv'])
        self.assertEqual((src, method), (None, 'ambiguous'))

    def test_name_match_preferred_over_size(self):
        # A confident name+size match wins even when other exact-size files exist.
        src, method = arrlib.choose_relink_source(
            1000, [('/lib/a.mkv', 1000)], ['/lib/a.mkv', '/lib/b.mkv'])
        self.assertEqual((src, method), ('/lib/a.mkv', 'name+size'))


if __name__ == '__main__':
    unittest.main(verbosity=2)
