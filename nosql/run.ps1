param([switch]$Quick, [switch]$ImportShopcontrols)
$ErrorActionPreference = 'Stop'
Push-Location -LiteralPath $PSScriptRoot
try {
    function Invoke-LabCommand([string]$Program, [string[]]$Arguments) {
        & $Program @Arguments
        if ($LASTEXITCODE -ne 0) {
            throw "$Program failed (exit $LASTEXITCODE): $($Arguments -join ' ')"
        }
    }

    Invoke-LabCommand 'npm.cmd' @('install', '--ignore-scripts', '--no-fund')
    Invoke-LabCommand 'npm.cmd' @('test')
    Invoke-LabCommand 'docker' @('compose', 'up', '-d', '--wait', 'postgres', 'mongo')
    Invoke-LabCommand 'npm.cmd' @('run', 'lab', '--', 'demo')
    if ($ImportShopcontrols) {
        Invoke-LabCommand 'npm.cmd' @('run', 'lab', '--', 'import')
    }
    $benchmarkArgs = @('run', 'lab', '--', 'benchmark')
    if ($Quick) { $benchmarkArgs += @('--sizes', '1000', '--samples', '24') }
    Invoke-LabCommand 'npm.cmd' $benchmarkArgs
    Invoke-LabCommand 'docker' @('compose', 'up', '-d', '--wait', 'neo4j')
    Invoke-LabCommand 'npm.cmd' @('run', 'lab', '--', 'graph')
    if ($ImportShopcontrols) {
        Invoke-LabCommand 'npm.cmd' @('run', 'lab', '--', 'graph', '--dataset', 'shopcontrols')
    }
}
finally {
    Pop-Location
}
