# dotfiles

## Additional Steps

* Write SSH key to ~/.ssh

## Windows Setup

1. Install [Gpg4win](https://www.gpg4win.org/)

   ```cmd
   choco install gpg4win
   ```

* <https://github.com/tonsky/FiraCode/wiki/Installing>

## Packages

* sudo ln -s /home/linuxbrew/.linuxbrew/bin/git /usr/local/bin/git
* sudo ln -s /home/linuxbrew/.linuxbrew/bin/zsh /usr/local/bin/zsh

### WSL Setup

1. Setup `$HOME` directory with dotfiles

    ```bash
    git clone --separate-git-dir=$HOME/.dotfiles git@github.com:reset/dotfiles-mac.git tmpdotfiles
    dotfiles config --local status.showUntrackedFiles no
    rsync --recursive --verbose --exclude '.git' tmpdotfiles/ $HOME/
    rm -r tmpdotfiles
    bin/install.sh
    ```

1. Run install

    ```bash
    bin/setup.sh
    ```

1. Run update

    ```bash
    bin/update.sh
    ```

### macOS

* brew install mas
* brew install telnet
* brew install reattach-to-user-namespace
* brew install --cask 1password
* brew install --cask alfred
* brew install --cask slack
* brew install --cask discord
* brew install --cask keybase
* brew install --cask dropbox
* brew install --cask notable
* brew install --cask zoom
* mas install 1295203466 (Windows RDP)

## Additional Personal

* Write pgp key

## VIM Setup

* Open vim and run `:PlugInstall`

## Unity macOS Setup

* jamie todo: unzip XCode & install
  * curl from bucket
  * xip -x Xcode_12.3.xip
  * mv Xcode.app /Applications/Xcode.app
  * sudo xcodebuild -license accept
  * xcrun --show-sdk-platform-path
* `defaults write -g com.apple.swipescrolldirection -bool FALSE`
* /bin/bash -c "$(curl -fsSL https://raw.githubusercontent.com/Homebrew/install/HEAD/install.sh)"
* brew analytics off
  * <https://docs.brew.sh/Analytics>
* brew install coreutils
* brew install buildkite/buildkite/buildkite-agent
* brew install envconsul
* brew tap homebrew/cask-fonts
* brew install --cask alacritty
* brew install --cask visual-studio-code
* brew install --cask unity-hub
* brew install --cask docker
* brew install --cask dotnet-sdk
* echo "eval \"\$(direnv hook zsh)\"" >>"$HOME/.zshrc"
* echo "eval \"\$(direnv hook bash)\"" >>"$HOME/.bashrc"
* git lfs install
* /Applications/Unity\ Hub.app/Contents/MacOS/Unity\ Hub -- --headless install --version 2019.4.17f1 --changeset 1667c8606c536 -m ios -m mac-il2cpp --childModules
  * This motherfucker is documented in release notes only: <https://unity3d.com/de/hub/whats-new>
* Write licensing config to /Library/Application Support/Unity/config/services-config.json

  ```json
  {"enableEntitlementLicensing":true,"licensingServiceBaseUrl":"http://10.0.0.149:8080","enableFloatingApi":true,"clientConnectTimeoutSec":5,"clientHandshakeTimeoutSec":10}
  ```

* /Applications/Unity/Hub/Editor/2019.4.17f1/Unity.app/Contents/MacOS/Unity -quit -batchmode -nographics -logFile -projectPath ~/code/tap/tap -executeMethod "OMG.Tap.Editor.BuildScript.BuildMacOSPlayer" --version=2


## Resources

* https://dev.to/bowmanjd/store-home-directory-config-files-dotfiles-in-git-using-bash-zsh-or-powershell-a-simple-approach-without-a-bare-repo-2if7
* https://zachrussell.net/blog/map-caps-lock-to-control-windows/
