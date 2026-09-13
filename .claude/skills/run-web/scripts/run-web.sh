#!/usr/bin/env bash
# Launch ZWarden.Web locally for manual inspection / screenshots (Feature 15 run-skill).
#
# Fails closed without a secret key ring (ADR 0015) and needs a seeded admin to reach any
# authenticated page, so this wires both with dev-only throwaway values. Nothing here is a
# production secret — the key is generated per run and the DB is a scratch file.
#
# Usage:  bash .claude/skills/run-web/scripts/run-web.sh [db_path]
#   db_path  Absolute Windows path to the SQLite file (default: a temp file). Pass the SAME path
#            the seeder used if you want demo servers to show.
#
# Env overrides: ZW_WEB_URL (default http://localhost:5063), ZW_ADMIN_EMAIL, ZW_ADMIN_PASSWORD.
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../../.." && pwd)"
cd "$REPO_ROOT"

# A Windows-style path — Microsoft.Data.Sqlite rejects the MSYS '/c/...' form.
DEFAULT_DB='C:\Users\Public\zwarden-dev.db'
DB_PATH="${1:-$DEFAULT_DB}"

export ASPNETCORE_ENVIRONMENT=Development
export ASPNETCORE_URLS="${ZW_WEB_URL:-http://localhost:5063}"
export ZWarden__Database__Provider=sqlite
export ConnectionStrings__ZWarden="Data Source=${DB_PATH}"

# A confirmed Tenant-Owner admin so /servers, /audit etc. are reachable (AdminBootstrapper).
export ZWarden__Admin__Email="${ZW_ADMIN_EMAIL:-admin@zwarden.test}"
export ZWarden__Admin__Password="${ZW_ADMIN_PASSWORD:-Sup3r-Str0ng-P@ss!}"

# Secret key ring: one 32-byte base64 key, generated per run unless the caller provides one.
if [ -z "${ZW_SECRET_KEYS:-}" ]; then
  KEY="$(openssl rand -base64 32 2>/dev/null || dotnet fsi --use:/dev/stdin <<<'printf "%s" (System.Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes 32))' 2>/dev/null)"
  export ZW_SECRET_KEYS="k1:${KEY}"
  export ZW_SECRET_ACTIVE_KEY_ID="k1"
fi

echo "[run-web] URL=${ASPNETCORE_URLS}  DB=${DB_PATH}  admin=${ZWarden__Admin__Email}"
echo "[run-web] launching (Ctrl+C to stop) ..."
exec dotnet run --project src/ZWarden.Web/ZWarden.Web.csproj --no-launch-profile -c Debug
