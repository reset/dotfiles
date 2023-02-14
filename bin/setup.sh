#!/usr/bin/env bash

set -euo pipefail

export HOMEBREW_REPOSITORY
export ZSH_CUSTOM="$HOME/.oh-my-zsh/custom"

if [[ $OSTYPE == 'darwin'* ]]; then
  HOMEBREW_REPOSITORY="/opt/homebrew"
else
  HOMEBREW_REPOSITORY="/home/linuxbrew/.linuxbrew"
fi

function configure_macos() {
  defaults write -g com.apple.swipescrolldirection -bool FALSE
}

function install_homebrew () {
  if ! command -v brew &> /dev/null; then
    curl -fsSL https://raw.githubusercontent.com/Homebrew/install/HEAD/install.sh | bash
  fi

  brew analytics off
}

function install_macos () {
  echo "Installing Brew packages..."
  brew tap homebrew/cask-fonts

  brew install \
    awscli \
    azure-cli \
    consul \
    coreutils \
    direnv \
    fzf \
    gcc \
    gh \
    git \
    git-lfs \
    gnupg \
    jq \
    mas \
    nomad \
    pinentry-mac \
    reattach-to-user-namespace \
    shfmt \
    telnet \
    terraform \
    tmux \
    vault \
    zsh

  echo "Installing Brew Cask packages..."
  brew install --cask 1password
  brew install --cask alacritty
  brew install --cask alfred
  brew install --cask discord
  brew install --cask dotnet-sdk
  brew install --cask font-fira-code
  brew install --cask go-agent
  brew install --cask google-chrome
  brew install --cask keybase
  brew install --cask dropbox
  brew install --cask notable
  brew install --cask slack
  brew install --cask steam
  brew install --cask unity-hub
  brew install --cask visual-studio-code
  brew install --cask zoom

  echo "Installing Mac App Store packages..."
  mas install 1295203466 # Windos RDP
  mas install 441258766 # Magnet
}

function install_ubuntu () {
  sudo apt-get update &&
    sudo apt-get upgrade &&
    sudo apt-get install -y \
      curl \
      git
}

function install_symlinks () {
  sudo ln -sf "$HOMEBREW_REPOSITORY/bin/git" /usr/local/bin/git
  sudo ln -sf "$HOMEBREW_REPOSITORY/bin/zsh" /usr/local/bin/zsh
}

install_homebrew

echo "Installing System Packages..."
if [[ $OSTYPE == 'darwin'* ]]; then
  configure_macos
  install_macos
else

  install_ubuntu
fi

echo "Linking system software..."
install_symlinks

# Install tmux
if [ ! -d "$HOME/.tmux" ]; then
  git clone https://github.com/gpakosz/.tmux.git "$HOME/.tmux"
fi

if [ ! -d "$HOME/.tmux/plugins/tpm" ]; then
  git clone https://github.com/tmux-plugins/tpm "$HOME/.tmux/plugins/tpm"
fi

# Install ohmyzsh
if [ ! -d "$HOME/.oh-my-zsh" ]; then
  curl -fsSL https://raw.github.com/ohmyzsh/ohmyzsh/master/tools/install.sh | bash
fi

# Install Plug
curl -fLo "$HOME/.vim/autoload/plug.vim" --create-dirs https://raw.githubusercontent.com/junegunn/vim-plug/master/plug.vim

# Install base16-shell
if [ ! -d "$HOME/.config/base16-shell" ]; then
  git clone https://github.com/chriskempson/base16-shell.git "$HOME/.config/base16-shell"
fi

# Install powerlevel10k
if [ ! -d "$ZSH_CUSTOM/themes/powerlevel10k" ]; then
  git clone https://github.com/romkatv/powerlevel10k.git "$ZSH_CUSTOM/themes/powerlevel10k"
fi
