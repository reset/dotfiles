#!/usr/bin/env bash

set -euo pipefail

sudo apt-get update &&
  sudo apt-get upgrade -y &&
  sudo apt-get install -y \
    build-essential \
    fonts-firacode \
    git-repair \
    gnutls-bin

curl -fsSL https://raw.githubusercontent.com/Homebrew/install/HEAD/install.sh | bash

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
