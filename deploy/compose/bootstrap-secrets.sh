#!/usr/bin/env bash
# ZWarden reference deployment — secrets bootstrap (F34, ADR 0037; D-3).
#
# Generates the secrets the stack fails closed without and writes them into a git-ignored `.env`:
#   - ZW_SECRET_KEYS / ZW_SECRET_ACTIVE_KEY_ID : the AES-256-GCM key ring (ADR 0015)
#   - POSTGRES_PASSWORD                        : the database password (used by the Postgres overlay)
#
# It NEVER overwrites an existing .env (your secrets are load-bearing — losing the key ring makes encrypted
# columns undecryptable). Delete .env yourself if you truly mean to re-key. Non-secret values (domain, ACME
# e-mail, enrollment, PZ image) are left for you to fill in — see the comments in .env.

set -euo pipefail
cd "$(dirname "$0")"

ENV_FILE=".env"
TEMPLATE=".env.example"

if [[ -f "$ENV_FILE" ]]; then
  echo "refusing to overwrite existing $ENV_FILE (delete it first to re-key)" >&2
  exit 1
fi

if [[ ! -f "$TEMPLATE" ]]; then
  echo "missing $TEMPLATE next to this script" >&2
  exit 1
fi

# A 256-bit key, base64-encoded, keyed as k1 (the active key). openssl is present on any Docker host.
key_ring="k1:$(openssl rand -base64 32)"
# A URL/shell-safe strong password (base64 of 24 random bytes, +// stripped).
pg_password="$(openssl rand -base64 24 | tr -d '+/=')"

# Start from the template so every documented setting is present, then fill the generated secrets in place.
cp "$TEMPLATE" "$ENV_FILE"

# Portable in-place substitution (works with both GNU and BSD sed via a temp file).
set_kv() {
  local key="$1" value="$2" tmp
  tmp="$(mktemp)"
  # Replace the first `KEY=...` line; value is written literally after the first `=`.
  awk -v k="$key" -v v="$value" 'BEGIN{done=0} !done && $0 ~ "^"k"=" {print k"="v; done=1; next} {print}' \
    "$ENV_FILE" > "$tmp"
  mv "$tmp" "$ENV_FILE"
}

set_kv "ZW_SECRET_KEYS" "$key_ring"
set_kv "ZW_SECRET_ACTIVE_KEY_ID" "k1"
set_kv "POSTGRES_PASSWORD" "$pg_password"

chmod 600 "$ENV_FILE" 2>/dev/null || true

echo "wrote $ENV_FILE with a freshly generated key ring and Postgres password."
echo "next: set ZWARDEN_DOMAIN (and ZWARDEN_ACME_EMAIL for Public TLS) in $ENV_FILE, then 'docker compose up -d'."
