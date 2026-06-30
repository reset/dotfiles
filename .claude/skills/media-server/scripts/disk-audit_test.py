#!/usr/bin/env python3
"""Tests for disk-audit.py — specifically the pure logic that doesn't
require a live Transmission/Radarr/Sonarr/filesystem.

Coverage:
  - to_host_path: container path → host path mapping
  - seed_verdict: ratio + seed-time thresholds
  - hours: seconds-to-string formatting
  - group_library_duplicates: logical-duplicate detection
"""
import importlib.util
import os
import sys
import unittest

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
spec = importlib.util.spec_from_file_location('disk_audit', os.path.join(HERE, 'disk-audit.py'))
disk_audit = importlib.util.module_from_spec(spec)
spec.loader.exec_module(disk_audit)


class TestToHostPath(unittest.TestCase):
    def test_container_path_translated(self):
        self.assertEqual(
            disk_audit.to_host_path('/downloads/movies/Foo (2024)/foo.mkv'),
            '/var/lib/transmission-daemon/downloads/movies/Foo (2024)/foo.mkv',
        )

    def test_bare_container_root(self):
        self.assertEqual(disk_audit.to_host_path('/downloads'),
                         '/var/lib/transmission-daemon/downloads')

    def test_unrelated_path_unchanged(self):
        # Sanity: a path that's already host-style shouldn't be mangled.
        self.assertEqual(disk_audit.to_host_path('/opt/arr/foo'), '/opt/arr/foo')


class TestSeedVerdict(unittest.TestCase):
    def test_ratio_met_is_safe(self):
        self.assertEqual(disk_audit.seed_verdict(1.5, 60), 'safe')

    def test_time_met_is_safe(self):
        # Long-seeded with low ratio still safe per IPTorrents-style rule.
        self.assertEqual(disk_audit.seed_verdict(0.1, 100 * 3600), 'safe')

    def test_neither_met_borderline_when_close(self):
        # ratio 0.6 (>= 0.5 of target) → borderline
        self.assertEqual(disk_audit.seed_verdict(0.6, 0), 'borderline')

    def test_far_from_targets_is_risky(self):
        self.assertEqual(disk_audit.seed_verdict(0.1, 60), 'risky')


class TestHours(unittest.TestCase):
    def test_under_one_hour(self):
        self.assertEqual(disk_audit.hours(120), '2m')

    def test_under_100h(self):
        self.assertEqual(disk_audit.hours(7200), '2.0h')

    def test_days(self):
        # 100h converts to "4d" (100/24=4.16 → int 4)
        self.assertEqual(disk_audit.hours(100 * 3600), '4d')


class TestGroupLibraryDuplicates(unittest.TestCase):
    """group_library_duplicates groups library inodes by parsed (title, year)."""

    def test_single_inode_per_movie_no_duplicates(self):
        paths_per_inode = {
            1: ['/var/lib/transmission-daemon/downloads/movies/Foo (2024)/foo.mkv'],
            2: ['/var/lib/transmission-daemon/downloads/movies/Bar (2023)/bar.mkv'],
        }
        inode_size = {1: 1000, 2: 2000}
        groups = disk_audit.group_library_duplicates(paths_per_inode, inode_size)
        # Each title appears with one inode.
        for entries in groups.values():
            self.assertEqual(len(entries), 1)

    def test_two_inodes_same_movie_grouped(self):
        # Mission Impossible: clean folder + flat scene file, same parsed title+year.
        paths_per_inode = {
            10: ['/var/lib/transmission-daemon/downloads/movies/Mission Impossible Dead Reckoning Part One (2023)/file.mkv'],
            11: ['/var/lib/transmission-daemon/downloads/movies/Mission.Impossible.Dead.Reckoning.Part.One.2023.1080p.AMZN.mkv'],
        }
        inode_size = {10: 11_000_000_000, 11: 12_000_000_000}
        groups = disk_audit.group_library_duplicates(paths_per_inode, inode_size)
        # Should find one group with two inodes — both parsed to same title+year.
        dupes = [v for v in groups.values() if len(v) > 1]
        self.assertEqual(len(dupes), 1)
        self.assertEqual(len(dupes[0]), 2)

    def test_non_library_paths_excluded(self):
        # Files in staging dirs (radarr/, tv-sonarr/) shouldn't count.
        paths_per_inode = {
            20: ['/var/lib/transmission-daemon/downloads/radarr/Foo.2024.x264-GROUP/foo.mkv'],
        }
        inode_size = {20: 5000}
        groups = disk_audit.group_library_duplicates(paths_per_inode, inode_size)
        self.assertEqual(len(groups), 0)

    def test_same_parent_dir_not_a_duplicate(self):
        # Karate Kid Collection: 4 different films, all in one folder.
        # All entries share the same parent → must NOT be flagged as duplicate.
        paths_per_inode = {
            30: ['/var/lib/transmission-daemon/downloads/movies/The.Karate.Kid.Collection/Part1.mkv'],
            31: ['/var/lib/transmission-daemon/downloads/movies/The.Karate.Kid.Collection/Part2.mkv'],
            32: ['/var/lib/transmission-daemon/downloads/movies/The.Karate.Kid.Collection/Part3.mkv'],
        }
        inode_size = {30: 3_000_000_000, 31: 3_000_000_000, 32: 3_000_000_000}
        groups = disk_audit.group_library_duplicates(paths_per_inode, inode_size)
        self.assertEqual(len(groups), 0, 'collection in single folder should not be flagged')

    def test_non_video_files_excluded(self):
        # PNG screenshots / .nfo files in scene folders must not be flagged
        # as duplicates regardless of their parsed names.
        paths_per_inode = {
            50: ['/var/lib/transmission-daemon/downloads/movies/Foo (2024)/foo.mkv'],
            51: ['/var/lib/transmission-daemon/downloads/movies/Foo.2024.WEB-x264/Foo.nfo'],
            52: ['/var/lib/transmission-daemon/downloads/movies/Foo.2024.WEB-x264/Screens/screen0001.png'],
        }
        inode_size = {50: 5_000_000_000, 51: 1024, 52: 500_000}
        groups = disk_audit.group_library_duplicates(paths_per_inode, inode_size)
        # Only the .mkv counts; group has one entry, not a duplicate.
        for entries in groups.values():
            self.assertEqual(len(entries), 1)

    def test_different_parents_are_duplicates(self):
        # Same movie via two different folders → real duplicate.
        paths_per_inode = {
            40: ['/var/lib/transmission-daemon/downloads/movies/Foo (2024)/foo.mkv'],
            41: ['/var/lib/transmission-daemon/downloads/movies/Foo.2024.1080p.WEB-x264-GRP/foo.mkv'],
        }
        inode_size = {40: 5_000_000_000, 41: 5_000_000_000}
        groups = disk_audit.group_library_duplicates(paths_per_inode, inode_size)
        self.assertEqual(len(groups), 1)
        self.assertEqual(len(list(groups.values())[0]), 2)


if __name__ == '__main__':
    unittest.main(verbosity=2)
