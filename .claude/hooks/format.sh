#!/usr/bin/env bash
# PostToolUse hook: format the file Claude just edited. Never blocks the edit (always exit 0).
set -uo pipefail

input="$(cat)"
if command -v jq >/dev/null 2>&1; then
  file="$(printf '%s' "$input" | jq -r '.tool_input.file_path // empty' 2>/dev/null || true)"
else
  file="$(printf '%s' "$input" | grep -o '"file_path"[[:space:]]*:[[:space:]]*"[^"]*"' \
    | head -1 | sed 's/.*"file_path"[[:space:]]*:[[:space:]]*"//; s/"$//')"
fi

[ -z "${file:-}" ] && exit 0
[ -f "$file" ] || exit 0

case "$file" in
  *.cs)
    dotnet format --include "$file" >/dev/null 2>&1 || true
    ;;
  *.ts|*.html|*.scss|*.css|*.json)
    # Use the Angular workspace's pinned prettier, never a floating npx download.
    prettier="$(git rev-parse --show-toplevel)/src/web/node_modules/.bin/prettier"
    if [ -x "$prettier" ]; then
      "$prettier" --write "$file" >/dev/null 2>&1 || true
    else
      echo "format.sh: prettier not installed; skipped $file (run: npm ci --prefix src/web)" >&2
    fi
    ;;
esac

exit 0
