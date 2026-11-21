param(
    [switch]$Publish,
    [switch]$Installer,
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$solution = Join-Path $PSScriptRoot 'SwiftBase.slnx'
$appProject = Join-Path $PSScriptRoot 'src\SwiftBase.App\SwiftBase.App.csproj'
$publishDir = Join-Path $PSScriptRoot 'artifacts\SwiftBase'

if ($Installer -and -not (Test-Path (Join-Path $publishDir 'SwiftBase.exe'))) {
    $Publish = $true
}

dotnet build $solution -c $Configuration
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

if ($Publish) {
    dotnet publish $appProject -c $Configuration -r win-x64 --self-contained true -o $publishDir
    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }

    Write-Host "Published to $publishDir"
}

if ($Installer) {
    $iscc = @(
        'C:\Program Files (x86)\Inno Setup 6\ISCC.exe',
        'C:\Program Files\Inno Setup 6\ISCC.exe'
    ) | Where-Object { Test-Path $_ } | Select-Object -First 1

    if (-not $iscc) {
        throw 'Inno Setup 6 compiler (ISCC.exe) was not found.'
    }

    $script = Join-Path $PSScriptRoot 'installer\SwiftBase-Setup.iss'
    & $iscc $script
    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }

    Write-Host "Installer created in $(Join-Path $PSScriptRoot 'artifacts\installer')"
}
