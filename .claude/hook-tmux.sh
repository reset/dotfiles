#!/usr/bin/env bash
set -euo pipefail

# Rename the current tmux window to reflect Claude Code state.
# Called from ~/.claude/settings.json hooks. No-op outside tmux.
#
# Usage: hook-tmux.sh <work|wait|stop>
#   work  — Claude is actively using tools (PreToolUse)
#   wait  — Claude needs attention / waiting for input (Notification)
#   stop  — Claude session ended (Stop)

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
  work)
    tmux rename-window -t "$TMUX_PANE" "⚙ $project"
    ;;
  wait)
    tmux rename-window -t "$TMUX_PANE" "● $project"
    ;;
  stop)
    tmux rename-window -t "$TMUX_PANE" "✓ $project"
    ;;
  *)
    echo >&2 "hook-tmux.sh: unknown action '$action' (expected work|wait|stop)"
    ;;
esac

exit 0
