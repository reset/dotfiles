# dotfiles

## Setup Order

* `defaults write -g com.apple.swipescrolldirection -bool FALSE`
* /bin/bash -c "$(curl -fsSL https://raw.githubusercontent.com/Homebrew/install/HEAD/install.sh)"
* brew analytics off
  * https://docs.brew.sh/Analytics
* brew install coreutils
* brew install zsh
* brew install tmux
* brew install git
* brew install git-lfs
* brew install gpg
* brew install fzf
* brew install buildkite/buildkite/buildkite-agent
* brew install envconsul
* brew tap homebrew/cask-fonts
* brew install --cask font-fira-code
* brew install --cask alacritty
* brew install --cask visual-studio-code
* brew install --cask unity-hub
* brew install --cask docker
* brew install --cask dotnet-sdk
* sh <(curl -L https://nixos.org/nix/install) --darwin-use-unencrypted-nix-store-volume
  * echo '. /Users/reset/.nix-profile/etc/profile.d/nix.sh' > ~/.profile
* echo "eval \"\$(direnv hook zsh)\"" >>"$HOME/.zshrc"
* echo "eval \"\$(direnv hook bash)\"" >>"$HOME/.bashrc"
* git lfs install
* /Applications/Unity\ Hub.app/Contents/MacOS/Unity\ Hub -- --headless install --version 2019.4.4f1 --changeset 1f1dac67805b -m ios -m mac-il2cpp
  * This motherfucker is documented in release notes only: https://unity3d.com/de/hub/whats-new

## Additional Steps

* Write SSH key to ~/.ssh

## Personal

* brew install mas
* brew install wireguard-tools
* brew install reattach-to-user-namespace
* brew install --cask 1password
* brew install --cask slack
* brew install --cask discord
* brew install --cask keybase
* brew install --cask dropbox
* brew install --cask zoom
* mas install 1451685025 (WireGuard)

## Additional Personal

* Write pgp key

## Profile setup

* sh -c "$(curl -fsSL https://raw.github.com/ohmyzsh/ohmyzsh/master/tools/install.sh)"
* git clone https://github.com/gpakosz/.tmux.git
* git clone https://github.com/tmux-plugins/tpm ~/.tmux/plugins/tpm
* curl -fLo ~/.vim/autoload/plug.vim --create-dirs \
    https://raw.githubusercontent.com/junegunn/vim-plug/master/plug.vim
* git clone https://github.com/chriskempson/base16-shell.git ~/.config/base16-shell
* git clone https://github.com/romkatv/powerlevel10k.git $ZSH_CUSTOM/themes/powerlevel10k

https://www.anand-iyer.com/blog/2018/a-simpler-way-to-manage-your-dotfiles.html

* alias dotfiles='/usr/local/bin/git --git-dir=$HOME/.dotfiles/ --work-tree=$HOME'
* git clone --separate-git-dir=$HOME/.dotfiles git@github.com:reset/dotfiles-mac.git tmpdotfiles
* rsync --recursive --verbose --exclude '.git' tmpdotfiles/ $HOME/
* rm -r tmpdotfiles

## Post Setup

* vim - `:PlugInstall`
