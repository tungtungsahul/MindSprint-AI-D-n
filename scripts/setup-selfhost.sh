#!/usr/bin/env bash
set -euo pipefail

SOURCE="${1:-.env.selfhost.example}"
TARGET="${2:-.env.selfhost}"

if [ ! -f "$SOURCE" ]; then
  echo "Missing template file: $SOURCE" >&2
  exit 1
fi

if [ ! -f "$TARGET" ]; then
  cp "$SOURCE" "$TARGET"
  echo "Created $TARGET from template without overwriting an existing config."
else
  echo "$TARGET already exists. Leaving it unchanged."
fi
