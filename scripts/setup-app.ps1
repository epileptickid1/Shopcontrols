param([string]$Database='store_lab')
$ErrorActionPreference='Stop'
$rootDir=Split-Path $PSScriptRoot -Parent
Push-Location $rootDir
try {
 & './scripts/start-db.ps1'
 & node './scripts/setup-app.mjs' $Database
 if($LASTEXITCODE -ne 0){throw 'Application database setup failed'}
} finally {Pop-Location}

