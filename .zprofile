if [[ $OSTYPE == 'darwin'* ]]; then
  eval "$(/opt/homebrew/bin/brew shellenv)"
else
  eval "$(/home/linuxbrew/.linuxbrew/bin/brew shellenv)"
fi

# After a brew upgrade zsh, the running tmux session still exports the old
# versioned fpath entry (e.g. Cellar/zsh/5.9/…). Strip any stale versioned
# entries and add the correct one for the current binary — upgrade-proof.
fpath=( "${(@)fpath:#/opt/homebrew/Cellar/zsh/*/share/zsh/functions}" )
fpath+=( "${${commands[zsh]:A}:h:h}/share/zsh/functions" )

emulate sh -c '. ~/.profile'
