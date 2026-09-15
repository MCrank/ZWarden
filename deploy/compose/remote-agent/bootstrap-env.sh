#!/usr/bin/env bash
# ZWarden remote Agent — environment bootstrap (F35, ADR 0038).
#
# A remote Agent holds NO key ring and NO database, so — unlike the control-plane stack's bootstrap-secrets.sh
# — this generates no secrets. It just scaffolds a git-ignored `.env` from the template for you to fill in:
# the control-plane domain and the one-time enrollment secret the wizard issues.
#
# It NEVER overwrites an existing .env (which may hold your enrollment secret mid-bootstrap). Delete it first
# if you mean to start over.

set -euo pipefail
cd "$(dirname "$0")"

ENV_FILE=".env"
TEMPLATE=".env.example"

if [[ -f "$ENV_FILE" ]]; then
  echo "refusing to overwrite existing $ENV_FILE (delete it first to start over)" >&2
  exit 1
fi

if [[ ! -f "$TEMPLATE" ]]; then
  echo "missing $TEMPLATE next to this script" >&2
  exit 1
fi

cp "$TEMPLATE" "$ENV_FILE"
chmod 600 "$ENV_FILE" 2>/dev/null || true

echo "wrote $ENV_FILE from $TEMPLATE."
echo "next: set ZWARDEN_DOMAIN and paste the wizard's ZWARDEN_ENROLLMENT_SECRET into $ENV_FILE, then 'docker compose up -d'."
