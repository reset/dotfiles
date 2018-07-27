ZSH=$HOME/.oh-my-zsh
ZSH_THEME="muse"
plugins=(git tmux)

source $ZSH/oh-my-zsh.sh
fpath=(~/.zsh $fpath)

BASE16_SHELL=$HOME/.config/base16-shell/
[ -n "$PS1" ] && [ -s $BASE16_SHELL/profile_helper.sh ] && eval "$($BASE16_SHELL/profile_helper.sh)"

export GUARD_NOTIFY=false
export GITHUB_USER=reset
export GITHUB_TOKEN=9dd887eaf5e78ab719b9628974ae2e5dc7113975
export ELIXIR_EBIN=/usr/local/lib/elixir/ebin
export MIX_EBIN=/usr/local/lib/mix/ebin
export CHEF_USER="reset"
export CHEF_KEY="/Users/reset/.chef/reset.pem"
export CHEF_SERVER_URL="https://api.opscode.com/organizations/undeadlabs"
export AWS_ACCESS_KEY_ID=AKIAJHPWS6EK6G2RXUQQ
export AWS_SECRET_ACCESS_KEY=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
export AWS_KEYPAIR_NAME="jamie-laptop"
export FASTLY_API_KEY=94c5262c807b3c079c0c3d6dc67815f8
export FASTLY_SERVICE_KEY=T32H9RqMWpCV9qhp3S9xq
export JAVA_HOME=`/usr/libexec/java_home`
export MONODEVELOP_SDB_TEST=1
export EDITOR=vim
export GOHOME=$HOME/go
export DYLD_LIBRARY_PATH=/Library/Developer/CommandLineTools/usr/lib:/Users/reset/.rustup/toolchains/nightly-x86_64-apple-darwin/lib
export OPENSSL_INCLUDE_DIR=/usr/local/opt/openssl/include
export DEP_OPENSSL_INCLUDE=/usr/local/opt/openssl/include
export HAB_AUTH_TOKEN=_QU5PTllNT1VTLUJPWC0xCmJsZHItMjAxNzA5MjcwMjM3MTQKVnBMUDZ5UlZWdzlQQ2FXYzN0RFo4eDB5SDB5UWM3UnNjMi80eG81S3dGZUI1aXdzYzRQakNWQ3JJbVBJYTJXbzQvRC9VS09TaE9FVnRGVHd0YXRzU2drQmk4WT0=
export CHANGELOG_GITHUB_TOKEN=$GITHUB_TOKEN
export VAGRANT_DEFAULT_PROVIDER="vmware_fusion"

PS1="$PS1"'$([ -n "$TMUX" ] && tmux setenv TMUXPWD_$(tmux display -p "#D" | tr -d %) "$PWD")'

export PATH=$HOME/.cargo/bin:$HOME/.rbenv/bin:$HOME/.rbenv/shims:$GOPATH/bin:$JAVA_HOME/bin:/usr/local/bin:/usr/local/sbin:$PATH
export RUST_SRC_PATH="$(rustc --print sysroot)/lib/rustlib/src/rust/src"

# added by travis gem
[ -f /Users/reset/.travis/travis.sh ] && source /Users/reset/.travis/travis.sh

eval $(/usr/libexec/path_helper -s)

export NVM_DIR="$HOME/.nvm"
[ -s "$NVM_DIR/nvm.sh" ] && \. "$NVM_DIR/nvm.sh"  # This loads nvm

alias e=vim
alias vi=vim
alias config='/usr/bin/git --git-dir=/Users/reset/.cfg/ --work-tree=/Users/reset'
eval "$(direnv hook zsh)"

