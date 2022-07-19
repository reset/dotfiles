#!/usr/bin/env bash

set -euo pipefail

sudo apt-get update &&
  sudo apt-get upgrade &&
  sudo apt-get install -y \
    curl \
    git

# Install Homebrew
curl -fsSL https://raw.githubusercontent.com/Homebrew/install/HEAD/install.sh | bash

# Install ohmyzsh
curl -fsSL https://raw.github.com/ohmyzsh/ohmyzsh/master/tools/install.sh | bash

# Install tmux
git clone https://github.com/gpakosz/.tmux.git "$HOME/.tmux"
git clone https://github.com/tmux-plugins/tpm "$HOME/.tmux/plugins/tpm"

# Install Plug
curl -fLo "$HOME/.vim/autoload/plug.vim" --create-dirs https://raw.githubusercontent.com/junegunn/vim-plug/master/plug.vim

# Install base16-shell
git clone https://github.com/chriskempson/base16-shell.git "$HOME/.config/base16-shell"

# Install powerlevel10k
git clone https://github.com/romkatv/powerlevel10k.git "$ZSH_CUSTOM/themes/powerlevel10k"
