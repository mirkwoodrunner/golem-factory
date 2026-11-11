<#
.SYNOPSIS
  Exports the game for the Steam Deck (Linux x86_64) in one command, and optionally copies it over.

.DESCRIPTION
  Runs the Godot 4.7.2 mono editor headless with the "Linux Deck" preset (godot/export_presets.cfg).
  The output folder holds GolemFactory.x86_64, GolemFactory.pck and
  data_GolemFactory.Godot_linuxbsd_x86_64/, a self-contained .NET 10 runtime; the Deck needs no dotnet.

  The first export for a platform can sit idle after it has written the pack (seen 2026-11-08, both
  Linux and Windows; later exports exit in about 10 s). So this script watches the log: once the pack
  is stored and the files exist, it gives Godot a grace period and then ends it.

  -Preset "Windows Smoke" exports the same pack for Windows, to run scenarios on an exported build
  without a Deck: GolemFactory.exe --headless --fixed-fps 60 -- --scenario world

  -RunPerf, with -DeckHost, then runs the perf scenario on the Deck, windowed on its own screen
  (Desktop Mode, DISPLAY=:0), and gamepad-ui with --frame-watch for the worst frames a first open
  costs (docs/steam-deck-design.md D8). Both print their verdicts here.

.EXAMPLE
  ./Tools/Deck/export-linux.ps1
  ./Tools/Deck/export-linux.ps1 -DeckHost steamdeck
  ./Tools/Deck/export-linux.ps1 -DeckHost steamdeck -RunPerf
#>
param(
    [string]$Out = (Join-Path $PSScriptRoot '..\..\build\deck'),
    [string]$Preset = 'Linux Deck',
    [string]$DeckHost = '',
    [string]$Godot = 'G:\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe',
    [int]$TimeoutSeconds = 900,
    [int]$GraceSeconds = 20,
    [switch]$RunPerf
)

$ErrorActionPreference = 'Stop'
$project = (Resolve-Path (Join-Path $PSScriptRoot '..\..\godot')).Path
$binary = if ($Preset -eq 'Linux Deck') { 'GolemFactory.x86_64' } else { 'GolemFactory.exe' }

if (Test-Path $Out) { Remove-Item -Recurse -Force $Out -Confirm:$false }
New-Item -ItemType Directory -Force $Out | Out-Null
$Out = (Resolve-Path $Out).Path
$log = Join-Path $Out 'export.log'

$process = Start-Process -FilePath $Godot -PassThru -NoNewWindow `
    -ArgumentList @('--headless', '--path', "`"$project`"", '--export-release', "`"$Preset`"", "`"$(Join-Path $Out $binary)`"") `
    -RedirectStandardOutput $log -RedirectStandardError "$log.err"

$start = Get-Date
$packedAt = $null
while (-not $process.HasExited) {
    Start-Sleep -Seconds 2
    $packed = (Test-Path (Join-Path $Out 'GolemFactory.pck')) -and
              (Select-String -Path $log -Pattern 'DONE.*savepack' -Quiet -ErrorAction SilentlyContinue)
    if ($packed -and -not $packedAt) { $packedAt = Get-Date }
    if ($packedAt -and ((Get-Date) - $packedAt).TotalSeconds -gt $GraceSeconds) {
        Write-Host "The pack is written; Godot is still running after $GraceSeconds s, so it is ended."
        Stop-Process -Id $process.Id -Force -Confirm:$false
        break
    }
    if (((Get-Date) - $start).TotalSeconds -gt $TimeoutSeconds) {
        Stop-Process -Id $process.Id -Force -Confirm:$false
        throw "The export didn't finish in $TimeoutSeconds s. See $log"
    }
}

$errors = Select-String -Path $log, "$log.err" -Pattern '^ERROR' -ErrorAction SilentlyContinue
if ($errors) { $errors | ForEach-Object { Write-Host $_.Line } }
foreach ($need in @($binary, 'GolemFactory.pck')) {
    if (-not (Test-Path (Join-Path $Out $need))) { throw "The export made no $need. See $log" }
}
$runtime = Get-ChildItem $Out -Directory -Filter 'data_GolemFactory.Godot_*' | Select-Object -First 1
if (-not $runtime) { throw "The export made no .NET folder. See $log" }

Write-Host "Exported '$Preset' to $Out ($([math]::Round((Get-ChildItem $Out -Recurse | Measure-Object Length -Sum).Sum / 1MB)) MB)."

if ($DeckHost) {
    # Needs SSH on the Deck (docs/steam-deck-design.md D0) and scp on this machine (Windows ships OpenSSH).
    ssh "deck@$DeckHost" 'mkdir -p ~/Games/GolemFactory'
    if ($LASTEXITCODE -ne 0) { throw "ssh to deck@$DeckHost failed" }
    scp -r "$Out\$binary" "$Out\GolemFactory.pck" "$($runtime.FullName)" "deck@${DeckHost}:~/Games/GolemFactory/"
    if ($LASTEXITCODE -ne 0) { throw "scp to deck@$DeckHost failed" }
    ssh "deck@$DeckHost" "chmod +x ~/Games/GolemFactory/$binary"
    Write-Host "Copied to deck@${DeckHost}:~/Games/GolemFactory/. Run it there from a Desktop Mode terminal first."
}

if ($RunPerf) {
    if (-not $DeckHost) { throw "-RunPerf needs -DeckHost" }
    # On the Deck's own screen, in Desktop Mode: a headless run would measure no drawing.
    foreach ($scenario in @('perf', 'gamepad-ui --frame-watch')) {
        Write-Host "Running $scenario on the Deck..."
        ssh "deck@$DeckHost" "cd ~/Games/GolemFactory && DISPLAY=:0 ./$binary --fixed-fps 60 -- --scenario $scenario 2>/dev/null | grep -E '^\[(scenario|frame-watch)'"
    }
    Write-Host "Record the numbers in docs/performance-report-deck.md (the template: docs/performance-report-template.md)."
}
