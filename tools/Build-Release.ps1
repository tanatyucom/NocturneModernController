param(
    [string]$Version = '2.0.1',
    [switch]$NoBuild
)

$ErrorActionPreference = 'Stop'

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$artifactRoot = Join-Path $repositoryRoot 'artifacts\release'
$packageName = "NocturneModernController-v$Version"
$stageRoot = Join-Path $artifactRoot $packageName
$modsRoot = Join-Path $stageRoot 'Mods'
$helperRoot = Join-Path $modsRoot 'NocturneModernController.Helper'
$zipPath = Join-Path $artifactRoot "$packageName.zip"

if (-not $NoBuild) {
    dotnet build (Join-Path $repositoryRoot 'NocturneModernController.csproj') -c Release --no-restore
    dotnet build (Join-Path $repositoryRoot 'helper\NocturneModernController.InputHelper.csproj') -c Release --no-restore
    dotnet build (Join-Path $repositoryRoot 'settings\NocturneModernController.Settings.csproj') -c Release --no-restore
}

$controllerOutput = Join-Path $repositoryRoot 'bin\Release\net6.0'
$helperOutput = Join-Path $repositoryRoot 'helper\bin\Release\net6.0'
$settingsOutput = Join-Path $repositoryRoot 'settings\bin\Release\net6.0-windows'
$sdlPath = Join-Path $repositoryRoot 'tools\ControllerSideRead\native\SDL3.dll'

$requiredFiles = @(
    (Join-Path $controllerOutput 'NocturneModernController.dll'),
    (Join-Path $helperOutput 'NocturneModernController.InputHelper.exe'),
    (Join-Path $helperOutput 'NocturneModernController.InputHelper.dll'),
    (Join-Path $helperOutput 'NocturneModernController.InputHelper.deps.json'),
    (Join-Path $helperOutput 'NocturneModernController.InputHelper.runtimeconfig.json'),
    (Join-Path $settingsOutput 'NocturneModernController.Settings.exe'),
    (Join-Path $settingsOutput 'NocturneModernController.Settings.dll'),
    (Join-Path $settingsOutput 'NocturneModernController.Settings.deps.json'),
    (Join-Path $settingsOutput 'NocturneModernController.Settings.runtimeconfig.json'),
    $sdlPath,
    (Join-Path $repositoryRoot 'README.md'),
    (Join-Path $repositoryRoot 'README_EN.md'),
    (Join-Path $repositoryRoot 'CHANGELOG.md'),
    (Join-Path $repositoryRoot 'LICENSE'),
    (Join-Path $repositoryRoot 'THIRD_PARTY_NOTICES.txt')
)

foreach ($file in $requiredFiles) {
    if (-not (Test-Path -LiteralPath $file)) {
        throw "Required release file is missing: $file"
    }
}

if (Test-Path -LiteralPath $stageRoot) {
    Remove-Item -LiteralPath $stageRoot -Recurse -Force
}
if (Test-Path -LiteralPath $zipPath) {
    Remove-Item -LiteralPath $zipPath -Force
}

New-Item -ItemType Directory -Path $helperRoot -Force | Out-Null

Copy-Item -LiteralPath (Join-Path $controllerOutput 'NocturneModernController.dll') -Destination $modsRoot

$helperFiles = @(
    'NocturneModernController.InputHelper.exe',
    'NocturneModernController.InputHelper.dll',
    'NocturneModernController.InputHelper.deps.json',
    'NocturneModernController.InputHelper.runtimeconfig.json'
)
foreach ($file in $helperFiles) {
    Copy-Item -LiteralPath (Join-Path $helperOutput $file) -Destination $helperRoot
}

$settingsFiles = @(
    'NocturneModernController.Settings.exe',
    'NocturneModernController.Settings.dll',
    'NocturneModernController.Settings.deps.json',
    'NocturneModernController.Settings.runtimeconfig.json'
)
foreach ($file in $settingsFiles) {
    Copy-Item -LiteralPath (Join-Path $settingsOutput $file) -Destination $helperRoot
}

Copy-Item -LiteralPath $sdlPath -Destination $helperRoot
Copy-Item -LiteralPath (Join-Path $repositoryRoot 'README.md') -Destination (Join-Path $stageRoot 'README.md')
Copy-Item -LiteralPath (Join-Path $repositoryRoot 'README_EN.md') -Destination (Join-Path $stageRoot 'README_EN.md')
Copy-Item -LiteralPath (Join-Path $repositoryRoot 'CHANGELOG.md') -Destination $stageRoot
Copy-Item -LiteralPath (Join-Path $repositoryRoot 'LICENSE') -Destination (Join-Path $stageRoot 'LICENSE.txt')
Copy-Item -LiteralPath (Join-Path $repositoryRoot 'THIRD_PARTY_NOTICES.txt') -Destination $stageRoot

Compress-Archive -Path (Join-Path $stageRoot '*') -DestinationPath $zipPath -CompressionLevel Optimal

$hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $zipPath).Hash
Write-Output "Created $zipPath"
Write-Output "SHA-256: $hash"
