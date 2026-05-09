#!/usr/bin/env bash
set -euo pipefail

# Rename the current tmux window to reflect Claude Code state.
# Called from ~/.claude/settings.json hooks. No-op outside tmux.
#
# Usage: hook-tmux.sh <stop|wait>

# No-op if not inside tmux
if [ -z "${TMUX_PANE:-}" ]; then
  exit 0
fi

action="${1:-stop}"

# Read stdin (Claude Code hook JSON) and extract cwd
cwd=$(jq -r '.cwd // ""' 2>/dev/null || echo "")

if [ -n "$cwd" ]; then
  project=$(basename "$cwd")
else
  project="claude"
fi

case "$action" in
  stop)
    tmux rename-window -t "$TMUX_PANE" "✓ $project"
    ;;
  wait)
    tmux rename-window -t "$TMUX_PANE" "⌛ $project"
    ;;
  *)
    echo >&2 "hook-tmux.sh: unknown action '$action' (expected stop|wait)"
    ;;
esac

exit 0
