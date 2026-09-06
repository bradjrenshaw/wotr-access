<#
.SYNOPSIS
    Install / update Wrath Access from this repo into the game (alpha-tester flow).

.DESCRIPTION
    Copies the prebuilt mod into the game's native-mod folder and enables it - no .NET
    SDK or build needed. The usual tester loop is:

        git pull
        .\deploy.ps1

    What it does (mirrors the Debug build's deploy, for the committed Release build):
      * finds the game (Steam library auto-detect, or pass -GameDir),
      * copies the mod to %LOCALLOW%\Owlcat Games\Pathfinder Wrath Of The Righteous\
        Modifications\WrathAccess\ (manifest + settings + Assemblies\ + assets\, plus the
        empty Bundles\ and Blueprints\ the loader requires),
      * copies prism.dll (native speech) next to Wrath.exe,
      * adds WrathAccess to EnabledModifications in OwlcatModificationManagerSettings.json
        (other mods and SourceDirectories preserved).

    Your own Wrath Access settings live in %LOCALLOW%\...\WrathAccess\ (NOT the mod folder),
    so they survive a redeploy. Close the game before running (prism.dll is locked while it
    runs). Restart the game afterwards to load the update.

.PARAMETER GameDir
    The "Pathfinder Second Adventure" install folder (the one containing Wrath.exe), if
    auto-detection cannot find it.
#>
[CmdletBinding()]
param([string]$GameDir)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot

function Find-GameDir([string]$override) {
    if ($override) {
        if (Test-Path (Join-Path $override 'Wrath.exe')) { return $override }
        throw "Wrath.exe not found in -GameDir '$override'."
    }
    $candidates = @()
    $steam = (Get-ItemProperty 'HKCU:\Software\Valve\Steam' -Name SteamPath -ErrorAction SilentlyContinue).SteamPath
    if (-not $steam) { $steam = (Get-ItemProperty 'HKLM:\SOFTWARE\WOW6432Node\Valve\Steam' -Name InstallPath -ErrorAction SilentlyContinue).InstallPath }
    if ($steam) {
        $steam = $steam -replace '/', '\'
        $candidates += (Join-Path $steam 'steamapps\common\Pathfinder Second Adventure')
        $vdf = Join-Path $steam 'steamapps\libraryfolders.vdf'
        if (Test-Path $vdf) {
            foreach ($m in [regex]::Matches((Get-Content $vdf -Raw), '"path"\s*"([^"]+)"')) {
                $lib = $m.Groups[1].Value -replace '\\\\', '\'
                $candidates += (Join-Path $lib 'steamapps\common\Pathfinder Second Adventure')
            }
        }
    }
    $candidates += 'C:\Program Files (x86)\Steam\steamapps\common\Pathfinder Second Adventure'
    foreach ($c in ($candidates | Select-Object -Unique)) {
        if (Test-Path (Join-Path $c 'Wrath.exe')) { return $c }
    }
    throw "Could not find the game. Re-run with -GameDir 'X:\...\steamapps\common\Pathfinder Second Adventure'."
}

# Add WrathAccess to EnabledModifications, preserving every other key (SourceDirectories, other mods).
function Enable-Mod([string]$localLow) {
    $path = Join-Path $localLow 'OwlcatModificationManagerSettings.json'
    if (-not (Test-Path $path)) {
        New-Item -ItemType Directory -Force -Path $localLow | Out-Null
        '{"EnabledModifications":["WrathAccess"],"SourceDirectories":[]}' | Set-Content -Path $path -Encoding UTF8
        return
    }
    Add-Type -AssemblyName System.Web.Extensions
    $ser = New-Object System.Web.Script.Serialization.JavaScriptSerializer
    $data = $ser.DeserializeObject((Get-Content -Path $path -Raw))
    $mods = @()
    if ($data.ContainsKey('EnabledModifications') -and $data['EnabledModifications']) { $mods = @($data['EnabledModifications']) }
    if ($mods -notcontains 'WrathAccess') {
        $data['EnabledModifications'] = @($mods + 'WrathAccess')
        $ser.Serialize($data) | Set-Content -Path $path -Encoding UTF8
    }
}

if (Get-Process -Name 'Wrath' -ErrorAction SilentlyContinue) {
    throw "Pathfinder: Wrath of the Righteous is running. Close it, then re-run .\deploy.ps1."
}

$dll = Join-Path $root 'deploy\Assemblies\WrathAccess.dll'
if (-not (Test-Path $dll)) {
    throw "deploy\Assemblies\WrathAccess.dll is missing. Pull the latest commit (or, if you build, run scripts\stage.ps1)."
}
$moduleDll = Join-Path $root 'deploy\Module\WrathAccess.Module.dll'
if (-not (Test-Path $moduleDll)) {
    throw "deploy\Module\WrathAccess.Module.dll is missing. Pull the latest commit (or, if you build, run scripts\stage.ps1)."
}

$game = Find-GameDir $GameDir
Write-Host "Game:    $game"
$localLow = Join-Path $env:USERPROFILE 'AppData\LocalLow\Owlcat Games\Pathfinder Wrath Of The Righteous'
$modDir = Join-Path $localLow 'Modifications\WrathAccess'
Write-Host "Mod dir: $modDir"

# Replace the mod folder wholesale so stale files cannot linger (no user data lives here).
if (Test-Path $modDir) { Remove-Item $modDir -Recurse -Force }
New-Item -ItemType Directory -Force -Path $modDir, "$modDir\Assemblies", "$modDir\Bundles", "$modDir\Blueprints" | Out-Null

Write-Host "Copying mod files..."
Copy-Item (Join-Path $root 'OwlcatModificationManifest.json') $modDir
Copy-Item (Join-Path $root 'OwlcatModificationSettings.json') $modDir
Copy-Item (Join-Path $root 'deploy\Assemblies\*.dll') "$modDir\Assemblies"
# The reloadable module goes in Module\ (NOT Assemblies\ - the game loader would lock it there;
# the host byte-loads it from here instead).
New-Item -ItemType Directory -Force -Path "$modDir\Module" | Out-Null
Copy-Item $moduleDll "$modDir\Module"
Copy-Item (Join-Path $root 'assets') $modDir -Recurse

# Bundled documentation (Help > Read documentation opens <ModDir>\docs\index.html). Present once
# build_docs.ps1 has staged it into deploy\docs; if absent, the in-game button falls back to the
# hosted site.
$docs = Join-Path $root 'deploy\docs'
if (Test-Path $docs) {
    New-Item -ItemType Directory -Force -Path "$modDir\docs" | Out-Null
    Copy-Item "$docs\*" "$modDir\docs" -Recurse -Force
}

# Enable BEFORE the Program Files copy: if prism.dll can't be written (strict ACLs), the mod
# must still load — it falls back to SAPI speech. The old order left a deployed-but-DISABLED
# mod on a partial failure: game boots fine, zero speech, nothing to hear (tester repro).
Write-Host "Enabling the mod..."
Enable-Mod $localLow

Write-Host "Copying prism.dll next to Wrath.exe..."
$prismOk = $true
try { Copy-Item (Join-Path $root 'vendor\prism.dll') $game -Force; Copy-Item (Join-Path $root 'vendor\loadstar.dll') $game -Force }
catch {
    $prismOk = $false
    Write-Host "WARNING: could not copy prism.dll into '$game'." -ForegroundColor Yellow
    Write-Host "  ($($_.Exception.Message))"
    Write-Host "  The mod will still run and speak through SAPI. For screen-reader speech, re-run"
    Write-Host "  this script from an elevated PowerShell (right-click, Run as administrator)."
}

# Verify out loud - every line here is a fact a tester can read back over Discord.
Write-Host ""
Write-Host "---- Verification ----"
$checks = @(
    @{ Name = 'Mod folder';           Ok = (Test-Path (Join-Path $modDir 'Assemblies\WrathAccess.dll')) },
    @{ Name = 'Reloadable module';    Ok = (Test-Path (Join-Path $modDir 'Module\WrathAccess.Module.dll')) },
    @{ Name = 'Manifest + settings';  Ok = ((Test-Path (Join-Path $modDir 'OwlcatModificationManifest.json')) -and (Test-Path (Join-Path $modDir 'OwlcatModificationSettings.json'))) },
    @{ Name = 'Assets';               Ok = (Test-Path (Join-Path $modDir 'assets\locale\enGB\ui.json')) },
    @{ Name = 'prism.dll by Wrath.exe'; Ok = ($prismOk -and (Test-Path (Join-Path $game 'prism.dll'))) },
    @{ Name = 'Enabled in mod manager'; Ok = ((Get-Content (Join-Path $localLow 'OwlcatModificationManagerSettings.json') -Raw) -match '"?WrathAccess"?') }
)
$allOk = $true
foreach ($c in $checks) {
    $mark = if ($c.Ok) { 'OK  ' } else { $allOk = $false; 'FAIL' }
    Write-Host ("  {0} {1}" -f $mark, $c.Name)
}

Write-Host ""
if ($allOk) {
    Write-Host "Wrath Access deployed and verified. Start the game to load this build." -ForegroundColor Green
} elseif ($prismOk) {
    Write-Host "Deployed with FAILURES above - the mod may not load. Please report the FAIL lines." -ForegroundColor Yellow
} else {
    Write-Host "Deployed; speech will use SAPI until prism.dll is in place (see warning above)." -ForegroundColor Yellow
}
