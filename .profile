#!/usr/bin/env bash

ulimit -n unlimited

export EDITOR=vim
export GITHUB_USER="reset"
export PATH="$HOME/.dotnet/tools:$HOME/.cargo/bin:$PATH"
export XDG_CACHE_HOME="$HOME/.cache"
export XDG_CONFIG_HOME="$HOME/.config"
export NVM_DIR="$HOME/.nvm"
export SSH_AGENT="$HOME/.ssh/ssh-agent"
export TERM=xterm-256color
export OMG_CONFIG_PATH="$XDG_CONFIG_HOME/omg"

# Skip running in VSCode devcontainer
if [ -z "${REMOTE_CONTAINERS+x}" ]; then
  if [ -z "$(pgrep gpg-agent)" ]; then
    eval "$(gpg-agent --daemon)"
  fi

  # Start ssh-agent if it isn't running
  if [ -z "${SSH_AUTH_SOCK+x}" ]; then
    # Check for a currently running instance of the agent
    if [ -z "$(pgrep ssh-agent)" ]; then
      # Launch a new instance of the agent
      ssh-agent -s &>"$SSH_AGENT"
    fi
    eval "$(cat "$SSH_AGENT")" &>/dev/null
  fi

  # Add ssh keys to ssh-agent
  ssh-add "$HOME/.ssh/id_ed25519" &>/dev/null
fi

if [ -f "$OMG_CONFIG_PATH/env" ]; then
  source "$OMG_CONFIG_PATH/env"
fi

if [ -f "$OMG_CONFIG_PATH/secrets" ]; then
  source "$OMG_CONFIG_PATH/secrets"
fi

# shellcheck source=/dev/null
if [ -e "$HOME/.nix-profile/etc/profile.d/nix.sh" ] ; then . "$HOME/.nix-profile/etc/profile.d/nix.sh"; fi # added by Nix installer

[ -s "$NVM_DIR/nvm.sh" ] && \. "$NVM_DIR/nvm.sh"  # Load nvm

alias vi=vim
alias dot='$(which git) --git-dir=$HOME/.dotfiles/ --work-tree=$HOME'
