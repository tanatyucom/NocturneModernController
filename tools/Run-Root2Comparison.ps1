<#
.SYNOPSIS
Root-2 single-variable physical-input comparison: B0 (plain direct launch,
no environment change) vs B1 (plain direct launch, SDL_GAMECONTROLLER_
IGNORE_DEVICES removed from the helper's environment only - nothing else
changed). Launch method, all other Steam/SDL environment variables, and
loaded modules are identical to B0/B by design (see
src/ExternalInputBridge.cs StartPlainDirectWithoutIgnoreDevicesEnv).

.DESCRIPTION
Unlike the earlier Root-1 snapshot-only runs, this script is built for a
short, clearly-signaled PHYSICAL input window per case (not a multi-minute
open-ended session): it auto-launches the game, waits for the helper's own
"monitoring started" marker file (written the instant game-PID detection +
the SDL device are both ready), then tells the user IN THIS CONSOLE exactly
when to start moving the controller and for how long. It reads the result
after the short window, prints the case's Right/Left stick + button +
device-identity findings, then closes the game and moves to the next case.

This script does not implement or propose any root-cause fix. It only
selects a case via the existing one-shot diagnostic launch-mode control
file and reports what is observed.

Also supports "B2" (Root-3 single-variable experiment: plain direct launch
with only SDL_JOYSTICK_HIDAPI_STEAMXBOX removed - see
src/ExternalInputBridge.cs StartPlainDirectWithoutSteamXboxHidapiEnv), and a
-CasesToRun parameter to run any subset/order of B0/B1/B2 - e.g.
-CasesToRun B2 to run B2 alone, not preceded by B0, to check whether a
symptom is specific to a launch sequence rather than the case itself.
#>
param(
    [int]$GamePidTimeoutMs = 90000,
    [int]$MonitorDurationMs = 18000,
    [string]$GameExePath = 'C:\Program Files (x86)\Steam\steamapps\common\smt3hd\smt3hd.exe',
    # Which case labels to run, in order. Defaults to the full B0/B1
    # comparison; pass -CasesToRun B1 to run B1 alone (clean, not preceded
    # by B0 in the same session) - e.g. to check whether a symptom occurs
    # standalone or only after a prior B0 launch in sequence. Does not
    # change condition A or the B0/B1 launch-mode implementations themselves.
    # 'A' runs condition A (Explorer, production launch path, UNCHANGED
    # code) through this same harness purely to capture a fresh, per-label
    # helper log for a side-by-side device-identity diff against B0/B1/B2/B3
    # - it does not modify or touch StartViaExplorer.
    [ValidateSet('A', 'B0', 'B1', 'B2', 'B3', 'B4', 'B5', 'B6', 'B7')]
    [string[]]$CasesToRun = @('B0', 'B1')
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path -LiteralPath $GameExePath)) {
    throw "smt3hd.exe が見つかりません: $GameExePath ; -GameExePath でパスを指定してください。"
}
$gameDir = Split-Path -Parent $GameExePath
$tempDir = [System.IO.Path]::GetTempPath()
$launchModeRequestPath = Join-Path $tempDir 'NocturneModernController.LaunchMode.Test.json'

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
    if ($procs.Count -eq 0) { return }
    foreach ($p in $procs) { try { $p.CloseMainWindow() | Out-Null } catch {} }
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline -and (Test-GameProcessRunning)) { Start-Sleep -Milliseconds 500 }
    foreach ($p in @(Get-Process -Name 'smt3hd' -ErrorAction SilentlyContinue)) {
        try { Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue } catch {}
    }
    Start-Sleep -Seconds 1
    Write-Host "GAME_CLOSED"
}

function Write-DiagnosticLaunchModeRequest {
    param([string]$LaunchMode, [string]$Label)
    $payload = [ordered]@{
        launchMode = $LaunchMode
        label = $Label
        gamePidTimeoutMs = $GamePidTimeoutMs
        monitorDurationMs = $MonitorDurationMs
    }
    ($payload | ConvertTo-Json -Compress) | Set-Content -LiteralPath $launchModeRequestPath -Encoding utf8 -NoNewline
    Write-Host "LAUNCH_MODE_REQUEST_CONTENT $(Get-Content -LiteralPath $launchModeRequestPath -Raw)"
}

function Wait-ForStartedMarker {
    param([string]$Label, [int]$TimeoutSeconds)
    $markerPath = Join-Path $tempDir "NocturneModernController.AxisTest.InGame.$Label.started.json"
    if (Test-Path -LiteralPath $markerPath) { Remove-Item -LiteralPath $markerPath -Force }
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        if (Test-Path -LiteralPath $markerPath) { return $true }
        Start-Sleep -Milliseconds 300
    }
    return $false
}

function Clear-StaleResult {
    # Must run BEFORE the monitor window starts, not after it closes: the
    # helper's own MonitorDeadline is timed from the same instant it wrote
    # the "started" marker, while this script only notices that marker up to
    # one polling interval later and then runs its own MonitorDurationMs
    # wait on top. That skew means the helper can finish writing THIS run's
    # result a few hundred ms before this script starts waiting for it. A
    # "delete if present, then poll for a new one" done AFTER the window
    # closes races that write and can delete the correct, freshly-written
    # result, then poll forever for a file that will never reappear
    # (real-machine testing reproduced this: RESULT_FILE_MISSING every time,
    # even though the helper's own log showed a normal completed result).
    # Clearing any stale leftover here, before Start-Game, avoids the race
    # entirely - the label is fresh for the rest of the case's lifetime.
    param([string]$Label)
    $resultPath = Join-Path $tempDir "NocturneModernController.AxisTest.InGame.$Label.result.json"
    if (Test-Path -LiteralPath $resultPath) { Remove-Item -LiteralPath $resultPath -Force }
}

function Wait-InGameResult {
    param([string]$Label, [int]$TimeoutSeconds)
    $resultPath = Join-Path $tempDir "NocturneModernController.AxisTest.InGame.$Label.result.json"
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        if (Test-Path -LiteralPath $resultPath) {
            Start-Sleep -Milliseconds 300
            try { return Get-Content -LiteralPath $resultPath -Raw | ConvertFrom-Json } catch { Start-Sleep -Milliseconds 300 }
        }
        Start-Sleep -Milliseconds 500
    }
    return $null
}

function Get-EnvLineValue {
    param([string]$LogPath, [string]$VarName)
    if (-not (Test-Path -LiteralPath $LogPath)) { return 'UNAVAILABLE' }
    $line = Select-String -Path $LogPath -Pattern 'DIAGSNAP steamEnv=' | Select-Object -First 1
    if (-not $line) { return 'UNAVAILABLE' }
    $text = $line.Line
    if ($text -match "$VarName=([^;]*)(;|$)") {
        $val = $Matches[1].Trim()
        if ($val -eq '' ) { return '(absent)' }
        return $val
    }
    return '(absent)'
}

function Get-SteamEnvLineRaw {
    param([string]$LogPath)
    if (-not (Test-Path -LiteralPath $LogPath)) { return $null }
    $line = Select-String -Path $LogPath -Pattern 'DIAGSNAP steamEnv=' | Select-Object -First 1
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

function Invoke-Beep {
    # [console]::beep is a Windows PowerShell 5.1 built-in (wraps the Win32
    # Beep API through the default audio device since Vista) - it plays
    # through normal speakers independent of window focus, so it is audible
    # even while the game holds exclusive fullscreen and the PowerShell
    # window is not visible at all.
    param([int]$Count, [int]$FrequencyHz = 1000, [int]$DurationMs = 200, [int]$GapMs = 150)
    for ($i = 0; $i -lt $Count; $i++) {
        [console]::beep($FrequencyHz, $DurationMs)
        if ($i -lt $Count - 1) { Start-Sleep -Milliseconds $GapMs }
    }
}

function Get-ModuleSet {
    param([string]$LogPath)
    if (-not (Test-Path -LiteralPath $LogPath)) { return @() }
    $names = Select-String -Path $LogPath -Pattern 'DIAGSNAP MODULE name="([^"]+)"' | ForEach-Object {
        $_.Matches[0].Groups[1].Value
    }
    return @($names | Sort-Object -Unique)
}

$allCaseDefs = @(
    @{ Label = 'A'; LaunchMode = 'A'; Description = 'A: Explorer経由 (production/Known Good, unchanged code)'; BeepStart = 9; BeepEnd = 10; TargetEnvVar = $null }
    @{ Label = 'B0'; LaunchMode = 'B'; Description = 'B0: Direct Launch (環境変更なし)'; BeepStart = 1; BeepEnd = 2; TargetEnvVar = 'SDL_GAMECONTROLLER_IGNORE_DEVICES' }
    @{ Label = 'B1'; LaunchMode = 'B1'; Description = 'B1: Direct Launch (SDL_GAMECONTROLLER_IGNORE_DEVICESのみ除去)'; BeepStart = 3; BeepEnd = 4; TargetEnvVar = 'SDL_GAMECONTROLLER_IGNORE_DEVICES' }
    @{ Label = 'B2'; LaunchMode = 'B2'; Description = 'B2: Direct Launch (SDL_JOYSTICK_HIDAPI_STEAMXBOXのみ除去)'; BeepStart = 5; BeepEnd = 6; TargetEnvVar = 'SDL_JOYSTICK_HIDAPI_STEAMXBOX' }
    @{ Label = 'B3'; LaunchMode = 'B3'; Description = 'B3: Direct Launch (SDL_GAMECONTROLLER_ALLOW_STEAM_VIRTUAL_GAMEPADのみ除去)'; BeepStart = 7; BeepEnd = 8; TargetEnvVar = 'SDL_GAMECONTROLLER_ALLOW_STEAM_VIRTUAL_GAMEPAD' }
    @{ Label = 'B4'; LaunchMode = 'B4'; Description = 'B4: Direct Launch (SteamGenericControllersのみ除去)'; BeepStart = 11; BeepEnd = 12; TargetEnvVar = 'SteamGenericControllers' }
    @{ Label = 'B5'; LaunchMode = 'B5'; Description = 'B5: Direct Launch (SDL_GAMECONTROLLER_USE_BUTTON_LABELSのみ除去)'; BeepStart = 13; BeepEnd = 14; TargetEnvVar = 'SDL_GAMECONTROLLER_USE_BUTTON_LABELS' }
    @{ Label = 'B6'; LaunchMode = 'B6'; Description = 'B6: Direct Launch (SDL_GAMECONTROLLER_IGNORE_DEVICESをOFF時観測値に置換)'; BeepStart = 15; BeepEnd = 16; TargetEnvVar = 'SDL_GAMECONTROLLER_IGNORE_DEVICES' }
    @{ Label = 'B7'; LaunchMode = 'B7'; Description = 'B7: Direct Launch (STEAM*/SDL_*/GAMEINPUT*環境変数を全除去)'; BeepStart = 17; BeepEnd = 18; TargetEnvVar = $null }
)
$caseDefs = @($CasesToRun | ForEach-Object { $lbl = $_; $allCaseDefs | Where-Object { $_.Label -eq $lbl } })

Write-Host "=== Root-2 Physical Input Comparison (Cases: $($CasesToRun -join ', ')) ==="
Write-Host "Steamの起動オプションは変更しません。ゲームの起動・終了はこのrunnerが自動的に行います。"
Write-Host ""

$results = @{}
$logPaths = @{}

foreach ($case in $caseDefs) {
    $label = $case.Label
    Write-Host ""
    Write-Host "=== Case $label`: $($case.Description) ==="

    try {
        if (Test-GameProcessRunning) {
            Write-Warning "smt3hd.exe がまだ起動しています。自動的に終了させます。"
            Stop-GameProcess
        }

        Clear-StaleResult -Label $label
        Write-DiagnosticLaunchModeRequest -LaunchMode $case.LaunchMode -Label $label
        Write-Host "ゲームを起動しています。準備が整うまでお待ちください（自動検出します）..."
        Start-Game | Out-Null

        $ready = Wait-ForStartedMarker -Label $label -TimeoutSeconds ([math]::Ceiling($GamePidTimeoutMs / 1000) + 30)
        if (-not $ready) {
            # ASCII-only marker line, kept separate from the Write-Warning text
            # below: a caller piping this script's output through an
            # ASCII-oriented filter (e.g. a non-UTF8 console codepage) can
            # miss multibyte Japanese/symbol lines entirely, so every
            # machine-detectable event also gets a plain-ASCII line.
            Write-Host "READY_NOT_DETECTED label=$label"
            Write-Warning "Case $label`: 準備完了(gamePid/SDL device)を検出できませんでした。RESULT_FILE_MISSING扱いとして次へ進みます。"
            $results[$label] = $null
            $logPaths[$label] = Join-Path $tempDir "NocturneModernController.AxisTest.InGame.$label.log"
            continue
        }

        # ASCII-only marker: this is the reliable "operate now" signal for any
        # automated watcher. Do not rely on the ★ block below for detection -
        # it is for a human directly watching this console, and multibyte
        # characters are not guaranteed to round-trip through every console
        # codepage/pipe (real-machine testing found ★ silently dropped when
        # this script's output was piped through an ASCII-default filter).
        Write-Host "OPERATE_NOW label=$label seconds=$([math]::Round($MonitorDurationMs/1000))"
        # Audible start-of-window signal (fullscreen hides this console
        # entirely, so this beep - not the text below - is the real cue).
        Invoke-Beep -Count $case.BeepStart

        Write-Host ""
        Write-Host "★★★★★★★★★★★★★★★★★★★★★★★★★★★★★★★★★"
        Write-Host "★ 今すぐコントローラーを操作してください（約 $([math]::Round($MonitorDurationMs/1000)) 秒間） ★"
        Write-Host "★★★★★★★★★★★★★★★★★★★★★★★★★★★★★★★★★"
        Write-Host "1. 右スティックを上下左右へ大きく動かす"
        Write-Host "2. 左スティックを上下左右へ大きく動かす"
        Write-Host "3. A / B / X / Y / LB / RB を1回ずつ押す"
        Write-Host ""

        $remaining = [math]::Round($MonitorDurationMs / 1000)
        while ($remaining -gt 0) {
            Write-Host "  ...残り約 $remaining 秒"
            $step = [Math]::Min(5, $remaining)
            Start-Sleep -Seconds $step
            $remaining -= $step
        }
        Write-Host "MONITOR_WINDOW_CLOSED label=$label"
        # Audible end-of-window signal - operating the controller past this
        # point is no longer captured for this case.
        Invoke-Beep -Count $case.BeepEnd
        Write-Host "監視終了。判定を取得しています..."

        $timeoutSeconds = [math]::Ceiling($MonitorDurationMs / 1000) + 20
        $result = Wait-InGameResult -Label $label -TimeoutSeconds $timeoutSeconds
        $results[$label] = $result
        $logPaths[$label] = Join-Path $tempDir "NocturneModernController.AxisTest.InGame.$label.log"

        if ($null -eq $result) {
            Write-Host "Case $label`: RESULT_FILE_MISSING"
        } else {
            $buttonsPressedList = @($result.buttons.PSObject.Properties | Where-Object { $_.Value.pressObserved } | ForEach-Object { $_.Name })
            Write-Host ("Case {0}: RightStick(X={1},Y={2}) LeftStick(X={3},Y={4}) Buttons=[{5}] Overall={6} reasons=[{7}]" -f `
                $label, $result.rightStickX, $result.rightStickY, $result.leftStickX, $result.leftStickY, `
                ($buttonsPressedList -join ','), $result.overall, ($result.failureReasons -join ','))
            Write-Host ("  maxAbsRightX={0} maxAbsRightY={1} maxAbsLeftX={2} maxAbsLeftY={3}" -f `
                $result.rightStick.maxAbsX, $result.rightStick.maxAbsY, $result.leftStick.maxAbsX, $result.leftStick.maxAbsY)
            Write-Host ("  DEVICE name=""{0}"" path=""{1}"" vid=0x{2:X4} pid=0x{3:X4} type={4} deviceCount={5} playerIndex={6} instanceId={7} connectionState={8} serial=""{9}""" -f `
                $result.selectedName, $result.selectedPath, $result.selectedVid, $result.selectedPid, $result.selectedType, `
                $result.deviceCount, $result.playerIndex, $result.selectedInstanceId, $result.connectionState, $result.serial)
        }

        $targetVar = $case.TargetEnvVar
        if ($targetVar) {
            $targetVarValue = Get-EnvLineValue -LogPath $logPaths[$label] -VarName $targetVar
            $targetVarPresent = $targetVarValue -ne '(absent)' -and $targetVarValue -ne 'UNAVAILABLE'
            Write-Host "  $targetVar present=$targetVarPresent"
        }
    } catch {
        Write-Warning "Case $label で予期しないエラーが発生しました: $_"
        $results[$label] = $null
        if (-not $logPaths.ContainsKey($label)) {
            $logPaths[$label] = Join-Path $tempDir "NocturneModernController.AxisTest.InGame.$label.log"
        }
    } finally {
        # このブロックは、上のtry内でエラーが発生した場合や、コントローラー
        # 操作中にユーザーがCtrl+Cで中断した場合でも必ず実行され、
        # smt3hd.exeがゾンビ状態で残らないようにする。
        Write-Host ""
        Write-Host "ゲームを自動的に終了します..."
        Stop-GameProcess
    }
}

Write-Host ""
Write-Host "=== Root-2 Summary ==="
foreach ($label in $CasesToRun) {
    $r = $results[$label]
    if ($null -eq $r) {
        Write-Host "$label`: RESULT_FILE_MISSING"
    } else {
        Write-Host ("{0}: Overall={1} RightStick(X={2},Y={3}) LeftStick(X={4},Y={5}) reasons=[{6}]" -f `
            $label, $r.overall, $r.rightStickX, $r.rightStickY, $r.leftStickX, $r.leftStickY, ($r.failureReasons -join ','))
    }
}

$bothCasesRan = ($CasesToRun -contains 'B0') -and ($CasesToRun -contains 'B1')
$unexpectedDiffs = $null
$ignoreKeyName = 'SDL_GAMECONTROLLER_IGNORE_DEVICES'

Write-Host ""
Write-Host "--- Unexpected environment differences (B0 vs B1, IGNORE_DEVICES自体は除外して評価) ---"
if ($bothCasesRan -and $logPaths['B0'] -and $logPaths['B1'] -and (Test-Path $logPaths['B0']) -and (Test-Path $logPaths['B1'])) {
    $envLineB0Raw = Get-SteamEnvLineRaw -LogPath $logPaths['B0']
    $envLineB1Raw = Get-SteamEnvLineRaw -LogPath $logPaths['B1']
    $mapB0 = Parse-SteamEnvLine -Line $envLineB0Raw
    $mapB1 = Parse-SteamEnvLine -Line $envLineB1Raw

    $allEnvKeys = [System.Collections.Generic.HashSet[string]]::new()
    foreach ($k in $mapB0.Keys) { [void]$allEnvKeys.Add($k) }
    foreach ($k in $mapB1.Keys) { [void]$allEnvKeys.Add($k) }
    $unexpectedDiffs = [System.Collections.Generic.List[string]]::new()
    foreach ($k in $allEnvKeys) {
        if ($k -eq $ignoreKeyName) { continue }
        $vB0 = if ($mapB0.Contains($k)) { $mapB0[$k] } else { '(absent)' }
        $vB1 = if ($mapB1.Contains($k)) { $mapB1[$k] } else { '(absent)' }
        if ($vB0 -ne $vB1) {
            $unexpectedDiffs.Add("$k : B0=$vB0 / B1=$vB1")
        }
    }

    if ($null -eq $envLineB0Raw -or $null -eq $envLineB1Raw) {
        Write-Host "UNAVAILABLE (片方または両方のログにDIAGSNAP steamEnv=行が見つかりません)"
    } elseif ($unexpectedDiffs.Count -eq 0) {
        Write-Host "NO_UNEXPECTED_ENV_DIFFERENCES ($ignoreKeyName 以外は完全一致)"
    } else {
        Write-Warning "UNEXPECTED_ENV_DIFFERENCES_FOUND:"
        $unexpectedDiffs | ForEach-Object { Write-Warning ("  " + $_) }
    }

    Write-Host ""
    Write-Host "--- Loaded module differences (B0 vs B1) ---"
    $modsB0 = Get-ModuleSet -LogPath $logPaths['B0']
    $modsB1 = Get-ModuleSet -LogPath $logPaths['B1']
    $onlyB0 = @($modsB0 | Where-Object { $modsB1 -notcontains $_ })
    $onlyB1 = @($modsB1 | Where-Object { $modsB0 -notcontains $_ })
    Write-Host ("ONLY_B0: {0}" -f ($(if ($onlyB0.Count -gt 0) { $onlyB0 -join ', ' } else { '(none)' })))
    Write-Host ("ONLY_B1: {0}" -f ($(if ($onlyB1.Count -gt 0) { $onlyB1 -join ', ' } else { '(none)' })))
} else {
    Write-Host "UNAVAILABLE (ログ不足)"
}

Write-Host ""
Write-Host "=== Root-2 Verdict (observation-based, no root-cause claim) ==="
$b0 = $results['B0']
$b1 = $results['B1']
if (-not $bothCasesRan) {
    Write-Host "VERDICT=SINGLE_CASE_RUN_NO_COMPARISON"
    Write-Host "今回はCase $($CasesToRun -join ', ') のみの単独実行です。B0-B1間の比較Verdictは対象外です。個別のOverall値・helper生ログを直接確認してください。"
} elseif ($null -eq $b0 -or $null -eq $b1) {
    Write-Host "VERDICT=INDETERMINATE_MISSING_RESULT"
    Write-Host "判定不能: 片方または両方の結果が取得できませんでした。"
} elseif ($b0.overall -eq 'PASS') {
    Write-Host "VERDICT=B0_PASS_BASELINE_NOT_REPRODUCED"
    Write-Host "B0がPASSしたため、Known Bad baselineが今回は再現していません。この比較はB0-B1間の因果評価には使用できません。"
} elseif ($b0.overall -ne 'PASS' -and $b1.overall -eq 'PASS') {
    Write-Host "VERDICT=B0_FAIL_B1_PASS_SUPPORTS_CAUSAL_ROLE"
    Write-Host "B0=FAIL / B1=PASS: SDL_GAMECONTROLLER_IGNORE_DEVICESの因果関与を強く支持する結果です（ただし他要因の関与を排除するものではありません）。"
} elseif ($b0.overall -ne 'PASS' -and $b1.overall -ne 'PASS') {
    Write-Host "VERDICT=B0_FAIL_B1_FAIL_IGNORE_DEVICES_ALONE_INSUFFICIENT"
    Write-Host "B0=FAIL / B1=FAIL: SDL_GAMECONTROLLER_IGNORE_DEVICES単独ではこの症状を説明できません。他の環境変数・gameoverlayrenderer64.dll等の関与を優先的に検討すべきです。"
} else {
    Write-Host "VERDICT=INDETERMINATE_OTHER"
    Write-Host "判定不能な組み合わせです。個別のOverall値を直接確認してください。"
}

# 全テスト完了の合図: 数え間違えないよう、カウント式の短いビープとは
# 明確に区別できる長め・単発のビープにする。
[console]::beep(1500, 1200)
Write-Host "ALL_TESTS_COMPLETE"

$summaryPath = Join-Path $tempDir 'NocturneModernController.Root2.Summary.txt'
$summaryLines = [System.Collections.Generic.List[string]]::new()
$summaryLines.Add("Root-2 comparison (Cases: $($CasesToRun -join ', ')) completed at $(Get-Date -Format o)")
foreach ($label in $CasesToRun) {
    $r = $results[$label]
    if ($null -eq $r) {
        $summaryLines.Add("$label`: RESULT_FILE_MISSING")
    } else {
        $buttonsPressedList = @($r.buttons.PSObject.Properties | Where-Object { $_.Value.pressObserved } | ForEach-Object { $_.Name })
        $summaryLines.Add(("{0}: Overall={1} RightStick(X={2},Y={3}) LeftStick(X={4},Y={5}) maxAbsRightX={6} maxAbsRightY={7} maxAbsLeftX={8} maxAbsLeftY={9} Buttons=[{10}] reasons=[{11}]" -f `
            $label, $r.overall, $r.rightStickX, $r.rightStickY, $r.leftStickX, $r.leftStickY, `
            $r.rightStick.maxAbsX, $r.rightStick.maxAbsY, $r.leftStick.maxAbsX, $r.leftStick.maxAbsY, `
            ($buttonsPressedList -join ','), ($r.failureReasons -join ',')))
        $summaryLines.Add(("  DEVICE name=""{0}"" path=""{1}"" vid=0x{2:X4} pid=0x{3:X4} type={4} deviceCount={5} playerIndex={6} instanceId={7}" -f `
            $r.selectedName, $r.selectedPath, $r.selectedVid, $r.selectedPid, $r.selectedType, $r.deviceCount, $r.playerIndex, $r.selectedInstanceId))
    }
}
$summaryLines.Add("Unexpected env differences: " + $(if ($unexpectedDiffs -and $unexpectedDiffs.Count -gt 0) { ($unexpectedDiffs -join ' | ') } else { 'NONE (or not applicable in single-case mode)' }))
$summaryLines -join [Environment]::NewLine | Set-Content -LiteralPath $summaryPath -Encoding utf8
Write-Host ""
Write-Host "Per-case logs: $tempDir\NocturneModernController.AxisTest.InGame.<label>.log"
Write-Host "Per-case results: $tempDir\NocturneModernController.AxisTest.InGame.<label>.result.json"
Write-Host ""
Write-Host "注意: この結果は観測のみです。IGNORE_DEVICES以外の根本原因（Overlay、他のSteam/SDL変数等）を排除するものではありません。"
