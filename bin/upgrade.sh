#!/usr/bin/env bash

set -euo pipefail

sudo apt-get update &&
  sudo apt-get upgrade -y &&
  sudo apt-get install -y \
    fonts-firacode \
    gnutls-bin

curl -fsSL https://raw.githubusercontent.com/Homebrew/install/HEAD/install.sh | bash

brew update && brew upgrade && brew install -q \
  awscli \
  azure-cli \
  consul \
  direnv \
  fzf \
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
