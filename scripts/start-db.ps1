param([int]$Port = 55432, [string]$RuntimePath)
$ErrorActionPreference = 'Stop'
$projectDir = Split-Path $PSScriptRoot -Parent
$localRuntime = Join-Path $projectDir '.runtime'
New-Item -ItemType Directory -Path $localRuntime -Force | Out-Null
$pointer = Join-Path $localRuntime 'portable-root.txt'
if (-not $RuntimePath) {
 if (Test-Path $pointer) { $RuntimePath = (Get-Content -LiteralPath $pointer -Raw).Trim() }
 else { $RuntimePath = Join-Path $env:TEMP 'stdb-postgres18' }
}
$RuntimePath = [IO.Path]::GetFullPath($RuntimePath)
if ($RuntimePath -match '[^\x00-\x7F]') { throw 'PostgreSQL on Windows needs an ASCII path. Supply -RuntimePath with a Latin-only path.' }
New-Item -ItemType Directory -Path $RuntimePath -Force | Out-Null
$pgBin = Join-Path $RuntimePath 'pgsql/bin'
if (-not (Test-Path (Join-Path $pgBin 'initdb.exe'))) {
 $archive = Join-Path $localRuntime 'postgresql.zip'
 if (-not (Test-Path $archive)) {
  Invoke-WebRequest 'https://get.enterprisedb.com/postgresql/postgresql-18.6-1-windows-x64-binaries.zip' -OutFile $archive
 }
 Add-Type -AssemblyName System.IO.Compression.FileSystem
 $zip = [IO.Compression.ZipFile]::OpenRead($archive)
 try {
  foreach ($entry in $zip.Entries) {
   if ($entry.FullName -match '^pgsql/(bin|lib|share)/' -and $entry.Name) {
    $dest = [IO.Path]::GetFullPath((Join-Path $RuntimePath $entry.FullName))
    if (-not $dest.StartsWith($RuntimePath + [IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) { throw 'Unexpected archive path' }
    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($dest)) | Out-Null
    [IO.Compression.ZipFileExtensions]::ExtractToFile($entry,$dest,$false)
   }
  }
 } finally { $zip.Dispose() }
}
$RuntimePath | Set-Content -LiteralPath $pointer -Encoding utf8
$dataDir = Join-Path $RuntimePath 'data2'
$passwordFile = Join-Path $localRuntime 'postgres-password.txt'
if (-not (Test-Path (Join-Path $dataDir 'global/pg_control'))) {
 if (Test-Path $dataDir) { throw "Incomplete data directory: $dataDir. Select a new RuntimePath; existing files are preserved." }
 $bytes = New-Object byte[] 32
 $rng=[System.Security.Cryptography.RandomNumberGenerator]::Create()
 try { $rng.GetBytes($bytes) } finally { $rng.Dispose() }
 [Convert]::ToBase64String($bytes) | Set-Content -LiteralPath $passwordFile -Encoding ascii
 $asciiPassword = Join-Path $RuntimePath 'postgres-password.txt'
 Copy-Item -LiteralPath $passwordFile -Destination $asciiPassword -Force
 Push-Location $RuntimePath
 try {
  & (Join-Path $pgBin 'initdb.exe') -D $dataDir -U postgres --encoding=UTF8 --locale=C --auth-host=scram-sha-256 --auth-local=scram-sha-256 "--pwfile=$asciiPassword" -c shared_buffers=32MB -c max_connections=30
  if ($LASTEXITCODE -ne 0) { throw 'initdb failed' }
 } finally { Pop-Location }
 @"
listen_addresses = '127.0.0.1'
port = $Port
timezone = 'Europe/Kyiv'
"@ | Add-Content -LiteralPath (Join-Path $dataDir 'postgresql.conf') -Encoding utf8
}
$previousPassword=$env:PGPASSWORD
try {
 if (Test-Path $passwordFile) {
  $env:PGPASSWORD=(Get-Content -LiteralPath $passwordFile -Raw).Trim()
  $actualData=& (Join-Path $pgBin 'psql.exe') -X -w -A -t -h 127.0.0.1 -p $Port -U postgres -d postgres -c 'SHOW data_directory' 2>$null
  if ($LASTEXITCODE -eq 0) {
   if ([IO.Path]::GetFullPath($actualData.Trim()) -ne [IO.Path]::GetFullPath($dataDir)) { throw 'Another PostgreSQL instance is using this port.' }
   Write-Output "PostgreSQL already ready. Data: $dataDir"
   return
  }
 }
} finally { $env:PGPASSWORD=$previousPassword }

& (Join-Path $pgBin 'pg_ctl.exe') status -D $dataDir *> $null
if ($LASTEXITCODE -ne 0) {
 & (Join-Path $pgBin 'pg_ctl.exe') start -D $dataDir -l (Join-Path $RuntimePath 'server.log') -w -t 30
 if ($LASTEXITCODE -ne 0) { throw 'PostgreSQL did not start; see server.log in RuntimePath' }
}
Write-Output "PostgreSQL ready. Data: $dataDir"


