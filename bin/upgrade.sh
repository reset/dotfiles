#!/usr/bin/env bash

set -euo pipefail

function upgrade_homebrew () {
  brew update && brew upgrade && brew install -q \
    awscli \
    azure-cli \
    consul \
    direnv \
    fzf \
    gcc \
    gh \
    git \
    git-lfs \
    gpg \
    jq \
    nomad \
    terraform \
    tmux \
    vault \
    zsh

  brew cleanup
}

function upgrade_ubuntu () {
  sudo apt-get update &&
    sudo apt-get upgrade -y &&
    sudo apt-get install -y \
      build-essential \
      fonts-firacode \
      git-repair \
      gnutls-bin
}

if [[ $OSTYPE == 'darwin'* ]]; then
  upgrade_homebrew
else
  upgrade_ubuntu
  upgrade_homebrew
fi
