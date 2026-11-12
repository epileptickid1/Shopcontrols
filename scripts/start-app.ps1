param([int]$Port=5080)
$ErrorActionPreference='Stop'
$rootDir=Split-Path $PSScriptRoot -Parent
Push-Location $rootDir
try {
 . './scripts/dotnet-env.ps1'
 & './scripts/setup-app.ps1'
 Restore-StoreProject 'app/Store.Web/Store.Web.csproj'
 & dotnet build 'app/Store.Web/Store.Web.csproj' -c Release --no-restore -m:1 -p:UseSharedCompilation=false
 if($LASTEXITCODE -ne 0){throw 'Application build failed'}
 Write-Output "Open http://127.0.0.1:$Port in your browser. Ctrl+C stops the application."
 Push-Location 'app/Store.Web'
 try { & dotnet './bin/Release/net10.0/Store.Web.dll' --urls "http://127.0.0.1:$Port" }
 finally {Pop-Location}
} finally {Pop-Location}

