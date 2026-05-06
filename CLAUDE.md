# Home Directory

`~` is Jamie's macOS environment, not a project — but a few conventions are worth knowing when work touches it.

## Dotfiles

Managed as a Git bare repo at `~/.dotfiles/` with `$HOME` as the working tree. Origin: `github.com/reset/dotfiles`.

Use the `dot` command (real script at `~/bin/dot`) — it wraps `git --git-dir=$HOME/.dotfiles --work-tree=$HOME` so it works in any shell, interactive or scripted:

```bash
dot status              # tracked-file changes
dot add ~/.zshrc        # stage
dot commit -m "..."     # commit
dot push                # push
```

Untracked files are hidden by design so `dot status` doesn't drown in everything under `~`. Inspect a specific path with `dot status --short -- <path>`.

## `~/bin/`

Personal executables, always in PATH. Plain scripts, no extension. New script: `chmod +x`, verify, `dot add`, commit.

## PATH layering

zsh assembles PATH: `~/bin` → Homebrew (`/opt/homebrew/bin`) → pnpm globals (`~/Library/pnpm/bin`) → direnv-injected nix shell paths when inside a project directory.
