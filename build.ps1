param([string]$DotnetPath = 'dotnet')
$ErrorActionPreference = 'Stop'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$output = Join-Path $PSScriptRoot 'dist/MchoseBattery'
& $DotnetPath run --project (Join-Path $PSScriptRoot 'tests/MchoseBattery.Tests') -c Release
if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
& $DotnetPath publish (Join-Path $PSScriptRoot 'src/MchoseBattery') -c Release -r win-x64 --self-contained true -p:DebugType=None -p:DebugSymbols=false -o $output
if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
foreach ($symbolFile in @('MchoseBattery.pdb', 'MchoseBattery.Core.pdb')) {
    $symbolPath = Join-Path $output $symbolFile
    if (Test-Path -LiteralPath $symbolPath) { Remove-Item -LiteralPath $symbolPath -Force }
}
foreach ($file in @('README.md', 'LICENSE', 'THIRD-PARTY-NOTICES.txt')) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot $file) -Destination $output -Force
}
New-Item -ItemType Directory -Path (Join-Path $output 'docs') -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'docs/icon-preview.png') -Destination (Join-Path $output 'docs/icon-preview.png') -Force
$artifacts = Join-Path $PSScriptRoot 'artifacts'
New-Item -ItemType Directory -Path $artifacts -Force | Out-Null
$zip = Join-Path $artifacts 'MCHOSE-A7pro-Battery-win-x64.zip'
Compress-Archive -Path $output -DestinationPath $zip -Force
$hash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -LiteralPath ($zip + '.sha256') -Value ($hash + '  ' + [IO.Path]::GetFileName($zip)) -Encoding ascii
Write-Output "Built: $zip"
