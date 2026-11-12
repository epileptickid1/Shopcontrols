$ErrorActionPreference='Stop'
$projectDir=Split-Path $PSScriptRoot -Parent
$runtimeDir=(Get-Content -LiteralPath (Join-Path $projectDir '.runtime/portable-root.txt') -Raw).Trim()
& (Join-Path $runtimeDir 'pgsql/bin/pg_ctl.exe') stop -D (Join-Path $runtimeDir 'data2') -m fast -w -t 30
if ($LASTEXITCODE -ne 0) { throw 'PostgreSQL stop failed' }

