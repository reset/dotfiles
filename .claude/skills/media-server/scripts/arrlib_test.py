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


if __name__ == '__main__':
    unittest.main(verbosity=2)
