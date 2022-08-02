#!/usr/bin/env bash

ulimit -n unlimited

if [ -z "${REMOTE_CONTAINERS+x}" ]; then
  if [ -z "$(pgrep gpg-agent)" ]; then
    eval "$(gpg-agent --daemon)"
  fi

  # Start ssh-agent if it isn't running
  if [ -z "${SSH_AUTH_SOCK+x}" ]; then
    # Check for a currently running instance of the agent
    if [ -z "$(pgrep ssh-agent)" ]; then
      # Launch a new instance of the agent
      ssh-agent -s &>"$HOME/.ssh/ssh-agent"
    fi
    eval "$(cat "$HOME/.ssh/ssh-agent")" &>/dev/null
  fi

  # Add ssh keys to ssh-agent
  ssh-add "$HOME/.ssh/id_ed25519" &>/dev/null
fi

export PATH="$HOME/.dotnet/tools:$HOME/.cargo/bin:$PATH"
export TERM=xterm-256color
export OMG_CONFIG_PATH="$HOME/.config/omg"

if [ -f "$OMG_CONFIG_PATH/env" ]; then
  source "$OMG_CONFIG_PATH/env"
fi

if [ -f "$OMG_CONFIG_PATH/secrets" ]; then
  source "$OMG_CONFIG_PATH/secrets"
fi

# shellcheck source=/dev/null
if [ -e /Users/reset/.nix-profile/etc/profile.d/nix.sh ]; then . /Users/reset/.nix-profile/etc/profile.d/nix.sh; fi # added by Nix installer

export NVM_DIR="$HOME/.nvm"
[ -s "$NVM_DIR/nvm.sh" ] && \. "$NVM_DIR/nvm.sh"  # This loads nvm
