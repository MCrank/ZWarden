#!/usr/bin/env pwsh
# ZWarden reference deployment — secrets bootstrap for Windows hosts (F34, ADR 0037; D-3).
#
# PowerShell twin of bootstrap-secrets.sh. Generates the AES-256-GCM key ring (ADR 0015) and the Postgres
# password into a git-ignored `.env`, starting from `.env.example`. NEVER overwrites an existing `.env`
# (losing the key ring makes encrypted columns undecryptable) — delete it yourself to re-key.

#Requires -Version 7.0
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$envFile   = Join-Path $scriptDir '.env'
$template  = Join-Path $scriptDir '.env.example'

if (Test-Path -LiteralPath $envFile) {
    Write-Error "refusing to overwrite existing .env (delete it first to re-key)"
}
if (-not (Test-Path -LiteralPath $template)) {
    Write-Error "missing .env.example next to this script"
}

function New-RandomBytesBase64 ([int]$count) {
    $bytes = [byte[]]::new($count)
    [System.Security.Cryptography.RandomNumberGenerator]::Fill($bytes)
    return [Convert]::ToBase64String($bytes)
}

# A 256-bit key keyed as k1 (the active key), and a shell/URL-safe strong DB password.
$keyRing    = "k1:" + (New-RandomBytesBase64 32)
$pgPassword = (New-RandomBytesBase64 24) -replace '[+/=]', ''

# Start from the template so every documented setting is present, then fill the generated secrets in place.
$lines = Get-Content -LiteralPath $template
$set = @{ 'ZW_SECRET_KEYS' = $keyRing; 'ZW_SECRET_ACTIVE_KEY_ID' = 'k1'; 'POSTGRES_PASSWORD' = $pgPassword }
$done = @{}

$out = foreach ($line in $lines) {
    $matched = $false
    foreach ($key in $set.Keys) {
        if (-not $done.ContainsKey($key) -and $line -match "^$([regex]::Escape($key))=") {
            $done[$key] = $true
            $matched = $true
            "$key=$($set[$key])"
            break
        }
    }
    if (-not $matched) { $line }
}

Set-Content -LiteralPath $envFile -Value $out -Encoding utf8

Write-Host "wrote .env with a freshly generated key ring and Postgres password."
Write-Host "next: set ZWARDEN_DOMAIN in .env, then 'docker compose up -d'."
