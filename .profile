if [ ! -n "$(pgrep gpg-agent)" ]; then
	eval "$(gpg-agent --daemon)"
fi

export PATH="$HOME/.dotnet/tools:$HOME/.cargo/bin:$PATH"
export PATH="/usr/local/opt/gpg-agent/bin:$PATH"
export TERM=xterm-256color
export GITHUB_USER=reset
export OMG_DD_HOSTNAME=jamie-laptop
export OMG_TAP_SRC_PATH=/mnt/c/Users/JamieWinsor/code/tap

if [ -e /Users/reset/.nix-profile/etc/profile.d/nix.sh ]; then . /Users/reset/.nix-profile/etc/profile.d/nix.sh; fi # added by Nix installer

eval "$(/home/linuxbrew/.linuxbrew/bin/brew shellenv)"
