#!/usr/bin/env pwsh
# ZWarden remote Agent — environment bootstrap for Windows hosts (F35, ADR 0038).
#
# PowerShell twin of bootstrap-env.sh. A remote Agent holds NO key ring and NO database, so this generates no
# secrets — it just scaffolds a git-ignored `.env` from `.env.example` for you to fill in (the control-plane
# domain and the one-time enrollment secret). NEVER overwrites an existing `.env` — delete it first to start over.

#Requires -Version 7.0
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$envFile   = Join-Path $scriptDir '.env'
$template  = Join-Path $scriptDir '.env.example'

if (Test-Path -LiteralPath $envFile) {
    Write-Error "refusing to overwrite existing .env (delete it first to start over)"
}
if (-not (Test-Path -LiteralPath $template)) {
    Write-Error "missing .env.example next to this script"
}

Copy-Item -LiteralPath $template -Destination $envFile

Write-Host "wrote .env from .env.example."
Write-Host "next: set ZWARDEN_DOMAIN and paste the wizard's ZWARDEN_ENROLLMENT_SECRET into .env, then 'docker compose up -d'."
