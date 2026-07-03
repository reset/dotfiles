# Cross-platform CLI tools — installed via Homebrew on both macOS and Linux.
# macOS-only formulae, GUI casks, and Mac App Store apps are guarded below so
# `brew bundle` stays clean on a Linux host (linuxbrew).
tap "hashicorp/tap"
brew "awscli"
brew "azure-cli"
brew "bash"
brew "binutils"
brew "cdrdao"
brew "cloc"
brew "cloudflared"
brew "hashicorp/tap/consul"
brew "coreutils"
brew "direnv"
brew "ffmpeg"
brew "fzf"
brew "gcc"
brew "gh"
brew "git"
brew "git-filter-repo"
brew "git-lfs"
brew "gnupg"
brew "go"
brew "imagemagick"
brew "jq"
brew "maven"
brew "p7zip"
brew "pkgconf"
brew "rsync"
brew "ruby"
brew "rust"
brew "shfmt"
brew "steamguard-cli"
brew "hashicorp/tap/terraform"
brew "tmux"
brew "transmission-cli"
brew "weechat"
brew "zsh"

# Linux-only formulae
if OS.linux?
  # macOS uses pinentry-mac; Linux uses the standard pinentry. install_symlinks
  # links $HOMEBREW_PREFIX/bin/pinentry into /usr/local/bin on both.
  brew "pinentry"
end

# macOS-only: formulae with no useful Linux counterpart, GUI casks, Mac App Store.
if OS.mac?
  tap "jaxxstorm/tap"

  brew "telnet"   # no Linux bottle on Homebrew — use apt on Linux if needed

  brew "colima"                       # Docker runtime for macOS; Linux runs Docker native
  brew "mas"                          # Mac App Store CLI
  brew "pinentry-mac"                 # GUI pinentry for macOS
  brew "reattach-to-user-namespace"   # tmux clipboard bridge, macOS-only
  brew "jaxxstorm/tap/aws-sso-creds"

  # GUI apps
  cask "1password"
  cask "1password-cli"
  cask "ghostty"
  cask "alfred"
  cask "brave-browser"
  cask "claude"
  cask "claude-code"
  cask "crossover"
  cask "deckset"
  cask "discord"
  cask "docker-desktop"
  cask "dotnet-sdk"
  cask "dropbox"
  cask "font-fira-code"
  cask "go-agent"
  cask "google-chrome"
  cask "google-drive"
  cask "keybase"
  cask "nordvpn"
  cask "obs"
  cask "p4"
  cask "p4v"
  cask "postman"
  cask "rar"
  cask "slack"
  cask "steam"
  cask "telegram"
  cask "unity-hub"
  cask "visual-studio-code"
  cask "vlc"
  cask "whatsapp"
  cask "zoom"

  # Mac App Store
  mas "1Password for Safari", id: 1569813296
  mas "Final Cut Pro", id: 424389933
  mas "Color Picker", id: 1545870783
  mas "Magnet", id: 441258766
  mas "Microsoft Excel", id: 462058435
  mas "Windows App", id: 1295203466
end
