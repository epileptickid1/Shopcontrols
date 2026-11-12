$ErrorActionPreference='Stop'
$rootDir=Split-Path $PSScriptRoot -Parent
Push-Location $rootDir
try {
 . './scripts/dotnet-env.ps1'
 $psFiles=@(Get-ChildItem -LiteralPath scripts -Filter '*.ps1')
 foreach($file in $psFiles) {
  $parseTokens=$null
  $parseIssues=$null
  [System.Management.Automation.Language.Parser]::ParseFile($file.FullName,[ref]$parseTokens,[ref]$parseIssues) | Out-Null
  if($parseIssues.Count -gt 0){throw ('PowerShell syntax failed in '+$file.Name+': '+($parseIssues.Message -join '; '))}
 }
 $jsFiles=@(Get-ChildItem -LiteralPath scripts,tests -Filter '*.mjs')+@(Get-ChildItem -LiteralPath 'app/Store.Web/wwwroot/js' -Filter '*.js')
 foreach($file in $jsFiles) {
  & node --check $file.FullName
  if($LASTEXITCODE -ne 0){throw ('JavaScript syntax failed: '+$file.Name)}
 }
 & node './tests/client-money.mjs'
 if($LASTEXITCODE -ne 0){throw 'Client money tests failed'}
 Restore-StoreProject 'app/Store.slnx'
 # Keep verification outputs separate from a running app's locked binaries.
 $checkOutput=Join-Path $rootDir '.runtime/check-build'
 & dotnet build 'app/Store.slnx' -c Release --no-restore -m:1 -p:UseSharedCompilation=false "-p:OutputPath=$checkOutput"
 if($LASTEXITCODE -ne 0){throw 'Solution build failed'}
 & dotnet (Join-Path $checkOutput 'Store.IntegrationTests.dll') --offline
 if($LASTEXITCODE -ne 0){throw 'Offline manager session checks failed'}
 $sdkVersion=(& dotnet --version).Trim()
 [System.IO.Directory]::CreateDirectory((Join-Path $rootDir 'reports')) | Out-Null
 [ordered]@{
  checkedAt=[DateTimeOffset]::UtcNow.ToString('o')
  dotnetSdk=$sdkVersion
  releaseBuild='passed'
  powerShellSyntaxFiles=$psFiles.Count
  javaScriptSyntaxFiles=$jsFiles.Count
  clientMoneyTests='passed'
  managerSessionChecks='passed'
  databaseIntegration='not-run-by-this-script'
  browserVerification='not-run-by-this-script'
 } | ConvertTo-Json | Set-Content -LiteralPath 'reports/app-build-check.json' -Encoding utf8
 Write-Output 'Build and offline checks passed. Database/browser checks are separate.'
} finally {Pop-Location}
