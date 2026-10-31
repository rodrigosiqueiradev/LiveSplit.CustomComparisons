# Builds the component and runs the engine tests.
# Full output goes to build-log.txt next to this script.
$ErrorActionPreference = 'Continue'
Set-Location $PSScriptRoot
$log = Join-Path $PSScriptRoot 'build-log.txt'
& {
    Write-Output "=== $(Get-Date -Format o)"
    Write-Output '=== dotnet --list-sdks'
    dotnet --list-sdks
    Write-Output '=== editor (npm)'
    Push-Location editor
    if (-not (Test-Path node_modules)) { npm install --no-audit --no-fund }
    npm run build
    Write-Output "=== editor exit code: $LASTEXITCODE"
    Pop-Location
    Write-Output '=== dotnet test (engine)'
    dotnet test tests/CustomComparisons.Engine.Tests --nologo -v minimal
    Write-Output "=== test exit code: $LASTEXITCODE"
    Write-Output '=== dotnet build (component)'
    dotnet build src/LiveSplit.CustomComparisons -c Release --nologo -v minimal
    Write-Output "=== build exit code: $LASTEXITCODE"
} *>&1 | Tee-Object -FilePath $log
Write-Host "`nDone. Log saved to $log" -ForegroundColor Cyan
