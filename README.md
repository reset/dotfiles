# dotfiles

## Windows Setup

1. Install [Gpg4win](https://www.gpg4win.org/)

  ```cmd
  choco install gpg4win
  ```

* <https://github.com/tonsky/FiraCode/wiki/Installing>

## macOS Setup

1. Install Command Line Tools

  ```cmd
  sudo dseditgroup -o edit -a ${USERNAME} -t user admin
  xcode-select --install
  ```

## Setup

1. Setup `$HOME` directory with dotfiles

    ```bash
    git clone --separate-git-dir=$HOME/.dotfiles https://github.com/reset/dotfiles.git tmpdotfiles
    $(which git) --git-dir=$HOME/.dotfiles/ --work-tree=$HOME config --local status.showUntrackedFiles no
    rsync --recursive --verbose --exclude '.git' tmpdotfiles/ $HOME/
    rm -r tmpdotfiles
    ```

1. Run install

    ```bash
    bin/setup.sh
    ```

1. Run update

    ```bash
    bin/update.sh
    ```

## VIM Setup

* Open vim and run `:PlugInstall`

## Claude Code

[Claude Code](https://claude.com/claude-code) is part of the toolchain — `bin/setup.sh` installs it via `brew install claude-code`. This repo tracks a personal skill at `.claude/skills/dotfiles/skill.md` that teaches Claude how to manage these dotfiles end-to-end: the `dot` wrapper, the `bin/setup.sh` install lanes, the bootstrap procedure, and the install-now-and-persist workflow. Once the dotfiles are cloned and Claude Code is installed, the skill auto-loads — no further setup.

The skill file is also useful as plain documentation. If you want to understand the dotfiles workflow without invoking Claude, read `.claude/skills/dotfiles/skill.md` directly.

To trigger the skill in a Claude Code session, just describe what you want to do — e.g. "install Rectangle and remember it across machines", "add a tmux binding", "set up a fresh Mac". The skill description in the frontmatter handles routing.

## Resources

* https://dev.to/bowmanjd/store-home-directory-config-files-dotfiles-in-git-using-bash-zsh-or-powershell-a-simple-approach-without-a-bare-repo-2if7
* https://zachrussell.net/blog/map-caps-lock-to-control-windows/
