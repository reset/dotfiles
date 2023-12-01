#!/usr/bin/env bash

set -euo pipefail

function upgrade_homebrew () {
  echo "Updating Homebrew packages..."
  brew update && brew upgrade

  echo "Cleaning up Homebrew packages..."
  brew cleanup
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

