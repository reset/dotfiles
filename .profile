#!/usr/bin/env bash

if [ -z "$(pgrep gpg-agent)" ]; then
  eval "$(gpg-agent --daemon)"
fi

# Start ssh-agent if it isn't running
if [ -z "$SSH_AUTH_SOCK" ]; then
  # Check for a currently running instance of the agent
  if [ -z "$(pgrep ssh-agent)" ]; then
    # Launch a new instance of the agent
    ssh-agent -s &>"$HOME/.ssh/ssh-agent"
  fi
  eval "$(cat "$HOME/.ssh/ssh-agent")" &>/dev/null
fi

# Add ssh keys to ssh-agent
ssh-add "$HOME/.ssh/id_ed25519" &>/dev/null

export PATH="$HOME/.dotnet/tools:$HOME/.cargo/bin:$PATH"
export PATH="/usr/local/opt/gpg-agent/bin:$PATH"
export TERM=xterm-256color
export GITHUB_USER=reset
export OMG_DD_HOSTNAME=jamie-laptop
export OMG_TAP_SRC_PATH=/mnt/c/Users/JamieWinsor/code/tap

if [ -e /Users/reset/.nix-profile/etc/profile.d/nix.sh ]; then . /Users/reset/.nix-profile/etc/profile.d/nix.sh; fi # added by Nix installer
