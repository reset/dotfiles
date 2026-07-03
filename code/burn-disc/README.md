# burn-disc

Extracts and burns a disc image (or an archive containing one) to a physical
CD, with a live terminal dashboard for the extract → convert → burn pipeline.
A C# port of the original `~/bin/burn-disc` bash script.

## Usage

```
burn-disc [<file>] [--speed N] [--dry-run]
```

Run with **no arguments** to open the full-screen library browser: it scans
the local library (`~/roms`, override with `LIBRARY_PATH`) and the media
server (`MEDIA_HOST`/`MEDIA_PATH`), and lets you navigate with vim keys
(`j/k`, `u/d`, `gg`, `G`), filter with `/`, burn the selected title with
`Enter` (server titles download first), and eject with `e`. All progress
renders live in-frame.

Or pass a file to burn it directly:

- **Archives:** `.7z` `.zip` `.rar` (containing a disc image)
- **Images:** `.bin/.cue` `.chd` `.ccd/.img` `.iso`
- `--speed N` — burn speed (default: the drive's minimum, safest for aging
  retro hardware). Flags and options are case-insensitive.
- `--dry-run` — extract and convert only, print the generated CUE, and exit.

The library browser and the direct-file path share the same
extract → convert → burn pipeline; the browser draws its progress inside the
full-screen frame, the direct path uses standalone progress bars.

## Build

```
make build      # debug build
make test       # run the test suite
make run ARGS='game.7z --dry-run'
make publish    # release binary -> dist/burn-disc
make install    # publish, then point ~/bin/burn-disc at the release binary
```

## How it works

The tool orchestrates the same external programs the bash version did and
parses their output for live progress:

| Step | Tool | Notes |
|------|------|-------|
| Extract `.7z/.zip/.rar` | `7z`, `unzip`, `unrar` | `7z -bsp1` streams a percentage |
| Convert `.chd` → bin/cue | `chdman` (`brew install mame`) | streams `% complete` |
| Convert `.ccd/.img` → cue | *native* | ported parser — no `python3` dependency |
| Burn bin/cue | `cdrdao` | `--swap` for CCD sources (big-endian audio) |
| Burn `.iso` | `cdrecord` (`brew install cdrtools`) | single data track |

Two details carried over from the shell version because they are load-bearing:

- **CCD audio is big-endian.** CloneCD `.img` files store audio sectors
  byte-swapped; cdrdao is invoked with `--swap` for every CCD source or all
  audio tracks burn as white noise.
- **cdrdao resolves `FILE` paths relative to its working directory**, not the
  cue's location — so the burn runs from the cue's directory with a basename.

## Dependencies

`cdrdao`, `cdrtools` (cdrecord), `p7zip` (7z), `unrar`, and `mame` (chdman) —
install what you need via Homebrew. Each is checked lazily with an install hint
only when a given input format actually requires it.
