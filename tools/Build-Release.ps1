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
#
# One run produces two independent packages: the Controller, and the standalone
# NocturneForceEncounter mod (versioned by its own csproj). The Force Encounter
# package never contains the Controller and vice versa. Both ZIPs are written
# as .partial files and only renamed into place once every step has succeeded.

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

function Get-ProjectVersion([string]$ProjectPath) {
    [xml]$project = Get-Content -LiteralPath $ProjectPath -Raw
    $projectVersion = ($project.Project.PropertyGroup | Where-Object { $_.Version } | Select-Object -First 1).Version
    if (-not $projectVersion) {
        throw "Version not found in $ProjectPath"
    }
    $projectVersion
}

# Writes $Entries (package path -> source file) with a fixed order and timestamp.
function Write-DeterministicZip([System.Collections.IDictionary]$Entries, [string]$Path) {
    foreach ($source in $Entries.Values) {
        if (-not (Test-Path -LiteralPath $source)) {
            throw "Required release file is missing: $source"
        }
    }
    if (Test-Path -LiteralPath $Path) {
        Remove-Item -LiteralPath $Path -Force
    }
    $zipStream = [IO.File]::Open($Path, [IO.FileMode]::CreateNew)
    try {
        $archive = [IO.Compression.ZipArchive]::new($zipStream, [IO.Compression.ZipArchiveMode]::Create)
        try {
            foreach ($name in ($Entries.Keys | Sort-Object -CaseSensitive)) {
                $entry = $archive.CreateEntry($name, [IO.Compression.CompressionLevel]::Optimal)
                $entry.LastWriteTime = $zipTimestamp
                $entryStream = $entry.Open()
                try {
                    $bytes = [IO.File]::ReadAllBytes($Entries[$name])
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
$partialZips = @()
try {
    Invoke-Native git @('-C', $repositoryRoot, 'worktree', 'add', '--detach', $workRoot, $commit)
    $worktreeAdded = $true

    if (-not $Version) {
        $Version = Get-ProjectVersion (Join-Path $workRoot 'NocturneModernController.csproj')
    }
    $forceEncounterProject = 'mods\NocturneForceEncounter\NocturneForceEncounter.csproj'
    $forceEncounterVersion = Get-ProjectVersion (Join-Path $workRoot $forceEncounterProject)

    # MSBuild splits PathMap on '='; the source side must end with a separator.
    $pathMap = "-p:PathMap=$workRoot\=/_/"
    $buildOptions = @('-c', 'Release', '--no-incremental', '-nologo', '-warnaserror',
        '-p:ContinuousIntegrationBuild=true', $pathMap)
    $projects = @(
        'NocturneModernController.csproj',
        'helper\NocturneModernController.InputHelper.csproj',
        'settings\NocturneModernController.Settings.csproj',
        'tests\DefaultBindingResolverTests\DefaultBindingResolverTests.csproj',
        $forceEncounterProject
    )
    foreach ($project in $projects) {
        Invoke-Native dotnet (@('build', (Join-Path $workRoot $project)) + $buildOptions)
    }

    $testsDll = Join-Path $workRoot 'tests\DefaultBindingResolverTests\bin\Release\net6.0\DefaultBindingResolverTests.dll'
    Invoke-Native dotnet @($testsDll)

    $controllerOutput = Join-Path $workRoot 'bin\Release\net6.0'
    $helperOutput = Join-Path $workRoot 'helper\bin\Release\net6.0'
    $settingsOutput = Join-Path $workRoot 'settings\bin\Release\net6.0-windows'
    $forceEncounterDll = Join-Path $workRoot 'mods\NocturneForceEncounter\bin\Release\net6.0\NocturneForceEncounter.dll'

    # NocturneForceEncounter must stay standalone: the Controller integration is
    # reflection-only, so the release DLL may not hard-reference the Controller.
    # Read in a child process: a reflection-only load cannot be repeated within
    # one AppDomain, and the child's exit releases the worktree file.
    $forceEncounterReferences = & powershell.exe -NoProfile -NonInteractive -Command `
        "[Reflection.Assembly]::ReflectionOnlyLoad([IO.File]::ReadAllBytes('$forceEncounterDll')).GetReferencedAssemblies() | ForEach-Object Name"
    if ($LASTEXITCODE -ne 0 -or -not $forceEncounterReferences) {
        throw "Cannot read the assembly references of $forceEncounterDll"
    }
    if ($forceEncounterReferences -contains 'NocturneModernController') {
        throw 'NocturneForceEncounter.dll references NocturneModernController.dll; the standalone mod must not.'
    }

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

    # Standalone package: no Controller files, no PDB, no settings.json (created at runtime).
    $forceEncounterEntries = [ordered]@{
        'Mods/NocturneForceEncounter.dll' = $forceEncounterDll
        'LICENSE.txt' = Join-Path $workRoot 'LICENSE'
    }

    New-Item -ItemType Directory -Path $artifactRoot -Force | Out-Null
    Add-Type -AssemblyName System.IO.Compression
    $packages = @(
        @{ Name = "NocturneModernController-v$Version"; Entries = $entries },
        @{ Name = "NocturneForceEncounter-v$forceEncounterVersion"; Entries = $forceEncounterEntries }
    )
    foreach ($package in $packages) {
        $package.ZipPath = Join-Path $artifactRoot "$($package.Name).zip"
        $package.PartialPath = "$($package.ZipPath).partial"
        $partialZips += $package.PartialPath
        Write-DeterministicZip $package.Entries $package.PartialPath
    }
    foreach ($package in $packages) {
        Move-Item -LiteralPath $package.PartialPath -Destination $package.ZipPath -Force
    }

    Write-Output "Commit:  $commit"
    Write-Output "Version: $Version (NocturneForceEncounter $forceEncounterVersion)"
    foreach ($package in $packages) {
        Write-Output "Created $($package.ZipPath)"
    }
    foreach ($name in @('Mods/NocturneModernController.dll',
            'Mods/NocturneModernController.Helper/NocturneModernController.Settings.dll',
            'Mods/NocturneModernController.Helper/NocturneModernController.Settings.exe',
            'Mods/NocturneModernController.Helper/NocturneModernController.InputHelper.dll',
            'Mods/NocturneModernController.Helper/NocturneModernController.InputHelper.exe',
            'Mods/NocturneModernController.Helper/SDL3.dll')) {
        Write-Output ("SHA-256 {0}: {1}" -f $name, (Get-Sha256 $entries[$name]))
    }
    Write-Output ("SHA-256 Mods/NocturneForceEncounter.dll: {0}" -f (Get-Sha256 $forceEncounterDll))
    foreach ($package in $packages) {
        Write-Output "SHA-256 $($package.Name).zip: $(Get-Sha256 $package.ZipPath)"
    }
}
finally {
    foreach ($partialZip in $partialZips) {
        if (Test-Path -LiteralPath $partialZip) {
            Remove-Item -LiteralPath $partialZip -Force -ErrorAction SilentlyContinue
        }
    }
    if ($worktreeAdded) {
        & git -C $repositoryRoot worktree remove --force $workRoot
    }
    if (Test-Path -LiteralPath $workRoot) {
        Remove-Item -LiteralPath $workRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
    & git -C $repositoryRoot worktree prune
}
