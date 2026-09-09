<#
.SYNOPSIS
In-game A/B/C FULL CONTROLLER INPUT comparison for NocturneModernController
(left stick, right stick, and representative buttons - not right stick
only), run while SMT3HD is actually running under Steam.

.DESCRIPTION
This is the in-game counterpart to Run-AxisTestComparison.ps1 (which never
starts the game). It does NOT launch the helper itself - the real MOD
(src/ExternalInputBridge.cs, running inside smt3hd.exe via MelonLoader)
launches its own helper using whichever condition (A/B/C) it selects, exactly
as it does in normal play.

LAUNCH MODE SELECTION (redesigned twice after real-machine testing):
  Attempt 1 (Steam Launch Options, Linux-style "VAR=value %command%"): Steam
  on Windows does not run launch options through a shell, so this was taken
  literally and Steam tried to launch a file literally named
  "NocturneModernController_LaunchMode=B" and failed.
  Attempt 2 (a compiled Steam Launch Options wrapper exe): worked for the env
  var itself, but the wrapper's own current directory was not the game's
  install directory (e.g. the user's Documents folder when launched from an
  interactive shell), so MelonLoader failed with "failed to find Bootstrap".
  Both approaches also required changing Steam's launch option field, which
  this design avoids entirely.

Current design: SMT3HD is always launched by the user completely normally
from Steam - no launch option changes, ever. A/B/C selection is a
diagnostic-only control file:
  %TEMP%\NocturneModernController.LaunchMode.Test.json
  {"launchMode":"B","label":"B","gamePidTimeoutMs":120000,"monitorDurationMs":20000}
src/ExternalInputBridge.cs reads this file ONCE at MelonLoader init, deletes
it immediately (one-shot; a stale B/C request can never affect a later
normal launch), and forwards label/timeouts to the helper's own existing
in-game axis-test request file. When the file is absent (the overwhelming
common case, including every ordinary play session), launch condition is
always "A" (Explorer, Known Good) - unchanged production default.

Real-machine testing (by Claude Code, launching smt3hd.exe directly with an
explicit, correct working directory - not through this runner's interactive
prompts) confirmed for all of A, B and C that:
  - MelonLoader boots normally (no Bootstrap error)
  - the diagnostic request is consumed and logged in MelonLoader's own log
  - REQUESTED_LAUNCH_MODE and ACTUAL_LAUNCH_MODE match the requested case
  - the helper's own ancestor-chain diagnostic matches the expected shape
    (explorer.exe present for A; smt3hd.exe as direct parent for B/C;
    inJob=False for C, confirming the breakaway actually took effect)
No physical right-stick input was performed during that verification, so it
proves the launch-mode plumbing only - not axis PASS/FAIL, which requires a
real user moving the stick.

Case C exercises the MOD's own compiled CreateProcess + CREATE_BREAKAWAY_FROM_JOB
implementation (ExternalInputBridge.TryStartWithBreakaway) - not a
reimplementation - because it is the MOD itself that launches the helper
here.

INTERACTION MODEL (revised twice after real-machine observations):
  v1 used Read-Host gates ("press Enter once the game has started" / "press
  Enter once you have closed it"). A real-machine session suggested the
  axis-freeze may be time/event-sensitive (an observation similar to the
  Steam-recording-unlocks-the-camera-channel note in
  docs/research/RIGHT_STICK_VIEW_AND_DASH_INVESTIGATION.md), and that forcing
  the user to Alt-Tab to this console window to press Enter right around the
  critical moment could itself be a confound (a focus-change event) rather
  than a neutral pause. v2 removed the keypresses but still asked the user
  to manually launch and close SMT3HD themselves, which made it unclear to
  the user when a case had actually finished.
  v3 (current): this script launches and closes SMT3HD itself. It launches
  smt3hd.exe directly (not through Steam's UI) with an explicit, correct
  WorkingDirectory - Claude Code's own real-machine verification confirmed
  this boots MelonLoader and every existing mod normally for all of A/B/C,
  identically to a Steam-initiated launch, as long as the Steam client
  itself is already running (the game's own steam_appid.txt lets its
  Steamworks check succeed without going through Steam's "Play" button).
  After the axis-test result is written (or the wait times out), this
  script closes the game itself (CloseMainWindow, then Stop-Process as a
  fallback) - the game is only ever left sitting at whatever screen it
  booted to, no gameplay input is sent to it, so nothing is lost by closing
  it automatically. The user still needs Steam running and logged in, but
  no longer touches Steam or this game window at all during a run.

This script deliberately does NOT:
  - touch save data, auto-load, send any input to the game, or progress it
  - launch or force-close Steam itself
  - edit Steam's launch-options config file or any Steam config file
It only reads/writes files in %TEMP%, and starts/stops the game process
directly, per this instruction's explicit request for full automation.
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
$axisTestControlFilePath = Join-Path $tempDir 'NocturneModernController.AxisTest.InGameRequest.json'
$launchModeRequestPath = Join-Path $tempDir 'NocturneModernController.LaunchMode.Test.json'

function Write-DiagnosticLaunchModeRequest {
    param([string]$Mode, [string]$Label)
    # Single unified request the MOD (src/ExternalInputBridge.cs) consumes
    # once at startup: it uses launchMode for itself and forwards
    # label/timeouts to the helper's own in-game axis-test request file.
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
    # Direct exe launch with an explicit, correct WorkingDirectory. See the
    # header comment: this was verified by Claude Code (real machine) to
    # boot MelonLoader and every existing mod normally, for A/B/C alike, as
    # long as Steam itself is already running.
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

function Get-StandaloneResult {
    param([string]$Label)
    $path = Join-Path $tempDir "NocturneModernController.AxisTest.$Label.result.json"
    if (Test-Path -LiteralPath $path) {
        try {
            return Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
        } catch {
            return $null
        }
    }
    return $null
}

function Show-AncestorVerification {
    param([string]$Label)
    $logPath = Join-Path $tempDir "NocturneModernController.AxisTest.InGame.$Label.log"
    if (Test-Path -LiteralPath $logPath) {
        $ancestorLine = Select-String -Path $logPath -Pattern 'DIAGSNAP ancestorChain=' | Select-Object -First 1
        $snapLine = Select-String -Path $logPath -Pattern 'DIAGSNAP pid=' | Select-Object -First 1
        if ($snapLine) { Write-Host ("HELPER_LOG " + $snapLine.Line.Trim()) }
        if ($ancestorLine) { Write-Host ("HELPER_LOG " + $ancestorLine.Line.Trim()) }
    } else {
        Write-Host "HELPER_LOG_NOT_FOUND path=$logPath"
    }
}

$caseDescriptions = @{
    'A' = 'Explorer経由 (Known Good launch shape)'
    'B' = 'Process.Start直接 (Known Bad launch shape)'
    'C' = 'CreateProcess + CREATE_BREAKAWAY_FROM_JOB (Known Bad launch shape)'
}

Write-Host "=== In-Game A/B/C Axis Test ==="
Write-Host "Steamの起動オプションは変更しません。ゲームの起動・終了はこのrunnerが自動的に行います（Steam自体は起動・ログイン済みにしておいてください）。"
Write-Host "各Caseの切り替えは、このrunnerが %TEMP%\NocturneModernController.LaunchMode.Test.json を書き換えることで行います。"
Write-Host "GameExePath: $GameExePath"

$inGameSummary = New-Object System.Collections.Generic.List[object]

foreach ($label in @('A', 'B', 'C')) {
    Write-Host ""
    Write-Host "=== In-Game Case $label`: $($caseDescriptions[$label]) ==="

    if (Test-GameProcessRunning) {
        Write-Warning "smt3hd.exe が既に起動しています。自動的に終了させてから続行します。"
        Stop-GameProcess
    }

    Write-DiagnosticLaunchModeRequest -Mode $label -Label $label

    Write-Host ""
    Write-Host "コントローラーテスト中です。ゲームは自動的に起動・終了します。"
    Write-Host "1. 左スティックを左右・上下へ大きく動かす"
    Write-Host "2. 右スティックを左右・上下へ大きく動かす"
    Write-Host "3. A / B / X / Y を1回ずつ押す"
    Write-Host "4. LB / RB を1回ずつ押す"
    Write-Host "5. D-padを上下左右へ1回ずつ押す"
    Write-Host "そのまま待ってください。（ゲームPID検出まで最大 $([math]::Round($GamePidTimeoutMs/1000)) 秒、検出後の監視は約 $([math]::Round($MonitorDurationMs/1000)) 秒です）"

    Start-Game | Out-Null

    $timeoutSeconds = [math]::Ceiling(($GamePidTimeoutMs + $MonitorDurationMs) / 1000) + 30
    $result = Wait-InGameResult -Label $label -TimeoutSeconds $timeoutSeconds

    Write-Host ""
    Write-Host "--- launch mode 検証 (observed, not assumed) ---"
    Show-AncestorVerification -Label $label

    if ($null -eq $result) {
        Write-Host "In-Game Case $label`: RESULT_FILE_MISSING"
        $inGameSummary.Add([pscustomobject]@{
            Case = $label; Requested = $label; Actual = 'UNKNOWN'
            Connected = 'UNKNOWN'; LeftStick = 'UNKNOWN'; RightStick = 'UNKNOWN'
            Buttons = 'UNKNOWN'; ControllerInput = 'UNKNOWN'
            Overall = 'FAIL'; Reasons = 'RESULT_FILE_MISSING'
        })
    } else {
        Write-Host ("REQUESTED_LAUNCH_MODE={0} ACTUAL_LAUNCH_MODE={1}" -f $result.requestedLaunchMode, $result.actualLaunchMode)
        if ($result.requestedLaunchMode -ne $label) {
            Write-Warning "REQUESTED_LAUNCH_MODE in the result ($($result.requestedLaunchMode)) does not match this case ($label) - check for a stale request file."
        }
        if ($result.actualLaunchMode -ne $result.requestedLaunchMode) {
            Write-Warning "ACTUAL_LAUNCH_MODE ($($result.actualLaunchMode)) differs from REQUESTED_LAUNCH_MODE ($($result.requestedLaunchMode)) - condition C likely fell back after a breakaway failure."
        }
        Write-Host ("DEVICE name=""{0}"" path=""{1}"" vid=0x{2:X4} pid=0x{3:X4} type={4} deviceCount={5}" -f `
            $result.selectedName, $result.selectedPath, $result.selectedVid, $result.selectedPid, `
            $result.selectedType, $result.deviceCount)
        Write-Host ("CONNECTED start={0} end={1} disconnectObserved={2} reconnectObserved={3}" -f `
            $result.controllerConnectedAtStart, $result.controllerConnectedAtEnd, `
            $result.disconnectObserved, $result.reconnectObserved)
        $buttonsPressedList = @($result.buttons.PSObject.Properties | Where-Object { $_.Value.pressObserved } | ForEach-Object { $_.Name })
        Write-Host ("BUTTONS pressed=[{0}] status={1}" -f ($buttonsPressedList -join ','), $result.buttonInputStatus)
        Write-Host ("In-Game Case {0}: LeftStick={1} RightStick={2} Buttons={3} ControllerInput={4} Overall={5} reasons=[{6}]" -f `
            $label, $result.leftStickStatus, $result.rightStickStatus, $result.buttonInputStatus, `
            $result.controllerInputStatus, $result.overall, ($result.failureReasons -join ','))
        $inGameSummary.Add([pscustomobject]@{
            Case = $label; Requested = $result.requestedLaunchMode; Actual = $result.actualLaunchMode
            Connected = $(if ($result.controllerConnectedAtEnd) { 'YES' } else { 'NO' })
            LeftStick = $result.leftStickStatus; RightStick = $result.rightStickStatus
            Buttons = $result.buttonInputStatus; ControllerInput = $result.controllerInputStatus
            Overall = $result.overall; Reasons = ($result.failureReasons -join ',')
        })
    }

    Write-Host ""
    Write-Host "ゲームを自動的に終了します..."
    Stop-GameProcess
}

Write-Host ""
Write-Host "=== In-Game Summary ==="
$inGameSummary | Format-Table -AutoSize | Out-Host

Write-Host ""
Write-Host "=== Standalone (control, most recent) vs In-Game ==="
$comparison = New-Object System.Collections.Generic.List[object]
foreach ($label in @('A', 'B', 'C')) {
    $standalone = Get-StandaloneResult -Label $label
    $inGameRow = $inGameSummary | Where-Object { $_.Case -eq $label } | Select-Object -First 1
    $comparison.Add([pscustomobject]@{
        Case = $label
        StandaloneOverall = if ($standalone) { $standalone.overall } else { 'NOT_AVAILABLE' }
        StandaloneReasons = if ($standalone) { ($standalone.failureReasons -join ',') } else { 'NOT_AVAILABLE' }
        InGameOverall = if ($inGameRow) { $inGameRow.Overall } else { 'NOT_AVAILABLE' }
        InGameReasons = if ($inGameRow) { $inGameRow.Reasons } else { 'NOT_AVAILABLE' }
    })
}
$comparison | Format-Table -AutoSize | Out-Host

$summaryPath = Join-Path $tempDir 'NocturneModernController.AxisTest.InGame.Summary.txt'
$lines = New-Object System.Collections.Generic.List[string]
$lines.Add('=== In-Game Summary ===')
$lines.Add(($inGameSummary | Format-Table -AutoSize | Out-String -Width 200))
$lines.Add('=== Standalone (control) vs In-Game ===')
$lines.Add(($comparison | Format-Table -AutoSize | Out-String -Width 200))
$lines -join [Environment]::NewLine | Set-Content -LiteralPath $summaryPath -Encoding utf8

Write-Host "Summary saved to: $summaryPath"
Write-Host "Per-case logs: $tempDir\NocturneModernController.AxisTest.InGame.<A|B|C>.log"
Write-Host ""
Write-Host "注意: このrunnerは原因を断定しません。GoodとBadの差の解釈は別途行ってください。"
