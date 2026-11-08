<#
.SYNOPSIS
  Launches the game for a soak (testscript/endless-soak.md) and samples it until it exits.

.DESCRIPTION
  Starts the windowed mono build with --print-fps and --log-file, so the frame rate lands in a log
  outside the editor. Every -IntervalSeconds it writes one CSV row: minutes since launch, the game's
  working set and private bytes (MB), handles, threads, the frame rate's lowest and mean reading
  since the last row, and the size of the newest save in user://saves. It stops when the game
  exits. Reads only; it never touches a save or the playtest report.

  Growth is what a soak looks for, so read the CSV against its own first rows (T+0), not against
  any absolute number.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File Tools\Soak\soak-sample.ps1
  powershell -ExecutionPolicy Bypass -File Tools\Soak\soak-sample.ps1 -IntervalSeconds 30 -OutDir C:\soak
  powershell -ExecutionPolicy Bypass -File Tools\Soak\soak-sample.ps1 -GameArgs "--quit-after 1500 -- --demo"

  With no -GameArgs it is a normal launch: the title screen, the player's own saves and settings.
#>
param(
    [string]$Godot = 'G:\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64.exe',
    [string]$Project = (Join-Path $PSScriptRoot '..\..\godot'),
    [int]$IntervalSeconds = 60,
    [string]$OutDir = (Join-Path $env:TEMP ('golem-soak-' + (Get-Date -Format 'yyyyMMdd-HHmm'))),
    # One string, split on spaces: `powershell -File` hands an array parameter over as a single
    # comma-joined word, which Godot ignores and starts a normal game.
    [string]$GameArgs = ''
)

$ErrorActionPreference = 'Stop'
if (-not (Test-Path $Godot)) { throw "No Godot at $Godot. Pass -Godot with the mono build's windowed exe." }
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$log = Join-Path $OutDir 'godot.log'
$csv = Join-Path $OutDir 'samples.csv'
$saves = Join-Path $env:APPDATA 'Godot\app_userdata\Golem Factory\saves'

$argList = @('--path', ('"' + (Resolve-Path $Project) + '"'), '--print-fps', '--log-file', ('"' + $log + '"')) + @($GameArgs -split '\s+' | Where-Object { $_ })
$game = Start-Process -FilePath $Godot -ArgumentList $argList -PassThru
$start = Get-Date
'minutes,working_set_mb,private_mb,handles,threads,fps_min,fps_mean,newest_save,newest_save_kb' | Set-Content -Encoding utf8 $csv
Write-Host "Soak running: pid $($game.Id). Samples: $csv  Log: $log"
Write-Host "Close the game to stop."

$read = 0
while ($true) {
    Start-Sleep -Seconds $IntervalSeconds
    $game.Refresh()
    if ($game.HasExited) { break }

    $fps = @()
    if (Test-Path $log) {
        $lines = @(Get-Content $log)
        if ($lines.Count -gt $read) {
            $fps = @($lines[$read..($lines.Count - 1)] | ForEach-Object {
                if ($_ -match 'FPS:\s*([0-9.]+)') { [double]$Matches[1] } })
            $read = $lines.Count
        }
    }
    $fpsMin = ''; $fpsMean = ''
    if ($fps.Count -gt 0) {
        $m = $fps | Measure-Object -Minimum -Average
        $fpsMin = [math]::Round($m.Minimum, 1); $fpsMean = [math]::Round($m.Average, 1)
    }

    $save = $null
    if (Test-Path $saves) {
        $save = Get-ChildItem $saves -Filter '*.json' | Sort-Object LastWriteTime -Descending | Select-Object -First 1
    }
    $row = @(
        [math]::Round(((Get-Date) - $start).TotalMinutes, 1),
        [math]::Round($game.WorkingSet64 / 1MB, 1),
        [math]::Round($game.PrivateMemorySize64 / 1MB, 1),
        $game.HandleCount,
        $game.Threads.Count,
        $fpsMin, $fpsMean,
        $(if ($save) { $save.Name } else { '' }),
        $(if ($save) { [math]::Round($save.Length / 1KB, 1) } else { '' })
    ) -join ','
    Add-Content -Encoding utf8 $csv $row
    Write-Host $row
}
Write-Host "The game exited after $([math]::Round(((Get-Date) - $start).TotalMinutes, 1)) minutes, code $($game.ExitCode). Samples: $csv"
