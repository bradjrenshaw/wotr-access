# run-game.ps1 - Build + deploy WrathAccess, launch the game with the dev server on, and BLOCK
# until it exits. Run as a BACKGROUND task: the blocking wait means the task completes the instant
# the game quits/crashes (your wake-up signal), and meanwhile you drive the live game over
# http://127.0.0.1:8771 (POST /eval, GET /speech, GET /health) from a separate foreground shell.
#
# The dev server is DEBUG-only and gated on WRATHACCESS_DEV=1, which we set here and which the game
# inherits ONLY because we launch Wrath.exe directly (a Steam launch would not inherit it). To restart:
# cancel this background task (its finally kills the game), then run again.
#
#   -NoBuild : launch the already-deployed build without rebuilding (re-test the exact binary).

param(
    [switch]$NoBuild,
    [switch]$Help
)

if ($Help) {
    Write-Host "Usage: .\scripts\run-game.ps1 [-NoBuild]"
    Write-Host "  Builds + deploys (Debug), launches the game with WRATHACCESS_DEV=1, blocks until exit."
    Write-Host "  Drive it over http://127.0.0.1:8771 while it runs. -NoBuild skips the rebuild."
    exit 0
}

$ErrorActionPreference = "Stop"
$Port = 8771

# --- Locate the install (override with WRATHACCESS_GAME) ---
$Game = $env:WRATHACCESS_GAME
if (-not $Game) { $Game = "C:\Program Files (x86)\Steam\steamapps\common\Pathfinder Second Adventure" }
$Exe = "$Game\Wrath.exe"
if (-not (Test-Path $Exe)) {
    Write-Host "ERROR: Wrath.exe not found at: $Game (set WRATHACCESS_GAME)." -ForegroundColor Red
    exit 1
}

# --- Kill any running instance FIRST (so the build's DLL copies aren't blocked by a locked file),
#     then wait for it to exit and release the dev-server port before relaunching. ---
Get-Process -Name Wrath -ErrorAction SilentlyContinue | ForEach-Object {
    Write-Host "Stopping existing Wrath (PID $($_.Id))..." -ForegroundColor Yellow
    $_ | Stop-Process -Force
}
$deadline = (Get-Date).AddSeconds(15)
while ($true) {
    $alive = Get-Process -Name Wrath -ErrorAction SilentlyContinue
    $portFree = $false
    try { $null = Get-NetTCPConnection -LocalPort $Port -ErrorAction Stop } catch { $portFree = $true }
    if (-not $alive -and $portFree) { break }
    if ((Get-Date) -gt $deadline) {
        Write-Host "WARNING: timed out waiting for old game / port $Port to free; launching anyway." -ForegroundColor Yellow
        break
    }
    Start-Sleep -Milliseconds 250
}

# --- Build + deploy (Debug) BEFORE launching, so "restart" implies "rebuild" and we never test a
#     stale DLL. A build failure aborts the launch. -NoBuild skips it. ---
if (-not $NoBuild) {
    dotnet build "$PSScriptRoot\..\WrathAccess.csproj" -c Debug
    if ($LASTEXITCODE -ne 0) {
        Write-Host "Build FAILED; NOT launching (the deployed DLL would be stale)." -ForegroundColor Red
        exit 1
    }
}

# --- Enable the dev server. Two gates, because launching Wrath.exe directly makes Steam relaunch it into
#     a fresh process that does NOT inherit our $env: var (the env gate then silently no-ops). The marker
#     file under persistentDataPath\WrathAccess is read regardless of how the game is (re)launched. ---
$LocalLow = "$env:USERPROFILE\AppData\LocalLow\Owlcat Games\Pathfinder Wrath Of The Righteous"
$MarkerDir = Join-Path $LocalLow "WrathAccess"
New-Item -ItemType Directory -Force -Path $MarkerDir | Out-Null
Set-Content -Path (Join-Path $MarkerDir "devserver.enable") -Value "1" -Encoding ascii
$env:WRATHACCESS_DEV = "1"

$proc = Start-Process -FilePath $Exe -WorkingDirectory $Game -PassThru
Write-Host "Launched Wrath (PID $($proc.Id)); dev server -> http://127.0.0.1:$Port. Waiting for /health..." -ForegroundColor Cyan

# Poll until the dev server answers (the game takes a while to boot to the entry point).
$health = curl.exe -s --retry 120 --retry-connrefused --retry-delay 1 "http://127.0.0.1:$Port/health"
if ($health -match "ok") {
    Write-Host "READY: dev server is up on $Port. Drive it with curl from another shell." -ForegroundColor Green
} else {
    Write-Host "WARNING: dev server never answered /health (direct-launch env not inherited? game relaunched via Steam?)." -ForegroundColor Yellow
}

try {
    $proc.WaitForExit()
} finally {
    if (-not $proc.HasExited) {
        Write-Host "Launcher stopping; killing game (PID $($proc.Id))..." -ForegroundColor Yellow
        $proc.Kill()
    }
}
Write-Host "Wrath exited with code $($proc.ExitCode)." -ForegroundColor Cyan
exit $proc.ExitCode
