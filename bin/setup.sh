#!/usr/bin/env bash

set -euo pipefail

export HOMEBREW_PREFIX
export ZSH_CUSTOM="$HOME/.oh-my-zsh/custom"

if [[ $OSTYPE == 'darwin'* ]]; then
  HOMEBREW_PREFIX="/opt/homebrew"
else
  HOMEBREW_PREFIX="/home/linuxbrew/.linuxbrew"
fi

#
# Main Functions
#

function configure_system () {
  echo "Configuring System..."
  if [[ $OSTYPE == 'darwin'* ]]; then
    _configure_system_macos
  fi
}

function install_homebrew () {
  if ! command -v brew &> /dev/null; then
    echo "Installing Homebrew..."
    curl -fsSL https://raw.githubusercontent.com/Homebrew/install/HEAD/install.sh | bash
  fi

  brew analytics off
}

function install_packages () {
  echo "Installing System Packages..."
  if [[ $OSTYPE == 'darwin'* ]]; then
    _install_packages_macos
  else
    _install_packages_ubuntu
  fi
}

function install_symlinks () {
  echo "Linking system software..."
  sudo mkdir -p /usr/local/bin
  if [[ $OSTYPE == 'darwin'* ]]; then
    sudo ln -sf "$HOMEBREW_PREFIX/bin/pinentry-mac" /usr/local/bin/pinentry
  else
    sudo ln -sf "$HOMEBREW_PREFIX/bin/pinentry" /usr/local/bin/pinentry
  fi
  sudo ln -sf "$HOMEBREW_PREFIX/bin/git" /usr/local/bin/git
  sudo ln -sf "$HOMEBREW_PREFIX/bin/gpg" /usr/local/bin/gpg
  sudo ln -sf "$HOMEBREW_PREFIX/bin/gpg-agent" /usr/local/bin/gpg-agent
  sudo ln -sf "$HOMEBREW_PREFIX/bin/zsh" /usr/local/bin/zsh
}

function setup_home () {
  echo "Setting up home directory..."
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
}

#
# Helper Functions
#

function _configure_system_macos () {
  defaults write NSGlobalDomain com.apple.swipescrolldirection -bool false
  _caps_lock_remap_macos
}

function _install_packages_macos () {
  echo "Installing macOS system packages..."
  softwareupdate --install-rosetta

  echo "Installing Brew packages..."
  brew tap jaxxstorm/tap

  brew install \
    awscli \
    aws-sso-creds \
    azure-cli \
    binutils \
    blender \
    brave-browser \
    cloc \
    consul \
    coreutils \
    deckset \
    direnv \
    ffmpeg \
    fzf \
    gcc \
    gh \
    gimp \
    git \
    git-lfs \
    go \
    gpg2 \
    gnupg \
    jq \
    mas \
    maven \
    obs \
    p4 \
    p4v \
    pinentry-mac \
    pkgconfig \
    python \
    rar \
    reattach-to-user-namespace \
    ruby \
    rust \
    shfmt \
    telegram \
    telnet \
    terraform \
    tmux \
    transmission-cli \
    zsh

  echo "Installing Brew Cask packages..."
  brew install --cask 1password
  brew install --cask 1password-cli
  brew install --cask alacritty
  brew install --cask alfred
  brew install --cask discord
  brew install --cask dotnet-sdk
  brew install --cask font-fira-code
  brew install --cask go-agent
  brew install --cask google-chrome
  brew install --cask keybase
  brew install --cask dropbox
  brew install --cask nordvpn
  brew install --cask notable
  brew install --cask postman
  brew install --cask slack
  brew install --cask steam
  brew install --cask unity-hub
  brew install --cask visual-studio-code
  brew install --cask zoom

  echo "Installing Mac App Store packages..."
  mas install 1295203466 # Windows RDP
  mas install 441258766 # Magnet
  mas install 462058435 # Microsoft Excel
  mas install 1545870783 # System Color Picker
}

_caps_lock_remap_macos () {
  plist_path="/Library/LaunchDaemons/com.local.keyremap.plist"

  sudo bash -c "cat > $plist_path" <<EOF
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>Label</key>
    <string>com.local.keyremap</string>
    <key>ProgramArguments</key>
    <array>
        <string>/usr/bin/hidutil</string>
        <string>property</string>
        <string>--set</string>
        <string>{"UserKeyMapping":[{"HIDKeyboardModifierMappingSrc":0x700000039,"HIDKeyboardModifierMappingDst":0x7000000E0}]}</string>
    </array>
    <key>RunAtLoad</key>
    <true/>
</dict>
</plist>
EOF

  echo "Plist file created at $plist_path"

  # Load the LaunchDaemon
  sudo launchctl load "$plist_path"
  echo "Caps Lock remapped to Control successfully!"
}


function _install_packages_ubuntu () {
  sudo apt-get update &&
    sudo apt-get upgrade &&
    sudo apt-get install -y \
      build-essential \
      curl \
      fonts-firacode \
      git \
      git-repair
      gnutls-bin
}

install_homebrew
install_packages
configure_system
install_symlinks
setup_home

