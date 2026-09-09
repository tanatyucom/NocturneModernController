<#
.SYNOPSIS
Root-2 single-variable experiment for NocturneModernController: does
SDL_GAMECONTROLLER_IGNORE_DEVICES alone explain the in-game Right Stick
freeze seen under condition B (plain direct Process.Start launch)?

.DESCRIPTION
This is a restricted variant of Run-InGameAxisTestComparison.ps1 (see that
script's header for the full launch-mode-selection history). It runs only
two in-game conditions, back to back, on the real machine:

  B0 - Known Bad baseline. Identical to condition "B" in
       src/ExternalInputBridge.cs (StartPlainDirect): plain
       Process.Start(helperPath) from inside smt3hd.exe, full inherited
       Steam/SDL environment untouched.
  B1 - Single Variable. Identical to B0 in every respect except one:
       src/ExternalInputBridge.cs's StartPlainDirectWithoutIgnoreDevicesEnv
       removes exactly SDL_GAMECONTROLLER_IGNORE_DEVICES from the helper's
       ProcessStartInfo.Environment before launch. No other variable is
       added, removed, or modified - Steam*, SDL_GAMECONTROLLER_ALLOW_STEAM_
       VIRTUAL_GAMEPAD, SDL_JOYSTICK_HIDAPI_STEAMXBOX, and everything else
       are inherited exactly as in B0.

Condition A (Explorer, Known Good) is NOT re-run here - it is unchanged
production default code, already CONFIRMED good in the Root-1 investigation,
and this experiment's question is specifically about B0 vs B1.

This script launches and closes SMT3HD itself (same interaction model as
Run-InGameAxisTestComparison.ps1 v3): Steam must already be running and
logged in, but the user does not touch Steam or the game window - only the
physical controller, when prompted, while the script's own console window
shows the countdown.

Verification performed automatically from the helper's own log (never
assumed):
  - SDL_GAMECONTROLLER_IGNORE_DEVICES present in B0's DIAGSNAP steamEnv=,
    absent in B1's.
  - every other Steam*/SDL_*/GAMEINPUT* variable in DIAGSNAP steamEnv= is
    byte-for-byte identical between B0 and B1 (single-variable guard).
  - DIAGSNAP MODULE list (gameoverlayrenderer64.dll in particular) is the
    same set in B0 and B1.
  - ancestor chain / REQUESTED_LAUNCH_MODE / ACTUAL_LAUNCH_MODE, as in the
    existing A/B/C runner.

This script deliberately does NOT:
  - touch save data, auto-load, send any input to the game, or progress it
  - launch or force-close Steam itself
  - edit Steam's launch-options config file or any Steam config file
  - change condition A's code path
It only reads/writes files in %TEMP%, and starts/stops the game process
directly.
#>
param(
    [int]$GamePidTimeoutMs = 120000,
    [int]$MonitorDurationMs = 20000,
    [string]$GameExePath = 'C:\Program Files (x86)\Steam\steamapps\common\smt3hd\smt3hd.exe'
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path -LiteralPath $GameExePath)) {
    throw "smt3hd.exe が見つかりません: $GameExePath ; -GameExePath でパスを指定してください。"
}
$gameDir = Split-Path -Parent $GameExePath

$tempDir = [System.IO.Path]::GetTempPath()
$launchModeRequestPath = Join-Path $tempDir 'NocturneModernController.LaunchMode.Test.json'

function Write-DiagnosticLaunchModeRequest {
    param([string]$Mode, [string]$Label)
    $payload = [ordered]@{
        launchMode = $Mode
        label = $Label
        gamePidTimeoutMs = $GamePidTimeoutMs
        monitorDurationMs = $MonitorDurationMs
    }
    ($payload | ConvertTo-Json -Compress) | Set-Content -LiteralPath $launchModeRequestPath -Encoding utf8 -NoNewline
    Write-Host "LAUNCH_MODE_REQUEST_CREATED path=$launchModeRequestPath"
    Write-Host "LAUNCH_MODE_REQUEST_CONTENT $(Get-Content -LiteralPath $launchModeRequestPath -Raw)"
}

function Test-GameProcessRunning {
    $procs = Get-Process -Name 'smt3hd' -ErrorAction SilentlyContinue
    return @($procs).Count -gt 0
}

function Start-Game {
    $proc = Start-Process -FilePath $GameExePath -WorkingDirectory $gameDir -PassThru
    Write-Host "GAME_LAUNCHED pid=$($proc.Id)"
    return $proc
}

function Stop-GameProcess {
    param([int]$TimeoutSeconds = 30)
    $procs = @(Get-Process -Name 'smt3hd' -ErrorAction SilentlyContinue)
    if ($procs.Count -eq 0) {
        return
    }
    foreach ($p in $procs) {
        try { $p.CloseMainWindow() | Out-Null } catch {}
    }
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline -and (Test-GameProcessRunning)) {
        Start-Sleep -Milliseconds 500
    }
    foreach ($p in @(Get-Process -Name 'smt3hd' -ErrorAction SilentlyContinue)) {
        try {
            Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue
        } catch {}
    }
    Start-Sleep -Seconds 1
    Write-Host "GAME_CLOSED"
}

function Wait-InGameResult {
    param([string]$Label, [int]$TimeoutSeconds)
    $resultPath = Join-Path $tempDir "NocturneModernController.AxisTest.InGame.$Label.result.json"
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
        Start-Sleep -Milliseconds 500
    }
    Write-Host "RESULT_FILE_NOT_DETECTED path=$resultPath (timed out after $TimeoutSeconds s)"
    return $null
}

function Get-HelperLogPath {
    param([string]$Label)
    Join-Path $tempDir "NocturneModernController.AxisTest.InGame.$Label.log"
}

function Get-SteamEnvLine {
    param([string]$Label)
    $logPath = Get-HelperLogPath -Label $Label
    if (-not (Test-Path -LiteralPath $logPath)) {
        return $null
    }
    $line = Select-String -Path $logPath -Pattern 'DIAGSNAP steamEnv=' | Select-Object -First 1
    if (-not $line) { return $null }
    return ($line.Line -replace '^.*DIAGSNAP steamEnv=', '')
}

function Parse-SteamEnvLine {
    param([string]$Line)
    # "KEY=value; KEY2=value2; ..." - split on "; " only (values themselves
    # should not contain "; " for the variables this summary targets).
    $map = [ordered]@{}
    if (-not $Line) { return $map }
    foreach ($part in ($Line -split '; ')) {
        $idx = $part.IndexOf('=')
        if ($idx -lt 0) { continue }
        $key = $part.Substring(0, $idx)
        $val = $part.Substring($idx + 1)
        $map[$key] = $val
    }
    return $map
}

function Get-ModuleSet {
    param([string]$Label)
    $logPath = Get-HelperLogPath -Label $Label
    $set = [System.Collections.Generic.HashSet[string]]::new()
    if (-not (Test-Path -LiteralPath $logPath)) { return $set }
    Select-String -Path $logPath -Pattern 'DIAGSNAP MODULE name="([^"]+)"' | ForEach-Object {
        [void]$set.Add($_.Matches[0].Groups[1].Value)
    }
    return $set
}

function Show-AncestorVerification {
    param([string]$Label)
    $logPath = Get-HelperLogPath -Label $Label
    if (Test-Path -LiteralPath $logPath) {
        $ancestorLine = Select-String -Path $logPath -Pattern 'DIAGSNAP ancestorChain=' | Select-Object -First 1
        $snapLine = Select-String -Path $logPath -Pattern 'DIAGSNAP pid=' | Select-Object -First 1
        $rootLine = Select-String -Path $logPath -Pattern 'Root-2 single-variable experiment' | Select-Object -First 1
        if ($snapLine) { Write-Host ("HELPER_LOG " + $snapLine.Line.Trim()) }
        if ($ancestorLine) { Write-Host ("HELPER_LOG " + $ancestorLine.Line.Trim()) }
        if ($rootLine) { Write-Host ("HELPER_LOG " + $rootLine.Line.Trim()) }
    } else {
        Write-Host "HELPER_LOG_NOT_FOUND path=$logPath"
    }
}

$caseDescriptions = @{
    'B'  = 'B0: Known Bad baseline (Process.Start直接、環境そのまま)'
    'B1' = 'B1: Single Variable (Process.Start直接、SDL_GAMECONTROLLER_IGNORE_DEVICESのみ除去)'
}

Write-Host "=== Root-2 Single Variable Experiment (B0 vs B1) ==="
Write-Host "Steamの起動オプションは変更しません。ゲームの起動・終了はこのrunnerが自動的に行います（Steam自体は起動・ログイン済みにしておいてください）。"
Write-Host "GameExePath: $GameExePath"

$inGameSummary = [System.Collections.Generic.List[object]]::new()
$envLines = @{}
$moduleSets = @{}

foreach ($label in @('B', 'B1')) {
    Write-Host ""
    Write-Host "=== Case $label`: $($caseDescriptions[$label]) ==="

    if (Test-GameProcessRunning) {
        Write-Warning "smt3hd.exe が既に起動しています。自動的に終了させてから続行します。"
        Stop-GameProcess
    }

    Write-DiagnosticLaunchModeRequest -Mode $label -Label $label

    Write-Host ""
    Write-Host "コントローラーテスト中です。ゲームは自動的に起動・終了します。"
    Write-Host "1. 左スティックを左右・上下へ大きく動かす"
    Write-Host "2. 右スティックを左右・上下へ大きく動かす（今回の主要判定です）"
    Write-Host "3. A / B / X / Y を1回ずつ押す"
    Write-Host "4. LB / RB を1回ずつ押す"
    Write-Host "5. D-padを上下左右へ1回ずつ押す"
    Write-Host "そのまま待ってください。（ゲームPID検出まで最大 $([math]::Round($GamePidTimeoutMs/1000)) 秒、検出後の監視は約 $([math]::Round($MonitorDurationMs/1000)) 秒です）"

    Start-Game | Out-Null

    $timeoutSeconds = [math]::Ceiling(($GamePidTimeoutMs + $MonitorDurationMs) / 1000) + 30
    $result = Wait-InGameResult -Label $label -TimeoutSeconds $timeoutSeconds

    Write-Host ""
    Write-Host "--- launch mode / environment 検証 (observed, not assumed) ---"
    Show-AncestorVerification -Label $label

    $envLine = Get-SteamEnvLine -Label $label
    $envLines[$label] = $envLine
    Write-Host ("HELPER_LOG DIAGSNAP steamEnv=" + $envLine)

    $moduleSets[$label] = Get-ModuleSet -Label $label
    $hasOverlay = $moduleSets[$label] -contains 'gameoverlayrenderer64.dll'
    Write-Host ("MODULE_CHECK gameoverlayrenderer64.dll loaded=" + $hasOverlay + " totalModules=" + $moduleSets[$label].Count)

    if ($null -eq $result) {
        Write-Host "Case $label`: RESULT_FILE_MISSING"
        $inGameSummary.Add([pscustomobject]@{
            Case = $label; Requested = $label; Actual = 'UNKNOWN'
            Connected = 'UNKNOWN'; LeftStick = 'UNKNOWN'; RightStick = 'UNKNOWN'
            Buttons = 'UNKNOWN'; ControllerInput = 'UNKNOWN'
            Overall = 'FAIL'; Reasons = 'RESULT_FILE_MISSING'
            Vid = 'UNKNOWN'; Pid = 'UNKNOWN'; DeviceName = 'UNKNOWN'
        })
    } else {
        Write-Host ("REQUESTED_LAUNCH_MODE={0} ACTUAL_LAUNCH_MODE={1}" -f $result.requestedLaunchMode, $result.actualLaunchMode)
        if ($result.requestedLaunchMode -ne $label) {
            Write-Warning "REQUESTED_LAUNCH_MODE in the result ($($result.requestedLaunchMode)) does not match this case ($label) - check for a stale request file."
        }
        Write-Host ("DEVICE name=""{0}"" path=""{1}"" vid=0x{2:X4} pid=0x{3:X4} type={4} deviceCount={5}" -f `
            $result.selectedName, $result.selectedPath, $result.selectedVid, $result.selectedPid, `
            $result.selectedType, $result.deviceCount)
        Write-Host ("CONNECTED start={0} end={1} disconnectObserved={2} reconnectObserved={3}" -f `
            $result.controllerConnectedAtStart, $result.controllerConnectedAtEnd, `
            $result.disconnectObserved, $result.reconnectObserved)
        $buttonsPressedList = @($result.buttons.PSObject.Properties | Where-Object { $_.Value.pressObserved } | ForEach-Object { $_.Name })
        Write-Host ("BUTTONS pressed=[{0}] status={1}" -f ($buttonsPressedList -join ','), $result.buttonInputStatus)
        Write-Host ("Case {0}: LeftStick={1} RightStick={2} Buttons={3} ControllerInput={4} Overall={5} reasons=[{6}]" -f `
            $label, $result.leftStickStatus, $result.rightStickStatus, $result.buttonInputStatus, `
            $result.controllerInputStatus, $result.overall, ($result.failureReasons -join ','))
        $inGameSummary.Add([pscustomobject]@{
            Case = $label; Requested = $result.requestedLaunchMode; Actual = $result.actualLaunchMode
            Connected = $(if ($result.controllerConnectedAtEnd) { 'YES' } else { 'NO' })
            LeftStick = $result.leftStickStatus; RightStick = $result.rightStickStatus
            Buttons = $result.buttonInputStatus; ControllerInput = $result.controllerInputStatus
            Overall = $result.overall; Reasons = ($result.failureReasons -join ',')
            Vid = ('0x{0:X4}' -f $result.selectedVid); Pid = ('0x{0:X4}' -f $result.selectedPid)
            DeviceName = $result.selectedName
        })
    }

    Write-Host ""
    Write-Host "ゲームを自動的に終了します..."
    Stop-GameProcess
}

Write-Host ""
Write-Host "=== Case Summary (B0 vs B1) ==="
$inGameSummary | Format-Table -AutoSize | Out-Host

Write-Host ""
Write-Host "=== Single-variable guard: IGNORE_DEVICES 実測 ==="
$mapB = Parse-SteamEnvLine -Line $envLines['B']
$mapB1 = Parse-SteamEnvLine -Line $envLines['B1']
$ignoreKey = 'SDL_GAMECONTROLLER_IGNORE_DEVICES'
$b0Ignore = if ($mapB.Contains($ignoreKey)) { $mapB[$ignoreKey] } else { '(absent)' }
$b1Ignore = if ($mapB1.Contains($ignoreKey)) { $mapB1[$ignoreKey] } else { '(absent)' }
Write-Host ("B0 $ignoreKey = $b0Ignore")
Write-Host ("B1 $ignoreKey = $b1Ignore")
$guardPass = ($b0Ignore -ne '(absent)') -and ($b1Ignore -eq '(absent)')
Write-Host ("SINGLE_VARIABLE_TARGET_CHANGE_OBSERVED=" + $guardPass)

Write-Host ""
Write-Host "=== Single-variable guard: 他の変数が変わっていないか ==="
$allKeys = [System.Collections.Generic.HashSet[string]]::new()
foreach ($k in $mapB.Keys) { [void]$allKeys.Add($k) }
foreach ($k in $mapB1.Keys) { [void]$allKeys.Add($k) }
$unexpectedDiffs = [System.Collections.Generic.List[string]]::new()
foreach ($k in $allKeys) {
    if ($k -eq $ignoreKey) { continue }
    $vB = if ($mapB.Contains($k)) { $mapB[$k] } else { '(absent)' }
    $vB1 = if ($mapB1.Contains($k)) { $mapB1[$k] } else { '(absent)' }
    if ($vB -ne $vB1) {
        $unexpectedDiffs.Add("$k : B0=$vB / B1=$vB1")
    }
}
if ($unexpectedDiffs.Count -eq 0) {
    Write-Host "NO_UNEXPECTED_ENV_DIFFERENCES (IGNORE_DEVICES以外は完全一致)"
} else {
    Write-Warning "UNEXPECTED_ENV_DIFFERENCES_FOUND:"
    $unexpectedDiffs | ForEach-Object { Write-Warning ("  " + $_) }
}

Write-Host ""
Write-Host "=== Single-variable guard: loaded modules (gameoverlayrenderer64.dll等) ==="
$modulesB = $moduleSets['B']
$modulesB1 = $moduleSets['B1']
if ($null -ne $modulesB -and $null -ne $modulesB1) {
    $onlyInB = @($modulesB | Where-Object { -not $modulesB1.Contains($_) })
    $onlyInB1 = @($modulesB1 | Where-Object { -not $modulesB.Contains($_) })
    Write-Host ("modules only in B0 (missing in B1): [{0}]" -f ($onlyInB -join ','))
    Write-Host ("modules only in B1 (missing in B0): [{0}]" -f ($onlyInB1 -join ','))
}

$summaryPath = Join-Path $tempDir 'NocturneModernController.Root2.Summary.txt'
$lines = [System.Collections.Generic.List[string]]::new()
$lines.Add('=== Root-2 Case Summary (B0 vs B1) ===')
$lines.Add(($inGameSummary | Format-Table -AutoSize | Out-String -Width 200))
$lines.Add("B0 $ignoreKey = $b0Ignore")
$lines.Add("B1 $ignoreKey = $b1Ignore")
$lines.Add("Unexpected env differences: " + $(if ($unexpectedDiffs.Count -eq 0) { 'NONE' } else { ($unexpectedDiffs -join ' | ') }))
$lines -join [Environment]::NewLine | Set-Content -LiteralPath $summaryPath -Encoding utf8

Write-Host ""
Write-Host "Summary saved to: $summaryPath"
Write-Host "Per-case logs: $tempDir\NocturneModernController.AxisTest.InGame.<B|B1>.log"
Write-Host ""
Write-Host "注意: このrunnerはSDL_GAMECONTROLLER_IGNORE_DEVICESの因果関与を検証するものであり、唯一のroot causeとは断定しません。"
