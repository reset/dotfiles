---
name: dotfiles
description: Manage Jamie's dotfiles — the bare-repo home-directory setup at ~/.dotfiles/ (origin github.com/reset/dotfiles). Use whenever the user wants to add, edit, install, or persist anything that lives under $HOME and should travel across machines. Triggers on "add X to my dotfiles", "commit my .zshrc / .gitconfig / etc.", "install X via brew/cask/mas (and remember it)", "add a script to ~/bin", "find the App Store ID for X", "what's in my dotfiles", "set up a new mac", "rerun setup.sh", "what's the dot command", and anywhere the user is reaching for git in $HOME (use `dot` instead). Also fires when installing or removing GUI apps or CLI tools that should persist for future machine reprovisioning — the change isn't done until the ~/Brewfile (for packages) or setup.sh has been updated and pushed.
---

# Dotfiles

Jamie's home directory is a Git bare repo at `~/.dotfiles/` with `$HOME` as the working tree. Origin: `github.com/reset/dotfiles`. Everything in this skill assumes the `dot` wrapper handles the bare-repo plumbing.

The single most important habit: **install + persist is one operation, not two.** When Jamie installs something he wants on his machines (a brew package, a cask, a Mac App Store app, a `~/bin` script), the work isn't done until it's recorded in the tracked manifest — the `~/Brewfile` for packages, `bin/setup.sh` for everything else — and the change is pushed to the dotfiles remote. Otherwise the next machine reprovision loses it silently.

## The `dot` wrapper

`dot` is the only correct way to interact with the dotfiles repo. It's a script at `~/bin/dot` that wraps `git --git-dir=$HOME/.dotfiles --work-tree=$HOME`. Use it the same way you'd use `git`:

```bash
dot status              # tracked-file changes only
dot ls-files            # list every tracked file
dot diff -- <path>      # diff a specific path (the `--` is required when ambiguous)
dot add <path>          # stage
dot commit -m "..."     # commit
dot push                # push to github.com:reset/dotfiles
```

Two gotchas:

- **`dot status` hides untracked files** — but not by accident. The bootstrap procedure runs `git config --local status.showUntrackedFiles no` against the bare repo so `$HOME`'s thousands of untracked files don't drown out the signal. If a fresh clone is showing all of `$HOME`, that config step was skipped — re-run it. To check whether a specific file is tracked, use `dot ls-files -- "$HOME"` or `dot status --short -- <path>`.
- **`dot ls-files` scopes to the current directory.** Like plain `git ls-files`, it only lists tracked files under CWD — so when you're sitting in a project dir (not `$HOME`), a bare `dot ls-files` or `dot ls-files | grep <name>` returns nothing and looks like "not tracked." Always pass an absolute pathspec: `dot ls-files -- "$HOME"` for the full set, `dot ls-files -- "$HOME/Brewfile"` to test one file. Same applies to `dot grep` — scope it with `-- "$HOME"`.
- **`dot diff <path>` may complain "ambiguous argument"** when the path isn't already known to git (new file, recently moved). Use `dot diff -- <path>` to disambiguate.

Never run `git` directly in `$HOME` for dotfile work — it'll either fail (no `.git` dir) or surprise you (if you happen to be inside a nested repo). Always `dot`.

## What's in the dotfiles

Run `dot ls-files` to see the current tracked set. The structure as of this writing:

| Path | Purpose |
|------|---------|
| `bin/dot` | The wrapper script itself |
| `bin/setup.sh` | Bootstrap a fresh Mac/Ubuntu — installs Homebrew, runs `brew bundle` against `~/Brewfile`, sets up symlinks/system config/framework clones |
| `Brewfile` | The package manifest — every `brew`/`cask`/`mas` install, consumed by `brew bundle`. This is where package installs are recorded (not inline in `setup.sh`) |
| `bin/update.sh` | Upgrade installed packages (`brew update && brew upgrade`) |
| `.zshrc`, `.zprofile`, `.profile` | Shell config |
| `.gitconfig`, `.gitignore-global` | Git config (aliases, signing, ignore patterns) |
| `.tmux.conf`, `.tmux.conf.local` | tmux config (gpakosz/.tmux fork) |
| `.vimrc`, `.ctags`, `.editorconfig` | Editor config |
| `.p10k.zsh`, `.fzf.zsh`, `.git-completion.zsh` | Shell extensions |
| `.gnupg/gpg.conf`, `.gnupg/gpg-agent.conf` | GPG / commit signing |
| `.config/alacritty/alacritty.toml` | Terminal emulator |
| `.config/direnv/direnv.toml`, `.config/direnv/direnvrc` | direnv |
| `.claude/CLAUDE.md`, `CLAUDE.md`, `README.md` | Personal Claude config, home-dir notes, bootstrap docs |
| `.claude/skills/<name>/` | Personal Claude Code skills (`dotfiles`, `grab`, `media-server`, `seo-audit`, `static-site-engineer`, `wp-migrate`) — tracked so they bootstrap on a fresh machine |
| `Library/Application Support/Code/User/settings.json` | VS Code settings |

If a file is in `$HOME` but not in this list, it's not tracked. Adding it = `dot add` it (covered below).

## Files where the edit target is indirect

Two tracked files look editable but you should edit a sibling instead — touching the wrong one either gets overwritten on regeneration or breaks an upstream sync.

- **`.tmux.conf`** is the gpakosz/.tmux upstream config. **Don't edit it.** All tmux customizations go in `.tmux.conf.local` — the upstream config sources it and exposes documented hooks for overrides.
- **`.p10k.zsh`** is generated by `p10k configure` but is heavily customized. **Don't regenerate without preserving.** If a fresh `p10k configure` run is needed, diff the new file against the tracked version and merge by hand before committing.

`.fzf.zsh` and `.git-completion.zsh` are also generated upstream artifacts but rarely change. If they do, treat the new version as a candidate, diff against tracked, decide whether the change is wanted.

## External clones that aren't part of the dotfiles

`bin/setup.sh` clones a handful of repos directly into `$HOME` to bootstrap the shell environment. **None of these are tracked by the dotfiles repo** — they live as independent clones with their own upstream remotes:

- `~/.oh-my-zsh/` — oh-my-zsh framework
- `~/.tmux/` — gpakosz/.tmux source (the `.tmux.conf` at home root is a symlink target into this clone's docs, but only `.tmux.conf` and `.tmux.conf.local` are tracked)
- `~/.tmux/plugins/tpm/` — tmux plugin manager
- `~/.config/base16-shell/` — base16 color schemes
- `~/.oh-my-zsh/custom/themes/powerlevel10k/` — p10k theme source
- `~/.vim/autoload/plug.vim` — vim-plug, a single downloaded file

Trying to `dot add` a file inside any of these directories won't work the way you'd expect — the file would be tracked at a path that the next `setup.sh` run would clobber. If a customization needs to persist, it goes into a tracked file at `$HOME` root (`.zshrc`, `.tmux.conf.local`, `.vimrc`) that *references* the cloned content.

## Adding a new file to the dotfiles

```bash
# 1. Create or edit the file at its real location under $HOME
vim ~/.config/whatever/config.toml

# 2. Stage it
dot add ~/.config/whatever/config.toml

# 3. Commit and push
dot commit -m "add whatever config"
dot push
```

That's it. The file lives where it needs to live (the app reads it from the real path), and the bare repo tracks it.

For new scripts in `~/bin/`: write the script with no extension (the shebang declares the language), `chmod +x`, sanity-check it, then `dot add` + commit + push.

## `~/Brewfile` — the package manifest

The **`~/Brewfile`** (tracked as `Brewfile` at the repo root) is the source of truth for "what should be installed on a fresh Jamie machine." It's a standard `brew bundle` Brewfile — `brew`, `cask`, `mas`, and `tap` lines. `bin/setup.sh` doesn't list packages inline; both `_install_packages_macos` and `_install_packages_ubuntu` just run `brew bundle install --file="$HOME/Brewfile"`. Adding a package = adding one line to the Brewfile, in the right lane.

### Brewfile structure

Two blocks, split by platform. The top block is the cross-platform set (installs on macOS and Linux/linuxbrew alike); the `if OS.mac?` block holds everything macOS-only — GUI casks, `mas` apps, and formulae with no useful Linux bottle. The guard keeps `brew bundle` clean on a Linux host.

```ruby
# Cross-platform CLI tools — macOS + Linux
brew "awscli"
brew "gh"
brew "git"
...

if OS.mac?
  tap "jaxxstorm/tap"
  brew "colima"                        # macOS-only formula
  brew "mas"                           # Mac App Store CLI
  cask "1password"
  cask "alacritty"
  ...
  mas "1Password for Safari", id: 1569813296
  mas "Windows App", id: 1295203466
end
```

### Picking a lane

**Brew is always preferred over the App Store when both options exist.** Cask installs don't require an Apple ID, install faster, and update through the same `brew upgrade` path as everything else. Only fall back to `mas` when there's no brew option.

The decision tree:

1. **`brew "<name>"`** if it's a CLI tool. Cross-platform tools go in the top block; macOS-only ones inside `if OS.mac?`. Alphabetized — slot the new entry in.
2. **`cask "<name>"`** if it's a GUI app and `brew search --cask <name>` finds it. Most GUI apps land here (Slack, Steam, Discord, VS Code, 1Password desktop, etc.). Casks are macOS-only, so they live inside the `if OS.mac?` block. One line each, alphabetized.
3. **`mas "<name>", id: <id>`** only when neither of the above is available. The classic cases: **Safari extensions** (App Store distribution is mandated by Apple's Safari extension entitlement), **Apple-distributed apps** (Final Cut Pro, Xcode), and a handful of paid apps the publisher only ships through the App Store (Magnet, etc.). Also inside `if OS.mac?`.

When in doubt: run `brew search --cask <name>` first. If it returns a hit, use `cask`. Only run `mas search <name>` after confirming brew has no answer.

### Finding a Mac App Store ID

```bash
mas search "<app name>"
```

First column is the numeric ID. Pick the right row (sometimes there are multiple matches — e.g. searching "1Password" returns both the manager and the Safari extension). Use that ID in the `id:` field: `mas "<human name>", id: <id>`.

### `mas` line format

`mas` lines carry a human-readable name **and** the numeric id — `mas "Magnet", id: 441258766` — so the name is self-documenting. `brew` and `cask` lines don't need a comment since the package name is already the identifier. If a cask's slug is cryptic, a trailing `# <what it is>` comment is fine.

### Applying + verifying a Brewfile change

`brew bundle` is declarative and idempotent — after editing the Brewfile you can re-run `brew bundle install --file="$HOME/Brewfile"` to converge this machine (it skips anything already installed). Removing a line does **not** uninstall the package; run the matching `brew uninstall`/`brew uninstall --cask`/`mas uninstall` yourself if you want it gone from the current machine now (`brew bundle cleanup` would prune everything not in the file, which is broader than you usually want).

### Extension points in `setup.sh`

Packages live in the Brewfile, but `setup.sh` still owns everything that isn't a package install. When a change is one of these, edit the function directly:

- **`_configure_system_macos`** — runs `defaults write` for macOS system preferences and the caps-lock-to-control remap. To make a new `defaults write` persist across machines, add it here. As of this writing the only entry is `NSGlobalDomain com.apple.swipescrolldirection -bool false` (natural scroll off).
- **`_install_symlinks`** — creates `/usr/local/bin/git`, `/usr/local/bin/gpg`, `/usr/local/bin/gpg-agent`, `/usr/local/bin/zsh`, and `/usr/local/bin/pinentry` as symlinks pointing into the Homebrew prefix. This exists because **`.gitconfig` references `/usr/local/bin/gpg` directly** (`gpg.program`) — the symlink lets that path resolve regardless of whether Homebrew is at `/opt/homebrew` (Apple Silicon) or `/usr/local` (Intel). If you change a binary path in `.gitconfig` or move Homebrew, both must move together.
- **`setup_home`** — clones external repos (oh-my-zsh, gpakosz/.tmux, base16-shell, powerlevel10k, vim-plug). See "External clones" above. Add new framework clones here, not as `dot add` operations.
- **`_caps_lock_remap_macos`** — installs a LaunchDaemon plist that maps Caps Lock to Control via `hidutil`. Sudo-required, idempotent (overwrites the plist).

## The "install now + persist" workflow

When the user says something like "install X" with the implicit expectation that it persists across machines:

1. **Pick the lane** (`brew`, `cask`, or `mas` — for mas, run `mas search "<name>"` to get the id first).
2. **Run the install** so it's on this machine right now (`brew install <name>`, `brew install --cask <name>`, or `mas install <id>`).
3. **Add the corresponding line to `~/Brewfile`** in the right lane — cross-platform `brew` at the top, everything macOS-only (casks, `mas`, macOS-only formulae) inside the `if OS.mac?` block. Keep the alphabetization.
4. **Verify with `dot diff -- "$HOME/Brewfile"`** that the change is what you expect.
5. **Commit and push:** `dot add "$HOME/Brewfile" && dot commit -m "..." && dot push`.

The reverse — "remove / uninstall X" — is the mirror image: delete the line from `~/Brewfile`, run the matching `brew uninstall`/`brew uninstall --cask`/`mas uninstall` to drop it from this machine, then commit + push.

If the user just wants to install something temporarily (testing a tool, evaluating an app), don't touch the Brewfile. Ask if it's unclear.

## Fresh-machine bootstrap

Setting up a new Mac from scratch is a multi-stage operation. The order matters — most steps depend on a prior one having run. The README at `~/README.md` is the canonical reference; what follows is the same procedure with the rationale called out.

### Stage 1: macOS prerequisites (before touching the dotfiles)

```bash
sudo dseditgroup -o edit -a ${USERNAME} -t user admin   # add to admin group
xcode-select --install                                  # Command Line Tools (gives us git)
```

Without Command Line Tools, the next step has no `git`. The admin group lets sudo work for the symlinks step later.

### Stage 2: Clone the dotfiles into `$HOME`

The bare-repo trick: clone into a temp dir, point the git-dir at `~/.dotfiles/`, then rsync the working tree into `$HOME`.

```bash
git clone --separate-git-dir=$HOME/.dotfiles https://github.com/reset/dotfiles.git tmpdotfiles
$(which git) --git-dir=$HOME/.dotfiles/ --work-tree=$HOME config --local status.showUntrackedFiles no
rsync --recursive --verbose --exclude '.git' tmpdotfiles/ $HOME/
rm -r tmpdotfiles
```

The `status.showUntrackedFiles no` line is what makes `dot status` usable — without it, `dot status` shows everything in `$HOME`. If a fresh machine has chatty `dot status`, this config got skipped and needs to run.

### Stage 3: Run setup

```bash
bash ~/bin/setup.sh
```

Order inside `setup.sh`: Homebrew → packages (`brew bundle install` against `~/Brewfile` — formulae + casks + mas) → macOS system config → `/usr/local/bin` symlinks → `$HOME` framework clones (oh-my-zsh, tmux, base16, p10k, vim-plug). Don't reorder — packages depend on Homebrew, symlinks depend on packages, framework clones depend on the directory layout being settled.

### Stage 4: Update everything

```bash
bash ~/bin/update.sh
```

Brings Homebrew packages up to current versions. Idempotent.

### Stage 5: Manual steps `setup.sh` can't do

These require human-in-the-loop interaction or secrets that aren't in the repo:

- **Import the GPG signing key** (`A3D661E75334E988`) — the private key isn't in the dotfiles. Pull it from 1Password / a secure backup and `gpg --import`. Without this, `git commit` fails because `commit.gpgsign = true`.
- **Place SSH keys** at `~/.ssh/id_ed25519` and `~/.ssh/admin_rsa` — `.profile` auto-loads them on shell start, but they have to be there first.
- **Populate the secrets files** (none of these are tracked):
  - `~/.config/omg/env` — sourced by `.profile`
  - `~/.config/omg/secrets` — sourced by `.profile`
  - `~/.secrets` — sourced by `.zshrc`
- ~~Install vim plugins~~ — no longer manual: `setup.sh` runs a headless `:PlugInstall` right after downloading `plug.vim`. Only needed by hand after editing the plug list in `.vimrc`.
- **Sign in to the Mac App Store** — `mas install` requires a logged-in Apple ID. If the App Store hasn't been opened and signed into yet, `mas install` lines fail silently.
- **Sign in to 1Password, Slack, etc.** — apps installed via cask/mas come up unconfigured.

### Windows

The README documents a Windows path (Gpg4win via chocolatey, FiraCode font), but `setup.sh` has no Windows lane — only macOS and Ubuntu. If a Windows lane is needed, that's a `setup.sh` enhancement, not a documented workflow yet.

## What does NOT belong in dotfiles

- **Project-specific config.** Anything tied to a specific repo (`.envrc`, project-local `.editorconfig`, project Claude configs) lives in that repo, not in dotfiles.
- **Secrets.** API keys, OAuth tokens, SSH private keys — never tracked. Secrets live in `~/.config/omg/secrets` (gitignored) or 1Password.
- **Local state.** Caches, history files, app databases. They re-generate on a fresh machine.
- **Anything Nix-managed.** The OMG dev shell is Nix-driven and reproduces itself per-project; don't try to hoist Nix-installed CLI tools into setup.sh.

If you're unsure whether something belongs, ask. The bias is "narrow tracking" — only files Jamie actually edits and wants to carry to a new machine.

## Common workflows quick-reference

| Task | Commands |
|------|----------|
| Add a new tracked file | `dot add <path>` → `dot commit -m "..."` → `dot push` |
| Edit an already-tracked file | edit it → `dot diff -- <path>` → `dot add <path>` → commit + push |
| List everything tracked | `dot ls-files -- "$HOME"` (bare `dot ls-files` scopes to CWD) |
| Check if file X is tracked | `dot ls-files -- "$HOME/<path>"` or `dot status --short -- <path>` |
| Customize tmux | edit `~/.tmux.conf.local` (NOT `.tmux.conf`) → commit |
| Find a brew package | `brew search <name>` (formula) or `brew search --cask <name>` (GUI) |
| Find a Mac App Store ID | `mas search "<app name>"` (first column = ID) — only when brew has no answer |
| Install + persist a brew tool | `brew install <name>` → add `brew "<name>"` to `~/Brewfile` → commit |
| Install + persist a cask | `brew install --cask <name>` → add `cask "<name>"` inside the `if OS.mac?` block in `~/Brewfile` → commit |
| Install + persist a mas app | `mas search` to find ID → `mas install <id>` → add `mas "<name>", id: <id>` inside `if OS.mac?` in `~/Brewfile` → commit |
| Remove + un-persist a package | delete its line from `~/Brewfile` → `brew uninstall [--cask] <name>` (or `mas uninstall <id>`) → commit |
| Bring this machine up to date | `bash ~/bin/update.sh` (runs `brew update && brew upgrade`) |
| Bootstrap a fresh machine | See "Fresh-machine bootstrap" — multi-stage; not just `bash ~/bin/setup.sh` |

## Out of scope

- **Modifying `~/.gitconfig`'s user.name/email/signing config** — these are sensitive (Jamie has explicit rules: never modify git config or bypass GPG signing). If a change is needed, ask first.
- **Touching `.dotfiles/` plumbing directly** — never `cd` into the bare repo, never edit refs/objects. Always go through `dot`.
- **Force-pushing to `reset/dotfiles`** — never. The remote is the canonical history; resolve conflicts forward, don't rewrite.
