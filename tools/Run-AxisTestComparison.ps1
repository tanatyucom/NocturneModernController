<#
.SYNOPSIS
Standalone A/B/C launch-context comparison for NocturneModernController.InputHelper's
--axis-test diagnostic mode. Does not start SMT3HD or Steam.

.DESCRIPTION
Runs the same physical-right-stick axis capture test three times, once per
helper launch method:
  A - explorer.exe launches the helper (Known Good launch shape)
  B - plain direct Process.Start (Known Bad launch shape)
  C - CreateProcess + CREATE_BREAKAWAY_FROM_JOB (Known Bad launch shape)

Because explorer.exe does not reliably forward extra command-line arguments
to the process it launches, this script does not rely on argv to trigger
--axis-test for condition A. Instead it writes a small control file
(NocturneModernController.AxisTest.Request.json in %TEMP%) that the helper
consumes at startup regardless of how it was launched, then waits for the
matching *.result.json file to appear.

Case C shells out to the small compiled tool
tools/AxisTestBreakawayLaunch/NocturneModernController.AxisTestBreakawayLaunch.exe
instead of calling kernel32 CreateProcess directly from this script. Real-
machine testing showed that raw CreateProcess invoked via Windows PowerShell
5.1's Add-Type (dynamically JIT-compiled code) consistently failed with
Win32 error 123 (ERROR_INVALID_NAME) - even for a trivial, fully-qualified,
flag-less target such as notepad.exe - while the identical struct/P-Invoke
pattern compiled normally via `dotnet build` succeeds. Delegating to a
normally-compiled binary avoids that PowerShell-hosted-reflection-specific
failure entirely.

Verbose per-case diagnostics (request file, helper PID/exe path/command
line, consumption state, exit state) are printed for every case so a
runner-level failure (this script) can be told apart from an actual axis
result.

This script intentionally does NOT reproduce being inside SMT3HD's own
process/job/Steam context - see the accompanying report for what this
comparison can and cannot conclude.
#>
param(
    [string]$HelperExePath = (Join-Path $PSScriptRoot '..\helper\bin\Release\net6.0\NocturneModernController.InputHelper.exe'),
    [string]$BreakawayLauncherPath = (Join-Path $PSScriptRoot 'AxisTestBreakawayLaunch\bin\Release\net6.0\NocturneModernController.AxisTestBreakawayLaunch.exe'),
    [int]$DurationMs = 10000
)

$ErrorActionPreference = 'Stop'

$HelperExePath = (Resolve-Path -LiteralPath $HelperExePath).Path
$BreakawayLauncherPath = (Resolve-Path -LiteralPath $BreakawayLauncherPath).Path
$helperDir = Split-Path -Parent $HelperExePath
$sdlSource = Join-Path $PSScriptRoot 'ControllerSideRead\native\SDL3.dll'
$sdlTarget = Join-Path $helperDir 'SDL3.dll'
if (-not (Test-Path -LiteralPath $sdlTarget) -and (Test-Path -LiteralPath $sdlSource)) {
    Copy-Item -LiteralPath $sdlSource -Destination $sdlTarget
    Write-Host "SDL3.dll copied next to helper: $sdlTarget"
}

$helperProcessName = [System.IO.Path]::GetFileNameWithoutExtension($HelperExePath)
$tempDir = [System.IO.Path]::GetTempPath()
$controlFilePath = Join-Path $tempDir 'NocturneModernController.AxisTest.Request.json'

function Write-ControlFile {
    param([string]$Label, [int]$Ms)
    # label/durationMs are lowercase on the wire on purpose - the helper
    # side deserializes with PropertyNameCaseInsensitive = true, so casing
    # here does not matter, but lowercase matches typical JSON style.
    $payload = [ordered]@{ label = $Label; durationMs = $Ms }
    ($payload | ConvertTo-Json -Compress) | Set-Content -LiteralPath $controlFilePath -Encoding utf8 -NoNewline
}

function Get-HelperProcesses {
    Get-CimInstance Win32_Process -Filter "Name='$helperProcessName.exe'" -ErrorAction SilentlyContinue
}

function Start-HelperCase {
    param([string]$Method, [string]$Label, [int]$Ms)

    Write-ControlFile -Label $Label -Ms $Ms
    $requestContent = Get-Content -LiteralPath $controlFilePath -Raw
    Write-Host "REQUEST_FILE_CREATED path=$controlFilePath"
    Write-Host "REQUEST_FILE_CONTENT $requestContent"

    $beforePids = @(Get-HelperProcesses | Select-Object -ExpandProperty ProcessId)

    switch ($Method) {
        'A' {
            Start-Process -FilePath 'explorer.exe' -ArgumentList "`"$HelperExePath`"" | Out-Null
        }
        'B' {
            Start-Process -FilePath $HelperExePath -WindowStyle Hidden | Out-Null
        }
        'C' {
            $launcherOutput = & $BreakawayLauncherPath $HelperExePath 2>&1
            $launcherExit = $LASTEXITCODE
            Write-Host "BREAKAWAY_LAUNCHER_EXITCODE=$launcherExit"
            Write-Host "BREAKAWAY_LAUNCHER_OUTPUT=$launcherOutput"
            if ($launcherExit -ne 0) {
                Write-Warning "Breakaway launcher failed (exit $launcherExit); falling back to plain direct launch for condition C."
                Start-Process -FilePath $HelperExePath -WindowStyle Hidden | Out-Null
            }
        }
    }

    Start-Sleep -Milliseconds 500
    $newProc = Get-HelperProcesses | Where-Object { $beforePids -notcontains $_.ProcessId } | Select-Object -First 1
    if ($newProc) {
        Write-Host "HELPER_PID=$($newProc.ProcessId)"
        Write-Host "HELPER_EXE_PATH=$($newProc.ExecutablePath)"
        Write-Host "HELPER_COMMAND_LINE=$($newProc.CommandLine)"
        Write-Host "HELPER_PARENT_PID=$($newProc.ParentProcessId)"
        if ($newProc.ExecutablePath -and ($newProc.ExecutablePath -ne $HelperExePath)) {
            Write-Warning "HELPER_EXE_PATH does not match the expected $HelperExePath - a different deployed copy may have been launched."
        }
    } else {
        Write-Host "HELPER_PID=NOT_FOUND (no new $helperProcessName.exe process was observed 500ms after launch)"
    }

    Start-Sleep -Milliseconds 500
    if (Test-Path -LiteralPath $controlFilePath) {
        Write-Host "REQUEST_FILE_STILL_EXISTS (helper did not consume it - it likely never reached TryConsumeAxisTestRequest, or never started)"
    } else {
        Write-Host "REQUEST_FILE_CONSUMED"
    }

    return $newProc
}

function Wait-AxisTestResult {
    param([string]$Label, [int]$TimeoutSeconds)
    $resultPath = Join-Path $tempDir "NocturneModernController.AxisTest.$Label.result.json"
    Write-Host "RESULT_FILE_EXPECTED path=$resultPath"
    if (Test-Path -LiteralPath $resultPath) {
        Remove-Item -LiteralPath $resultPath -Force
    }
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        if (Test-Path -LiteralPath $resultPath) {
            Start-Sleep -Milliseconds 300
            try {
                Write-Host "RESULT_FILE_DETECTED path=$resultPath"
                return Get-Content -LiteralPath $resultPath -Raw | ConvertFrom-Json
            } catch {
                Start-Sleep -Milliseconds 300
            }
        }
        Start-Sleep -Milliseconds 250
    }
    Write-Host "RESULT_FILE_NOT_DETECTED path=$resultPath (timed out after $TimeoutSeconds s)"
    return $null
}

$caseDefs = @(
    @{ Label = 'A'; Method = 'A'; Description = 'Explorer経由 (Known Good launch shape)' }
    @{ Label = 'B'; Method = 'B'; Description = 'Process.Start直接 (Known Bad launch shape)' }
    @{ Label = 'C'; Method = 'C'; Description = 'CreateProcess + CREATE_BREAKAWAY_FROM_JOB (Known Bad launch shape)' }
)

$summary = New-Object System.Collections.Generic.List[object]

foreach ($case in $caseDefs) {
    Write-Host ""
    Write-Host "=== Case $($case.Label): $($case.Description) ==="
    Write-Host "右スティックを左右・上下に一度ずつ動かしてください（約 $([math]::Round($DurationMs / 1000)) 秒間）。"

    $proc = Start-HelperCase -Method $case.Method -Label $case.Label -Ms $DurationMs
    $result = Wait-AxisTestResult -Label $case.Label -TimeoutSeconds ([math]::Ceiling($DurationMs / 1000) + 20)

    if ($proc) {
        $stillRunning = Get-HelperProcesses | Where-Object { $_.ProcessId -eq $proc.ProcessId }
        Write-Host ("HELPER_EXIT_STATE=" + $(if ($stillRunning) { "STILL_RUNNING" } else { "EXITED" }))
    } else {
        Write-Host "HELPER_EXIT_STATE=UNKNOWN (PID was never identified)"
    }

    if ($null -eq $result) {
        Write-Host "Case $($case.Label): RESULT_FILE_MISSING"
        $summary.Add([pscustomobject]@{
            Case = $case.Label; Overall = 'FAIL'; X = 'UNKNOWN'; Y = 'UNKNOWN'
            MaxAbsX = 'n/a'; MaxAbsY = 'n/a'; Reasons = 'RESULT_FILE_MISSING'
        })
        continue
    }

    Write-Host ("Case {0}: Overall={1}  X={2}  Y={3}  maxAbsX={4}  maxAbsY={5}  reasons=[{6}]" -f `
        $case.Label, $result.overall, $result.rightStickX, $result.rightStickY, `
        $result.maxAbsX, $result.maxAbsY, ($result.failureReasons -join ','))
    $summary.Add([pscustomobject]@{
        Case = $case.Label; Overall = $result.overall; X = $result.rightStickX; Y = $result.rightStickY
        MaxAbsX = $result.maxAbsX; MaxAbsY = $result.maxAbsY; Reasons = ($result.failureReasons -join ',')
    })
}

Write-Host ""
Write-Host "=== Summary ==="
$summary | Format-Table -AutoSize | Out-Host

$summaryPath = Join-Path $tempDir 'NocturneModernController.AxisTest.Summary.txt'
$summary | Format-Table -AutoSize | Out-String -Width 200 | Set-Content -LiteralPath $summaryPath -Encoding utf8
Write-Host "Summary saved to: $summaryPath"
Write-Host "Per-case logs: $tempDir\NocturneModernController.AxisTest.<A|B|C>.log"
