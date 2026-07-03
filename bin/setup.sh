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
    # The Homebrew installer needs curl/git/build tools. On a fresh Linux box some
    # (notably curl) may be absent, so install them before the installer runs —
    # this function runs before install_packages, so we can't rely on that lane.
    if [[ $OSTYPE != 'darwin'* ]]; then
      sudo apt-get update && sudo apt-get install -y build-essential curl file git procps
    fi
    echo "Installing Homebrew..."
    curl -fsSL https://raw.githubusercontent.com/Homebrew/install/HEAD/install.sh | bash
  fi

  # The installer doesn't modify the current shell's environment, so put brew on
  # PATH for the rest of this script on a fresh machine (both macOS and Linux).
  if [[ -x "$HOMEBREW_PREFIX/bin/brew" ]]; then
    eval "$("$HOMEBREW_PREFIX/bin/brew" shellenv)"
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

  # GPG requires 700 on its home dir — rsync during dotfile bootstrap creates it
  # at 755, which causes "unsafe permissions" warnings and operation timeouts.
  mkdir -p "$HOME/.gnupg"
  chmod 700 "$HOME/.gnupg"
}

function setup_home () {
  echo "Setting up home directory..."
  # Install tmux
  if [[ ! -d "$HOME/.tmux" ]]; then
    git clone https://github.com/gpakosz/.tmux.git "$HOME/.tmux"
  fi

  if [[ ! -d "$HOME/.tmux/plugins/tpm" ]]; then
    git clone https://github.com/tmux-plugins/tpm "$HOME/.tmux/plugins/tpm"
  fi

  # Install ohmyzsh
  if [[ ! -d "$HOME/.oh-my-zsh" ]]; then
    curl -fsSL https://raw.github.com/ohmyzsh/ohmyzsh/master/tools/install.sh | bash
  fi

  # Install Plug
  curl -fLo "$HOME/.vim/autoload/plug.vim" --create-dirs https://raw.githubusercontent.com/junegunn/vim-plug/master/plug.vim

  # Install base16-shell
  if [[ ! -d "$HOME/.config/base16-shell" ]]; then
    git clone https://github.com/chriskempson/base16-shell.git "$HOME/.config/base16-shell"
  fi

  # Install powerlevel10k
  if [[ ! -d "$ZSH_CUSTOM/themes/powerlevel10k" ]]; then
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
  brew trust jaxxstorm/tap
  brew bundle install --file="$HOME/Brewfile"
}

function _caps_lock_remap_macos () {
  local plist_path="/Library/LaunchDaemons/com.local.keyremap.plist"

  sudo bash -c "cat > \"$plist_path\"" <<EOF
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
  echo "Installing Ubuntu system packages..."
  sudo apt-get update
  sudo apt-get upgrade -y
  # System prerequisites: Homebrew-on-Linux build deps plus a few base tools not
  # worth pulling through brew.
  sudo apt-get install -y \
    build-essential \
    curl \
    file \
    fonts-firacode \
    git \
    git-repair \
    gnutls-bin \
    procps

  # Cross-platform CLI tools via Homebrew — macOS-only entries in the Brewfile
  # are guarded with `if OS.mac?`, so this installs only the shared set on Linux.
  brew bundle install --file="$HOME/Brewfile"
}

install_homebrew
install_packages
configure_system
install_symlinks
setup_home
