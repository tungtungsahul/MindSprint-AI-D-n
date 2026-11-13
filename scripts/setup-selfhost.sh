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

required_vars=(
  "POSTGRES_DB"
  "POSTGRES_USER"
  "POSTGRES_PASSWORD"
  "ConnectionStrings__Default"
  "Jwt__Key"
  "Cors__AllowedOrigins"
)

missing=()
for key in "${required_vars[@]}"; do
  if ! grep -Eq "^${key}=" "$TARGET"; then
    missing+=("$key")
  fi
done

if [ "${#missing[@]}" -gt 0 ]; then
  echo "Missing required values in $TARGET: ${missing[*]}" >&2
  exit 1
fi

echo "Validated required self-host settings in $TARGET."
