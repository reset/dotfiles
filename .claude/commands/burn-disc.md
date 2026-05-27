# /burn-disc [platform]

Burn a disc image (or archive containing one) to a physical CD.

Optionally pass a platform name for platform-specific guidance:
- `/burn-disc sega-cd`
- `/burn-disc ps1`
- `/burn-disc saturn`

## Tool

`~/tools/burn-disc` handles the full pipeline:
archive extraction → format detection → CHD conversion → burning via cdrdao.

```
~/tools/burn-disc '<path>' [--speed N]
```

## General workflow

1. Identify the file from the user's message. If ambiguous, list candidates in
   the current directory and ask.
2. Check that `cdrdao` is installed (`brew install cdrdao`). Install any other
   missing deps before re-running rather than stopping.
3. Run the tool. It auto-detects the drive's minimum write speed and uses that
   as the default — slower is more compatible with aging retro hardware. Only
   override with `--speed N` if the user has a specific reason.
4. If a drive isn't found, run `cdrdao scanbus` or `drutil list` and help the
   user identify the right device.

## Dependencies and how to install them

| Dep | Purpose | Install |
|-----|---------|---------|
| `cdrdao` | burn bin/cue | `brew install cdrdao` |
| `cdrecord` | burn ISO | `brew install cdrtools` |
| `7z` | extract .7z | `brew install p7zip` |
| `unrar` | extract .rar | `brew install unrar` |
| `chdman` | convert .chd | `brew install mame` |

## Common issues

**No .cue alongside .bin** — look in the same directory for a differently-cased
or slightly differently-named .cue. If it's genuinely missing, warn the user:
a raw .bin burned without cue track data will produce a broken disc.

**CHD format** — the tool converts automatically once `chdman` is installed.

**Burn failed mid-disc** — coaster. Start fresh with a new CD-R.

---

## Platform: Sega CD

Format: always bin/cue (multi-track — data + audio). CHD is also common.
A bare ISO will be missing the audio tracks and produce a broken disc.

Speed: Drive minimum (auto-detected). Sega CD laser assemblies are 30+ years
old; the slowest your drive supports is ideal. The tool defaults to this
automatically.

Media: CD-R only — Sega CD hardware cannot read CD-RW. Verbatim or Taiyo
Yuden media has better compatibility than no-name discs.

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
