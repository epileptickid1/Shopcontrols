$ErrorActionPreference='Stop'
$rootDir=Split-Path $PSScriptRoot -Parent
$signalPath=Join-Path $rootDir '.runtime/stop-db.signal'
if(Test-Path -LiteralPath $signalPath){Remove-Item -LiteralPath $signalPath}
& (Join-Path $PSScriptRoot 'start-db.ps1')
try {
 while(-not (Test-Path -LiteralPath $signalPath)){Start-Sleep -Seconds 1}
} finally {
 & (Join-Path $PSScriptRoot 'stop-db.ps1')
}

