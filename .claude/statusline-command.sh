#!/usr/bin/env bash
set -euo pipefail

input=$(cat)

cwd=$(echo "$input" | jq -r '.cwd')
model=$(echo "$input" | jq -r '.model.display_name')
remaining=$(echo "$input" | jq -r '.context_window.remaining_percentage // empty')

# Shorten home directory to ~
home="$HOME"
short_cwd="${cwd/#$home/\~}"

# Git branch (skip optional locks)
branch=""
if git -C "$cwd" rev-parse --git-dir > /dev/null 2>&1; then
  branch=$(git -C "$cwd" -c gc.auto=0 symbolic-ref --short HEAD 2>/dev/null || git -C "$cwd" rev-parse --short HEAD 2>/dev/null || true)
fi

# Build output
parts=()
parts+=("$(printf '\033[34m%s\033[0m' "$short_cwd")")
if [ -n "$branch" ]; then
  parts+=("$(printf '\033[33m%s\033[0m' "$branch")")
fi
parts+=("$(printf '\033[36m%s\033[0m' "$model")")
if [ -n "$remaining" ]; then
  remaining_int=$(printf '%.0f' "$remaining")
  parts+=("$(printf '\033[32mctx:%s%%\033[0m' "$remaining_int")")
fi

printf '%s' "${parts[0]}"
for part in "${parts[@]:1}"; do
  printf ' \033[2m|\033[0m %s' "$part"
done
printf '\n'
