ZSH=$HOME/.oh-my-zsh
ZSH_THEME="muse"
plugins=(git tmux)

source $ZSH/oh-my-zsh.sh
fpath=(~/.zsh $fpath)

BASE16_SHELL=$HOME/.config/base16-shell/
[ -n "$PS1" ] && [ -s $BASE16_SHELL/profile_helper.sh ] && eval "$($BASE16_SHELL/profile_helper.sh)"
PS1="$PS1"'$([ -n "$TMUX" ] && tmux setenv TMUXPWD_$(tmux display -p "#D" | tr -d %) "$PWD")'

export EDITOR=vim
export GOHOME=$HOME/go
export DYLD_LIBRARY_PATH=/Library/Developer/CommandLineTools/usr/lib:/Users/reset/.rustup/toolchains/nightly-x86_64-apple-darwin/lib
export PATH=$GOPATH/bin:$JAVA_HOME/bin:/usr/local/bin:/usr/local/sbin:$PATH
export RUST_SRC_PATH="$(rustc --print sysroot)/lib/rustlib/src/rust/src"
export GPG_TTY=$(TTY)

alias e=vim
alias vi=vim
alias config='/usr/bin/git --git-dir=/Users/reset/.cfg/ --work-tree=/Users/reset'

eval $(/usr/libexec/path_helper -s)
eval "$(direnv hook zsh)"

[ -f ~/.fzf.zsh ] && source ~/.fzf.zsh

