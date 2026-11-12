param([string]$Database='store_webtests')
$ErrorActionPreference='Stop'
$rootDir=Split-Path $PSScriptRoot -Parent
Push-Location $rootDir
try {
 . './scripts/dotnet-env.ps1'
 & './scripts/start-db.ps1'
 & node './scripts/prepare-app-tests.mjs' $Database
 if($LASTEXITCODE -ne 0){throw 'Test database setup failed'}
 Restore-StoreProject 'app/Store.IntegrationTests/Store.IntegrationTests.csproj'
 $testOutput=Join-Path $rootDir '.runtime/test-build'
 & dotnet build 'app/Store.IntegrationTests/Store.IntegrationTests.csproj' -c Release --no-restore -m:1 -p:UseSharedCompilation=false "-p:OutputPath=$testOutput"
 if($LASTEXITCODE -ne 0){throw 'Integration test build failed'}
 $env:StoreTestConnectionString=(Get-Content -LiteralPath '.runtime/app-test-connection.txt' -Raw).Trim()
 $env:StoreTestManager2ConnectionString=(Get-Content -LiteralPath '.runtime/app-test-manager-2-connection.txt' -Raw).Trim()
 try {
  & dotnet (Join-Path $testOutput 'Store.IntegrationTests.dll')
  if($LASTEXITCODE -ne 0){throw 'Integration tests failed. See reports/app-integration-tests.json'}
 } finally {
  Remove-Item Env:StoreTestConnectionString -ErrorAction SilentlyContinue
  Remove-Item Env:StoreTestManager2ConnectionString -ErrorAction SilentlyContinue
 }
} finally {Pop-Location}

