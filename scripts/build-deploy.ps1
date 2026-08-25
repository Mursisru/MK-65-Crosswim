$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location -LiteralPath $root
dotnet build ".\MK65Crosswim\MK65Crosswim.csproj" -c Release
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
Write-Host "Build OK"
