param([switch]$Publish, [switch]$Check)
$ErrorActionPreference = 'Stop'
$solution = Join-Path $PSScriptRoot 'zhijian.slnx'
& dotnet restore $solution --locked-mode
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
& dotnet build $solution -c Release --no-restore
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
if ($Check) {
    $results = Join-Path $PSScriptRoot ('artifacts/checks-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
    & dotnet run --project (Join-Path $PSScriptRoot 'Zhijian.Checks/Zhijian.Checks.csproj') -c Release --no-build -- $results
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
if ($Publish) {
    & dotnet publish (Join-Path $PSScriptRoot 'zhijian.app/zhijian.app.csproj') -c Release -r win-x64 --self-contained true -o (Join-Path $PSScriptRoot 'publish/win-x64')
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
