ZSH=$HOME/.oh-my-zsh
ZSH_THEME="muse"
plugins=(git tmux)

source $ZSH/oh-my-zsh.sh
fpath=(~/.zsh $fpath)

# Base16 Shell
BASE16_SHELL="$HOME/.config/base16-shell/"
[ -n "$PS1" ] && \
    [ -s "$BASE16_SHELL/profile_helper.sh" ] && \
        eval "$("$BASE16_SHELL/profile_helper.sh")"

PS1="$PS1"'$([ -n "$TMUX" ] && tmux setenv TMUXPWD_$(tmux display -p "#D" | tr -d %) "$PWD")'
if [ "$TMUX" = "" ]; then tmux; fi

export EDITOR=vim
export GOPATH=$HOME/go
export DYLD_LIBRARY_PATH=/Library/Developer/CommandLineTools/usr/lib:/Users/reset/.rustup/toolchains/nightly-x86_64-apple-darwin/lib
export PATH=$GOPATH/bin:$JAVA_HOME/bin:/usr/local/bin:/usr/local/sbin:$PATH
export GPG_TTY=$(TTY)
export VAGRANT_DEFAULT_PROVIDER=vmware_desktop

alias vi=vim
alias dotfiles='/usr/local/bin/git --git-dir=$HOME/.dotfiles/ --work-tree=$HOME'

eval $(/usr/libexec/path_helper -s)

[ -f ~/.fzf.zsh ] && source ~/.fzf.zsh
[ -f ~/.secrets ] && source ~/.secrets

eval "$(direnv hook zsh)"
