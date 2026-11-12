# Dot-source this file from the project scripts.
$projectRoot=Split-Path $PSScriptRoot -Parent
$env:DOTNET_CLI_HOME=Join-Path $projectRoot '.runtime/dotnet'
$env:DOTNET_CLI_TELEMETRY_OPTOUT='1'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE='1'
$env:MSBuildEnableWorkloadResolver='false'
$env:NUGET_PACKAGES=Join-Path $projectRoot '.runtime/nuget'
function Restore-StoreProject([string]$Project) {
 $localFeed=Join-Path $projectRoot '.runtime/nuget'
 $userFeed=Join-Path $env:USERPROFILE '.nuget/packages'
 $restoreArgs=@('restore',$Project,'--ignore-failed-sources','--disable-parallel','-m:1','-p:NuGetAudit=false')
 # Local packages support offline demonstrations on this computer.
 if(Test-Path (Join-Path $localFeed 'npgsql/10.0.3/npgsql.10.0.3.nupkg')) {
  $restoreArgs+=@('--source',$localFeed)
 } elseif(Test-Path (Join-Path $userFeed 'npgsql/10.0.3/npgsql.10.0.3.nupkg')) {
  $restoreArgs+=@('--source',$userFeed)
 } else {
  $restoreArgs+=@('--source','https://api.nuget.org/v3/index.json')
 }
 & dotnet @restoreArgs
 if($LASTEXITCODE -ne 0){throw 'NuGet restore failed'}
}

