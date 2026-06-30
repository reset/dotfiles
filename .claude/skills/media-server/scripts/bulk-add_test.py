#!/usr/bin/env python3
"""Tests for bulk-add.py — parser, match validation, ignore handling."""
import importlib.util
import os
import sys
import tempfile
import unittest

# Load bulk-add.py as a module (hyphen in filename → importlib).
HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
spec = importlib.util.spec_from_file_location('bulk_add', os.path.join(HERE, 'bulk-add.py'))
bulk_add = importlib.util.module_from_spec(spec)
spec.loader.exec_module(bulk_add)


class TestParseTitleYear(unittest.TestCase):
    """parse_title_year must extract a clean title and (optional) year.

    Real-world inputs come from two sources:
      - scene-named folders: `Better.Off.Dead.1985.720p.BluRay.x264-x0r`
      - Radarr/Sonarr-renamed folders: `Avatar - Fire and Ash (2025)`
      - per-episode scene folders: `What.We.Do.in.the.Shadows.S01E01.720p.WEB.H264-MEMENTO`
    """

    def test_radarr_renamed_movie(self):
        self.assertEqual(
            bulk_add.parse_title_year('Avatar - Fire and Ash (2025)'),
            ('Avatar Fire and Ash', 2025),
        )

    def test_scene_named_movie_with_year(self):
        self.assertEqual(
            bulk_add.parse_title_year('Better.Off.Dead.1985.720p.BluRay.x264-x0r'),
            ('Better Off Dead', 1985),
        )

    def test_scene_named_movie_filename(self):
        self.assertEqual(
            bulk_add.parse_title_year('Avatar Fire and Ash 2025 1080p WEB-DL x264 DuaL-TURKO.mkv'),
            ('Avatar Fire and Ash', 2025),
        )

    def test_tv_per_episode_strips_s01e01_marker(self):
        # The bug: this used to leave "S01E01" in the search term,
        # which caused Sonarr to fuzzy-match nonsense.
        self.assertEqual(
            bulk_add.parse_title_year('What.We.Do.in.the.Shadows.S01E01.720p.WEB.H264-MEMENTO'),
            ('What We Do in the Shadows', None),
        )

    def test_tv_season_range_marker(self):
        self.assertEqual(
            bulk_add.parse_title_year('Nathan.for.You.S01-S04.1080p.AMZN.WEB-DL.DD+2.0.x264-SiGMA'),
            ('Nathan for You', None),
        )

    def test_tv_with_bracket_scene_tag(self):
        # The bug: this used to keep "[apopleptc]" or leak quality info,
        # producing the wrong Sonarr match (live-action Cowboy Bebop 2021).
        self.assertEqual(
            bulk_add.parse_title_year('[apopleptc] Cowboy Bebop Dual Audio (10 bit 1080p)'),
            ('Cowboy Bebop', None),
        )

    def test_tv_with_decade_in_title(self):
        # "70s" is not a year. The parser must keep it in the title.
        # Trailing [NO RAR] tag is informational, not a title element.
        title, year = bulk_add.parse_title_year('That.70s.Show.COMPLETE.WS.BDRip-Scene [NO RAR]')
        self.assertIn('70s Show', title)
        self.assertIsNone(year)

    def test_hyphenated_scene_release(self):
        title, year = bulk_add.parse_title_year('Beavis.and.Butthead-COMPLETE-MiXED')
        self.assertEqual(title, 'Beavis and Butthead')
        self.assertIsNone(year)

    def test_clean_title_no_extras(self):
        self.assertEqual(
            bulk_add.parse_title_year('Dragon Ball Kai'),
            ('Dragon Ball Kai', None),
        )

    def test_per_episode_with_year_in_show_name(self):
        # The Curse (2023) - "2023" IS the year here, not part of the title.
        self.assertEqual(
            bulk_add.parse_title_year('The.Curse.2023.S01E01.1080p.HEVC.x265-MeGusta'),
            ('The Curse', 2023),
        )


class TestBestMatch(unittest.TestCase):
    """best_match must reject lookups that don't actually match the parsed title/year."""

    def test_year_required_when_present(self):
        # Parsed year 1998, Sonarr returns the 2021 live-action remake first.
        # Must NOT accept it.
        results = [
            {'title': 'Cowboy Bebop (live-action)', 'year': 2021, 'tvdbId': 1},
            {'title': 'Cowboy Bebop', 'year': 1998, 'tvdbId': 2},
        ]
        hit = bulk_add.best_match('Cowboy Bebop', 1998, results)
        self.assertIsNotNone(hit)
        self.assertEqual(hit['tvdbId'], 2)

    def test_year_mismatch_returns_none(self):
        # Parsed year 1998, no result has that year. Must bail.
        results = [{'title': 'Cowboy Bebop', 'year': 2021, 'tvdbId': 1}]
        self.assertIsNone(bulk_add.best_match('Cowboy Bebop', 1998, results))

    def test_yearless_match_requires_title_overlap(self):
        # No parsed year. Sonarr returns nonsense as result[0]. Must reject.
        results = [
            {'title': 'Eminence in Shadow', 'year': 2022, 'tvdbId': 99},
            {'title': 'What We Do in the Shadows', 'year': 2019, 'tvdbId': 7},
        ]
        hit = bulk_add.best_match('What We Do in the Shadows', None, results)
        self.assertIsNotNone(hit)
        self.assertEqual(hit['tvdbId'], 7)

    def test_yearless_no_overlap_returns_none(self):
        # No year, no result with overlapping title → don't add anything.
        results = [{'title': 'Some Other Show', 'year': 2022, 'tvdbId': 99}]
        self.assertIsNone(bulk_add.best_match('Nathan for You', None, results))

    def test_empty_results(self):
        self.assertIsNone(bulk_add.best_match('Anything', None, []))
        self.assertIsNone(bulk_add.best_match('Anything', 2020, []))

    def test_closer_length_overlap_wins_dragonball(self):
        # Folder "Dragonball Z Kai Season 1 4" should match "Dragon Ball Z Kai"
        # (length 14, closer) over "Dragon Ball Z" (length 11, further).
        results = [
            {'title': 'Dragon Ball Z', 'year': 1989, 'tvdbId': 81472},
            {'title': 'Dragon Ball Z Kai', 'year': 2009, 'tvdbId': 121361},
        ]
        hit = bulk_add.best_match('Dragonball Z Kai Season 1 4', None, results)
        self.assertEqual(hit['tvdbId'], 121361)

    def test_closer_length_overlap_wins_beavis(self):
        # Folder "Beavis and Butthead" should match the plain show, not the
        # superset "Mike Judge's Beavis and Butt-Head".
        results = [
            {'title': "Mike Judge's Beavis and Butt-Head", 'year': 2022, 'tvdbId': 99},
            {'title': 'Beavis and Butt-Head', 'year': 1993, 'tvdbId': 71665},
        ]
        hit = bulk_add.best_match('Beavis and Butthead', None, results)
        self.assertEqual(hit['tvdbId'], 71665)

    def test_exact_match_wins_over_superset(self):
        # The (1998) anime is the exact match. The (2021) live-action is a
        # superset. Without a parsed year, the closer length wins.
        results = [
            {'title': 'Cowboy Bebop (live-action)', 'year': 2021, 'tvdbId': 1},
            {'title': 'Cowboy Bebop', 'year': 1998, 'tvdbId': 2},
        ]
        hit = bulk_add.best_match('Cowboy Bebop', None, results)
        self.assertEqual(hit['tvdbId'], 2)


class TestShouldSkipFolder(unittest.TestCase):
    """should_skip_folder must honor the Jellyfin .ignore convention."""

    def test_skip_when_ignore_file_present(self):
        with tempfile.TemporaryDirectory() as d:
            with open(os.path.join(d, '.ignore'), 'w') as f:
                f.write('')
            self.assertTrue(bulk_add.should_skip_folder(d))

    def test_dont_skip_normal_folder(self):
        with tempfile.TemporaryDirectory() as d:
            self.assertFalse(bulk_add.should_skip_folder(d))


if __name__ == '__main__':
    unittest.main(verbosity=2)
