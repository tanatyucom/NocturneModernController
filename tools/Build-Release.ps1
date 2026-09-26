param(
    # Commit, tag or branch to package. Resolved to a commit id before building.
    [string]$Ref = 'HEAD',
    # Defaults to <Version> in NocturneModernController.csproj at $Ref.
    [string]$Version = ''
)

# Reproducible release build.
#
# The release is built from a fresh temporary worktree of $Ref, never from the
# caller's working tree, so uncommitted/untracked files and working-tree line
# endings cannot leak into the package. Compilation uses
# ContinuousIntegrationBuild + PathMap so the temporary path is not embedded,
# and the ZIP is written with a fixed entry order and timestamp. Building the
# same commit twice, from any location, gives byte-identical DLLs and ZIP.

$ErrorActionPreference = 'Stop'

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$artifactRoot = Join-Path $repositoryRoot 'artifacts\release'
# SDL3.dll is a third-party binary kept out of git (tools\ControllerSideRead\Fetch-Sdl.ps1).
$sdlPath = Join-Path $repositoryRoot 'tools\ControllerSideRead\native\SDL3.dll'
$zipTimestamp = [DateTimeOffset]::new(2000, 1, 1, 0, 0, 0, [TimeSpan]::Zero)

function Invoke-Native {
    param([string]$FilePath, [string[]]$Arguments)
    & $FilePath @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$FilePath $($Arguments -join ' ') failed with exit code $LASTEXITCODE"
    }
}

function Get-Sha256([string]$Path) {
    (Get-FileHash -Algorithm SHA256 -LiteralPath $Path).Hash
}

if (-not (Test-Path -LiteralPath $sdlPath)) {
    throw "SDL3.dll is missing: $sdlPath (run tools\ControllerSideRead\Fetch-Sdl.ps1)"
}

$commit = (& git -C $repositoryRoot rev-parse --verify "$Ref^{commit}").Trim()
if ($LASTEXITCODE -ne 0 -or -not $commit) {
    throw "Cannot resolve ref '$Ref'"
}

$workRoot = Join-Path ([IO.Path]::GetTempPath()) ("nmc-release-" + [Guid]::NewGuid().ToString('N'))
$worktreeAdded = $false
try {
    Invoke-Native git @('-C', $repositoryRoot, 'worktree', 'add', '--detach', $workRoot, $commit)
    $worktreeAdded = $true

    if (-not $Version) {
        [xml]$coreProject = Get-Content -LiteralPath (Join-Path $workRoot 'NocturneModernController.csproj') -Raw
        $Version = ($coreProject.Project.PropertyGroup | Where-Object { $_.Version } | Select-Object -First 1).Version
        if (-not $Version) {
            throw 'Version not found in NocturneModernController.csproj'
        }
    }

    # MSBuild splits PathMap on '='; the source side must end with a separator.
    $pathMap = "-p:PathMap=$workRoot\=/_/"
    $buildOptions = @('-c', 'Release', '--no-incremental', '-nologo', '-warnaserror',
        '-p:ContinuousIntegrationBuild=true', $pathMap)
    $projects = @(
        'NocturneModernController.csproj',
        'helper\NocturneModernController.InputHelper.csproj',
        'settings\NocturneModernController.Settings.csproj',
        'tests\DefaultBindingResolverTests\DefaultBindingResolverTests.csproj'
    )
    foreach ($project in $projects) {
        Invoke-Native dotnet (@('build', (Join-Path $workRoot $project)) + $buildOptions)
    }

    $testsDll = Join-Path $workRoot 'tests\DefaultBindingResolverTests\bin\Release\net6.0\DefaultBindingResolverTests.dll'
    Invoke-Native dotnet @($testsDll)

    $controllerOutput = Join-Path $workRoot 'bin\Release\net6.0'
    $helperOutput = Join-Path $workRoot 'helper\bin\Release\net6.0'
    $settingsOutput = Join-Path $workRoot 'settings\bin\Release\net6.0-windows'

    # Package path -> source file. PDBs are not shipped.
    $entries = [ordered]@{
        'Mods/NocturneModernController.dll' = Join-Path $controllerOutput 'NocturneModernController.dll'
    }
    foreach ($file in @('NocturneModernController.InputHelper.exe', 'NocturneModernController.InputHelper.dll',
            'NocturneModernController.InputHelper.deps.json', 'NocturneModernController.InputHelper.runtimeconfig.json')) {
        $entries["Mods/NocturneModernController.Helper/$file"] = Join-Path $helperOutput $file
    }
    foreach ($file in @('NocturneModernController.Settings.exe', 'NocturneModernController.Settings.dll',
            'NocturneModernController.Settings.deps.json', 'NocturneModernController.Settings.runtimeconfig.json')) {
        $entries["Mods/NocturneModernController.Helper/$file"] = Join-Path $settingsOutput $file
    }
    $entries['Mods/NocturneModernController.Helper/SDL3.dll'] = $sdlPath
    $entries['README.md'] = Join-Path $workRoot 'README.md'
    $entries['README_EN.md'] = Join-Path $workRoot 'README_EN.md'
    $entries['CHANGELOG.md'] = Join-Path $workRoot 'CHANGELOG.md'
    $entries['LICENSE.txt'] = Join-Path $workRoot 'LICENSE'
    $entries['THIRD_PARTY_NOTICES.txt'] = Join-Path $workRoot 'THIRD_PARTY_NOTICES.txt'

    foreach ($source in $entries.Values) {
        if (-not (Test-Path -LiteralPath $source)) {
            throw "Required release file is missing: $source"
        }
    }

    New-Item -ItemType Directory -Path $artifactRoot -Force | Out-Null
    $packageName = "NocturneModernController-v$Version"
    $zipPath = Join-Path $artifactRoot "$packageName.zip"
    if (Test-Path -LiteralPath $zipPath) {
        Remove-Item -LiteralPath $zipPath -Force
    }

    Add-Type -AssemblyName System.IO.Compression
    $zipStream = [IO.File]::Open($zipPath, [IO.FileMode]::CreateNew)
    try {
        $archive = [IO.Compression.ZipArchive]::new($zipStream, [IO.Compression.ZipArchiveMode]::Create)
        try {
            foreach ($name in ($entries.Keys | Sort-Object -CaseSensitive)) {
                $entry = $archive.CreateEntry($name, [IO.Compression.CompressionLevel]::Optimal)
                $entry.LastWriteTime = $zipTimestamp
                $entryStream = $entry.Open()
                try {
                    $bytes = [IO.File]::ReadAllBytes($entries[$name])
                    $entryStream.Write($bytes, 0, $bytes.Length)
                }
                finally {
                    $entryStream.Dispose()
                }
            }
        }
        finally {
            $archive.Dispose()
        }
    }
    finally {
        $zipStream.Dispose()
    }

    Write-Output "Commit:  $commit"
    Write-Output "Version: $Version"
    Write-Output "Created $zipPath"
    foreach ($name in @('Mods/NocturneModernController.dll',
            'Mods/NocturneModernController.Helper/NocturneModernController.Settings.dll',
            'Mods/NocturneModernController.Helper/NocturneModernController.Settings.exe',
            'Mods/NocturneModernController.Helper/NocturneModernController.InputHelper.dll',
            'Mods/NocturneModernController.Helper/NocturneModernController.InputHelper.exe',
            'Mods/NocturneModernController.Helper/SDL3.dll')) {
        Write-Output ("SHA-256 {0}: {1}" -f $name, (Get-Sha256 $entries[$name]))
    }
    Write-Output "SHA-256 ${packageName}.zip: $(Get-Sha256 $zipPath)"
}
finally {
    if ($worktreeAdded) {
        & git -C $repositoryRoot worktree remove --force $workRoot
    }
    if (Test-Path -LiteralPath $workRoot) {
        Remove-Item -LiteralPath $workRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
    & git -C $repositoryRoot worktree prune
}
