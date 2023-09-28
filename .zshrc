DEFAULT_USER="reset"
export ZSH=$HOME/.oh-my-zsh
export ZSH_CUSTOM="$HOME/.oh-my-zsh/custom"
export ZSH_THEME="powerlevel10k/powerlevel10k"
export GOPATH=$HOME/go
# Why did I comment this out? What is wrong with the load order?
# export PATH=$HOME/.dotnet/tools:$GOPATH/bin:$JAVA_HOME/bin:/usr/local/bin:/usr/local/sbin:$HOME/bin:$PATH

plugins=(git z tmux ssh-agent)
zstyle :omz:plugins:ssh-agent agent-forwarding on
source $ZSH/oh-my-zsh.sh

fpath=(~/.zsh $fpath)

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
  if [ "$TMUX" = "" ]; then tmux; fi

  export AWS_DEFAULT_REGION="us-west-2"
  export GPG_TTY=$(tty)
fi

eval "$(direnv hook zsh)"

[ -f ~/.fzf.zsh ] && source ~/.fzf.zsh
[ -f ~/.secrets ] && source ~/.secrets

# To customize prompt, run `p10k configure` or edit ~/.p10k.zsh.
[[ ! -f ~/.p10k.zsh ]] || source ~/.p10k.zsh

# pnpm
export PNPM_HOME="/Users/reset/Library/pnpm"
case ":$PATH:" in
  *":$PNPM_HOME:"*) ;;
  *) export PATH="$PNPM_HOME:$PATH" ;;
esac
# pnpm end
#
