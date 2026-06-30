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


if __name__ == '__main__':
    unittest.main(verbosity=2)
