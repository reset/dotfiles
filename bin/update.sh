#!/usr/bin/env bash

set -euo pipefail

function upgrade_homebrew () {
  echo "Updating Homebrew packages..."
  brew update && brew upgrade

  echo "Installing new Brewfile entries..."
  brew bundle install --file="$HOME/Brewfile"

  echo "Cleaning up Homebrew packages..."
  brew cleanup -s
  rm -rf "$(brew --cache)"
}

function update_repos () {
  echo "Updating cloned repos..."
  local repos=(
    "$HOME/.oh-my-zsh"
    "$HOME/.oh-my-zsh/custom/themes/powerlevel10k"
    "$HOME/.tmux"
    "$HOME/.tmux/plugins/tpm"
    "$HOME/.config/base16-shell"
    "$HOME/code/burn-disc"
  )
  local repo
  for repo in "${repos[@]}"; do
    if [[ -d "$repo/.git" ]]; then
      echo "  $repo"
      # --ff-only so a repo with local changes fails cleanly instead of merging.
      git -C "$repo" pull --ff-only --quiet || echo >&2 "  (skipped ${repo##*/} — not fast-forward)"
    fi
  done

  # Rebuild burn-disc so the ~/bin/burn-disc shim runs the latest.
  if [[ -d "$HOME/code/burn-disc/.git" ]]; then
    make -C "$HOME/code/burn-disc" publish >/dev/null 2>&1 || echo >&2 "  (burn-disc build failed — run 'make -C ~/code/burn-disc publish')"
  fi
}

function upgrade_ubuntu () {
  echo "Updating apt packages..."
  sudo apt-get update && sudo apt-get upgrade -y

  echo "Cleaning up apt packages..."
  sudo apt autoremove -y
}

if [[ $OSTYPE == 'darwin'* ]]; then
  upgrade_homebrew
else
  upgrade_ubuntu
  upgrade_homebrew
fi

update_repos

