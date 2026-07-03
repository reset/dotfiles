DEFAULT_USER="reset"
export ZSH=$HOME/.oh-my-zsh
export ZSH_CUSTOM="$HOME/.oh-my-zsh/custom"
export ZSH_THEME="powerlevel10k/powerlevel10k"
export GOPATH=$HOME/go
export GH_EDITOR="code --wait"
export PATH=$HOME/.dotnet/tools:$GOPATH/bin:$JAVA_HOME/bin:/usr/local/bin:/usr/local/sbin:$HOME/bin:$PATH

fpath=(~/.zsh ~/.zsh/completions $fpath)

plugins=(git z tmux ssh-agent)
zstyle :omz:plugins:ssh-agent agent-forwarding on
source $ZSH/oh-my-zsh.sh

# Base16 Shell
BASE16_SHELL="$HOME/.config/base16-shell/"
[ -n "$PS1" ] && \
    [ -s "$BASE16_SHELL/profile_helper.sh" ] && \
        source "$BASE16_SHELL/profile_helper.sh"

base16_solarflare

# Enable Powerlevel10k instant prompt. Should stay close to the top of ~/.zshrc.
# Initialization code that may require console input (password prompts, [y/n]
# confirmations, etc.) must go above this block; everything else may go below.
if [[ -r "${XDG_CACHE_HOME:-$HOME/.cache}/p10k-instant-prompt-${(%):-%n}.zsh" ]]; then
  source "${XDG_CACHE_HOME:-$HOME/.cache}/p10k-instant-prompt-${(%):-%n}.zsh"
fi

# Skip running in VSCode devcontainer
if [ -z "${REMOTE_CONTAINERS+x}" ]; then
  # PS1="$PS1"'$([ -n "$TMUX" ] && tmux setenv TMUXPWD_$(tmux display -p "#D" | tr -d %) "$PWD")'
  # Auto-launch tmux on an interactive workstation only. On the server
  # (DOTFILES_PROFILE=server, set in the untracked ~/.config/omg/env) skip it —
  # a server shell stays lean and doesn't spawn tmux or the macOS-only
  # statusline widgets it drives.
  if [ "${DOTFILES_PROFILE:-workstation}" != "server" ] && [ "$TMUX" = "" ]; then tmux; fi

  export AWS_DEFAULT_REGION="us-west-2"
  export GPG_TTY=$(tty)
fi

eval "$(direnv hook zsh)"

[ -f ~/.fzf.zsh ] && source ~/.fzf.zsh
[ -f ~/.secrets ] && source ~/.secrets

# To customize prompt, run `p10k configure` or edit ~/.p10k.zsh.
[[ ! -f ~/.p10k.zsh ]] || source ~/.p10k.zsh

# pnpm
if [[ $OSTYPE == 'darwin'* ]]; then
  export PNPM_HOME="$HOME/Library/pnpm"
else
  export PNPM_HOME="$HOME/.local/share/pnpm"
fi
case ":$PATH:" in
  *":$PNPM_HOME/bin:"*) ;;
  *) export PATH="$PNPM_HOME/bin:$PATH" ;;
esac
# pnpm end
#

