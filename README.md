# dotfiles

## Windows Setup

1. Install [Gpg4win](https://www.gpg4win.org/)

  ```cmd
  choco install gpg4win
  ```

* <https://github.com/tonsky/FiraCode/wiki/Installing>

## macOS Setup

1. Install Command Line Tools

  ```cmd
  sudo dseditgroup -o edit -a ${USERNAME} -t user admin
  xcode-select --install
  ```

## Setup

1. Setup `$HOME` directory with dotfiles

    ```bash
    git clone --separate-git-dir=$HOME/.dotfiles https://github.com/reset/dotfiles.git tmpdotfiles
    $(which git) --git-dir=$HOME/.dotfiles/ --work-tree=$HOME config --local status.showUntrackedFiles no
    rsync --recursive --verbose --exclude '.git' tmpdotfiles/ $HOME/
    rm -r tmpdotfiles
    ```

1. Run install

    ```bash
    bin/setup.sh
    ```

1. Run update

    ```bash
    bin/update.sh
    ```

## VIM Setup

* Open vim and run `:PlugInstall`

## Resources

* https://dev.to/bowmanjd/store-home-directory-config-files-dotfiles-in-git-using-bash-zsh-or-powershell-a-simple-approach-without-a-bare-repo-2if7
* https://zachrussell.net/blog/map-caps-lock-to-control-windows/
