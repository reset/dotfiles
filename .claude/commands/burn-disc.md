# /burn-disc [platform]

Burn a disc image (or archive containing one) to a physical CD.

Optionally pass a platform name for platform-specific guidance:
- `/burn-disc sega-cd`
- `/burn-disc ps1`
- `/burn-disc saturn`

## Tool

`~/tools/burn-disc` handles the full pipeline:
archive extraction → format detection → CHD/CCD conversion → burning via cdrdao.

```
~/tools/burn-disc '<path>' [--speed N] [--dry-run]
```

`--dry-run` extracts and converts without burning, then prints the generated
CUE. **Always run this first on an unfamiliar image** to verify track structure
before committing a disc.

## General workflow

1. Identify the file from the user's message. If ambiguous, list candidates in
   the current directory and ask.
2. Check that `cdrdao` is installed (`brew install cdrdao`). Install any other
   missing deps before re-running rather than stopping.
3. **Run `--dry-run` first.** Verify the CUE looks right — correct number of
   tracks, correct track types (data vs audio), sensible timestamps.
4. Run without `--dry-run` to burn. The tool auto-detects the drive's minimum
   write speed and uses that as the default.
5. If a drive isn't found, run `cdrdao scanbus` or `drutil list` and help the
   user identify the right device.

## Dependencies and how to install them

| Dep | Purpose | Install |
|-----|---------|---------|
| `cdrdao` | burn bin/cue | `brew install cdrdao` |
| `cdrecord` | burn ISO | `brew install cdrtools` |
| `7z` | extract .7z | `brew install p7zip` |
| `unrar` | extract .rar | `brew install unrar` |
| `chdman` | convert .chd | `brew install mame` |
| `python3` | convert .ccd | ships with macOS |

## Common issues

**No .cue alongside .bin** — look in the same directory for a differently-cased
or slightly differently-named .cue. If it's genuinely missing, warn the user:
a raw .bin burned without cue track data will produce a broken disc.

**CHD format** — the tool converts automatically once `chdman` is installed.

**CCD format** — the tool converts automatically using the Python parser. Note:
CCD files do NOT use `[TRACK N]` sections — they use `[Entry N]` with hex
`Point` fields (see format reference below). The parser handles this correctly.

**cdrdao "cannot open file" errors** — cdrdao resolves `FILE` paths in the CUE
relative to the *invocation directory*, not the CUE file's location. The tool
handles this by `cd`-ing into the CUE's directory before burning. If you're
running cdrdao manually, do the same.

**Burn failed mid-disc** — coaster. Start fresh with a new CD-R.

---

## CCD format reference

CloneCD `.ccd` files are the control file for `.img`/`.sub` disc images.
They use `[Entry N]` sections, not `[TRACK N]`. Track identity comes from
the `Point` field (hex):

- `0xa0` — first track number and disc type metadata
- `0xa1` — last track number
- `0xa2` — lead-out position
- `0x01`–`0x63` — actual track entries (track 1–99)

Key fields per track entry:
- `Control=0x04` → data track; `Control=0x00` → audio track
- `PLBA` → sector offset in the `.img` file where the track starts
- The `.sub` file contains subchannel data — not needed for standard burns

The `.img` file starts at PLBA 0 (track 1's INDEX 01). Audio tracks 2+
include a standard 150-sector (2-second) pregap in the image immediately
before their PLBA.

---

## Platform: Sega CD

Format: bin/cue or CCD/img (multi-track — data + audio). CHD is also common.
A bare ISO will be missing audio tracks and produce a broken disc.

Speed: Drive minimum (auto-detected). Sega CD laser assemblies are 30+ years
old; the slowest your drive supports is ideal.

Media: CD-R only — Sega CD hardware cannot read CD-RW. Verbatim or Taiyo
Yuden media has better compatibility than no-name discs.

**After burning:** the Sega CD has internal battery-backed memory that may
need to be formatted before it can save. If the system prompts about memory,
go to the Options or Memory Manager screen and format it before launching
the game.

Notable games with multi-track audio: Snatcher, Sonic CD, Popful Mail,
Shining Force CD. These will be silent or broken if burned as ISO.

---

## Platform: PS1

Format: bin/cue or CHD. Multi-track is common (audio tracks).

Speed: Drive minimum (auto-detected). PS1 drives are similarly aged.

Media: CD-R only. Some PS1 models are picky about dye type — Verbatim is the
safe choice.

Note: PS1 games require a modchip or soft-mod (e.g. FreePSXBoot) to boot
burned discs. Burning is only half the equation.

---

## Platform: Saturn

Format: bin/cue or CHD. Data-only (no audio tracks in most titles, but some
have CDDA).

Speed: Drive minimum (auto-detected). Saturn drives are notoriously finicky —
lower is better, and the tool defaults to the slowest your drive supports.

Media: CD-R only. High-quality media matters more here than on PS1/Sega CD.

Note: Saturn requires a mod board, cartridge mod (Action Replay with swap
trick), or Fenrir/Rhea ODE to boot burned discs.
